import { useState } from 'react'
import { Search, Users } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { STAFF_ROLES, type Role } from '@/auth/roles'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Input } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { formatDateTime } from '@/lib/billing'
import { type AdminUser, roleLabels } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

function RoleEditor({ user, onDone }: { user: AdminUser; onDone: (saved: boolean) => void }) {
  const [roles, setRoles] = useState<Role[]>(user.roles.filter((r) => r !== 'learner'))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const toggle = (r: Role) => setRoles((x) => (x.includes(r) ? x.filter((y) => y !== r) : [...x, r]))

  async function save() {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/users/${user.id}/roles`, { method: 'PUT', json: { roles } })
      onDone(true)
    } catch (e) {
      setError(problemText(e))
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-3 bg-surface-2 px-5 py-4">
      <div className="flex flex-wrap gap-4">
        {STAFF_ROLES.map((r) => (
          <label key={r} className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={roles.includes(r)} onChange={() => toggle(r)} />
            {roleLabels[r]}
          </label>
        ))}
      </div>
      <p className="text-xs text-muted">Every account keeps the learner role. Changes apply on the person's next request.</p>
      {error && <p role="alert" className="text-sm text-danger">{error}</p>}
      <div className="flex gap-2">
        <Button size="sm" onClick={save} disabled={busy}>{busy && <Spinner className="size-4" />}Save roles</Button>
        <Button size="sm" variant="ghost" onClick={() => onDone(false)}>Cancel</Button>
      </div>
    </div>
  )
}

export default function UsersPage() {
  const { roles: myRoles } = useAuth()
  const isOwner = myRoles.includes('owner_admin')
  const [state, reload] = useApi<AdminUser[]>('/api/admin/users')
  const [q, setQ] = useState('')
  const [staffOnly, setStaffOnly] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const term = q.trim().toLowerCase()
  const rows = (state.data ?? []).filter((u) =>
    (!term || u.email.toLowerCase().includes(term) || (u.fullName ?? '').toLowerCase().includes(term))
    && (!staffOnly || u.roles.some((r) => r !== 'learner')))

  return (
    <>
      <PageHeader title="Users & roles" description={isOwner ? 'Everyone who has signed in. Only the owner can change roles.' : 'Everyone who has signed in.'} />
      <div className="mb-4 flex flex-wrap items-center gap-4">
        <div className="relative min-w-60 flex-1">
          <Search className="absolute top-3 left-3 size-4 text-muted" />
          <Input className="pl-9" placeholder="Search by name or email" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search users" />
        </div>
        <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={staffOnly} onChange={(e) => setStaffOnly(e.target.checked)} />Staff only</label>
      </div>
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load users.</p>}
      {state.status === 'ok' && rows.length === 0 && <EmptyState icon={Users} title="No users match" />}
      {rows.length > 0 && (
        <Card className="p-0">
          <ul>
            {rows.map((u) => (
              <li key={u.id} className="border-b border-border last:border-0">
                <div className="flex flex-wrap items-center gap-3 px-5 py-3 text-sm">
                  <div className="min-w-56 flex-1">
                    <p className="font-medium">{u.fullName ?? u.email}</p>
                    <p className="text-xs text-muted">{u.email} · last sign-in {formatDateTime(u.lastLoginAt)}</p>
                  </div>
                  <div className="flex flex-wrap gap-1.5">
                    {u.roles.map((r) => <Badge key={r} tone={r === 'owner_admin' ? 'brand' : r === 'learner' ? 'neutral' : 'success'}>{roleLabels[r] ?? r}</Badge>)}
                  </div>
                  {isOwner && editing !== u.id && <Button size="sm" variant="secondary" onClick={() => setEditing(u.id)}>Change roles</Button>}
                </div>
                {editing === u.id && <RoleEditor user={u} onDone={(saved) => { setEditing(null); if (saved) reload() }} />}
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  )
}
