import { Link, useLocation } from 'react-router-dom'
import { Lock, LogIn } from 'lucide-react'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { NotFound } from '@/pages/StatusPage'

/** What to show when the API refuses content: 401 sign in, 402 unlock, anything else not found. */
export function AccessPrompt({ status, courseSlug }: { status: number; courseSlug?: string }) {
  const location = useLocation()
  if (status !== 401 && status !== 402) return <NotFound />
  const signIn = status === 401
  const Icon = signIn ? LogIn : Lock
  return (
    <Card className="mx-auto my-12 max-w-md text-center">
      <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-brand-500/10 text-brand-500">
        <Icon className="size-6" />
      </span>
      <h2 className="mt-4 text-lg font-semibold">{signIn ? 'Sign in to continue' : 'This content is locked'}</h2>
      <p className="mt-2 text-sm text-muted">
        {signIn
          ? 'Sign in with Google to open this lesson.'
          : 'Buy this course or join a membership that includes it to unlock every lesson.'}
      </p>
      <div className="mt-6 flex justify-center gap-3">
        {signIn ? (
          <Link to={`/login?next=${encodeURIComponent(location.pathname)}`} className={buttonClass()}>
            Sign in
          </Link>
        ) : (
          <Link to="/pricing" className={buttonClass()}>
            See options
          </Link>
        )}
        {courseSlug && (
          <Link to={`/courses/${courseSlug}`} className={buttonClass('secondary')}>
            Course overview
          </Link>
        )}
      </div>
    </Card>
  )
}
