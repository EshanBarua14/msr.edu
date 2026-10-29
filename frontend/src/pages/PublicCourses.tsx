import { useMemo, useState } from 'react'
import { motion, type Variants } from 'motion/react'
import { BookOpen, Search } from 'lucide-react'
import { CourseCard } from '@/components/learn/CourseCard'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import type { CourseSummary } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

const grid: Variants = { hidden: {}, show: { transition: { staggerChildren: 0.05 } } }
const cell: Variants = { hidden: { opacity: 0, y: 12 }, show: { opacity: 1, y: 0 } }

export default function PublicCourses() {
  const [state] = useApi<CourseSummary[]>('/api/courses')
  const [query, setQuery] = useState('')
  const [category, setCategory] = useState<string | null>(null)

  const courses = useMemo(() => state.data ?? [], [state.data])
  const categories = useMemo(() => [...new Set(courses.map((c) => c.category).filter((c): c is string => !!c))], [courses])
  const shown = courses.filter(
    (c) =>
      (!category || c.category === category) &&
      (!query || `${c.title} ${c.summary ?? ''}`.toLowerCase().includes(query.toLowerCase())),
  )

  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <PageHeader title="Courses" description="Browse the catalogue and open any free preview lesson." />

      {state.status === 'loading' && (
        <div className="grid place-items-center py-20 text-brand-500">
          <Spinner className="size-8" />
        </div>
      )}
      {state.status === 'error' && <p className="text-danger">Could not load courses. Try again in a moment.</p>}
      {state.status === 'ok' && courses.length === 0 && (
        <EmptyState icon={BookOpen} title="No courses published yet">
          Published courses and their free previews will appear here.
        </EmptyState>
      )}

      {courses.length > 0 && (
        <>
          <div className="mb-6 flex flex-wrap items-center gap-3">
            <label className="relative w-full sm:w-72">
              <span className="sr-only">Search courses</span>
              <Search className="pointer-events-none absolute top-1/2 left-3.5 size-4 -translate-y-1/2 text-muted" />
              <input
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Search courses"
                className="h-11 w-full rounded-full border border-border bg-surface pr-4 pl-10 text-sm outline-none focus:border-brand-500"
              />
            </label>
            {categories.length > 1 &&
              [null, ...categories].map((c) => (
                <button
                  key={c ?? 'all'}
                  type="button"
                  onClick={() => setCategory(c)}
                  aria-pressed={category === c}
                  className={`h-9 rounded-full px-4 text-sm font-medium transition-colors ${
                    category === c ? 'bg-brand-600 text-white' : 'bg-surface-2 text-muted hover:text-ink'
                  }`}
                >
                  {c ?? 'All'}
                </button>
              ))}
          </div>
          {shown.length === 0 ? (
            <p className="py-12 text-center text-muted">No courses match your search.</p>
          ) : (
            <motion.div variants={grid} initial="hidden" animate="show" className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
              {shown.map((c) => (
                <motion.div key={c.id} variants={cell}>
                  <CourseCard course={c} />
                </motion.div>
              ))}
            </motion.div>
          )}
        </>
      )}
    </div>
  )
}
