import { Link } from 'react-router-dom'
import { PlayCircle } from 'lucide-react'
import { CourseCover } from '@/components/learn/CourseCard'
import { Badge } from '@/components/ui/Badge'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { type CourseProgress, lessonPath, percent } from '@/lib/learning'

export function CourseProgressRow({ c }: { c: CourseProgress }) {
  const pct = percent(c.completedLessons, c.totalLessons)
  const to = c.lastLesson ? lessonPath(c.slug, c.lastLesson.id) : `/courses/${c.slug}`
  return (
    <Link to={to} className="group flex items-center gap-4 rounded-2xl p-2 hover:bg-surface-2">
      <div className="w-28 shrink-0 overflow-hidden rounded-xl">
        <CourseCover url={c.coverUrl} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-2">
          <p className="truncate font-medium group-hover:text-brand-600">{c.title}</p>
          {!c.hasAccess && <Badge>Preview</Badge>}
        </div>
        {c.lastLesson && <p className="truncate text-xs text-muted">Last: {c.lastLesson.title}</p>}
        <div className="mt-2 flex items-center gap-3">
          <ProgressBar value={pct} className="flex-1" />
          <span className="text-xs font-semibold tabular-nums">{pct}%</span>
        </div>
      </div>
      <PlayCircle className="size-6 shrink-0 text-brand-500 opacity-0 transition-opacity group-hover:opacity-100" />
    </Link>
  )
}
