import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { FileText, FolderSync } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Input, Select, Toggle } from '@/components/ui/Form'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { type AdminResource, type CourseEditor, type CourseResources, type DriveStatus, problemText, type SyncResult } from '@/lib/admin'
import { statusTone } from '@/lib/content'
import { formatBytes } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

function ResourceRow({ r, course, onSaved }: { r: AdminResource; course: CourseEditor; onSaved: (r: AdminResource) => void }) {
  const [error, setError] = useState<string | null>(null)
  const save = async (patch: Partial<AdminResource>) => {
    setError(null)
    const next = { ...r, ...patch }
    try {
      onSaved(await api<AdminResource>(`/api/admin/resources/${r.id}`, {
        method: 'PUT',
        json: { name: next.name, lessonId: next.lessonId, isPreview: next.isPreview, status: next.status },
      }))
    } catch (err) {
      setError(problemText(err))
    }
  }
  return (
    <li className="flex flex-wrap items-center gap-3 border-b border-border px-5 py-3 text-sm last:border-0">
      <FileText className="size-4 text-muted" />
      <div className="min-w-0 flex-1">
        <p className="truncate font-medium">{r.name}</p>
        <p className="text-xs text-muted">{[r.mimeType, formatBytes(r.sizeBytes)].filter(Boolean).join(' · ')}</p>
        {error && <p className="text-xs text-danger">{error}</p>}
      </div>
      <div className="w-44">
        <Select aria-label="Attach to lesson" className="h-8 text-xs" value={r.lessonId ?? ''} onChange={(e) => save({ lessonId: e.target.value || null })}>
          <option value="">Course level</option>
          {course.lessons.map((l) => <option key={l.id} value={l.id}>{l.title}</option>)}
        </Select>
      </div>
      <Toggle checked={r.isPreview} onChange={(v) => save({ isPreview: v })} label="Preview" />
      <div className="w-32">
        <Select aria-label="Status" className="h-8 text-xs" value={r.status} onChange={(e) => save({ status: e.target.value as AdminResource['status'] })}>
          <option value="draft">Draft</option>
          <option value="published">Published</option>
          <option value="archived">Archived</option>
        </Select>
      </div>
      <Badge tone={statusTone[r.status]}>{r.status}</Badge>
    </li>
  )
}

export function CourseResourcesTab({ course }: { course: CourseEditor }) {
  const [drive] = useApi<DriveStatus>('/api/admin/drive/status')
  const [state, reload] = useApi<CourseResources>(`/api/admin/courses/${course.id}/resources`)
  const [folder, setFolder] = useState('')
  const [busy, setBusy] = useState(false)
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null)
  const [edited, setEdited] = useState<Record<string, AdminResource>>({})

  async function sync(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg(null)
    try {
      const r = await api<SyncResult>(`/api/admin/courses/${course.id}/drive/sync`, { json: { folder: folder || state.data?.driveFolderId } })
      setMsg({ ok: true, text: `Synced: ${r.added} new, ${r.updated} updated, ${r.archived} archived, ${r.skipped} skipped. New files start as drafts.` })
      setEdited({})
      reload()
    } catch (err) {
      setMsg({ ok: false, text: problemText(err) })
    } finally {
      setBusy(false)
    }
  }

  const resources = (state.data?.resources ?? []).map((r) => edited[r.id] ?? r)

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <form onSubmit={sync} className="flex flex-col gap-3">
          <div className="flex flex-wrap gap-2">
            <div className="min-w-0 flex-1">
              <Input
                aria-label="Google Drive folder link"
                placeholder={state.data?.driveFolderId ? `Folder ${state.data.driveFolderId}` : 'Paste a Google Drive folder link'}
                value={folder}
                onChange={(e) => setFolder(e.target.value)}
                disabled={!drive.data?.configured}
              />
            </div>
            <Button type="submit" disabled={busy || !drive.data?.configured || (!folder && !state.data?.driveFolderId)}>
              {busy ? <Spinner className="size-4" /> : <FolderSync className="size-4" />}Sync from Drive
            </Button>
          </div>
          {drive.data && !drive.data.configured && (
            <p className="text-sm text-warning">Google Drive isn’t connected yet. See <Link to="/admin/drive" className="underline">Google Drive</Link> for setup.</p>
          )}
          {drive.data?.configured && (
            <p className="text-xs text-muted">Share the folder with <span className="font-mono">{drive.data.accountEmail}</span> as a Viewer.
              {state.data?.driveSyncedAt && ` Last synced ${new Date(state.data.driveSyncedAt).toLocaleString()}.`}</p>
          )}
          {msg && <p role="status" className={`text-sm ${msg.ok ? 'text-success' : 'text-danger'}`}>{msg.text}</p>}
        </form>
      </Card>
      {resources.length > 0 && (
        <Card className="p-0">
          <ul>
            {resources.map((r) => (
              <ResourceRow key={r.id} r={r} course={course} onSaved={(x) => setEdited((all) => ({ ...all, [x.id]: x }))} />
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}
