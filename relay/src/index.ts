import { SECURITY_HEADERS, serveAsset } from './assets';
export { Room } from './room';

const ROOM_ID = /^[A-Za-z0-9_-]{22}$/;

/** rclicker's own origin, so no other site's pages share its cookies, storage or scripts. */
const HOME = 'https://clicker.rotmanav.ca';

/**
 * Older addresses, with the path the relay was mounted under. Their sockets keep working
 * for apps already installed (0.2.0 used workers.dev, 0.2.1 rotmanav.ca/clicker); every
 * page there redirects to HOME, keeping the path, query and #k= fragment.
 */
const LEGACY_HOSTS = new Map([
  ['rotmanav.ca', '/clicker'],
  ['www.rotmanav.ca', '/clicker'],
  ['rclicker.cameron-rotman.workers.dev', ''],
]);

const SOCKETS = new Set(['/ws/host', '/ws/phone']);

// Redirects from other sites' origins carry no HSTS: that's the main site's own decision.
const LEGACY_HEADERS: Record<string, string> = Object.fromEntries(
  Object.entries(SECURITY_HEADERS).filter(([name]) => name !== 'Strict-Transport-Security'),
);

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);
    let path = url.pathname;

    const mount = LEGACY_HOSTS.get(url.hostname);
    if (mount !== undefined) {
      if (path !== mount && !path.startsWith(`${mount}/`)) return notFound();
      path = path.slice(mount.length) || '/';
      if (!SOCKETS.has(path)) {
        return new Response(null, { status: 301, headers: { Location: `${HOME}${path}${url.search}`, ...LEGACY_HEADERS } });
      }
    }

    if (!SOCKETS.has(path)) return serveAsset(request, path);

    if (request.method !== 'GET' || request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') {
      return new Response('Expected a WebSocket upgrade.', { status: 426 });
    }

    // Per-IP cap on new connections (Cloudflare always sets CF-Connecting-IP in production).
    const ip = request.headers.get('CF-Connecting-IP');
    if (ip !== null && !(await env.CONNECT_LIMITER.limit({ key: ip })).success) {
      return new Response('Too many connections. Try again in a minute.', { status: 429, headers: { 'Retry-After': '60' } });
    }

    const origin = request.headers.get('Origin');
    if (origin !== null && origin !== url.origin) return new Response('Forbidden.', { status: 403 });

    const room = url.searchParams.get('room') ?? '';
    if (!ROOM_ID.test(room)) return new Response('Bad room id.', { status: 400 });

    const inner = new URL(request.url);
    inner.pathname = path;
    const stub = env.ROOMS.get(env.ROOMS.idFromName(room));
    return stub.fetch(new Request(inner, request));
  },
} satisfies ExportedHandler<Env>;

function notFound(): Response {
  return new Response('Not found.', { status: 404, headers: { 'Cache-Control': 'no-store', ...LEGACY_HEADERS } });
}
