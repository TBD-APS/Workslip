import { readFileSync } from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

type ReleaseTarget = 'production' | 'staging'

interface ReleaseEnvironmentPolicy {
  enableDevelopmentEndpoints: boolean
}

interface ReleasePolicy {
  environments: Record<ReleaseTarget, ReleaseEnvironmentPolicy>
}

const releasePolicy = JSON.parse(
  readFileSync(path.resolve(__dirname, '../../config/release-environments.json'), 'utf8'),
) as ReleasePolicy
const requestedReleaseTarget = process.env.VITE_RELEASE_TARGET
const releaseTarget: ReleaseTarget = requestedReleaseTarget === 'staging' ? 'staging' : 'production'
const releaseTestingEnabled = releasePolicy.environments[releaseTarget]?.enableDevelopmentEndpoints === true

export default defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
  },
  define: {
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
    __WORKSLIP_RELEASE_TARGET__: JSON.stringify(releaseTarget),
    __WORKSLIP_RELEASE_TESTING_ENABLED__: JSON.stringify(releaseTestingEnabled),
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
