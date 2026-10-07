// Shared constants and small helpers for the relay.

/** WebSocket close codes. The phone page and the PC app react to these. */
export const Close = {
  /** Bad or unexpected first message. */
  BadRequest: 4400,
  /** Session regenerated, expired, or the key is wrong. Do not reconnect; scan the new QR code. */
  SessionEnded: 4401,
  /** A different computer already owns this room. */
  Forbidden: 4403,
  /** The same browser opened the remote again; this older connection was replaced. */
  Replaced: 4408,
  /** Another phone is already the controller. */
  Busy: 4409,
  /** The PC app was closed (or has been offline too long). */
  ReceiverClosed: 4410,
} as const;

/** Exact text of the keep-alive ping; the runtime answers it without waking the Durable Object. */
export const PING = '{"t":"ping"}';
export const PONG = '{"t":"pong"}';

export const MAX_MESSAGE_CHARS = 2048;
export const MAX_MESSAGES_PER_SECOND = 20;
/** A new socket must authenticate within this time. */
export const AUTH_TIMEOUT_MS = 10_000;
/** A phone that drops keeps its seat this long (screen lock, network blip). */
export const SEAT_GRACE_MS = 30_000;
/** Show a definite disconnect after a short blip, without preventing PC reclaim. */
export const HOST_NOTIFY_MS = 10_000;
/** If the PC stays offline this long, the room ends. */
export const HOST_GONE_MS = 2 * 60 * 60 * 1000;
/** Hard limit on a room's life, matching the PC's 12 h session lifetime with margin. */
export const MAX_ROOM_MS = 24 * 60 * 60 * 1000;
/** How long an ended room is remembered (so old QR codes say "ended" instead of hanging). */
export const TOMBSTONE_MS = 24 * 60 * 60 * 1000;

const B64URL = /^[A-Za-z0-9_-]+$/;
export const CLIENT_ID = /^[A-Za-z0-9_-]{8,64}$/;

/** 32 bytes as unpadded base64url. */
export function isKey(value: unknown): value is string {
  return typeof value === 'string' && value.length === 43 && B64URL.test(value);
}

/** 12-byte AES-GCM nonce as unpadded base64url. */
export function isIv(value: unknown): value is string {
  return typeof value === 'string' && value.length === 16 && B64URL.test(value);
}

/** Ciphertext + tag as base64url. Real messages are ~200 chars. */
export function isCiphertext(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 22 && value.length <= 1400 && B64URL.test(value);
}

export function isPid(value: unknown): value is string {
  return typeof value === 'string' && value.length === 22 && B64URL.test(value);
}

export function base64Url(bytes: Uint8Array): string {
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function randomId(): string {
  return base64Url(crypto.getRandomValues(new Uint8Array(16)));
}

export async function sha256Base64Url(text: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return base64Url(new Uint8Array(digest));
}

/** Length-safe constant-time string comparison. */
export function safeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

/** Friendly device name for the PC's status line. */
export function deviceLabel(userAgent: string | null): string {
  const ua = userAgent ?? '';
  if (/iPhone/i.test(ua)) return 'iPhone';
  if (/iPad/i.test(ua)) return 'iPad';
  if (/Android/i.test(ua)) return 'Android';
  if (/Windows/i.test(ua)) return 'Windows browser';
  if (/Macintosh/i.test(ua)) return 'Mac or iPad';
  return ua ? 'browser' : 'phone';
}
