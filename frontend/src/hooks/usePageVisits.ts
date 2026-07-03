import { useState, useEffect } from 'react'
import { recordPageVisit, getPageVisitCount } from '../api/pageVisits'

export function usePageVisits(pagePath: string) {
  const [count, setCount] = useState<number | null>(null)

  useEffect(() => {
    async function track() {
      await recordPageVisit(pagePath)
      const c = await getPageVisitCount(pagePath)
      setCount(c)
    }
    track().catch(console.error)
  }, [pagePath])

  return { count }
}
