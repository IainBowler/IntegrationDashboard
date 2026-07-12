import { http, HttpResponse } from 'msw'

const API_BASE = 'http://localhost:3000'

export const mockUser = {
  userId: 1,
  provider: 'okta',
  email: 'user@example.com',
  displayName: 'Test User',
}

export const mockTokenResponse = {
  accessToken: 'access-token-1',
  expiresInSeconds: 900,
  refreshToken: 'refresh-token-1',
  user: mockUser,
}

export const mockRotatedTokenResponse = {
  ...mockTokenResponse,
  accessToken: 'access-token-2',
  refreshToken: 'refresh-token-2',
}

function hasBearerToken(request: Request): boolean {
  return request.headers.get('Authorization')?.startsWith('Bearer ') ?? false
}

export const handlers = [
  http.post(`${API_BASE}/page-visits`, () => {
    return new HttpResponse(null, { status: 201 })
  }),

  http.get(`${API_BASE}/page-visits/count`, ({ request }) => {
    const url = new URL(request.url)
    const pagePath = url.searchParams.get('pagePath') ?? ''
    return HttpResponse.json({ count: pagePath === '/dashboard' ? 42 : 0 })
  }),

  http.post(`${API_BASE}/auth/token`, () => {
    return HttpResponse.json(mockTokenResponse)
  }),

  http.post(`${API_BASE}/auth/refresh`, () => {
    return HttpResponse.json(mockRotatedTokenResponse)
  }),

  http.post(`${API_BASE}/auth/logout`, () => {
    return new HttpResponse(null, { status: 204 })
  }),

  http.get(`${API_BASE}/auth/me`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return HttpResponse.json(mockUser)
  }),

  http.get(`${API_BASE}/api/integrations`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return HttpResponse.json([{ name: 'salesforce', displayName: 'Salesforce' }])
  }),

  http.get(`${API_BASE}/api/integrations/salesforce/statistics`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return HttpResponse.json({
      name: 'salesforce',
      displayName: 'Salesforce',
      endpoints: [
        {
          endpointName: 'auth',
          direction: 'Inbound',
          totalCalls: 5,
          successCount: 4,
          avgDurationMs: 12.5,
          maxDurationMs: 30,
          lastCalledAtUtc: '2026-07-12T09:00:00Z',
          lastStatusCode: 200,
        },
        {
          endpointName: 'accounts',
          direction: 'Inbound',
          totalCalls: 3,
          successCount: 3,
          avgDurationMs: 250,
          maxDurationMs: 400,
          lastCalledAtUtc: '2026-07-12T09:05:00Z',
          lastStatusCode: 200,
        },
        {
          endpointName: 'token',
          direction: 'Outbound',
          totalCalls: 0,
          successCount: 0,
          avgDurationMs: null,
          maxDurationMs: null,
          lastCalledAtUtc: null,
          lastStatusCode: null,
        },
        {
          endpointName: 'query',
          direction: 'Outbound',
          totalCalls: 3,
          successCount: 2,
          avgDurationMs: 180.4,
          maxDurationMs: 320,
          lastCalledAtUtc: '2026-07-12T09:05:01Z',
          lastStatusCode: 502,
        },
      ],
    })
  }),

  http.get(`${API_BASE}/api/integrations/salesforce/auth`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return new HttpResponse(null, { status: 200 })
  }),

  http.get(`${API_BASE}/api/integrations/salesforce/accounts`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return HttpResponse.json([
      {
        id: '001A',
        name: 'Acme',
        industry: 'Technology',
        type: 'Customer - Direct',
        website: 'https://acme.example',
        lastModifiedDate: '2026-07-01T12:34:56+00:00',
      },
    ])
  }),

  http.get(`${API_BASE}/page-visits/summary`, ({ request }) => {
    if (!hasBearerToken(request)) {
      return new HttpResponse(null, { status: 401 })
    }
    return HttpResponse.json({
      pages: [
        { pagePath: '/', count: 12 },
        { pagePath: '/dashboard', count: 5 },
      ],
    })
  }),
]
