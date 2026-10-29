import { Link } from 'react-router-dom'
import { ListChecks } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import type { QuizListRow } from '@/lib/admin'
import { statusTone } from '@/lib/content'
import { useApi } from '@/lib/useApi'

export default function QuizList() {
  const [state] = useApi<QuizListRow[]>('/api/admin/quizzes')
  const pending = state.data?.reduce((n, q) => n + q.pendingReview, 0) ?? 0
  return (
    <>
      <PageHeader
        title="Quizzes & questions"
        description={pending > 0 ? `${pending} AI-drafted questions are waiting for review.` : 'Add quizzes from a course’s Quizzes tab.'}
      />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'ok' && state.data.length === 0 && (
        <EmptyState icon={ListChecks} title="No quizzes yet">
          Open a course and use <Link to="/admin/courses" className="font-medium text-brand-600 hover:underline">Quizzes → Add quiz</Link>.
        </EmptyState>
      )}
      {state.status === 'ok' && state.data.length > 0 && (
        <Card className="p-0">
          {state.data.map((q) => (
            <Link key={q.id} to={`/admin/quizzes/${q.id}`} className="flex items-center gap-3 border-b border-border px-5 py-3 text-sm last:border-0 hover:bg-surface-2">
              <div className="min-w-0 flex-1">
                <p className="truncate font-medium">{q.title}</p>
                <p className="truncate text-xs text-muted">{q.courseTitle}</p>
              </div>
              <span className="text-xs text-muted">{q.questions} questions</span>
              {q.pendingReview > 0 && <Badge tone="warning">{q.pendingReview} to review</Badge>}
              <Badge tone={statusTone[q.status]}>{q.status}</Badge>
            </Link>
          ))}
        </Card>
      )}
    </>
  )
}
