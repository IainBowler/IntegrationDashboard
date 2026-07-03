import { http, HttpResponse } from 'msw'

const API_BASE = 'http://localhost:3000'

export const handlers = [
  http.post(`${API_BASE}/page-visits`, () => {
    return new HttpResponse(null, { status: 201 })
  }),

  http.get(`${API_BASE}/page-visits/count`, ({ request }) => {
    const url = new URL(request.url)
    const pagePath = url.searchParams.get('pagePath') ?? ''
    return HttpResponse.json({ count: pagePath === '/dashboard' ? 42 : 0 })
  }),
]
