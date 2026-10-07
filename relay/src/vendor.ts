// Third-party browser libraries for the "present in the browser" page (pdf.js, a QR encoder).
// They are too large to bundle into the Worker, so the relay fetches each file from a public
// npm mirror on first use, checks it against the SHA-384 pinned in vendor-manifest.ts, and
// serves it from rclicker's own origin (cached). A file that doesn't match is never served,
// so a compromised mirror can't change what runs on the page, and pages keep their strict
// same-origin CSP. Browsers only ever talk to rclicker's address.
import { SECURITY_HEADERS } from './assets';
import { VENDOR_FILES } from './vendor-manifest';

const MIRRORS = ['https://cdn.jsdelivr.net/npm/', 'https://unpkg.com/'];

const TYPES: Record<string, string> = {
  mjs: 'text/javascript; charset=utf-8',
  js: 'text/javascript; charset=utf-8',
  wasm: 'application/wasm',
  bcmap: 'application/octet-stream',
  pfb: 'application/octet-stream',
  ttf: 'font/ttf',
  icc: 'application/vnd.iccprofile',
};

// pdf.js renders in a Web Worker, which takes its CSP from its own script response.
const WORKER_CSP = "default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'";

export async function serveVendor(
  request: Request,
  path: string,
  ctx: ExecutionContext,
  files: typeof VENDOR_FILES = VENDOR_FILES,
): Promise<Response> {
  const entry = Object.hasOwn(files, path) ? files[path] : undefined;
  if (!entry) {
    return new Response('Not found.', { status: 404, headers: { 'Cache-Control': 'no-store', ...SECURITY_HEADERS } });
  }
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Method not allowed.', { status: 405, headers: { Allow: 'GET, HEAD', ...SECURITY_HEADERS } });
  }

  const [source, sha384] = entry;
  // Keyed by content hash, so a new version can never be served from an old cache entry.
  const cacheKey = new Request(`https://vendor.rclicker.invalid/${sha384.replace(/[+/=]/g, '_')}`);
  const cache = caches.default;
  let response = await cache.match(cacheKey);

  if (!response) {
    const body = await fetchVerified(source, sha384);
    if (!body) {
      return new Response('Could not load a library file. Try again in a minute.', {
        status: 502,
        headers: { 'Cache-Control': 'no-store', ...SECURITY_HEADERS },
      });
    }
    const headers: Record<string, string> = {
      'Content-Type': TYPES[path.split('.').pop() ?? ''] ?? 'text/plain; charset=utf-8',
      'Cache-Control': 'public, max-age=604800',
      ...SECURITY_HEADERS,
    };
    if (path.endsWith('/pdf.worker.min.mjs')) headers['Content-Security-Policy'] = WORKER_CSP;
    response = new Response(body, { headers });
    ctx.waitUntil(cache.put(cacheKey, response.clone()));
  }

  return request.method === 'HEAD' ? new Response(null, response) : response;
}

async function fetchVerified(source: string, sha384: string): Promise<ArrayBuffer | null> {
  for (const mirror of MIRRORS) {
    try {
      const upstream = await fetch(mirror + source, { cf: { cacheTtl: 86400, cacheEverything: true } });
      if (!upstream.ok) continue;
      const body = await upstream.arrayBuffer();
      if (base64(await crypto.subtle.digest('SHA-384', body)) === sha384) return body;
      console.error(`vendor: hash mismatch for ${source} from ${mirror}`);
    } catch (error) {
      console.error(`vendor: ${source} from ${mirror} failed: ${String(error)}`);
    }
  }
  return null;
}

function base64(buffer: ArrayBuffer): string {
  let binary = '';
  for (const b of new Uint8Array(buffer)) binary += String.fromCharCode(b);
  return btoa(binary);
}
