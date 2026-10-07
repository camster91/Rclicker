// The website and phone remote, bundled into the Worker (see "rules" in wrangler.jsonc).
// .js files are stored as .js.txt so the bundler treats them as text, not Worker code.
import appJs from '../public/app.js.txt';
import presentCss from '../public/present.css';
import presentHtml from '../public/present.html';
import presentJs from '../public/present.js.txt';
import remoteHtml from '../public/remote.html';
import siteCss from '../public/site.css';
import siteHtml from '../public/site.html';
import siteJs from '../public/site.js.txt';
import stylesCss from '../public/styles.css';

/** Where the marketing page's Download buttons go. */
export const DOWNLOAD_URL = 'https://github.com/camster91/Rclicker/releases/latest/download/rclicker.exe';

export const SECURITY_HEADERS: Record<string, string> = {
  'X-Content-Type-Options': 'nosniff',
  'X-Frame-Options': 'DENY',
  'Referrer-Policy': 'no-referrer',
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Resource-Policy': 'same-origin',
  'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), screen-wake-lock=(self)',
  'Strict-Transport-Security': 'max-age=31536000',
  // Keep every page out of search engines (the pages also carry a robots meta tag).
  'X-Robots-Tag': 'noindex, nofollow',
};

const PAGE_CSP =
  "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
  "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

// "Present in the browser" renders PDFs with pdf.js (served same-origin from /vendor/): it needs
// its Web Worker, canvas images, and fonts that pdf.js loads from the PDF's own data.
const PRESENT_CSP =
  "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; " +
  "connect-src 'self'; worker-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

const HTML = 'text/html; charset=utf-8';
const JS = 'text/javascript; charset=utf-8';
const CSS = 'text/css; charset=utf-8';

// The remote and its scripts are never cached (always the latest security fixes);
// the marketing page may be cached briefly.
const FILES: Record<string, { body: string; type: string; cache: string; csp?: string }> = {
  '/': { body: siteHtml, type: HTML, cache: 'public, max-age=300' },
  '/site.css': { body: siteCss, type: CSS, cache: 'public, max-age=300' },
  '/site.js': { body: siteJs, type: JS, cache: 'no-store' },
  '/remote': { body: remoteHtml, type: HTML, cache: 'no-store' },
  '/app.js': { body: appJs, type: JS, cache: 'no-store' },
  '/styles.css': { body: stylesCss, type: CSS, cache: 'no-store' },
  '/present': { body: presentHtml, type: HTML, cache: 'no-store', csp: PRESENT_CSP },
  '/present.js': { body: presentJs, type: JS, cache: 'no-store' },
  '/present.css': { body: presentCss, type: CSS, cache: 'no-store' },
};

/** Serves the whitelisted pages. `path` is relative to where the relay is mounted. Anything else is 404. */
export function serveAsset(request: Request, path: string): Response {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    return new Response('Method not allowed.', { status: 405, headers: { Allow: 'GET, HEAD', ...SECURITY_HEADERS } });
  }

  if (path === '/download') {
    return new Response(null, { status: 302, headers: { Location: DOWNLOAD_URL, 'Cache-Control': 'no-store', ...SECURITY_HEADERS } });
  }

  const file = FILES[path];
  if (!file) {
    return new Response('Not found.', { status: 404, headers: { 'Cache-Control': 'no-store', ...SECURITY_HEADERS } });
  }

  const headers: Record<string, string> = { 'Content-Type': file.type, 'Cache-Control': file.cache, ...SECURITY_HEADERS };
  if (file.type === HTML) headers['Content-Security-Policy'] = file.csp ?? PAGE_CSP;
  return new Response(request.method === 'HEAD' ? null : file.body, { headers });
}
