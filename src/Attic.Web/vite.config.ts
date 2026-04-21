import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  // Aspire injects services__api__http__0 (HTTP) and services__api__https__0 (HTTPS) URIs.
  const apiBase =
    env.services__api__https__0 ||
    env.services__api__http__0 ||
    'http://localhost:5000';

  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 3000,
      proxy: {
        '/api': { target: apiBase, changeOrigin: true, secure: false, cookieDomainRewrite: 'localhost' },
        '/hub': { target: apiBase, changeOrigin: true, secure: false, ws: true },
      },
    },
  };
});
