import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
export default defineConfig({ plugins: [react()], base: '/film/', server: { host: 'localhost', port: 5176, strictPort: true, proxy: {
 '/api/movies': { target: 'http://localhost:5130' }, '/api': { target: 'http://localhost:5120' },
} } })
