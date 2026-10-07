import { cloudflareTest } from '@cloudflare/vitest-pool-workers';
import { defineConfig } from 'vitest/config';

export default defineConfig({
  plugins: [
    cloudflareTest({
      wrangler: { configPath: './wrangler.jsonc' },
      // Stand-ins for the addresses set on the real Worker (see src/index.ts).
      miniflare: {
        bindings: {
          HOME_ORIGIN: 'https://clicker.example.com',
          LEGACY_HOSTS: JSON.stringify({ 'example.org': '/clicker', 'www.example.org': '/clicker', 'rclicker.example.workers.dev': '' }),
        },
      },
    }),
  ],
});
