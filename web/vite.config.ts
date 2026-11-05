/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

const apiTarget = process.env.STRATEGY_API_URL ?? 'http://localhost:5080';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: false },
      '/openapi': { target: apiTarget, changeOrigin: false },
      '/health': { target: apiTarget, changeOrigin: false },
      // OAuth for MCP clients and the MCP server itself (Phase 5).
      '/connect': { target: apiTarget, changeOrigin: false },
      '/.well-known': { target: apiTarget, changeOrigin: false },
      '/mcp': { target: apiTarget, changeOrigin: false },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    // No inline scripts or styles, so the production CSP can stay strict.
    assetsInlineLimit: 0,
    rollupOptions: {
      output: {
        // Vendor code changes rarely: separate chunks stay cached across releases.
        // ECharts (with zrender) is its own chunk, loaded only when a chart renders (ADR 0148).
        manualChunks(id) {
          const pkg = /node_modules\/((?:@[^/]+\/)?[^/]+)\//.exec(id)?.[1];
          if (!pkg) return undefined;
          if (pkg === 'echarts' || pkg === 'zrender') return 'echarts';
          if (pkg === 'react' || pkg === 'react-dom' || pkg === 'scheduler') return 'react';
          if (pkg.startsWith('@tanstack/')) return 'tanstack';
          if (pkg === 'i18next' || pkg === 'react-i18next') return 'i18n';
          if (pkg === 'zod' || pkg === 'react-hook-form' || pkg === '@hookform/resolvers') return 'forms';
          return undefined;
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    include: ['src/**/*.test.{ts,tsx}', 'scripts/**/*.test.mjs'],
    css: false,
    restoreMocks: true,
  },
});
