// The phone page, bundled into the Worker (see "rules" in wrangler.jsonc).
// app.js is stored as app.js.txt so the bundler treats it as text, not as Worker code.
import appJs from '../public/app.js.txt';
import indexHtml from '../public/index.html';
import stylesCss from '../public/styles.css';

const SECURITY_HEADERS: Record<string, string> = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
  'Cache-Control': 'no-store',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), screen-wake-lock=(self)',
  'Strict-Transport-Security': 'max-age=31536000',
};

const PAGE_CSP =
  "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
  "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

const FILES: Record<string, { body: string; type: string }> = {
  '/': { body: indexHtml, type: 'text/html; charset=utf-8' },
  '/index.html': { body: indexHtml, type: 'text/html; charset=utf-8' },
  '/app.js': { body: appJs, type: 'text/javascript; charset=utf-8' },
  '/styles.css': { body: stylesCss, type: 'text/css; charset=utf-8' },
};

/** Serves the three phone-page files from a fixed whitelist. Anything else is 404. */
export function serveAsset(request: Request, pathname: string): Response {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Method not allowed.', { status: 405, headers: { Allow: 'GET, HEAD', ...SECURITY_HEADERS } });
  }

  const file = FILES[pathname];
  if (!file) {
    return new Response('Not found.', { status: 404, headers: SECURITY_HEADERS });
  }

  const headers: Record<string, string> = { 'Content-Type': file.type, ...SECURITY_HEADERS };
  if (file.type.startsWith('text/html')) headers['Content-Security-Policy'] = PAGE_CSP;
  return new Response(request.method === 'HEAD' ? null : file.body, { headers });
}
