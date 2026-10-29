import { useState, type FormEvent } from 'react'
import { KeyRound, Plus, Search } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field, Input, Select } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { type AdminEntitlement, type CoursePrice, formatDate, fromLocalInput, type PlanEditor, statusLabel } from '@/lib/billing'
import { useApi } from '@/lib/useApi'

function GrantForm({ onDone }: { onDone: (saved: boolean) => void }) {
  const [courses] = useApi<CoursePrice[]>('/api/admin/pricing')
  const [plans] = useApi<PlanEditor[]>('/api/admin/plans')
  const [email, setEmail] = useState('')
  const [target, setTarget] = useState('')
  const [until, setUntil] = useState('')
  const [reason, setReason] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const [kind, id] = target.split(':')
    setBusy(true)
    setError(null)
    try {
      await api('/api/admin/entitlements', {
        json: { email, courseId: kind === 'course' ? id : null, planId: kind === 'plan' ? id : null, expiresAt: fromLocalInput(until), reason },
      })
      onDone(true)
    } catch (err) {
      setError(problemText(err))
      setBusy(false)
    }
  }

  return (
    <Card className="mb-6">
      <form onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <Field label="Learner email" hint="They must have signed in at least once.">{(id) => <Input id={id} type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus />}</Field>
        <Field label="Course or plan">
          {(id) => (
            <Select id={id} value={target} onChange={(e) => setTarget(e.target.value)} required>
              <option value="">Choose…</option>
              <optgroup label="Courses">{courses.status === 'ok' && courses.data.map((c) => <option key={c.courseId} value={`course:${c.courseId}`}>{c.title}</option>)}</optgroup>
              <optgroup label="Plans">{plans.status === 'ok' && plans.data.map((p) => <option key={p.id} value={`plan:${p.id}`}>{p.name}</option>)}</optgroup>
            </Select>
          )}
        </Field>
        <Field label="Access ends" hint="Empty = no end date">{(id) => <Input id={id} type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} />}</Field>
        <Field label="Reason" hint="Kept in the audit log">{(id) => <Input id={id} value={reason} onChange={(e) => setReason(e.target.value)} required />}</Field>
        {error && <p role="alert" className="text-sm text-danger sm:col-span-2">{error}</p>}
        <div className="flex gap-2 sm:col-span-2">
          <Button type="submit" disabled={busy}>{busy && <Spinner className="size-4" />}Grant access</Button>
          <Button variant="ghost" onClick={() => onDone(false)}>Cancel</Button>
        </div>
      </form>
    </Card>
  )
}

export default function EntitlementsPage() {
  const { roles } = useAuth()
  const canChange = roles.includes('owner_admin') || roles.includes('finance_manager')
  const [email, setEmail] = useState('')
  const [search, setSearch] = useState('')
  const [state, reload] = useApi<AdminEntitlement[]>(`/api/admin/entitlements${search ? `?email=${encodeURIComponent(search)}` : ''}`)
  const [granting, setGranting] = useState(false)

  async function revoke(e: AdminEntitlement) {
    const reason = window.prompt(`Revoke ${e.title} for ${e.userEmail}? Give a reason:`)
    if (!reason?.trim()) return
    try {
      await api(`/api/admin/entitlements/${e.id}/revoke`, { json: { reason } })
      reload()
    } catch (err) {
      window.alert(problemText(err))
    }
  }

  return (
    <>
      <PageHeader
        title="Learner entitlements"
        description="Who can access what, and why. Grant access by hand for scholarships or support cases."
        actions={canChange && !granting && <Button onClick={() => setGranting(true)}><Plus className="size-4" />Grant access</Button>}
      />
      {granting && <GrantForm onDone={(saved) => { setGranting(false); if (saved) reload() }} />}
      <form className="relative mb-4 max-w-md" onSubmit={(e) => { e.preventDefault(); setSearch(email.trim()) }}>
        <Search className="absolute top-3 left-3 size-4 text-muted" />
        <Input className="pl-9" placeholder="Filter by learner email" value={email} onChange={(e) => setEmail(e.target.value)} aria-label="Filter by email" />
      </form>
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load entitlements.</p>}
      {state.status === 'ok' && state.data.length === 0 && <EmptyState icon={KeyRound} title="No entitlements found" />}
      {state.status === 'ok' && state.data.length > 0 && (
        <Card className="overflow-x-auto p-0">
          <table className="w-full min-w-[48rem] text-sm">
            <thead className="border-b border-border text-left text-xs text-muted">
              <tr>
                <th className="px-5 py-3 font-medium">Learner</th><th className="px-3 py-3 font-medium">Access to</th><th className="px-3 py-3 font-medium">Source</th>
                <th className="px-3 py-3 font-medium">Status</th><th className="px-3 py-3 font-medium">Ends</th><th className="px-5 py-3" />
              </tr>
            </thead>
            <tbody>
              {state.data.map((e) => (
                <tr key={e.id} className="border-b border-border last:border-0">
                  <td className="px-5 py-3">{e.userEmail}</td>
                  <td className="px-3 py-3">{e.title}<span className="block text-xs text-muted">{e.kind}</span></td>
                  <td className="px-3 py-3">{statusLabel(e.source)}{e.grantReason && <span className="block text-xs text-muted">{e.grantReason}</span>}</td>
                  <td className="px-3 py-3"><StatusBadge status={e.status} />{e.statusReason && <span className="block text-xs text-muted">{e.statusReason}</span>}</td>
                  <td className="px-3 py-3 text-muted">{e.expiresAt ? formatDate(e.expiresAt) : 'Never'}</td>
                  <td className="px-5 py-3 text-right">
                    {canChange && e.status === 'active' && <Button variant="ghost" size="sm" onClick={() => revoke(e)}>Revoke</Button>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </>
  )
}
