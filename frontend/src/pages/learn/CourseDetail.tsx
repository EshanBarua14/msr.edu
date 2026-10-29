import { Link, useParams } from 'react-router-dom'
import { motion } from 'motion/react'
import { CheckCircle2, ListChecks, Lock } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { AccessPrompt } from '@/components/learn/AccessPrompt'
import { CourseCover } from '@/components/learn/CourseCard'
import { lessonIcon } from '@/components/learn/lessonIcon'
import { Badge } from '@/components/ui/Badge'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { FullPageSpinner } from '@/components/ui/Spinner'
import { type CourseDetail as Detail, flatLessons, formatDuration, lessonPath, percent, quizPath } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

export default function CourseDetail() {
  const { slug = '' } = useParams()
  const { status } = useAuth()
  const [state] = useApi<Detail>(`/api/courses/${encodeURIComponent(slug)}`)

  if (state.status === 'loading') return <FullPageSpinner />
  if (state.status === 'error') return <AccessPrompt status={state.error.status} />

  const d = state.data
  const lessons = flatLessons(d)
  const done = new Set(d.completedLessonIds)
  const progress = percent(lessons.filter((l) => done.has(l.id)).length, lessons.length)
  const next = lessons.find((l) => !l.locked && !done.has(l.id)) ?? lessons.find((l) => !l.locked)
  const signedIn = status === 'signed_in'

  return (
    <div className="mx-auto max-w-6xl px-4 py-10 sm:px-6">
      <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} className="grid gap-8 lg:grid-cols-[1fr_22rem]">
        <div>
          <div className="flex flex-wrap gap-2">
            {d.course.category && <Badge>{d.course.category}</Badge>}
            {d.course.isFree && <Badge tone="success">Free</Badge>}
            {d.hasAccess && !d.course.isFree && <Badge tone="brand">Unlocked</Badge>}
          </div>
          <h1 className="mt-3 text-3xl font-bold tracking-tight sm:text-4xl">{d.course.title}</h1>
          {d.course.summary && <p className="mt-3 text-lg text-muted">{d.course.summary}</p>}
          {d.description && <p className="mt-6 whitespace-pre-line leading-relaxed">{d.description}</p>}

          <h2 className="mt-10 mb-4 text-xl font-semibold">Syllabus</h2>
          <div className="flex flex-col gap-4">
            {d.modules.map((m) => (
              <Card key={m.id ?? 'loose'} className="p-0">
                <h3 className="border-b border-border px-5 py-4 font-semibold">{m.title}</h3>
                <ul>
                  {m.lessons.map((l) => {
                    const Icon = done.has(l.id) ? CheckCircle2 : l.locked ? Lock : lessonIcon[l.kind]
                    const row = (
                      <>
                        <Icon className={`size-4 shrink-0 ${done.has(l.id) ? 'text-success' : 'text-muted'}`} />
                        <span className="flex-1">{l.title}</span>
                        {l.isPreview && !d.hasAccess && <Badge tone="brand">Preview</Badge>}
                        {formatDuration(l.durationSeconds) && <span className="text-xs text-muted">{formatDuration(l.durationSeconds)}</span>}
                      </>
                    )
                    return (
                      <li key={l.id} className="border-b border-border last:border-0">
                        {l.locked ? (
                          <div className="flex items-center gap-3 px-5 py-3 text-sm text-muted">{row}</div>
                        ) : (
                          <Link to={lessonPath(slug, l.id)} className="flex items-center gap-3 px-5 py-3 text-sm hover:bg-surface-2">
                            {row}
                          </Link>
                        )}
                      </li>
                    )
                  })}
                </ul>
              </Card>
            ))}
            {d.quizzes.length > 0 && (
              <Card className="p-0">
                <h3 className="border-b border-border px-5 py-4 font-semibold">Quizzes</h3>
                <ul>
                  {d.quizzes.map((q) => (
                    <li key={q.id} className="border-b border-border last:border-0">
                      {q.locked ? (
                        <div className="flex items-center gap-3 px-5 py-3 text-sm text-muted">
                          <Lock className="size-4" />
                          <span className="flex-1">{q.title}</span>
                        </div>
                      ) : (
                        <Link to={quizPath(slug, q.id)} className="flex items-center gap-3 px-5 py-3 text-sm hover:bg-surface-2">
                          <ListChecks className="size-4 text-muted" />
                          <span className="flex-1">{q.title}</span>
                          <span className="text-xs text-muted">{q.questionCount} questions</span>
                        </Link>
                      )}
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </div>
        </div>

        <aside className="lg:sticky lg:top-24 lg:self-start">
          <Card className="overflow-hidden p-0">
            <CourseCover url={d.course.coverUrl} />
            <div className="flex flex-col gap-4 p-5">
              {signedIn && d.hasAccess && (
                <div>
                  <div className="mb-2 flex justify-between text-sm">
                    <span className="text-muted">Your progress</span>
                    <span className="font-semibold tabular-nums">{progress}%</span>
                  </div>
                  <ProgressBar value={progress} />
                </div>
              )}
              {next && (
                <Link to={lessonPath(slug, next.id)} className={buttonClass('primary', 'md', 'w-full')}>
                  {done.size > 0 ? 'Continue' : d.hasAccess ? 'Start course' : 'Watch free preview'}
                </Link>
              )}
              {!d.hasAccess && (
                <Link to="/pricing" className={buttonClass(next ? 'secondary' : 'primary', 'md', 'w-full')}>
                  Unlock full course
                </Link>
              )}
              <p className="text-center text-xs text-muted">
                {lessons.length} {lessons.length === 1 ? 'lesson' : 'lessons'}{d.quizzes.length > 0 && ` · ${d.quizzes.length} ${d.quizzes.length === 1 ? 'quiz' : 'quizzes'}`}
              </p>
            </div>
          </Card>
        </aside>
      </motion.div>
    </div>
  )
}
