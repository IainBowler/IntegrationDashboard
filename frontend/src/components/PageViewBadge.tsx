import { usePageVisits } from '../hooks/usePageVisits'
import './PageViewBadge.css'

interface Props {
  pagePath: string
}

export function PageViewBadge({ pagePath }: Props) {
  const { count } = usePageVisits(pagePath)

  if (count === null) return null

  return (
    <div className="page-view-badge" aria-label={`${count} page views`}>
      {count.toLocaleString()} views
    </div>
  )
}
