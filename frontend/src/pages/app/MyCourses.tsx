import { Link } from 'react-router-dom'
import { BookOpen } from 'lucide-react'
import { CourseProgressRow } from '@/components/learn/CourseProgressRow'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import type { MyLearning } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

export default function MyCourses() {
  const [state] = useApi<MyLearning>('/api/me/learning')
  return (
    <>
      <PageHeader title="My courses" description="Everything you have started, most recent first." />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load your courses.</p>}
      {state.status === 'ok' &&
        (state.data.courses.length === 0 ? (
          <EmptyState icon={BookOpen} title="No courses started yet">
            <Link to="/courses" className="font-medium text-brand-600 hover:underline">Browse the catalogue</Link>
          </EmptyState>
        ) : (
          <Card className="flex flex-col gap-1 p-3">
            {state.data.courses.map((c) => <CourseProgressRow key={c.courseId} c={c} />)}
          </Card>
        ))}
    </>
  )
}
