import { SECURITY_HEADERS, serveAsset } from './assets';
import { serveVendor } from './vendor';
export { Room } from './room';

const ROOM_ID = /^[A-Za-z0-9_-]{22}$/;

/**
 * Addresses come from the Worker's settings, not from this repository:
 *   HOME_ORIGIN   rclicker's own origin (e.g. https://clicker.example.com), so no other
 *                 site's pages share its cookies, storage or scripts.
 *   LEGACY_HOSTS  JSON object of older addresses → the path the relay was mounted under,
 *                 e.g. {"example.org": "/clicker", "rclicker.example.workers.dev": ""}.
 *                 Their sockets keep working for apps already installed; every page there
 *                 redirects to HOME_ORIGIN, keeping the path, query and #k= fragment.
 * Without HOME_ORIGIN (local development) every address is treated as home.
 */
function legacyHosts(env: Env): Map<string, string> {
  if (!env.LEGACY_HOSTS) return new Map();
  return new Map(Object.entries(JSON.parse(env.LEGACY_HOSTS) as Record<string, string>));
}

const SOCKETS = new Set(['/ws/host', '/ws/phone']);

// Redirects from other sites' origins carry no HSTS: that's the main site's own decision.
const LEGACY_HEADERS: Record<string, string> = Object.fromEntries(
  Object.entries(SECURITY_HEADERS).filter(([name]) => name !== 'Strict-Transport-Security'),
);

export default {
  async fetch(request, env, ctx): Promise<Response> {
    const url = new URL(request.url);
    let path = url.pathname;

    const mount = env.HOME_ORIGIN ? legacyHosts(env).get(url.hostname) : undefined;
    if (mount !== undefined) {
      if (path !== mount && !path.startsWith(`${mount}/`)) return notFound();
      path = path.slice(mount.length) || '/';
      if (!SOCKETS.has(path)) {
        return new Response(null, { status: 301, headers: { Location: `${env.HOME_ORIGIN}${path}${url.search}`, ...LEGACY_HEADERS } });
      }
    }

    if (path.startsWith('/vendor/')) return serveVendor(request, path, ctx);
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
