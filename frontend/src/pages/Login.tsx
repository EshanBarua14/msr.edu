import { useState } from 'react'
import { Navigate, useSearchParams } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { AlertCircle, CheckCircle2, Info } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { homeFor, safeReturnPath, type Role } from '@/auth/roles'
import { AnimatedBackground } from '@/components/AnimatedBackground'
import { Button } from '@/components/ui/Button'
import { LogoMark } from '@/components/ui/Logo'
import { Spinner } from '@/components/ui/Spinner'

function GoogleIcon() {
  return (
    <svg viewBox="0 0 48 48" className="size-5" aria-hidden>
      <path fill="#FFC107" d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z" />
      <path fill="#FF3D00" d="m6.3 14.7 6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z" />
      <path fill="#4CAF50" d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z" />
      <path fill="#1976D2" d="M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C36.9 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z" />
    </svg>
  )
}

type Phase = 'idle' | 'redirecting' | 'error'

const notices: Record<string, { tone: 'info' | 'error'; text: string }> = {
  cancelled: { tone: 'info', text: 'Sign-in was cancelled. You can try again whenever you are ready.' },
  failed: { tone: 'error', text: 'We could not complete sign-in. Please try again.' },
  signed_out: { tone: 'info', text: 'You have been signed out.' },
}

const devRoles: { label: string; roles: Role[] }[] = [
  { label: 'Learner', roles: ['learner'] },
  { label: 'Owner admin', roles: ['learner', 'owner_admin'] },
  { label: 'Content manager', roles: ['learner', 'content_manager'] },
  { label: 'Finance manager', roles: ['learner', 'finance_manager'] },
]

export default function Login() {
  const { status, roles, providers, signInWithGoogle, devSignIn } = useAuth()
  const [params] = useSearchParams()
  const [phase, setPhase] = useState<Phase>('idle')
  const [error, setError] = useState<string | null>(null)
  const next = safeReturnPath(params.get('next'))
  const notice = notices[params.get('notice') ?? '']

  if (status === 'signed_in') return <Navigate to={next ?? homeFor(roles)} replace />

  const start = () => {
    setError(null)
    setPhase('redirecting')
    signInWithGoogle(next ?? undefined)
  }

  const devStart = async (r: Role[]) => {
    setError(null)
    try {
      await devSignIn(r)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Dev sign-in failed.')
    }
  }

  return (
    <div className="relative isolate grid min-h-dvh place-items-center overflow-hidden px-4 py-12">
      <AnimatedBackground />
      <motion.div
        initial={{ opacity: 0, y: 24, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.5, ease: [0.22, 1, 0.36, 1] }}
        className="relative w-full max-w-md rounded-[2rem] border border-border/70 bg-surface/80 p-8 shadow-2xl backdrop-blur-xl sm:p-10"
      >
        <motion.div
          initial={{ rotate: -12, scale: 0.6, opacity: 0 }}
          animate={{ rotate: 0, scale: 1, opacity: 1 }}
          transition={{ delay: 0.15, type: 'spring', stiffness: 260, damping: 18 }}
          className="mx-auto w-fit"
        >
          <LogoMark className="size-14" />
        </motion.div>
        <h1 className="mt-6 text-center text-2xl font-bold tracking-tight">Welcome to MSR.edu</h1>
        <p className="mt-2 text-center text-sm text-muted">Sign in or create your account with Google.</p>

        <AnimatePresence mode="wait">
          {(notice || error) && (
            <motion.div
              key={error ?? notice?.text}
              initial={{ opacity: 0, height: 0 }}
              animate={{ opacity: 1, height: 'auto' }}
              exit={{ opacity: 0, height: 0 }}
              className="overflow-hidden"
            >
              <div
                role={error || notice?.tone === 'error' ? 'alert' : 'status'}
                className={
                  error || notice?.tone === 'error'
                    ? 'mt-6 flex gap-2 rounded-2xl bg-danger/10 p-3 text-sm text-danger'
                    : 'mt-6 flex gap-2 rounded-2xl bg-brand-500/10 p-3 text-sm text-brand-600 dark:text-brand-400'
                }
              >
                {error || notice?.tone === 'error' ? <AlertCircle className="size-4 shrink-0" /> : <Info className="size-4 shrink-0" />}
                {error ?? notice?.text}
              </div>
            </motion.div>
          )}
        </AnimatePresence>

        <Button
          variant="secondary"
          size="lg"
          className="mt-8 w-full"
          onClick={start}
          disabled={phase === 'redirecting' || !providers?.google}
        >
          {phase === 'redirecting' ? <Spinner className="size-5 text-brand-500" /> : <GoogleIcon />}
          {phase === 'redirecting' ? 'Opening Google…' : 'Continue with Google'}
        </Button>

        {providers && !providers.google && (
          <p className="mt-3 text-center text-xs text-muted">Google sign-in is not configured on the server yet (see README).</p>
        )}

        {providers?.devLogin && (
          <div className="mt-8 border-t border-border pt-6">
            <p className="text-center text-xs font-semibold tracking-wider text-muted uppercase">Development: sign in as</p>
            <div className="mt-3 grid grid-cols-2 gap-2">
              {devRoles.map((p) => (
                <Button key={p.label} variant="ghost" size="sm" className="border border-border" onClick={() => devStart(p.roles)}>
                  {p.label}
                </Button>
              ))}
            </div>
          </div>
        )}

        <p className="mt-8 flex items-center justify-center gap-1.5 text-center text-xs text-muted">
          <CheckCircle2 className="size-3.5 text-success" /> We only use your name, email and profile photo.
        </p>
      </motion.div>
    </div>
  )
}
