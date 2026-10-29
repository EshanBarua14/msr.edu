import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { ArrowLeft, ArrowRight, CheckCircle2, Download, Lock } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { AccessPrompt } from '@/components/learn/AccessPrompt'
import { lessonIcon } from '@/components/learn/lessonIcon'
import { Button } from '@/components/ui/Button'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { ProgressBar } from '@/components/ui/ProgressBar'
import { FullPageSpinner, Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type CourseDetail, flatLessons, formatBytes, type LessonContent, type LessonResource, lessonPath, percent, videoSource } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

interface ProgressResponse {
  lessonId: string
  progressPct: number
  completed: boolean
}

function Video({ refUrl, onEnded }: { refUrl: string; onEnded: () => void }) {
  const source = videoSource(refUrl)
  if (!source) return null
  return (
    <div className="aspect-video overflow-hidden rounded-[var(--radius-card)] bg-black">
      {source.kind === 'embed' ? (
        <iframe
          src={source.src}
          title="Lesson video"
          className="size-full"
          allow="encrypted-media; picture-in-picture; fullscreen"
          referrerPolicy="strict-origin-when-cross-origin"
          sandbox="allow-scripts allow-same-origin allow-presentation"
        />
      ) : (
        <video src={source.src} controls controlsList="nodownload" className="size-full" onEnded={onEnded} />
      )}
    </div>
  )
}

