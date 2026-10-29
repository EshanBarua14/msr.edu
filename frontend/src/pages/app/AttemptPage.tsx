import { Link, useParams } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { AttemptReview } from '@/components/learn/AttemptReview'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { type AttemptResult, type QuizDetail, quizPath } from '@/lib/learning'
import { useApi } from '@/lib/useApi'
import { NotFound } from '@/pages/StatusPage'

export default function AttemptPage() {
  const { attemptId = '' } = useParams()
  const [state] = useApi<AttemptResult>(`/api/quiz-attempts/${encodeURIComponent(attemptId)}`)
  // Option labels; if access has since lapsed the review still shows marks and explanations.
  const [quiz] = useApi<QuizDetail>(state.data ? `/api/quizzes/${state.data.quizId}` : null)

  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  if (state.status === 'error') return <NotFound />
  const r = state.data
  return (
    <>
      <Link to="/app/quizzes" className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="size-4" /> Quiz results
      </Link>
      <PageHeader
        title={r.quizTitle}
        description={`Submitted ${new Date(r.submittedAt).toLocaleString()}`}
        actions={<Link to={quizPath(r.courseSlug, r.quizId)} className="text-sm font-medium text-brand-600 hover:underline">Open quiz</Link>}
      />
      <AttemptReview result={r} quiz={quiz.data} />
    </>
  )
}
