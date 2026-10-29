import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Field, Input, Select, Textarea, Toggle } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type CourseEditor, type LessonEditor, type LessonFields, problemText } from '@/lib/admin'
import { slugify } from '@/lib/content'
import { videoSource } from '@/lib/learning'
import { useApi } from '@/lib/useApi'
import { NotFound } from '@/pages/StatusPage'

export default function LessonEditorPage() {
  const { id = '' } = useParams()
  const [state] = useApi<LessonEditor>(`/api/admin/lessons/${encodeURIComponent(id)}`)
  if (state.status === 'error') return <NotFound />
  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  return <Editor key={state.data.id} initial={state.data} />
}

function Editor({ initial }: { initial: LessonEditor }) {
  const navigate = useNavigate()
  const [course] = useApi<CourseEditor>(`/api/admin/courses/${initial.courseId}`)
  const [f, setF] = useState<LessonFields>(initial)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const set = <K extends keyof LessonFields>(k: K, v: LessonFields[K]) => setF((x) => ({ ...x, [k]: v }))
  const back = `/admin/courses/${initial.courseId}`
  const videoOk = !f.videoRef || videoSource(f.videoRef) !== null

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      setF(await api<LessonEditor>(`/api/admin/lessons/${initial.id}`, { method: 'PUT', json: f }))
      setMsg({ ok: true, text: 'Saved.' })
    } catch (err) {
      setMsg({ ok: false, text: problemText(err) })
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!window.confirm('Delete this draft lesson?')) return
    try {
      await api(`/api/admin/lessons/${initial.id}`, { method: 'DELETE' })
      navigate(back)
    } catch (err) {
      setMsg({ ok: false, text: problemText(err) })
    }
  }

  return (
    <>
      <Link to={back} className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="size-4" />{course.data?.title ?? 'Course'}
      </Link>
      <PageHeader title={f.title || 'Untitled lesson'} description="Lesson content is only sent to learners who can open it." />
      <form onSubmit={save} className="grid gap-6 lg:grid-cols-[1fr_18rem]">
        <Card className="grid gap-4">
          <Field label="Title">{(id) => <Input id={id} value={f.title} onChange={(e) => set('title', e.target.value)} required />}</Field>
          <Field label="Summary">{(id) => <Input id={id} value={f.summary ?? ''} onChange={(e) => set('summary', e.target.value || null)} />}</Field>
          <Field label="Video link" hint="YouTube, Google Drive or a direct https video file.">
            {(id) => (
              <Input id={id} type="url" value={f.videoRef ?? ''} onChange={(e) => set('videoRef', e.target.value || null)} aria-invalid={!videoOk} className={videoOk ? '' : 'border-danger'} />
            )}
          </Field>
          <Field label="Lesson text" hint="Shown below the video. Line breaks are kept.">
            {(id) => <Textarea id={id} value={f.body ?? ''} onChange={(e) => set('body', e.target.value || null)} className="min-h-72" />}
          </Field>
        </Card>
        <div className="flex flex-col gap-4">
          <Card className="grid gap-4">
            <Field label="Status">
              {(id) => (
                <Select id={id} value={f.status} onChange={(e) => set('status', e.target.value as LessonFields['status'])}>
                  <option value="draft">Draft</option>
                  <option value="published">Published</option>
                  <option value="archived">Archived</option>
                </Select>
              )}
            </Field>
            <Toggle checked={f.isPreview} onChange={(v) => set('isPreview', v)} label="Free preview (anyone can open)" />
            <Field label="Type">
              {(id) => (
                <Select id={id} value={f.kind} onChange={(e) => set('kind', e.target.value as LessonFields['kind'])}>
                  <option value="text">Text</option>
                  <option value="video">Video</option>
                  <option value="document">Document</option>
                  <option value="quiz">Quiz</option>
                </Select>
              )}
            </Field>
            <Field label="Module">
              {(id) => (
                <Select id={id} value={f.moduleId ?? ''} onChange={(e) => set('moduleId', e.target.value || null)}>
                  <option value="">Ungrouped</option>
                  {course.data?.modules.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
                </Select>
              )}
            </Field>
            <Field label="Duration (minutes)">
              {(id) => (
                <Input
                  id={id}
                  type="number"
                  min={0}
                  value={f.durationSeconds === null ? '' : Math.round(f.durationSeconds / 60)}
                  onChange={(e) => set('durationSeconds', e.target.value === '' ? null : Number(e.target.value) * 60)}
                />
              )}
            </Field>
            <Field label="URL slug">{(id) => <Input id={id} value={f.slug} onChange={(e) => set('slug', slugify(e.target.value))} required />}</Field>
          </Card>
          <div className="flex flex-wrap items-center gap-3">
            <Button type="submit" disabled={busy || !videoOk}>{busy && <Spinner className="size-4" />}Save lesson</Button>
            {initial.status === 'draft' && (
              <Button variant="ghost" onClick={remove} aria-label="Delete lesson"><Trash2 className="size-4" /></Button>
            )}
          </div>
          {msg && <p role="status" className={`text-sm ${msg.ok ? 'text-success' : 'text-danger'}`}>{msg.text}</p>}
        </div>
      </form>
    </>
  )
}
