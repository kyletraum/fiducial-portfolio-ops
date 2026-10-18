// Spike F: the dev proxy alone, configured as web/vite.config.ts configures it (a /api
// prefix, changeOrigin) and pointed at the spike API. No plugins; nothing is served.
// A plain object, not defineConfig(): this directory has no node_modules to import from.
export default {
  server: {
    proxy: {
      '/api': { target: process.env.SPIKE_API, changeOrigin: true },
    },
  },
};
