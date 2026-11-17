/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'happy-dom',
    include: ['src/**/*.test.tsx'],
  },
  server: {
    proxy: {
      '/api': {
        target: process.env.API_BASE_URL ?? 'http://localhost:5000',
        changeOrigin: true,
      },
      '/health': {
        target: process.env.API_BASE_URL ?? 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
