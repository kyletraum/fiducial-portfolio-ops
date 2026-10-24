// Spike I remainder (G-5): the dev proxy as web/vite.config.ts configures it (a /api prefix,
// changeOrigin, no CORS), pointed at the spike API, and serving page/index.html.
// A plain object, not defineConfig(): this directory has no node_modules to import from.
export default {
  root: new URL('./page', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'),
  server: {
    cors: false,
    proxy: {
      '/api': { target: process.env.SPIKE_API, changeOrigin: true },
    },
  },
};
