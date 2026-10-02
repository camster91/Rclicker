import { DurableObject } from 'cloudflare:workers';
import {
  AUTH_TIMEOUT_MS,
  CLIENT_ID,
  Close,
  HOST_GONE_MS,
  MAX_MESSAGES_PER_SECOND,
  MAX_MESSAGE_CHARS,
  MAX_ROOM_MS,
  PING,
  PONG,
  SEAT_GRACE_MS,
  TOMBSTONE_MS,
  deviceLabel,
  isCiphertext,
  isIv,
  isKey,
  isPid,
  randomId,
  safeEqual,
  sha256Base64Url,
} from './protocol';

type Role = 'pending-host' | 'pending-phone' | 'host' | 'phone' | 'closed';

/** Per-socket state; survives hibernation via serializeAttachment. */
interface Attachment {
  role: Role;
  connectedAt: number;
  label: string;
  pid?: string;
  client?: string;
  windowStart?: number;
  count?: number;
}

interface RoomInfo {
  /** SHA-256 of the PC's private host key: only that PC can claim the room again. */
  hostKeyHash: string;
  /** SHA-256 of the phone token derived from the QR key. The relay never sees the QR key. */
  phoneAuthHash: string;
  createdAt: number;
  hostLeftAt?: number;
  endedAt?: number;
}

/** A dropped phone's reserved seat. */
interface Seat {
  client: string;
  pid: string;
  label: string;
  until: number;
}

type Message = Record<string, unknown> & { t: string };

/**
 * One room = one PC and at most one phone.
 *
 * Messages (JSON text frames):
 *   PC    → relay  {t:"claim", v:1, hostKey, phoneAuthHash} first, then {t:"msg", pid, iv, ct} | {t:"end", reason}
 *   relay → PC     {t:"ready"} | {t:"phone", event:"join"|"leave"|"released", pid, label, held?} | {t:"msg", pid, iv, ct}
 *   phone → relay  {t:"auth", token, client} first, then {t:"msg", iv, ct}
 *   relay → phone  {t:"host", online} | {t:"msg", iv, ct}
 *   either         {"t":"ping"} → {"t":"pong"} (answered by the runtime)
 * iv/ct are AES-GCM payloads the relay cannot read.
 */
