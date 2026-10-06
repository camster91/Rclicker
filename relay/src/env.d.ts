// Plain-text settings set on the Worker in Cloudflare (see src/index.ts), not in wrangler.jsonc.
interface Env {
  HOME_ORIGIN?: string;
  LEGACY_HOSTS?: string;
}
