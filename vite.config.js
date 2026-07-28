import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { mkdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'

function sitesWorker() {
  return {
    name: 'octant-sites-worker',
    closeBundle() {
      const serverDirectory = resolve('dist/server')
      mkdirSync(serverDirectory, { recursive: true })
      writeFileSync(
        resolve(serverDirectory, 'index.js'),
        `export default {
  async fetch(request, env) {
    const response = await env.ASSETS.fetch(request)
    const acceptsHtml = request.headers.get('accept')?.includes('text/html')
    if (response.status !== 404 || request.method !== 'GET' || !acceptsHtml) return response
    const fallbackUrl = new URL('/index.html', request.url)
    return env.ASSETS.fetch(new Request(fallbackUrl, request))
  },
}
`,
      )
    },
  }
}

export default defineConfig({
  plugins: [vue(), sitesWorker()],
  build: {
    outDir: 'dist/assets',
    emptyOutDir: true,
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
  },
})
