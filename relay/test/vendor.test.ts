import { SELF, createExecutionContext, waitOnExecutionContext } from 'cloudflare:test';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { serveVendor } from '../src/vendor';
import { VENDOR_FILES } from '../src/vendor-manifest';

async function sha384(text: string): Promise<string> {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-384', new TextEncoder().encode(text)));
  return btoa(String.fromCharCode(...digest));
}

async function serve(path: string, files: typeof VENDOR_FILES, method = 'GET') {
  const ctx = createExecutionContext();
  const response = await serveVendor(new Request(`https://relay.test${path}`, { method }), path, ctx, files);
  await waitOnExecutionContext(ctx);
  return response;
}

afterEach(() => vi.restoreAllMocks());

describe('vendor files', () => {
  it('serves a file only after its hash matches, then from cache', async () => {
    const body = `export const v = ${Math.random()};`;
    const files = { '/vendor/t/lib.mjs': ['lib@1.0.0/lib.mjs', await sha384(body)] as const };
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(body));

    const first = await serve('/vendor/t/lib.mjs', files);
    expect(first.status).toBe(200);
    expect(await first.text()).toBe(body);
    expect(first.headers.get('Content-Type')).toBe('text/javascript; charset=utf-8');
    expect(first.headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
    expect(String(fetchSpy.mock.calls[0]?.[0])).toBe('https://cdn.jsdelivr.net/npm/lib@1.0.0/lib.mjs');

    const again = await serve('/vendor/t/lib.mjs', files);
    expect(await again.text()).toBe(body);
    expect(fetchSpy).toHaveBeenCalledTimes(1); // Second answer came from the cache.
  });

  it('never serves a file whose hash does not match, from any mirror', async () => {
    const files = { '/vendor/t/evil.mjs': ['lib@1.0.0/evil.mjs', await sha384('the real file')] as const };
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response('tampered'));

    const response = await serve('/vendor/t/evil.mjs', files);
    expect(response.status).toBe(502);
    expect(await response.text()).not.toContain('tampered');
    expect(fetchSpy.mock.calls.map((c) => String(c[0]))).toEqual([
      'https://cdn.jsdelivr.net/npm/lib@1.0.0/evil.mjs',
      'https://unpkg.com/lib@1.0.0/evil.mjs',
    ]);
  });

  it('falls back to the second mirror when the first fails', async () => {
    const body = `ok ${Math.random()}`;
    const files = { '/vendor/t/data.bcmap': ['lib@1.0.0/data.bcmap', await sha384(body)] as const };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) =>
      String(input).startsWith('https://unpkg.com/') ? new Response(body) : new Response('down', { status: 503 }));

    const response = await serve('/vendor/t/data.bcmap', files);
    expect(response.status).toBe(200);
    expect(await response.text()).toBe(body);
    expect(response.headers.get('Content-Type')).toBe('application/octet-stream');
  });

  it("gives pdf.js's worker its own strict CSP", async () => {
    const body = `self.onmessage = () => ${Math.random()};`;
    const files = { '/vendor/pdfjs/build/pdf.worker.min.mjs': ['x@1/pdf.worker.min.mjs', await sha384(body)] as const };
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () => new Response(body));

    const response = await serve('/vendor/pdfjs/build/pdf.worker.min.mjs', files);
    expect(response.headers.get('Content-Security-Policy')).toContain("script-src 'self' 'wasm-unsafe-eval'");
    expect(response.headers.get('Content-Security-Policy')).toContain("connect-src 'self'");
  });

  it('only serves listed files, and only for GET/HEAD', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    expect((await SELF.fetch('https://relay.test/vendor/pdfjs/build/pdf.mjs')).status).toBe(404);
    expect((await SELF.fetch('https://relay.test/vendor/../wrangler.jsonc')).status).toBe(404);
    expect((await SELF.fetch('https://relay.test/vendor/constructor')).status).toBe(404);
    expect((await serve('/vendor/pdfjs/legacy/build/pdf.min.mjs', VENDOR_FILES, 'POST')).status).toBe(405);
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it('pins every pdf.js file the present page needs', () => {
    for (const path of ['/vendor/pdfjs/legacy/build/pdf.min.mjs', '/vendor/pdfjs/legacy/build/pdf.worker.min.mjs', '/vendor/qrcode/dist/qrcode.mjs',
      '/vendor/pdfjs/wasm/openjpeg.wasm', '/vendor/pdfjs/standard_fonts/LiberationSans-Regular.ttf', '/vendor/pdfjs/cmaps/78-H.bcmap']) {
      expect(VENDOR_FILES[path]?.[1], path).toMatch(/^[A-Za-z0-9+/]{64}$/);
    }
  });
});

describe('present page', () => {
  it('is served with its own CSP that allows the pdf.js worker but nothing third-party', async () => {
    const page = await SELF.fetch('https://relay.test/present');
    expect(page.status).toBe(200);
    expect(await page.text()).toContain('Present a PDF from this browser');
    const csp = page.headers.get('Content-Security-Policy') ?? '';
    expect(csp).toContain("worker-src 'self'");
    expect(csp).toContain("script-src 'self';");
    expect(csp).not.toMatch(/https?:/);
    expect(page.headers.get('X-Robots-Tag')).toBe('noindex, nofollow');
    for (const path of ['/present.js', '/present.css']) {
      expect((await SELF.fetch(`https://relay.test${path}`)).status, path).toBe(200);
    }
  });

  it('redirects to clicker.example.com from the old addresses', async () => {
    const response = await SELF.fetch('https://example.org/clicker/present', { redirect: 'manual' });
    expect(response.headers.get('Location')).toBe('https://clicker.example.com/present');
  });
});
