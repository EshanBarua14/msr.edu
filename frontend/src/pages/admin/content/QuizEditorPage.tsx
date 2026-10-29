import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Check, CheckCircle2, Circle, Pencil, Plus, Sparkles, Trash2, X } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Field, Input, Select, Textarea, Toggle } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type CourseEditor, type OptionDto, type QuestionDto, type QuizEditor, type QuizFields, problemText } from '@/lib/admin'
import { useApi } from '@/lib/useApi'
import { NotFound } from '@/pages/StatusPage'

const reviewTone = { draft: 'warning', approved: 'success', rejected: 'danger' } as const
const blankOptions = (): OptionDto[] => [
  { id: null, label: '', isCorrect: true },
  { id: null, label: '', isCorrect: false },
]

function QuestionForm({ initial, onSubmit, onCancel, submitLabel }: {
  initial?: QuestionDto
  onSubmit: (q: { prompt: string; explanation: string | null; marks: number; options: OptionDto[] }) => Promise<void>
  onCancel?: () => void
  submitLabel: string
}) {
  const [prompt, setPrompt] = useState(initial?.prompt ?? '')
  const [explanation, setExplanation] = useState(initial?.explanation ?? '')
  const [marks, setMarks] = useState(initial?.marks ?? 1)
  const [options, setOptions] = useState<OptionDto[]>(initial?.options ?? blankOptions())
  const [busy, setBusy] = useState(false)
  const setOption = (i: number, o: Partial<OptionDto>) => setOptions((all) => all.map((x, j) => (j === i ? { ...x, ...o } : x)))

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    try {
      await onSubmit({ prompt, explanation: explanation || null, marks, options })
      if (!initial) {
        setPrompt('')
        setExplanation('')
        setOptions(blankOptions())
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <form onSubmit={submit} className="grid gap-4">
      <Field label="Question">{(id) => <Textarea id={id} value={prompt} onChange={(e) => setPrompt(e.target.value)} className="min-h-20" required />}</Field>
      <fieldset className="grid gap-2">
        <legend className="mb-1.5 text-sm font-medium">Options <span className="font-normal text-muted">(tick the correct one)</span></legend>
        {options.map((o, i) => (
          <div key={i} className="flex items-center gap-2">
            <button
              type="button"
              aria-label={o.isCorrect ? 'Correct answer' : 'Mark as correct'}
              aria-pressed={o.isCorrect}
              onClick={() => setOption(i, { isCorrect: !o.isCorrect })}
              className={o.isCorrect ? 'text-success' : 'text-muted hover:text-ink'}
            >
              {o.isCorrect ? <CheckCircle2 className="size-5" /> : <Circle className="size-5" />}
            </button>
            <Input aria-label={`Option ${i + 1}`} value={o.label} onChange={(e) => setOption(i, { label: e.target.value })} />
            <Button size="sm" variant="ghost" aria-label="Remove option" disabled={options.length <= 2} onClick={() => setOptions((all) => all.filter((_, j) => j !== i))}>
              <X className="size-4" />
            </Button>
          </div>
        ))}
        <Button size="sm" variant="ghost" className="w-fit" onClick={() => setOptions((all) => [...all, { id: null, label: '', isCorrect: false }])}>
          <Plus className="size-4" />Add option
        </Button>
      </fieldset>
      <div className="grid gap-4 sm:grid-cols-[1fr_8rem]">
        <Field label="Explanation" hint="Shown to learners after they submit.">
          {(id) => <Input id={id} value={explanation} onChange={(e) => setExplanation(e.target.value)} />}
        </Field>
        <Field label="Marks">{(id) => <Input id={id} type="number" min={0.5} step={0.5} value={marks} onChange={(e) => setMarks(Number(e.target.value))} />}</Field>
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={busy}>{busy && <Spinner className="size-4" />}{submitLabel}</Button>
        {onCancel && <Button variant="ghost" onClick={onCancel}>Cancel</Button>}
      </div>
    </form>
  )
}

function QuestionCard({ q, index, onChange, onError }: { q: QuestionDto; index: number; onChange: (quiz: QuizEditor | null) => void; onError: (m: string) => void }) {
  const [editing, setEditing] = useState(false)
  const call = async (fn: () => Promise<QuizEditor>) => {
    try {
      onChange(await fn())
    } catch (err) {
      onError(problemText(err))
      throw err
    }
  }
  const remove = () => {
    if (!window.confirm('Delete this question?')) return
    api(`/api/admin/questions/${q.id}`, { method: 'DELETE' }).then(() => onChange(null), (err: unknown) => onError(problemText(err)))
  }
  const review = (approve: boolean) => call(() => api<QuizEditor>(`/api/admin/questions/${q.id}/review`, { json: { approve } })).catch(() => {})

  return (
    <Card className={q.reviewStatus === 'draft' ? 'border-warning/50' : q.reviewStatus === 'rejected' ? 'opacity-60' : ''}>
      {editing ? (
        <QuestionForm
          initial={q}
          submitLabel="Save question"
          onCancel={() => setEditing(false)}
          onSubmit={(body) => call(() => api<QuizEditor>(`/api/admin/questions/${q.id}`, { method: 'PUT', json: body })).then(() => setEditing(false))}
        />
      ) : (
        <>
          <div className="flex flex-wrap items-start gap-2">
            <p className="flex-1 font-medium whitespace-pre-line">{index + 1}. {q.prompt}</p>
            {q.aiGenerated && <Badge tone="brand"><Sparkles className="mr-1 size-3" />AI draft</Badge>}
            <Badge tone={reviewTone[q.reviewStatus]}>{q.reviewStatus}</Badge>
            <span className="text-xs text-muted">{q.marks} mark{q.marks === 1 ? '' : 's'}</span>
          </div>
          <ul className="mt-3 grid gap-1.5 text-sm">
            {q.options.map((o) => (
              <li key={o.id} className="flex items-center gap-2">
                {o.isCorrect ? <CheckCircle2 className="size-4 text-success" /> : <Circle className="size-4 text-muted" />}
                {o.label}
              </li>
            ))}
          </ul>
          {q.explanation && <p className="mt-3 rounded-xl bg-surface-2 p-3 text-sm">{q.explanation}</p>}
          <div className="mt-4 flex flex-wrap gap-2">
            {q.reviewStatus !== 'approved' && <Button size="sm" onClick={() => review(true)}><Check className="size-4" />Approve</Button>}
            {q.reviewStatus !== 'rejected' && <Button size="sm" variant="secondary" onClick={() => review(false)}><X className="size-4" />Reject</Button>}
            <Button size="sm" variant="ghost" onClick={() => setEditing(true)}><Pencil className="size-4" />Edit</Button>
            <Button
              size="sm"
              variant="ghost"
              aria-label="Delete question"
              onClick={remove}
            >
              <Trash2 className="size-4" />
            </Button>
          </div>
        </>
      )}
    </Card>
  )
}

function Settings({ quiz, lessons, onSaved }: { quiz: QuizEditor; lessons: { id: string; title: string }[]; onSaved: (q: QuizEditor) => void }) {
  const [f, setF] = useState<QuizFields>(quiz)
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const set = <K extends keyof QuizFields>(k: K, v: QuizFields[K]) => setF((x) => ({ ...x, [k]: v }))
  const optionalNumber = (v: string, scale = 1) => (v === '' ? null : Math.max(1, Number(v)) * scale)

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      onSaved(await api<QuizEditor>(`/api/admin/quizzes/${quiz.id}`, { method: 'PUT', json: f }))
      setMsg({ ok: true, text: 'Saved.' })
    } catch (err) {
      setMsg({ ok: false, text: problemText(err) })
    } finally {
      setBusy(false)
    }
  }

  return (
    <Card>
      <form onSubmit={save} className="grid gap-4">
        <Field label="Title">{(id) => <Input id={id} value={f.title} onChange={(e) => set('title', e.target.value)} required />}</Field>
        <Field label="Description">{(id) => <Input id={id} value={f.description ?? ''} onChange={(e) => set('description', e.target.value || null)} />}</Field>
        <div className="grid grid-cols-3 gap-3">
          <Field label="Pass %">{(id) => <Input id={id} type="number" min={0} max={100} value={f.passScorePct} onChange={(e) => set('passScorePct', Number(e.target.value))} />}</Field>
          <Field label="Minutes">{(id) => <Input id={id} type="number" min={1} placeholder="None" value={f.timeLimitSeconds ? f.timeLimitSeconds / 60 : ''} onChange={(e) => set('timeLimitSeconds', optionalNumber(e.target.value, 60))} />}</Field>
          <Field label="Attempts">{(id) => <Input id={id} type="number" min={1} placeholder="∞" value={f.maxAttempts ?? ''} onChange={(e) => set('maxAttempts', optionalNumber(e.target.value))} />}</Field>
        </div>
        <Field label="After lesson">
          {(id) => (
            <Select id={id} value={f.lessonId ?? ''} onChange={(e) => set('lessonId', e.target.value || null)}>
              <option value="">Course level</option>
              {lessons.map((l) => <option key={l.id} value={l.id}>{l.title}</option>)}
            </Select>
          )}
        </Field>
        <Field label="Status">
          {(id) => (
            <Select id={id} value={f.status} onChange={(e) => set('status', e.target.value as QuizFields['status'])}>
              <option value="draft">Draft</option>
              <option value="published">Published</option>
              <option value="archived">Archived</option>
            </Select>
          )}
        </Field>
        <Toggle checked={f.isPreview} onChange={(v) => set('isPreview', v)} label="Free preview" />
        <div className="flex items-center gap-3">
          <Button type="submit" disabled={busy}>{busy && <Spinner className="size-4" />}Save settings</Button>
          {msg && <span role="status" className={`text-sm ${msg.ok ? 'text-success' : 'text-danger'}`}>{msg.text}</span>}
        </div>
      </form>
    </Card>
  )
}

