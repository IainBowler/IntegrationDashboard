import { describe, expect, it } from 'vitest'
import { http, HttpResponse } from 'msw'
import { server } from '../mocks/server'
import { mockRotatedTokenResponse } from '../mocks/handlers'
import { authFetch } from './http'
import { getRefreshToken, setAccessToken, storeSession } from '../auth/session'

const API_BASE = 'http://localhost:3000'

describe('authFetch', () => {
  it('attaches the Authorization header when an access token is set', async () => {
    setAccessToken('token-abc')
    let seenAuthHeader: string | null = null
    server.use(
      http.get(`${API_BASE}/data`, ({ request }) => {
        seenAuthHeader = request.headers.get('Authorization')
        return HttpResponse.json({ ok: true })
      }),
    )

    await authFetch(`${API_BASE}/data`)

    expect(seenAuthHeader).toBe('Bearer token-abc')
  })

  it('refreshes the session and retries once on a 401', async () => {
    storeSession({ accessToken: 'stale-token', refreshToken: 'refresh-token-1' })
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => {
        refreshCalls += 1
        return HttpResponse.json(mockRotatedTokenResponse)
      }),
      http.get(`${API_BASE}/data`, ({ request }) =>
        request.headers.get('Authorization') === 'Bearer access-token-2'
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 }),
      ),
    )

    const response = await authFetch(`${API_BASE}/data`)

    expect(response.status).toBe(200)
    expect(refreshCalls).toBe(1)
    expect(getRefreshToken()).toBe('refresh-token-2')
  })

  it('shares a single refresh across concurrent 401s', async () => {
    storeSession({ accessToken: 'stale-token', refreshToken: 'refresh-token-1' })
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => {
        refreshCalls += 1
        return HttpResponse.json(mockRotatedTokenResponse)
      }),
      http.get(`${API_BASE}/data`, ({ request }) =>
        request.headers.get('Authorization') === 'Bearer access-token-2'
          ? HttpResponse.json({ ok: true })
          : new HttpResponse(null, { status: 401 }),
      ),
    )

    const [first, second] = await Promise.all([
      authFetch(`${API_BASE}/data`),
      authFetch(`${API_BASE}/data`),
    ])

    expect(first.status).toBe(200)
    expect(second.status).toBe(200)
    expect(refreshCalls).toBe(1)
  })

  it('returns the original 401 and clears the session when the refresh fails', async () => {
    storeSession({ accessToken: 'stale-token', refreshToken: 'expired-refresh' })
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => new HttpResponse(null, { status: 401 })),
      http.get(`${API_BASE}/data`, () => new HttpResponse(null, { status: 401 })),
    )

    const response = await authFetch(`${API_BASE}/data`)

    expect(response.status).toBe(401)
    expect(getRefreshToken()).toBeNull()
  })

  it('does not attempt a refresh when no refresh token is stored', async () => {
    setAccessToken('stale-token')
    let refreshCalls = 0
    server.use(
      http.post(`${API_BASE}/auth/refresh`, () => {
        refreshCalls += 1
        return HttpResponse.json(mockRotatedTokenResponse)
      }),
      http.get(`${API_BASE}/data`, () => new HttpResponse(null, { status: 401 })),
    )

    const response = await authFetch(`${API_BASE}/data`)

    expect(response.status).toBe(401)
    expect(refreshCalls).toBe(0)
  })
})
