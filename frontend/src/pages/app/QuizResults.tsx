import { ListChecks } from 'lucide-react'
import { AttemptRow } from '@/components/learn/AttemptRow'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import type { AttemptSummary } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

export default function QuizResults() {
  const [state] = useApi<AttemptSummary[]>('/api/me/quiz-attempts')
  return (
    <>
      <PageHeader title="Quiz results" description="Every submitted attempt, graded on the server." />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load your results.</p>}
      {state.status === 'ok' &&
        (state.data.length === 0 ? (
          <EmptyState icon={ListChecks} title="No quiz attempts yet" />
        ) : (
          <Card className="flex flex-col p-2">
            {state.data.map((a) => <AttemptRow key={a.id} a={a} />)}
          </Card>
        ))}
    </>
  )
}
