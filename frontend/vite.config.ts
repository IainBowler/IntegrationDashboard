import { configDefaults, defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test-setup.ts'],
    exclude: [...configDefaults.exclude, 'e2e/**'], // e2e specs belong to Playwright
    env: {
      VITE_API_BASE_URL: 'http://localhost:3000',
    },
  },
})
