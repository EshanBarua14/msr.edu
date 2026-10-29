import { BookOpen } from 'lucide-react'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'

export default function PublicCourses() {
  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <PageHeader title="Courses" description="Browse the catalogue and open any free preview lesson." />
      <EmptyState icon={BookOpen} title="No courses published yet">
        Published courses and their free previews will appear here.
      </EmptyState>
    </div>
  )
}
