import { Outlet, useLocation } from 'react-router'
import { PageViewBadge } from './PageViewBadge'

export function Layout() {
  const location = useLocation()

  return (
    <>
      <Outlet />
      <PageViewBadge pagePath={location.pathname} />
    </>
  )
}
