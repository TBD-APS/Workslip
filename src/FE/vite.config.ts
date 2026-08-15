import path from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  test: {
    environment: 'jsdom',
    include: ['./src/**/*.{test,spec}.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
  },
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
  build: {
    sourcemap: true,
    rolldownOptions: {
      output: {
        // Keep the application bootstrap distinguishable from lazy route chunks
        // so the service-worker glob cannot accidentally precache a route named
        // index.tsx or another common chunk.
        entryFileNames: 'assets/app-[hash].js',
        chunkFileNames: 'assets/chunks/[name]-[hash].js',
      },
    },
  },
  server: {
    // Listen on the developer machine's LAN interface as well as localhost so
    // the same local full-stack build can be exercised from a physical phone.
    // API requests remain same-origin in the browser and are proxied by Vite to
    // the local backend, so the phone never needs direct access to port 5262.
    host: '0.0.0.0',
    port: 5270,
    proxy: {
      '/api': {
        target: 'http://localhost:5262',
        changeOrigin: true,
        secure: false,
      }
    },
    watch: {
      usePolling: true
    }
  },
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['favicon.svg'],
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
      devOptions: {
        enabled: true,
        type: 'module',
      },
      injectManifest: {
        // Install only the navigation shell and bootstrap JavaScript. CSS,
        // fonts, images and lazy chunks are cached when the browser actually
        // requests them, preventing service-worker installation from downloading
        // the entire authenticated application during a public login visit.
        globPatterns: [
          '**/*.html',
          '**/*.webmanifest',
          'assets/app-*.js',
        ],
      },
      manifest: {
        name: 'Workslip',
        short_name: 'Workslip',
        description: 'Den digitale arbejdsseddel til VVS',
        theme_color: '#050505',
        background_color: '#050505',
        display: 'standalone',
        icons: [
          {
            src: 'pwa-192x192.png',
            sizes: '192x192',
            type: 'image/png'
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any maskable'
          }
        ]
      }
    })
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
})