export default function LessonPlayer() {
  const { slug = '', lessonId = '' } = useParams()
  const navigate = useNavigate()
  const { status } = useAuth()
  const signedIn = status === 'signed_in'
  const [course] = useApi<CourseDetail>(`/api/courses/${encodeURIComponent(slug)}`)
  const [lesson] = useApi<LessonContent>(`/api/lessons/${encodeURIComponent(lessonId)}/content`)
  const [resources] = useApi<LessonResource[]>(`/api/lessons/${encodeURIComponent(lessonId)}/resources`)
  // Completed on the server when the page loaded, plus what was completed here since.
  const [justCompleted, setJustCompleted] = useState<Set<string>>(new Set())
  const [saving, setSaving] = useState(false)
  const completed = new Set([...(course.data?.completedLessonIds ?? []), ...justCompleted])

  // Opening a lesson counts as starting it and moves "Continue learning" here.
  useEffect(() => {
    if (signedIn && lesson.status === 'ok') {
      api(`/api/lessons/${lessonId}/progress`, { json: { progressPct: 10, completed: false } }).catch(() => {})
    }
  }, [signedIn, lesson.status, lessonId])

  if (course.status === 'loading' || (lesson.status === 'loading' && !lesson.data)) return <FullPageSpinner />
  if (course.status === 'error') return <AccessPrompt status={course.error.status} />
  if (lesson.status === 'error') return <AccessPrompt status={lesson.error.status} courseSlug={slug} />

  const d = course.data
  const all = flatLessons(d)
  const index = all.findIndex((l) => l.id === lessonId)
  const prev = all.slice(0, Math.max(index, 0)).reverse().find((l) => !l.locked)
  const next = all.slice(index + 1).find((l) => !l.locked)
  const isDone = completed.has(lessonId)
  const content = lesson.data

  async function complete() {
    if (!signedIn || isDone) return
    setSaving(true)
    try {
      const res = await api<ProgressResponse>(`/api/lessons/${lessonId}/progress`, { json: { progressPct: 100, completed: true } })
      if (res.completed) setJustCompleted((s) => new Set(s).add(lessonId))
    } finally {
      setSaving(false)
    }
  }

  async function completeAndContinue() {
    await complete()
    if (next) navigate(lessonPath(slug, next.id))
  }

  return (
    <div className="mx-auto grid max-w-7xl gap-6 px-4 py-8 sm:px-6 lg:grid-cols-[1fr_20rem]">
      <div className="min-w-0">
        <Link to={`/courses/${slug}`} className="inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
          <ArrowLeft className="size-4" /> {d.course.title}
        </Link>
        <AnimatePresence mode="wait">
          <motion.article
            key={lessonId}
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="mt-4 flex flex-col gap-6"
          >
            <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">{content.title}</h1>
            {content.videoRef && <Video refUrl={content.videoRef} onEnded={complete} />}
            {content.body && (
              <Card>
                <div className="leading-relaxed whitespace-pre-line">{content.body}</div>
              </Card>
            )}
            {!content.videoRef && !content.body && !resources.data?.length && <p className="text-muted">This lesson has no content yet.</p>}
            {resources.data && resources.data.length > 0 && (
              <Card className="p-0">
                <h2 className="border-b border-border px-5 py-3 text-sm font-semibold">Resources</h2>
                <ul>
                  {resources.data.map((r) => (
                    <li key={r.id} className="border-b border-border last:border-0">
                      <a href={`/api/resources/${r.id}/download`} className="flex items-center gap-3 px-5 py-3 text-sm hover:bg-surface-2">
                        <Download className="size-4 text-brand-500" />
                        <span className="flex-1">{r.name}</span>
                        {formatBytes(r.sizeBytes) && <span className="text-xs text-muted">{formatBytes(r.sizeBytes)}</span>}
                      </a>
                    </li>
                  ))}
                </ul>
              </Card>
            )}

            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-6">
              {prev ? (
                <Link to={lessonPath(slug, prev.id)} className={buttonClass('ghost')}>
                  <ArrowLeft className="size-4" /> Previous
                </Link>
              ) : (
                <span />
              )}
              {signedIn ? (
                <Button onClick={completeAndContinue} disabled={saving || (isDone && !next)}>
                  {saving ? <Spinner className="size-4" /> : isDone ? <CheckCircle2 className="size-4" /> : null}
                  {isDone ? (next ? 'Next lesson' : 'Completed') : next ? 'Complete and continue' : 'Mark complete'}
                  {next && <ArrowRight className="size-4" />}
                </Button>
              ) : (
                <Link to={`/login?next=${encodeURIComponent(lessonPath(slug, lessonId))}`} className={buttonClass()}>
                  Sign in to track progress
                </Link>
              )}
            </div>
          </motion.article>
        </AnimatePresence>
      </div>

      <aside className="lg:sticky lg:top-24 lg:self-start">
        <Card className="p-0">
          <div className="border-b border-border p-4">
            <p className="text-sm font-semibold">Course content</p>
            {signedIn && (
              <ProgressBar className="mt-3" value={percent(all.filter((l) => completed.has(l.id)).length, all.length)} />
            )}
          </div>
          <nav aria-label="Lessons" className="max-h-[60dvh] overflow-y-auto p-2">
            {d.modules.map((m) => (
              <div key={m.id ?? 'loose'} className="mb-2">
                <p className="px-3 py-2 text-xs font-semibold tracking-wide text-muted uppercase">{m.title}</p>
                {m.lessons.map((l) => {
                  const Icon = completed.has(l.id) ? CheckCircle2 : l.locked ? Lock : lessonIcon[l.kind]
                  const cls = `flex items-center gap-2 rounded-xl px-3 py-2 text-sm ${
                    l.id === lessonId ? 'bg-brand-500/10 font-medium text-brand-600' : l.locked ? 'text-muted' : 'hover:bg-surface-2'
                  }`
                  const inner = (
                    <>
                      <Icon className={`size-4 shrink-0 ${completed.has(l.id) ? 'text-success' : ''}`} />
                      <span className="truncate">{l.title}</span>
                    </>
                  )
                  return l.locked ? (
                    <div key={l.id} className={cls}>{inner}</div>
                  ) : (
                    <Link key={l.id} to={lessonPath(slug, l.id)} className={cls} aria-current={l.id === lessonId ? 'page' : undefined}>
                      {inner}
                    </Link>
                  )
                })}
              </div>
            ))}
          </nav>
        </Card>
      </aside>
    </div>
  )
}
