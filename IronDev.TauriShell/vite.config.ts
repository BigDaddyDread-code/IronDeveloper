import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const apiProxyTarget =
    process.env.IRONDEV_API_PROXY_TARGET ??
    env.IRONDEV_API_PROXY_TARGET ??
    env.VITE_IRONDEV_API_BASE_URL ??
    'https://localhost:7000';

  return {
    plugins: [react()],
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      proxy: {
        '/irondev-api': {
          target: apiProxyTarget,
          changeOrigin: true,
          secure: false,
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
  };
});
