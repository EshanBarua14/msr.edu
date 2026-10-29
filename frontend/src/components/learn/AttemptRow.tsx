import { Link } from 'react-router-dom'
import { Badge } from '@/components/ui/Badge'
import { type AttemptSummary, percent } from '@/lib/learning'

export function AttemptRow({ a }: { a: AttemptSummary }) {
  return (
    <Link to={`/app/quizzes/${a.id}`} className="flex items-center gap-3 rounded-2xl p-3 hover:bg-surface-2">
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{a.quizTitle}</p>
        <p className="text-xs text-muted">{new Date(a.submittedAt).toLocaleString()}</p>
      </div>
      <span className="text-sm font-semibold tabular-nums">{percent(a.score, a.maxScore)}%</span>
      <Badge tone={a.passed ? 'success' : 'danger'}>{a.passed ? 'Passed' : 'Failed'}</Badge>
    </Link>
  )
}
