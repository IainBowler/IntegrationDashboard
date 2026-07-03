const API_BASE = import.meta.env.VITE_API_BASE_URL ?? ''

export async function recordPageVisit(pagePath: string): Promise<void> {
  await fetch(`${API_BASE}/page-visits`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ pagePath }),
  })
}

export async function getPageVisitCount(pagePath: string): Promise<number> {
  const res = await fetch(
    `${API_BASE}/page-visits/count?pagePath=${encodeURIComponent(pagePath)}`
  )
  const data = (await res.json()) as { count: number }
  return data.count
}
