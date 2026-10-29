import { defineConfig } from 'vite';
import preact from '@preact/preset-vite';

declare const process: { env: Record<string, string | undefined> };

// `npm run dev` proxies the API to a running debugger (the fake game by default).
const target = process.env.URDBG_URL ?? 'http://127.0.0.1:18765';

export default defineConfig({
  plugins: [preact()],
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    target: 'es2022',
    chunkSizeWarningLimit: 1200,
    // The page is served with a CSP that forbids inline scripts.
    modulePreload: { polyfill: false },
  },
  server: {
    proxy: Object.fromEntries(['/api', '/help', '/urdbg.py'].map(path => [path, { target, changeOrigin: false }])),
  },
});
