import { Link } from 'react-router-dom'
import { motion } from 'motion/react'
import { BookOpen } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import type { CourseSummary } from '@/lib/learning'

export function CourseCover({ url, className = '' }: { url: string | null; className?: string }) {
  return url ? (
    <img src={url} alt="" loading="lazy" className={`aspect-video w-full object-cover ${className}`} />
  ) : (
    <div className={`grid aspect-video w-full place-items-center bg-gradient-to-br from-brand-500/20 via-brand-400/10 to-accent-400/20 ${className}`}>
      <BookOpen className="size-10 text-brand-500" />
    </div>
  )
}

export function CourseCard({ course }: { course: CourseSummary }) {
  return (
    <motion.div whileHover={{ y: -4 }} transition={{ type: 'spring', stiffness: 300, damping: 24 }}>
      <Link
        to={`/courses/${course.slug}`}
        className="group block h-full overflow-hidden rounded-[var(--radius-card)] border border-border bg-surface shadow-[var(--shadow-card)] focus-visible:outline-2 focus-visible:outline-brand-500"
      >
        <CourseCover url={course.coverUrl} />
        <div className="flex flex-col gap-2 p-5">
          <div className="flex flex-wrap gap-2">
            {course.category && <Badge>{course.category}</Badge>}
            {course.isFree && <Badge tone="success">Free</Badge>}
          </div>
          <h3 className="font-semibold group-hover:text-brand-600">{course.title}</h3>
          {course.summary && <p className="line-clamp-2 text-sm text-muted">{course.summary}</p>}
        </div>
      </Link>
    </motion.div>
  )
}