export default function QuizEditorPage() {
  const { id = '' } = useParams()
  const [state, reload] = useApi<QuizEditor>(`/api/admin/quizzes/${encodeURIComponent(id)}`)
  if (state.status === 'error') return <NotFound />
  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  return <Editor key={state.data.id} initial={state.data} reload={reload} />
}

function Editor({ initial, reload }: { initial: QuizEditor; reload: () => void }) {
  const navigate = useNavigate()
  const [course] = useApi<CourseEditor>(`/api/admin/courses/${initial.courseId}`)
  const [quiz, setQuiz] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const pending = quiz.questions.filter((q) => q.reviewStatus === 'draft').length
  // Deleting returns no body; refetch in that case.
  const update = (q: QuizEditor | null) => (q ? setQuiz(q) : reload())

  async function addQuestion(body: object) {
    setError(null)
    try {
      setQuiz(await api<QuizEditor>(`/api/admin/quizzes/${quiz.id}/questions`, { json: body }))
    } catch (err) {
      setError(problemText(err))
      throw err
    }
  }

  async function remove() {
    if (!window.confirm('Delete this quiz and all its questions?')) return
    try {
      await api(`/api/admin/quizzes/${quiz.id}`, { method: 'DELETE' })
      navigate(`/admin/courses/${quiz.courseId}`)
    } catch (err) {
      setError(problemText(err))
    }
  }

  return (
    <>
      <Link to={`/admin/courses/${quiz.courseId}`} className="mb-4 inline-flex items-center gap-1 text-sm text-muted hover:text-ink">
        <ArrowLeft className="size-4" />{quiz.courseTitle}
      </Link>
      <PageHeader
        title={quiz.title}
        description={`${quiz.questions.length} questions · ${quiz.attempts} learner attempts${pending ? ` · ${pending} awaiting review` : ''}`}
        actions={quiz.attempts === 0 && <Button variant="ghost" onClick={remove}><Trash2 className="size-4" />Delete quiz</Button>}
      />
      {error && <p role="alert" className="mb-4 text-sm text-danger">{error}</p>}
      <div className="grid gap-6 lg:grid-cols-[1fr_20rem]">
        <div className="flex flex-col gap-4">
          {quiz.questions.map((q, i) => (
            <QuestionCard key={q.id} q={q} index={i} onChange={update} onError={setError} />
          ))}
          <Card>
            <h2 className="mb-4 font-semibold">Add a question</h2>
            <QuestionForm submitLabel="Add question" onSubmit={addQuestion} />
          </Card>
        </div>
        <div className="lg:sticky lg:top-24 lg:self-start">
          <Settings quiz={quiz} lessons={course.data?.lessons ?? []} onSaved={setQuiz} />
          <p className="mt-3 text-xs text-muted">
            Only approved questions are shown to learners and graded. Questions written here are approved when saved; AI drafts wait for review.
          </p>
        </div>
      </div>
    </>
  )
}
