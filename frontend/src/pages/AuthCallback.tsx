import { useEffect, useState } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { motion } from 'motion/react'
import { CheckCircle2 } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { homeFor, safeReturnPath } from '@/auth/roles'
import { Spinner } from '@/components/ui/Spinner'

/** Landing spot after the API finishes the Google OAuth exchange and sets the session cookie. */
export default function AuthCallback() {
  const { status, roles, refresh } = useAuth()
  const [params] = useSearchParams()
  const [timedOut, setTimedOut] = useState(false)
  const [done, setDone] = useState(false)
  const oauthError = params.get('error')
  const next = safeReturnPath(params.get('next'))

  // The API set the session cookie before redirecting here; load it.
  useEffect(() => {
    void refresh()
  }, [refresh])

  useEffect(() => {
    const t = setTimeout(() => setTimedOut(true), 15000)
    return () => clearTimeout(t)
  }, [])

  useEffect(() => {
    if (status !== 'signed_in') return
    const t = setTimeout(() => setDone(true), 700)
    return () => clearTimeout(t)
  }, [status])

  if (oauthError) {
    const notice = oauthError === 'access_denied' ? 'cancelled' : 'failed'
    return <Navigate to={`/login?notice=${notice}`} replace />
  }
  if (done) return <Navigate to={next ?? homeFor(roles)} replace />
  if (timedOut && status !== 'signed_in') return <Navigate to="/login?notice=failed" replace />

  return (
    <div className="grid min-h-dvh place-items-center px-4">
      <motion.div initial={{ opacity: 0, y: 8 }} animate={{ opacity: 1, y: 0 }} className="flex flex-col items-center gap-4 text-center">
        {status === 'signed_in' ? (
          <motion.span initial={{ scale: 0.4 }} animate={{ scale: 1 }} transition={{ type: 'spring', stiffness: 300, damping: 15 }}>
            <CheckCircle2 className="size-12 text-success" />
          </motion.span>
        ) : (
          <Spinner className="size-10 text-brand-500" />
        )}
        <p className="font-medium">{status === 'signed_in' ? 'Signed in. Taking you in…' : 'Finishing sign-in…'}</p>
      </motion.div>
    </div>
  )
}
