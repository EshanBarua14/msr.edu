import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowDown, ArrowLeft, ArrowUp, ExternalLink, ListChecks, Pencil, Plus, Trash2 } from 'lucide-react'
import { lessonIcon } from '@/components/learn/lessonIcon'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Field, Input, Select, Textarea, Toggle } from '@/components/ui/Form'
import { slugify, statusTone } from '@/lib/content'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type Category, type CourseEditor, type CourseFields, type LessonRow, problemText } from '@/lib/admin'
import { useApi } from '@/lib/useApi'
import { CourseResourcesTab } from './CourseResourcesTab'
import { NotFound } from '@/pages/StatusPage'

function Details({ course, categories, onSaved }: { course: CourseEditor; categories: Category[]; onSaved: (c: CourseEditor) => void }) {
  const [f, setF] = useState<CourseFields>(course)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const set = <K extends keyof CourseFields>(k: K, v: CourseFields[K]) => setF((x) => ({ ...x, [k]: v }))

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      onSaved(await api<CourseEditor>(`/api/admin/courses/${course.id}`, { method: 'PUT', json: f }))
      setMsg({ ok: true, text: 'Saved.' })
    } catch (err) {
      setMsg({ ok: false, text: problemText(err) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <form onSubmit={save} className="grid gap-4 sm:grid-cols-2">
        <Field label="Title">{(id) => <Input id={id} value={f.title} onChange={(e) => set('title', e.target.value)} required />}</Field>
        <Field label="URL slug">{(id) => <Input id={id} value={f.slug} onChange={(e) => set('slug', slugify(e.target.value))} required />}</Field>
        <Field label="Summary" className="sm:col-span-2">
          {(id) => <Input id={id} value={f.summary ?? ''} onChange={(e) => set('summary', e.target.value || null)} maxLength={200} />}
        </Field>
        <Field label="Description" className="sm:col-span-2">
          {(id) => <Textarea id={id} value={f.description ?? ''} onChange={(e) => set('description', e.target.value || null)} />}
        </Field>
        <Field label="Cover image URL" hint="https only">
          {(id) => <Input id={id} type="url" value={f.coverUrl ?? ''} onChange={(e) => set('coverUrl', e.target.value || null)} />}
        </Field>
        <Field label="Category">
          {(id) => (
            <Select id={id} value={f.categoryId ?? ''} onChange={(e) => set('categoryId', e.target.value || null)}>
              <option value="">None</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Status" hint="Published courses appear in the catalogue. Archive instead of deleting.">
          {(id) => (
            <Select id={id} value={f.status} onChange={(e) => set('status', e.target.value as CourseFields['status'])}>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
              <option value="archived">Archived</option>
            </Select>
          )}
        </Field>
        <div className="flex items-end pb-2">
          <Toggle checked={f.isFree} onChange={(v) => set('isFree', v)} label="Free for signed-in learners" />
        </div>
        <div className="flex items-center gap-3 sm:col-span-2">
          <Button type="submit" disabled={busy}>{busy && <Spinner className="size-4" />}Save details</Button>
          {msg && <span role="status" className={`text-sm ${msg.ok ? 'text-success' : 'text-danger'}`}>{msg.text}</span>}
        </div>
      </form>
    </Card>
  )
}

function Syllabus({ course, onChange }: { course: CourseEditor; onChange: (c: CourseEditor) => void }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  const [moduleTitle, setModuleTitle] = useState('')
  const groups: { id: string | null; title: string }[] = [
    ...course.modules,
    ...(course.lessons.some((l) => !l.moduleId) || course.modules.length === 0 ? [{ id: null, title: 'Ungrouped lessons' }] : []),
  ]

  const run = async (fn: () => Promise<void>) => {
    setError(null)
    try {
      await fn()
    } catch (err) {
      setError(problemText(err))
    }
  }

  const reload = async () => onChange(await api<CourseEditor>(`/api/admin/courses/${course.id}`))

  /** Sends the full order; the server checks every module and lesson is listed exactly once. */
  const saveOrder = (modules: string[], lessons: LessonRow[]) =>
    run(async () =>
      onChange(
        await api<CourseEditor>(`/api/admin/courses/${course.id}/order`, {
          method: 'PUT',
          json: { modules, lessons: lessons.map((l) => ({ id: l.id, moduleId: l.moduleId })) },
        }),
      ),
    )

  const ordered = (lessons: LessonRow[]) =>
    groups.flatMap((g) => lessons.filter((l) => l.moduleId === g.id))

  function moveModule(index: number, delta: number) {
    const ids = course.modules.map((m) => m.id)
    const [m] = ids.splice(index, 1)
    ids.splice(index + delta, 0, m)
    void saveOrder(ids, course.lessons)
  }

  function moveLesson(lesson: LessonRow, delta: number) {
    const inGroup = course.lessons.filter((l) => l.moduleId === lesson.moduleId)
    const i = inGroup.findIndex((l) => l.id === lesson.id)
    const j = i + delta
    if (j < 0 || j >= inGroup.length) return
    ;[inGroup[i], inGroup[j]] = [inGroup[j], inGroup[i]]
    const others = course.lessons.filter((l) => l.moduleId !== lesson.moduleId)
    void saveOrder(course.modules.map((m) => m.id), ordered([...others, ...inGroup]))
  }

  function setModule(lesson: LessonRow, moduleId: string | null) {
    const lessons = course.lessons.filter((l) => l.id !== lesson.id).concat({ ...lesson, moduleId })
    void saveOrder(course.modules.map((m) => m.id), ordered(lessons))
  }

  const addModule = (e: FormEvent) => {
    e.preventDefault()
    void run(async () => {
      await api(`/api/admin/courses/${course.id}/modules`, { json: { title: moduleTitle } })
      setModuleTitle('')
      await reload()
    })
  }

  const renameModule = (id: string, current: string) => {
    const title = window.prompt('Module title', current)?.trim()
    if (title && title !== current) void run(async () => { await api(`/api/admin/modules/${id}`, { method: 'PUT', json: { title } }); await reload() })
  }

  const deleteModule = (id: string) => {
    if (window.confirm('Delete this module? Its lessons are kept and become ungrouped.'))
      void run(async () => { await api(`/api/admin/modules/${id}`, { method: 'DELETE' }); await reload() })
  }

  const addLesson = (moduleId: string | null) =>
    void run(async () => {
      const n = course.lessons.length + 1
      const body = { moduleId, slug: `lesson-${n}-${Date.now().toString(36)}`, title: `New lesson ${n}`, summary: null, kind: 'text', durationSeconds: null, isPreview: false, status: 'draft', body: null, videoRef: null }
      const created = await api<{ id: string }>(`/api/admin/courses/${course.id}/lessons`, { json: body })
      navigate(`/admin/lessons/${created.id}`)
    })

  return (
    <div className="flex flex-col gap-4">
      {error && <p role="alert" className="text-sm text-danger">{error}</p>}
      {groups.map((g) => {
        const lessons = course.lessons.filter((l) => l.moduleId === g.id)
        const mIndex = course.modules.findIndex((m) => m.id === g.id)
        return (
          <Card key={g.id ?? 'none'} className="p-0">
            <div className="flex items-center gap-2 border-b border-border px-5 py-3">
              <h3 className="flex-1 font-semibold">{g.title}</h3>
              {g.id && (
                <>
                  <Button size="sm" variant="ghost" aria-label="Move module up" disabled={mIndex === 0} onClick={() => moveModule(mIndex, -1)}><ArrowUp className="size-4" /></Button>
                  <Button size="sm" variant="ghost" aria-label="Move module down" disabled={mIndex === course.modules.length - 1} onClick={() => moveModule(mIndex, 1)}><ArrowDown className="size-4" /></Button>
                  <Button size="sm" variant="ghost" aria-label="Rename module" onClick={() => renameModule(g.id!, g.title)}><Pencil className="size-4" /></Button>
                  <Button size="sm" variant="ghost" aria-label="Delete module" onClick={() => deleteModule(g.id!)}><Trash2 className="size-4" /></Button>
                </>
              )}
            </div>
            <ul>
              {lessons.map((l, i) => {
                const Icon = lessonIcon[l.kind]
                return (
                  <li key={l.id} className="flex flex-wrap items-center gap-2 border-b border-border px-5 py-2.5 text-sm last:border-0">
                    <Icon className="size-4 text-muted" />
                    <Link to={`/admin/lessons/${l.id}`} className="min-w-0 flex-1 truncate font-medium hover:text-brand-600">{l.title}</Link>
                    {l.isPreview && <Badge tone="brand">Preview</Badge>}
                    <Badge tone={statusTone[l.status]}>{l.status}</Badge>
                    {course.modules.length > 0 && (
                      <div className="w-36">
                        <Select aria-label="Module" className="h-8 text-xs" value={l.moduleId ?? ''} onChange={(e) => setModule(l, e.target.value || null)}>
                          <option value="">Ungrouped</option>
                          {course.modules.map((m) => <option key={m.id} value={m.id}>{m.title}</option>)}
                        </Select>
                      </div>
                    )}
                    <Button size="sm" variant="ghost" aria-label="Move lesson up" disabled={i === 0} onClick={() => moveLesson(l, -1)}><ArrowUp className="size-4" /></Button>
                    <Button size="sm" variant="ghost" aria-label="Move lesson down" disabled={i === lessons.length - 1} onClick={() => moveLesson(l, 1)}><ArrowDown className="size-4" /></Button>
                  </li>
                )
              })}
            </ul>
            <div className="px-5 py-3">
              <Button size="sm" variant="secondary" onClick={() => addLesson(g.id)}><Plus className="size-4" />Add lesson</Button>
            </div>
          </Card>
        )
      })}
      <form onSubmit={addModule} className="flex gap-2">
        <div className="w-full max-w-xs"><Input aria-label="New module title" placeholder="New module title" value={moduleTitle} onChange={(e) => setModuleTitle(e.target.value)} /></div>
        <Button type="submit" variant="secondary" disabled={!moduleTitle.trim()}><Plus className="size-4" />Add module</Button>
      </form>
    </div>
  )
}

function Quizzes({ course }: { course: CourseEditor }) {
  const navigate = useNavigate()
  const [error, setError] = useState<string | null>(null)
  async function add() {
    try {
      const body = { title: 'New quiz', description: null, lessonId: null, passScorePct: 60, timeLimitSeconds: null, maxAttempts: null, isPreview: false, status: 'draft' }
      const { id } = await api<{ id: string }>(`/api/admin/courses/${course.id}/quizzes`, { json: body })
      navigate(`/admin/quizzes/${id}`)
    } catch (err) {
      setError(problemText(err))
    }
  }
  return (
    <Card className="p-0">
      <ul>
        {course.quizzes.map((q) => (
          <li key={q.id} className="flex items-center gap-2 border-b border-border px-5 py-3 text-sm">
            <ListChecks className="size-4 text-muted" />
            <Link to={`/admin/quizzes/${q.id}`} className="flex-1 font-medium hover:text-brand-600">{q.title}</Link>
            <span className="text-xs text-muted">{q.questions} questions</span>
            {q.pendingReview > 0 && <Badge tone="warning">{q.pendingReview} to review</Badge>}
            <Badge tone={statusTone[q.status]}>{q.status}</Badge>
          </li>
        ))}
      </ul>
      <div className="flex items-center gap-3 px-5 py-3">
        <Button size="sm" variant="secondary" onClick={add}><Plus className="size-4" />Add quiz</Button>
        {error && <span className="text-sm text-danger">{error}</span>}
      </div>
    </Card>
  )
}

export default function CourseEditorPage() {
  const { id = '' } = useParams()
  const [state] = useApi<CourseEditor>(`/api/admin/courses/${encodeURIComponent(id)}`)
  if (state.status === 'error') return <NotFound />
  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  return <Editor key={state.data.id} initial={state.data} />
}

function Editor({ initial }: { initial: CourseEditor }) {
  const [categories] = useApi<Category[]>('/api/admin/categories')
  const [course, setCourse] = useState(initial)
  const [tab, setTab] = useState<'syllabus' | 'details' | 'quizzes' | 'resources'>('syllabus')

  const tabs = [
    { key: 'syllabus', label: `Syllabus (${course.lessons.length})` },
    { key: 'quizzes', label: `Quizzes (${course.quizzes.length})` },
    { key: 'resources', label: 'Resources' },
    { key: 'details', label: 'Details' },
  ] as const

  return (
    <>
      <Link to="/admin/courses" className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-ink"><ArrowLeft className="size-4" />Courses</Link>
      <PageHeader
        title={course.title}
        description={`/${course.slug}`}
        actions={
          <div className="flex items-center gap-3">
            <Badge tone={statusTone[course.status]}>{course.status}</Badge>
            <Link to={`/courses/${course.slug}`} target="_blank" className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:underline">
              View <ExternalLink className="size-3.5" />
            </Link>
          </div>
        }
      />
      <div role="tablist" className="mb-6 flex gap-1 overflow-x-auto rounded-full bg-surface-2 p-1 text-sm sm:w-fit">
        {tabs.map((t) => (
          <button
            key={t.key}
            role="tab"
            aria-selected={tab === t.key}
            onClick={() => setTab(t.key)}
            className={`flex-1 rounded-full px-4 py-1.5 whitespace-nowrap font-medium transition-colors sm:flex-none ${tab === t.key ? 'bg-surface shadow' : 'text-muted hover:text-ink'}`}
          >
            {t.label}
          </button>
        ))}
      </div>
      {tab === 'syllabus' && <Syllabus course={course} onChange={setCourse} />}
      {tab === 'quizzes' && <Quizzes course={course} />}
      {tab === 'resources' && <CourseResourcesTab course={course} />}
      {tab === 'details' && <Details course={course} categories={categories.data ?? []} onSaved={setCourse} />}
    </>
  )
}