export class Room extends DurableObject<Env> {
  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair(PING, PONG));
  }

  override async fetch(request: Request): Promise<Response> {
    const role: Role = new URL(request.url).pathname === '/ws/host' ? 'pending-host' : 'pending-phone';
    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.ctx.acceptWebSocket(server);
    const attachment: Attachment = {
      role,
      connectedAt: Date.now(),
      label: deviceLabel(request.headers.get('User-Agent')),
    };
    server.serializeAttachment(attachment);
    await this.reschedule();
    return new Response(null, { status: 101, webSocket: client });
  }

  override async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
    const att = attachmentOf(ws);
    if (att.role === 'closed') return;

    if (typeof message !== 'string' || message.length > MAX_MESSAGE_CHARS) {
      this.closeSocket(ws, 1009, 'message too large');
      return;
    }

    if (!withinRate(ws, att)) {
      this.closeSocket(ws, 1008, 'too many messages');
      return;
    }

    let msg: Message;
    try {
      const parsed: unknown = JSON.parse(message);
      if (!parsed || typeof parsed !== 'object' || typeof (parsed as Message).t !== 'string') return;
      msg = parsed as Message;
    } catch {
      return; // Ignore malformed JSON.
    }

    switch (att.role) {
      case 'pending-host':
        await this.onClaim(ws, att, msg);
        break;
      case 'pending-phone':
        await this.onAuth(ws, att, msg);
        break;
      case 'host':
        await this.onHostMessage(ws, msg);
        break;
      case 'phone':
        this.onPhoneMessage(att, ws, msg);
        break;
    }
  }

  override async webSocketClose(ws: WebSocket): Promise<void> {
    await this.onSocketGone(ws);
  }

  override async webSocketError(ws: WebSocket): Promise<void> {
    await this.onSocketGone(ws);
  }

  override async alarm(): Promise<void> {
    const now = Date.now();

    for (const ws of this.ctx.getWebSockets()) {
      const att = attachmentOf(ws);
      if ((att.role === 'pending-host' || att.role === 'pending-phone') && now - att.connectedAt >= AUTH_TIMEOUT_MS) {
        this.closeSocket(ws, 1008, 'authentication timeout');
      }
    }

    const seat = await this.ctx.storage.get<Seat>('seat');
    if (seat && seat.until <= now) {
      await this.ctx.storage.delete('seat');
      this.sendToHost({ t: 'phone', event: 'released', pid: seat.pid, label: seat.label });
    }

    const info = await this.ctx.storage.get<RoomInfo>('info');
    if (info) {
      if (info.endedAt !== undefined) {
        if (now - info.endedAt >= TOMBSTONE_MS) {
          await this.ctx.storage.deleteAll();
        }
      } else if (now - info.createdAt >= MAX_ROOM_MS) {
        await this.endRoom(info, Close.SessionEnded, 'session expired');
      } else if (info.hostLeftAt !== undefined && this.sockets('host').length === 0 && now - info.hostLeftAt >= HOST_GONE_MS) {
        await this.endRoom(info, Close.ReceiverClosed, 'computer offline');
      }
    }

    await this.reschedule();
  }

  // ---- PC ----

  private async onClaim(ws: WebSocket, att: Attachment, msg: Message): Promise<void> {
    if (msg.t !== 'claim' || msg.v !== 1 || !isKey(msg.hostKey) || !isKey(msg.phoneAuthHash)) {
      this.closeSocket(ws, Close.BadRequest, 'expected claim');
      return;
    }

    const hostKeyHash = await sha256Base64Url(msg.hostKey);
    let info = await this.ctx.storage.get<RoomInfo>('info');
    if (info?.endedAt !== undefined) {
      this.closeSocket(ws, Close.SessionEnded, 'session ended');
      return;
    }

    if (info && !safeEqual(info.hostKeyHash, hostKeyHash)) {
      this.closeSocket(ws, Close.Forbidden, 'room belongs to another computer');
      return;
    }

    info ??= { hostKeyHash, phoneAuthHash: msg.phoneAuthHash, createdAt: Date.now() };
    delete info.hostLeftAt;
    await this.ctx.storage.put('info', info);

    for (const other of this.sockets('host')) {
      if (other !== ws) this.closeSocket(other, Close.Replaced, 'replaced');
    }

    att.role = 'host';
    ws.serializeAttachment(att);
    send(ws, { t: 'ready' });

    for (const phone of this.sockets('phone')) {
      const p = attachmentOf(phone);
      send(ws, { t: 'phone', event: 'join', pid: p.pid, label: p.label });
    }

    const seat = await this.ctx.storage.get<Seat>('seat');
    if (seat && seat.until > Date.now()) {
      send(ws, { t: 'phone', event: 'leave', pid: seat.pid, label: seat.label, held: true });
    }

    this.sendToPhones({ t: 'host', online: true });
    await this.reschedule();
  }

  private async onHostMessage(ws: WebSocket, msg: Message): Promise<void> {
    if (msg.t === 'msg') {
      if (!isPid(msg.pid) || !isIv(msg.iv) || !isCiphertext(msg.ct)) return;
      const phone = this.sockets('phone').find((p) => attachmentOf(p).pid === msg.pid);
      if (phone) send(phone, { t: 'msg', iv: msg.iv, ct: msg.ct });
      return;
    }

    if (msg.t === 'end') {
      const info = await this.ctx.storage.get<RoomInfo>('info');
      const code = msg.reason === 'shutdown' ? Close.ReceiverClosed : Close.SessionEnded;
      if (info) await this.endRoom(info, code, msg.reason === 'shutdown' ? 'receiver closed' : 'session ended');
      this.closeSocket(ws, 1000, 'ended');
      await this.reschedule();
    }
  }

  // ---- phone ----

  private async onAuth(ws: WebSocket, att: Attachment, msg: Message): Promise<void> {
    if (msg.t !== 'auth' || !isKey(msg.token)) {
      this.closeSocket(ws, Close.SessionEnded, 'not authorised');
      return;
    }

    const info = await this.ctx.storage.get<RoomInfo>('info');
    if (!info || info.endedAt !== undefined || !safeEqual(await sha256Base64Url(msg.token), info.phoneAuthHash)) {
      this.closeSocket(ws, Close.SessionEnded, 'session ended');
      return;
    }

    const client = typeof msg.client === 'string' && CLIENT_ID.test(msg.client) ? msg.client : `anon-${randomId()}`;
    const active = this.sockets('phone');
    if (active.some((p) => attachmentOf(p).client !== client)) {
      this.closeSocket(ws, Close.Busy, 'busy');
      return;
    }

    const seat = await this.ctx.storage.get<Seat>('seat');
    if (seat && seat.until > Date.now() && seat.client !== client) {
      this.closeSocket(ws, Close.Busy, 'busy');
      return;
    }

    // Same browser again (new tab, or reconnecting before the old socket timed out).
    for (const old of active) {
      this.closeSocket(old, Close.Replaced, 'replaced');
      this.sendToHost({ t: 'phone', event: 'leave', pid: attachmentOf(old).pid, label: att.label, held: false });
    }

    att.role = 'phone';
    att.pid = randomId();
    att.client = client;
    ws.serializeAttachment(att);
    await this.ctx.storage.delete('seat');

    send(ws, { t: 'host', online: this.sockets('host').length > 0 });
    this.sendToHost({ t: 'phone', event: 'join', pid: att.pid, label: att.label });
    await this.reschedule();
  }

  private onPhoneMessage(att: Attachment, ws: WebSocket, msg: Message): void {
    if (msg.t !== 'msg' || !isIv(msg.iv) || !isCiphertext(msg.ct)) return;
    const host = this.sockets('host')[0];
    if (!host) {
      send(ws, { t: 'host', online: false });
      return;
    }

    send(host, { t: 'msg', pid: att.pid, iv: msg.iv, ct: msg.ct });
  }

  // ---- lifecycle ----

  private async onSocketGone(ws: WebSocket): Promise<void> {
    const att = attachmentOf(ws);
    const role = att.role;
    att.role = 'closed';
    ws.serializeAttachment(att);
    try {
      ws.close(1000, 'bye');
    } catch {
      // Already closed.
    }

    if (role === 'host' && this.sockets('host').length === 0) {
      const info = await this.ctx.storage.get<RoomInfo>('info');
      if (info && info.endedAt === undefined) {
        info.hostLeftAt = Date.now();
        await this.ctx.storage.put('info', info);
        this.sendToPhones({ t: 'host', online: false });
      }
    } else if (role === 'phone' && att.pid && att.client) {
      const seat: Seat = { client: att.client, pid: att.pid, label: att.label, until: Date.now() + SEAT_GRACE_MS };
      if (this.sockets('phone').length === 0) {
        await this.ctx.storage.put('seat', seat);
      }

      this.sendToHost({ t: 'phone', event: 'leave', pid: att.pid, label: att.label, held: this.sockets('phone').length === 0 });
    }

    await this.reschedule();
  }

  private async endRoom(info: RoomInfo, code: number, reason: string): Promise<void> {
    for (const phone of this.sockets('phone')) this.closeSocket(phone, code, reason);
    for (const pending of this.sockets('pending-phone')) this.closeSocket(pending, code, reason);
    info.endedAt = Date.now();
    await this.ctx.storage.put('info', info);
    await this.ctx.storage.delete('seat');
  }

  /** One alarm drives every deadline: auth timeouts, seat expiry, room expiry, cleanup. */
  private async reschedule(): Promise<void> {
    const deadlines: number[] = [];
    for (const ws of this.ctx.getWebSockets()) {
      const att = attachmentOf(ws);
      if (att.role === 'pending-host' || att.role === 'pending-phone') deadlines.push(att.connectedAt + AUTH_TIMEOUT_MS);
    }

    const seat = await this.ctx.storage.get<Seat>('seat');
    if (seat) deadlines.push(seat.until);

    const info = await this.ctx.storage.get<RoomInfo>('info');
    if (info) {
      if (info.endedAt !== undefined) {
        deadlines.push(info.endedAt + TOMBSTONE_MS);
      } else {
        deadlines.push(info.createdAt + MAX_ROOM_MS);
        if (info.hostLeftAt !== undefined) deadlines.push(info.hostLeftAt + HOST_GONE_MS);
      }
    }

    if (deadlines.length === 0) {
      await this.ctx.storage.deleteAlarm();
    } else {
      await this.ctx.storage.setAlarm(Math.min(...deadlines));
    }
  }

  private sockets(role: Role): WebSocket[] {
    return this.ctx.getWebSockets().filter((ws) => ws.readyState === 1 && attachmentOf(ws).role === role);
  }

  private sendToHost(payload: object): void {
    for (const host of this.sockets('host')) send(host, payload);
  }

  private sendToPhones(payload: object): void {
    for (const phone of this.sockets('phone')) send(phone, payload);
  }

  private closeSocket(ws: WebSocket, code: number, reason: string): void {
    const att = attachmentOf(ws);
    att.role = 'closed';
    ws.serializeAttachment(att);
    try {
      ws.close(code, reason);
    } catch {
      // Already closed.
    }
  }
}

function attachmentOf(ws: WebSocket): Attachment {
  return (ws.deserializeAttachment() as Attachment | null) ?? { role: 'closed', connectedAt: 0, label: 'phone' };
}

function withinRate(ws: WebSocket, att: Attachment): boolean {
  const now = Date.now();
  if (att.windowStart === undefined || now - att.windowStart >= 1000) {
    att.windowStart = now;
    att.count = 0;
  }

  att.count = (att.count ?? 0) + 1;
  ws.serializeAttachment(att);
  return att.count <= MAX_MESSAGES_PER_SECOND;
}

function send(ws: WebSocket, payload: object): void {
  try {
    ws.send(JSON.stringify(payload));
  } catch {
    // Socket went away; its close handler cleans up.
  }
}
