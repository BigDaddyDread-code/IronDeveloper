import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/irondev-api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/irondev-api/, '')
      }
    }
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true
  },
  clearScreen: false
});
