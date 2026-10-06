import { SELF, env, runDurableObjectAlarm, runInDurableObject } from 'cloudflare:test';
import { describe, expect, it } from 'vitest';
import { Close, PING, PONG, deviceLabel, randomId, sha256Base64Url } from '../src/protocol';

const IPHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15';
const ANDROID_UA = 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/140.0 Mobile';

interface Peer {
  ws: WebSocket;
  next(): Promise<Record<string, unknown>>;
  closed: Promise<number>;
  send(payload: unknown): void;
}

function key(): string {
  // 32 random bytes, base64url (43 chars) — same shape as the PC's keys.
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function open(path: string, headers: Record<string, string> = {}, base = 'https://relay.test'): Promise<Peer> {
  const response = await SELF.fetch(`${base}${path}`, { headers: { Upgrade: 'websocket', ...headers } });
  expect(response.status).toBe(101);
  const ws = response.webSocket!;
  ws.accept();

  const inbox: Record<string, unknown>[] = [];
  const waiters: ((m: Record<string, unknown>) => void)[] = [];
  ws.addEventListener('message', (event) => {
    const msg = JSON.parse(event.data as string) as Record<string, unknown>;
    const waiter = waiters.shift();
    if (waiter) waiter(msg);
    else inbox.push(msg);
  });
  const closed = new Promise<number>((resolve) => ws.addEventListener('close', (event) => resolve(event.code)));

  return {
    ws,
    closed,
    send: (payload) => ws.send(typeof payload === 'string' ? payload : JSON.stringify(payload)),
    next: () =>
      new Promise((resolve, reject) => {
        const queued = inbox.shift();
        if (queued) return resolve(queued);
        const timer = setTimeout(() => reject(new Error('no message')), 2000);
        waiters.push((m) => {
          clearTimeout(timer);
          resolve(m);
        });
      }),
  };
}

async function newRoom() {
  const room = randomId();
  const hostKey = key();
  const phoneToken = key();
  return { room, hostKey, phoneToken, phoneAuthHash: await sha256Base64Url(phoneToken) };
}

type Room = Awaited<ReturnType<typeof newRoom>>;

async function host(r: Room, hostKey = r.hostKey): Promise<Peer> {
  const peer = await open(`/ws/host?room=${r.room}`);
  peer.send({ t: 'claim', v: 1, hostKey, phoneAuthHash: r.phoneAuthHash });
  return peer;
}

async function phone(r: Room, client = 'client-aaaa', ua = IPHONE_UA, token = r.phoneToken): Promise<Peer> {
  const peer = await open(`/ws/phone?room=${r.room}`, { 'User-Agent': ua });
  peer.send({ t: 'auth', token, client });
  return peer;
}

async function connected(r: Room) {
  const pc = await host(r);
  expect(await pc.next()).toEqual({ t: 'ready' });
  const p = await phone(r);
  expect(await p.next()).toEqual({ t: 'host', online: true });
  const join = await pc.next();
  expect(join).toMatchObject({ t: 'phone', event: 'join', label: 'iPhone' });
  return { pc, p, pid: join.pid as string };
}

const IV = 'AAAAAAAAAAAAAAAA';
const CT = 'Y2lwaGVydGV4dC1nb2VzLWhlcmUtYW5kLXRhZw';

describe('routing', () => {
  it('serves the marketing page and the phone remote with security headers', async () => {
    const site = await SELF.fetch('https://relay.test/');
    expect(site.status).toBe(200);
    expect(await site.text()).toContain('Download for Windows');
    expect(site.headers.get('Content-Security-Policy')).toContain("default-src 'none'");

    const remote = await SELF.fetch('https://relay.test/remote');
    expect(remote.status).toBe(200);
    expect(await remote.text()).toContain('data-command="presentation.next"');
    expect(remote.headers.get('Content-Security-Policy')).toContain("default-src 'none'");
    expect(remote.headers.get('X-Frame-Options')).toBe('DENY');
    expect(remote.headers.get('Cache-Control')).toBe('no-store');
  });

  it('keeps every page out of search engines', async () => {
    for (const path of ['/', '/remote', '/app.js', '/download', '/nope']) {
      const response = await SELF.fetch(`https://relay.test${path}`, { redirect: 'manual' });
      expect(response.headers.get('X-Robots-Tag'), path).toBe('noindex, nofollow');
    }
    const redirect = await SELF.fetch('https://rotmanav.ca/clicker/', { redirect: 'manual' });
    expect(redirect.headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
    for (const path of ['/', '/remote']) {
      expect(await (await SELF.fetch(`https://relay.test${path}`)).text(), path).toContain('<meta name="robots" content="noindex, nofollow">');
    }
  });

  it('serves only whitelisted files', async () => {
    const files: [string, string][] = [['/app.js', 'text/javascript'], ['/styles.css', 'text/css'], ['/site.css', 'text/css'], ['/site.js', 'text/javascript']];
    for (const [path, type] of files) {
      const response = await SELF.fetch(`https://relay.test${path}`);
      expect(response.status).toBe(200);
      expect(response.headers.get('Content-Type')).toContain(type);
    }
    for (const path of ['/app.js.txt', '/index.html', '/src/room.ts', '/wrangler.jsonc', '/..%2fwrangler.jsonc', '/APP.JS', '/clickerx/remote']) {
      expect((await SELF.fetch(`https://relay.test${path}`)).status).toBe(404);
    }
    expect((await SELF.fetch('https://relay.test/', { method: 'POST' })).status).toBe(405);
  });

  it('redirects Download to the latest release', async () => {
    const response = await SELF.fetch('https://relay.test/download', { redirect: 'manual' });
    expect(response.status).toBe(302);
    expect(response.headers.get('Location')).toBe('https://github.com/camster91/Rclicker/releases/latest/download/rclicker.exe');
  });

  it('redirects pages on the old addresses to clicker.rotmanav.ca', async () => {
    const cases: [string, string][] = [
      ['https://rotmanav.ca/clicker', 'https://clicker.rotmanav.ca/'],
      ['https://rotmanav.ca/clicker/', 'https://clicker.rotmanav.ca/'],
      ['https://www.rotmanav.ca/clicker/remote', 'https://clicker.rotmanav.ca/remote'],
      ['https://rotmanav.ca/clicker/download', 'https://clicker.rotmanav.ca/download'],
      ['https://rotmanav.ca/clicker/remote?x=1', 'https://clicker.rotmanav.ca/remote?x=1'],
      ['https://rclicker.cameron-rotman.workers.dev/', 'https://clicker.rotmanav.ca/'],
      ['https://rclicker.cameron-rotman.workers.dev/remote', 'https://clicker.rotmanav.ca/remote'],
    ];
    for (const [from, to] of cases) {
      const response = await SELF.fetch(from, { redirect: 'manual' });
      expect(response.status, from).toBe(301);
      expect(response.headers.get('Location'), from).toBe(to);
      // rotmanav.ca's HSTS policy is the main site's call, not rclicker's.
      expect(response.headers.get('Strict-Transport-Security'), from).toBeNull();
    }
    expect((await SELF.fetch('https://rotmanav.ca/clickerx', { redirect: 'manual' })).status).toBe(404);
    expect((await SELF.fetch('https://rotmanav.ca/', { redirect: 'manual' })).status).toBe(404);
  });

  it('keeps the old addresses\' sockets working for installed apps', async () => {
    for (const [base, mount] of [['https://rotmanav.ca', '/clicker'], ['https://rclicker.cameron-rotman.workers.dev', '']]) {
      const r = await newRoom();
      const pc = await open(`${mount}/ws/host?room=${r.room}`, {}, base);
      pc.send({ t: 'claim', v: 1, hostKey: r.hostKey, phoneAuthHash: r.phoneAuthHash });
      expect(await pc.next()).toEqual({ t: 'ready' });
      // The phone page now lives on clicker.rotmanav.ca; same room, same relay.
      const p = await open(`/ws/phone?room=${r.room}`, { 'User-Agent': IPHONE_UA, Origin: 'https://clicker.rotmanav.ca' }, 'https://clicker.rotmanav.ca');
      p.send({ t: 'auth', token: r.phoneToken, client: 'client-legacy' });
      expect(await p.next()).toEqual({ t: 'host', online: true });
      expect(await pc.next()).toMatchObject({ t: 'phone', event: 'join', label: 'iPhone' });
    }
  });

  it('serves the site at the root of clicker.rotmanav.ca with HSTS', async () => {
    const site = await SELF.fetch('https://clicker.rotmanav.ca/');
    expect(site.status).toBe(200);
    expect(site.headers.get('Strict-Transport-Security')).toContain('max-age=');
    expect((await SELF.fetch('https://clicker.rotmanav.ca/clicker/remote')).status).toBe(404);
  });

  it('limits new connections per IP address', async () => {
    const headers = { Upgrade: 'websocket', 'CF-Connecting-IP': '203.0.113.7' };
    // A bad room id is refused after the limiter, so no rooms are created. The limiter's
    // window follows the wall clock and may roll over mid-loop, so allow up to two windows.
    const statuses: number[] = [];
    while (statuses.length < 121 && statuses.at(-1) !== 429) {
      statuses.push((await SELF.fetch('https://relay.test/ws/phone?room=x', { headers })).status);
    }
    expect(statuses.at(-1)).toBe(429);
    expect(statuses.length).toBeGreaterThanOrEqual(61);
    expect(statuses.slice(0, 60).every((s) => s === 400)).toBe(true);
    // Another address is unaffected.
    const other = await SELF.fetch('https://relay.test/ws/phone?room=x', { headers: { ...headers, 'CF-Connecting-IP': '203.0.113.8' } });
    expect(other.status).toBe(400);
  });

  it('rejects non-WebSocket requests, bad room ids and other origins', async () => {
    expect((await SELF.fetch('https://relay.test/ws/host?room=' + randomId())).status).toBe(426);
    const bad = await SELF.fetch('https://relay.test/ws/phone?room=../../x', { headers: { Upgrade: 'websocket' } });
    expect(bad.status).toBe(400);
    const cross = await SELF.fetch('https://relay.test/ws/phone?room=' + randomId(), {
      headers: { Upgrade: 'websocket', Origin: 'https://evil.example' },
    });
    expect(cross.status).toBe(403);
    const same = await SELF.fetch('https://relay.test/ws/phone?room=' + randomId(), {
      headers: { Upgrade: 'websocket', Origin: 'https://relay.test' },
    });
    expect(same.status).toBe(101);
  });
});

describe('pairing', () => {
  it('connects a PC and a phone and forwards encrypted payloads both ways', async () => {
    const r = await newRoom();
    const { pc, p, pid } = await connected(r);

    p.send({ t: 'msg', iv: IV, ct: CT });
    expect(await pc.next()).toEqual({ t: 'msg', pid, iv: IV, ct: CT });

    pc.send({ t: 'msg', pid, iv: IV, ct: CT });
    expect(await p.next()).toEqual({ t: 'msg', iv: IV, ct: CT });
  });

  it('refuses a phone with the wrong token', async () => {
    const r = await newRoom();
    const pc = await host(r);
    await pc.next();

    const p = await phone(r, 'client-aaaa', IPHONE_UA, key());
    expect(await p.closed).toBe(Close.SessionEnded);
  });

  it('refuses a phone when no PC ever claimed the room', async () => {
    const r = await newRoom();
    const p = await phone(r);
    expect(await p.closed).toBe(Close.SessionEnded);
  });

  it('refuses a different computer claiming an existing room', async () => {
    const r = await newRoom();
    const pc = await host(r);
    await pc.next();

    const intruder = await host(r, key());
    expect(await intruder.closed).toBe(Close.Forbidden);
  });

  it('refuses a malformed claim', async () => {
    const r = await newRoom();
    const pc = await open(`/ws/host?room=${r.room}`);
    pc.send({ t: 'claim', v: 1, hostKey: 'short', phoneAuthHash: r.phoneAuthHash });
    expect(await pc.closed).toBe(Close.BadRequest);
  });

  it('tells the phone when the PC is offline, and the PC can come back', async () => {
    const r = await newRoom();
    const { pc } = await connected(r);
    const p2 = await phone(r, 'client-aaaa'); // same browser reconnects
    await p2.next(); // host online

    pc.ws.close(1001, 'network drop');
    expect(await p2.next()).toEqual({ t: 'host', online: false });

    const back = await host(r);
    expect(await back.next()).toEqual({ t: 'ready' });
    expect(await back.next()).toMatchObject({ t: 'phone', event: 'join' });
    expect(await p2.next()).toEqual({ t: 'host', online: true });
  });

  it('answers keep-alive pings', async () => {
    const r = await newRoom();
    const { p } = await connected(r);
    p.send(PING);
    expect(JSON.stringify(await p.next())).toBe(PONG);
  });
});

describe('one phone at a time', () => {
  it('tells a second phone the remote is busy', async () => {
    const r = await newRoom();
    await connected(r);
    const other = await phone(r, 'client-bbbb', ANDROID_UA);
    expect(await other.closed).toBe(Close.Busy);
  });

  it('lets the same browser take over from its older tab', async () => {
    const r = await newRoom();
    const { pc, p, pid } = await connected(r);

    const tab2 = await phone(r, 'client-aaaa');
    expect(await p.closed).toBe(Close.Replaced);
    expect(await tab2.next()).toEqual({ t: 'host', online: true });
    expect(await pc.next()).toMatchObject({ t: 'phone', event: 'leave', pid, held: false });
    expect(await pc.next()).toMatchObject({ t: 'phone', event: 'join' });
  });

  it('holds the seat for a dropped phone, then releases it', async () => {
    const r = await newRoom();
    const { pc, p, pid } = await connected(r);

    p.ws.close(1001, 'screen locked');
    expect(await pc.next()).toMatchObject({ t: 'phone', event: 'leave', pid, held: true });

    const other = await phone(r, 'client-bbbb', ANDROID_UA);
    expect(await other.closed).toBe(Close.Busy);

    // Pretend the grace period is over, then run the room's alarm.
    const stub = env.ROOMS.get(env.ROOMS.idFromName(r.room));
    await runInDurableObject(stub, async (_instance, state) => {
      const seat = await state.storage.get<{ until: number }>('seat');
      await state.storage.put('seat', { ...seat, until: Date.now() - 1 });
    });
    await runDurableObjectAlarm(stub);
    expect(await pc.next()).toMatchObject({ t: 'phone', event: 'released', pid });

    const now = await phone(r, 'client-bbbb', ANDROID_UA);
    expect(await now.next()).toEqual({ t: 'host', online: true });
    expect(await pc.next()).toMatchObject({ t: 'phone', event: 'join', label: 'Android' });
  });

  it('lets the dropped phone come back during the grace period', async () => {
    const r = await newRoom();
    const { pc, p } = await connected(r);
    p.ws.close(1001, 'screen locked');
    await pc.next();

    const back = await phone(r, 'client-aaaa');
    expect(await back.next()).toEqual({ t: 'host', online: true });
  });
});

describe('ending a session', () => {
  it('New session: phone is told the session ended and the old QR stops working', async () => {
    const r = await newRoom();
    const { pc, p } = await connected(r);

    pc.send({ t: 'end', reason: 'regenerated' });
    expect(await p.closed).toBe(Close.SessionEnded);

    const again = await phone(r);
    expect(await again.closed).toBe(Close.SessionEnded);
    const reclaim = await host(r);
    expect(await reclaim.closed).toBe(Close.SessionEnded);
  });

  it('Quit: phone is told the receiver closed', async () => {
    const r = await newRoom();
    const { pc, p } = await connected(r);

    pc.send({ t: 'end', reason: 'shutdown' });
    expect(await p.closed).toBe(Close.ReceiverClosed);
  });
});

describe('hostile input', () => {
  it('ignores malformed and unexpected messages without forwarding them', async () => {
    const r = await newRoom();
    const { pc, p, pid } = await connected(r);

    p.send('not json');
    p.send({ t: 'msg', iv: 'bad', ct: CT });
    p.send({ t: 'msg', iv: IV, ct: '<script>' });
    p.send({ t: 'press-key', key: 'F4' });
    p.send({ t: 'end', reason: 'shutdown' }); // phones cannot end sessions
    pc.send({ t: 'msg', pid: 'nobody-nobody-nobody-x', iv: IV, ct: CT });
    p.send({ t: 'msg', iv: IV, ct: CT });

    expect(await pc.next()).toEqual({ t: 'msg', pid, iv: IV, ct: CT }); // only the valid one arrived
  });

  it('drops oversized messages and floods', async () => {
    const r = await newRoom();
    const { p } = await connected(r);
    p.send('x'.repeat(5000));
    expect(await p.closed).toBe(1009);

    const r2 = await newRoom();
    const c2 = await connected(r2);
    for (let i = 0; i < 40; i++) c2.p.send({ t: 'msg', iv: IV, ct: CT });
    expect(await c2.p.closed).toBe(1008);
  });

  it('closes sockets that never authenticate', async () => {
    const r = await newRoom();
    const lurker = await open(`/ws/phone?room=${r.room}`);
    const stub = env.ROOMS.get(env.ROOMS.idFromName(r.room));
    await runInDurableObject(stub, async (_instance, state) => {
      for (const ws of state.getWebSockets()) {
        ws.serializeAttachment({ ...(ws.deserializeAttachment() as object), connectedAt: 0 });
      }
    });
    await runDurableObjectAlarm(stub);
    expect(await lurker.closed).toBe(1008);
  });
});

describe('helpers', () => {
  it('labels devices', () => {
    expect(deviceLabel(IPHONE_UA)).toBe('iPhone');
    expect(deviceLabel(ANDROID_UA)).toBe('Android');
    expect(deviceLabel(null)).toBe('phone');
  });
});

describe('phone-side crypto (same algorithm as public/app.js)', () => {
  // Independent vectors (Python hmac/hashlib/cryptography); the C# tests use the same ones.
  const KEY = 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8';
  const enc = new TextEncoder();
  const b64 = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  const unb64 = (text: string) => Uint8Array.from(atob(text.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (text.length % 4)) % 4)), (c) => c.charCodeAt(0));

  it('derives room, phone token and decrypts with WebCrypto', async () => {
    const hmac = await crypto.subtle.importKey('raw', unb64(KEY), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
    const mac = async (label: string) => new Uint8Array(await crypto.subtle.sign('HMAC', hmac, enc.encode(label)));
    const room = b64((await mac('rclicker/v1/room')).slice(0, 16));
    const phone = b64(await mac('rclicker/v1/phone'));

    expect(room).toBe('bncOdSXPsr83gBpAiVfdXA');
    expect(phone).toBe('tF7RBRIVqAILOAVAhmjK6Wb-tazsqrv3GOizNQKYTdI');
    expect(await sha256Base64Url(phone)).toBe('ShJRZDxA8UMVn7WtNFVrxvMlfFXszqkKxXsz78HldQ4');

    const aes = await crypto.subtle.importKey('raw', await mac('rclicker/v1/enc'), { name: 'AES-GCM' }, false, ['decrypt']);
    const plain = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: unb64('AAECAwQFBgcICQoL'), additionalData: enc.encode('rclicker/v1/p2h/' + room) },
      aes,
      unb64('rolBs-nc5q9ZPh-mYywM-5RegNXM-2vbBW7a5IxQsc6bIh8gL6dZxVZ1bktYSWGH3tvZeiLwcYYnttyPLp2w'),
    );
    expect(new TextDecoder().decode(plain)).toBe('{"type":"presentation.next","id":1,"nonce":"n"}');
  });
});
