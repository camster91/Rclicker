// Execute the shipping phone script with a minimal DOM and socket transport.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { webcrypto } = require('node:crypto');
const { runInNewContext } = require('node:vm');

test('phone shows disconnect, disables taps, and accepts a fresh encrypted hello', async () => {
  const element = () => ({
    textContent: '', hidden: true, attributes: {}, listeners: {},
    classList: { toggle() {}, add() {}, remove() {} },
    setAttribute(name, value) { this.attributes[name] = value; },
    getAttribute(name) { return this.attributes[name]; },
    addEventListener(name, callback) { this.listeners[name] = callback; },
    focus() {}
  });
  const ids = Object.fromEntries(['status', 'status-text', 'message', 'awake', 'overlay',
    'overlay-title', 'overlay-text', 'overlay-retry'].map((id) => [id, element()]));
  const button = element();
  button.attributes['data-command'] = 'presentation.next';
  let socket;
  let connected;
  const constructed = new Promise((resolve) => { connected = resolve; });
  class Socket {
    static OPEN = 1; static CLOSED = 3; static CLOSING = 2; static CONNECTING = 0;
    readyState = 1;
    sent = [];
    constructor() { socket = this; connected(); }
    send(message) { this.sent.push(message); }
    close() { this.readyState = 3; }
    receive(message) { this.onmessage({ data: JSON.stringify(message) }); }
  }
  const key = Buffer.alloc(32, 7);
  const timers = new Map();
  let timerId = 0;
  const schedule = (callback) => { timers.set(++timerId, callback); return timerId; };
  runInNewContext(readFileSync(require('node:path').join(__dirname, '../public/app.js.txt'), 'utf8'), {
    window: {
      crypto: webcrypto,
      localStorage: { getItem: () => 'test-client-aaaa', setItem() {} },
      location: { hash: '#k=' + key.toString('base64url'), protocol: 'https:', host: 'relay.test', pathname: '/remote' },
      addEventListener() {}
    },
    document: {
      visibilityState: 'visible', getElementById: (id) => ids[id],
      querySelectorAll: () => [button], addEventListener() {}
    },
    navigator: {}, WebSocket: Socket, TextEncoder, TextDecoder, URLSearchParams,
    Uint8Array, atob, btoa,
    setTimeout: schedule, setInterval: schedule,
    clearTimeout: (id) => timers.delete(id), clearInterval: (id) => timers.delete(id)
  });
  await constructed;
  socket.onopen();
  socket.receive({ t: 'host', online: false });
  assert.equal(ids['status-text'].textContent, 'Waiting for computer…');
  socket.receive({ t: 'host', online: false, disconnected: true });
  assert.equal(ids['status-text'].textContent, 'Computer disconnected');
  assert.equal(button.attributes['aria-disabled'], 'true');
  const sent = socket.sent.length;
  button.listeners.click();
  assert.equal(socket.sent.length, sent, 'a disabled tap sends no command');
  assert.equal(ids.overlay.hidden, true, 'a disconnect notice does not end the session');

  // Relay online alone does not enable commands: a fresh encrypted hello must arrive.
  socket.receive({ t: 'host', online: true });
  assert.equal(button.attributes['aria-disabled'], 'true');
  const hmac = await webcrypto.subtle.importKey('raw', key, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const mac = async (label) => Buffer.from(await webcrypto.subtle.sign('HMAC', hmac, new TextEncoder().encode(label)));
  const room = (await mac('rclicker/v1/room')).subarray(0, 16).toString('base64url');
  const aes = await webcrypto.subtle.importKey('raw', await mac('rclicker/v1/enc'), 'AES-GCM', false, ['encrypt']);
  const iv = Buffer.alloc(12, 3);
  const ciphertext = await webcrypto.subtle.encrypt({ name: 'AES-GCM', iv,
    additionalData: new TextEncoder().encode('rclicker/v1/h2p/' + room) }, aes,
    new TextEncoder().encode(JSON.stringify({ type: 'hello', nonce: 'fresh-session-nonce' })));
  socket.receive({ t: 'msg', iv: iv.toString('base64url'), ct: Buffer.from(ciphertext).toString('base64url') });
  const deadline = Date.now() + 2000;
  while (Date.now() < deadline && ids['status-text'].textContent !== 'Connected') {
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
  assert.equal(ids['status-text'].textContent, 'Connected');
  assert.equal(button.attributes['aria-disabled'], 'false');
});
