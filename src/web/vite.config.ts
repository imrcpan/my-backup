import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { defineConfig } from 'vite'

// The built interface goes into the server, which serves it from its wwwroot.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  build: { outDir: '../Backup.Server/wwwroot', emptyOutDir: true },
  server: { proxy: { '/api': 'http://localhost:5174' } },
})
