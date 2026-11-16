import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Keyframe dashboard build. The dev server and preview both listen on 8701 (plan D7).
// KEYFRAME_SERVER_URL is only used to proxy /openapi.json as a same-origin fallback for the
// API Explorer; every other call goes straight to the server entered at login.
const serverUrl = process.env.KEYFRAME_SERVER_URL || 'http://127.0.0.1:8700';
const proxy = { '/openapi.json': { target: serverUrl, changeOrigin: true } };

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 8701,
    strictPort: true,
    proxy
  },
  preview: {
    host: '127.0.0.1',
    port: 8701,
    strictPort: true,
    proxy
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    chunkSizeWarningLimit: 900,
    rollupOptions: {
      output: {
        manualChunks: {
          vendor: ['react', 'react-dom', 'react-router-dom'],
          i18n: ['i18next', 'react-i18next', 'i18next-browser-languagedetector']
        }
      }
    }
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.js'],
    css: false,
    include: ['src/**/*.test.{js,jsx}']
  }
});
