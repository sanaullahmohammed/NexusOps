import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

/** Where /api requests are forwarded when running the dev server. */
const FALLBACK_API_TARGET = 'http://localhost:5186';

// `||` rather than `??`: an empty-string SERVER_HTTPS is not null or undefined, so `??` would
// pass it straight through and proxy to "" — the exact bug this fallback exists to prevent.
// Both this and the guard below must treat empty as absent, or they disagree.
const apiTarget =
  process.env.SERVER_HTTPS || process.env.SERVER_HTTP || FALLBACK_API_TARGET;

if (!process.env.SERVER_HTTPS && !process.env.SERVER_HTTP) {
  console.info(
    `[vite] Aspire environment not detected; proxying /api to ${FALLBACK_API_TARGET}. ` +
      'Start via the AppHost to have this wired automatically.'
  );
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Proxy API calls to the app service.
      // SERVER_HTTPS / SERVER_HTTP are injected by the Aspire AppHost. Running `npm run dev`
      // directly leaves both undefined, which previously registered the proxy with no target.
      '/api': {
        target: apiTarget,
        changeOrigin: true
      }
    }
  }
});
