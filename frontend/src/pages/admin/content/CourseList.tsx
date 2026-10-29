import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { BookOpen, Plus } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field, Input } from '@/components/ui/Form'
import { slugify, statusTone } from '@/lib/content'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type CourseRow, problemText } from '@/lib/admin'
import { useApi } from '@/lib/useApi'

function NewCourse({ onCancel }: { onCancel: () => void }) {
  const navigate = useNavigate()
  const [title, setTitle] = useState('')
  const [slug, setSlug] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const body = { slug: slug || slugify(title), title, summary: null, description: null, coverUrl: null, categoryId: null, status: 'draft', isFree: false, position: 0 }
      const { id } = await api<{ id: string }>('/api/admin/courses', { json: body })
      navigate(`/admin/courses/${id}`)
    } catch (err) {
      setError(problemText(err))
      setBusy(false)
    }
  }

  return (
    <Card className="mb-6">
      <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <Field label="Title">{(id) => <Input id={id} value={title} onChange={(e) => setTitle(e.target.value)} required autoFocus />}</Field>
        <Field label="URL slug" hint={`/courses/${slug || slugify(title) || '…'}`}>
          {(id) => <Input id={id} value={slug} placeholder={slugify(title)} onChange={(e) => setSlug(slugify(e.target.value))} />}
        </Field>
        {error && <p role="alert" className="text-sm text-danger sm:col-span-2">{error}</p>}
        <div className="flex gap-2 sm:col-span-2">
          <Button type="submit" disabled={busy || !title.trim()}>{busy && <Spinner className="size-4" />}Create draft</Button>
          <Button variant="ghost" onClick={onCancel}>Cancel</Button>
        </div>
      </form>
    </Card>
  )
}

export default function CourseList() {
  const [state] = useApi<CourseRow[]>('/api/admin/courses')
  const [creating, setCreating] = useState(false)

  return (
    <>
      <PageHeader
        title="Courses"
        description="Create courses, arrange lessons and publish when ready."
        actions={!creating && <Button onClick={() => setCreating(true)}><Plus className="size-4" />New course</Button>}
      />
      {creating && <NewCourse onCancel={() => setCreating(false)} />}
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load courses.</p>}
      {state.status === 'ok' && state.data.length === 0 && !creating && (
        <EmptyState icon={BookOpen} title="No courses yet">Create your first course to start adding lessons.</EmptyState>
      )}
      {state.status === 'ok' && state.data.length > 0 && (
        <Card className="overflow-x-auto p-0">
          <table className="w-full min-w-[40rem] text-sm">
            <thead className="border-b border-border text-left text-xs text-muted">
              <tr>
                <th className="px-5 py-3 font-medium">Course</th>
                <th className="px-3 py-3 font-medium">Status</th>
                <th className="px-3 py-3 font-medium">Lessons</th>
                <th className="px-3 py-3 font-medium">Quizzes</th>
                <th className="px-5 py-3 font-medium">Updated</th>
              </tr>
            </thead>
            <tbody>
              {state.data.map((c) => (
                <tr key={c.id} className="border-b border-border last:border-0 hover:bg-surface-2">
                  <td className="px-5 py-3">
                    <Link to={`/admin/courses/${c.id}`} className="font-medium hover:text-brand-600">{c.title}</Link>
                    <p className="text-xs text-muted">/{c.slug}{c.category && ` · ${c.category}`}{c.isFree && ' · Free'}</p>
                  </td>
                  <td className="px-3 py-3"><Badge tone={statusTone[c.status]}>{c.status}</Badge></td>
                  <td className="px-3 py-3 tabular-nums">{c.lessons}</td>
                  <td className="px-3 py-3 tabular-nums">
                    {c.quizzes}
                    {c.pendingReview > 0 && <span className="ml-2"><Badge tone="warning">{c.pendingReview} to review</Badge></span>}
                  </td>
                  <td className="px-5 py-3 text-muted">{new Date(c.updatedAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </>
  )
}
