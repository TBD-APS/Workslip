import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import path from "node:path";

export default defineConfig({
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
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
      includeAssets: ['favicon.ico', 'apple-touch-icon.png', 'masked-icon.svg'],
      strategies: 'generateSW',
      workbox: {
        cleanupOutdatedCaches: true, // Sletter gamle caches automatisk ved ny version
        skipWaiting: true,           // Tvinger den nye SW til at overtage med det samme
        clientsClaim: true           // Overtager kontrollen over faner/apps med det samme
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
