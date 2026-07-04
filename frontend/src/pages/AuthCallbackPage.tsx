import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { exchangeCode } from '../api/authClient'
import { useAuth } from '../auth/useAuth'

/**
 * Landing spot for the API's post-login redirect. Reads the one-time handoff
 * code from the URL fragment (which never reaches server logs), scrubs it
 * from history, exchanges it for tokens, and moves on to the dashboard.
 */
export function AuthCallbackPage() {
  const navigate = useNavigate()
  const { setSession } = useAuth()
  const [failed, setFailed] = useState(false)
  const startedRef = useRef(false)

  useEffect(() => {
    // the handoff code is single-use — never exchange it twice
    if (startedRef.current) return
    startedRef.current = true

    const params = new URLSearchParams(window.location.hash.slice(1))
    const code = params.get('code')
    window.history.replaceState(null, '', window.location.pathname)

    if (!code) {
      setFailed(true)
      return
    }

    exchangeCode(code)
      .then((tokens) => {
        if (!tokens) {
          setFailed(true)
          return
        }
        setSession(tokens)
        navigate('/dashboard', { replace: true })
      })
      .catch(() => setFailed(true))
  }, [navigate, setSession])

  if (failed) {
    return (
      <main className="auth-callback">
        <h1>Sign-in failed</h1>
        <p>Something went wrong while signing you in. Please try again.</p>
        <Link to="/">Return to the home page</Link>
      </main>
    )
  }

  return (
    <main className="auth-callback">
      <p>Signing you in…</p>
    </main>
  )
}
