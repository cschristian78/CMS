import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  appType: 'spa',
  build: {
    outDir: '../wwwroot',
    emptyOutDir: false,
    rollupOptions: {
      output: {
        // Stable entry name so wwwroot/index.html stays valid after rebuilds (deep links / refreshing routes load the SPA).
        entryFileNames: 'assets/app.js',
        chunkFileNames: 'assets/[name]-[hash].js',
        assetFileNames: 'assets/[name]-[hash][extname]',
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'https://localhost:7044', secure: false },
      '/css': { target: 'https://localhost:7044', secure: false },
    },
  },
});
