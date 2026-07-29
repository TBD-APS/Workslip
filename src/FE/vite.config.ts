import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import path from "node:path";

export default defineConfig({
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
    host: '127.0.0.1',
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
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg'],
      strategies: 'injectManifest',
      srcDir: 'src',
      filename: 'sw.ts',
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
