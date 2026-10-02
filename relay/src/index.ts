// rclicker relay Worker.
//
// Phone page: public/, bundled into the Worker and served by serveAsset().
// /ws/host?room=ID   PC receiver (outbound connection from the presentation computer)
// /ws/phone?room=ID  phone browser
//
// Each room is one Durable Object. The relay only forwards opaque, end-to-end
// encrypted payloads between one PC and one phone; it never sees the session key.

import { serveAsset } from './assets';

export { Room } from './room';

const ROOM_ID = /^[A-Za-z0-9_-]{22}$/;

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname !== '/ws/host' && url.pathname !== '/ws/phone') {
      return serveAsset(request, url.pathname);
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

    const stub = env.ROOMS.get(env.ROOMS.idFromName(room));
    return stub.fetch(request);
  },
} satisfies ExportedHandler<Env>;
