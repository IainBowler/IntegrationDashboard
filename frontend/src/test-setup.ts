import '@testing-library/jest-dom'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { cleanup } from '@testing-library/react'
import { server } from './mocks/server'
import { clearSession } from './auth/session'

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
afterEach(() => {
  server.resetHandlers()
  cleanup()
  clearSession() // reset the module-level auth session between tests
  sessionStorage.clear()
})
afterAll(() => server.close())
