import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { getMe, getPageVisitSummary } from '../api/dashboard'
import type { PageVisitSummaryItem } from '../api/dashboard'
import type { AuthUser } from '../api/authClient'
import { useAuth } from '../auth/useAuth'
import '../App.css'

export function DashboardPage() {
  const { logout } = useAuth()
  const navigate = useNavigate()
  const [profile, setProfile] = useState<AuthUser | null>(null)
  const [summary, setSummary] = useState<PageVisitSummaryItem[] | null>(null)

  useEffect(() => {
    getMe().then(setProfile).catch(console.error)
    getPageVisitSummary().then(setSummary).catch(console.error)
  }, [])

  function handleLogout() {
    logout()
    navigate('/', { replace: true })
  }

  return (
    <main className="dashboard">
      <header className="dashboard-header">
        <h1>Dashboard</h1>
        <button type="button" className="counter" onClick={handleLogout}>
          Sign out
        </button>
      </header>

      <section aria-label="Profile">
        <h2>Your profile</h2>
        {profile ? (
          <dl>
            <dt>Name</dt>
            <dd>{profile.displayName ?? '—'}</dd>
            <dt>Email</dt>
            <dd>{profile.email ?? '—'}</dd>
            <dt>Signed in with</dt>
            <dd>{profile.provider}</dd>
          </dl>
        ) : (
          <p>Loading profile…</p>
        )}
      </section>

      <section aria-label="Page views">
        <h2>Page views</h2>
        {summary ? (
          <table>
            <thead>
              <tr>
                <th scope="col">Page</th>
                <th scope="col">Views</th>
              </tr>
            </thead>
            <tbody>
              {summary.map((item) => (
                <tr key={item.pagePath}>
                  <td>{item.pagePath}</td>
                  <td>{item.count.toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <p>Loading stats…</p>
        )}
      </section>
    </main>
  )
}
