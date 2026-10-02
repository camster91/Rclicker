// rclicker relay Worker.
//
// Mounted at the domain root (rclicker.<account>.workers.dev) and at rotmanav.ca/clicker:
//   /            marketing page          /remote      phone remote (key in the #fragment)
//   /download    latest rclicker.exe     /ws/host     PC receiver (outbound from the presentation PC)
//                                        /ws/phone    phone browser
//
// Each room is one Durable Object. The relay only forwards opaque, end-to-end
// encrypted payloads between one PC and one phone; it never sees the session key.

import { SECURITY_HEADERS, serveAsset } from './assets';

export { Room } from './room';

const ROOM_ID = /^[A-Za-z0-9_-]{22}$/;

/** URL prefix the relay is mounted under on a shared domain. */
const MOUNT = '/clicker';

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);

    let path = url.pathname;
    if (path === MOUNT) {
      // Relative links on the page need the trailing slash.
      return new Response(null, { status: 301, headers: { Location: `${MOUNT}/${url.search}`, ...SECURITY_HEADERS } });
    }

    if (path.startsWith(`${MOUNT}/`)) {
      path = path.slice(MOUNT.length);
    }

    if (path !== '/ws/host' && path !== '/ws/phone') {
      return serveAsset(request, path);
    }

    if (request.method !== 'GET' || request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') {
      return new Response('Expected a WebSocket upgrade.', { status: 426 });
    }

    // Browsers send Origin. Only our own phone page may open phone sockets.
    // The PC receiver is a native client and sends no Origin.
    const origin = request.headers.get('Origin');
    if (origin !== null && origin !== url.origin) {
      return new Response('Forbidden.', { status: 403 });
    }

    const room = url.searchParams.get('room') ?? '';
    if (!ROOM_ID.test(room)) {
      return new Response('Bad room id.', { status: 400 });
    }

    // The room only needs to know which side is connecting.
    const inner = new URL(request.url);
    inner.pathname = path;
    const stub = env.ROOMS.get(env.ROOMS.idFromName(room));
    return stub.fetch(new Request(inner, request));
  },
} satisfies ExportedHandler<Env>;
