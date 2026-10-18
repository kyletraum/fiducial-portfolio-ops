import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The browser only ever calls a relative /api (Spike B). Under `aspire run` this dev
// server forwards it to the API. Aspire names the injected variables after the AppHost
// resource - "api" gives API_HTTPS / API_HTTP - so renaming that resource breaks the
// proxy with a 502, not a startup error. Fail loudly instead.
const apiTarget = process.env.API_HTTPS || process.env.API_HTTP;

export default defineConfig(({ command }) => {
  if (command === 'serve' && !apiTarget) {
    throw new Error('API_HTTPS/API_HTTP not set: run through `aspire run`, and keep the AppHost resource named "api".');
  }
  return {
    plugins: [react()],
    server: {
      // NO CORS. Vite's dev server answers CORS itself, and by default allows any localhost or
      // *.localhost origin on any port - so a page on attacker.localhost could READ /api through
      // this proxy, and its preflighted writes were let through to the API (whose Sec-Fetch-Site
      // check refused them). Found by tests/E2E/CrossSiteBrowserTests, 2026-10-18. This app's
      // page is served by this same server, so it never needs CORS.
      cors: false,
      proxy: {
        '/api': { target: apiTarget, changeOrigin: true },
      },
    },
  };
});
