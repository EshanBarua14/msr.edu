import { Fragment, useState } from 'react'
import { Link } from 'react-router-dom'
import { ShieldCheck } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Input } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { formatDateTime } from '@/lib/billing'
import { type IssueView, issueLabels } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

function IssueCard({ issue, onChanged }: { issue: IssueView; onChanged: () => void }) {
  const [note, setNote] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const open = issue.status === 'open' || issue.status === 'investigating'

  async function set(status: string) {
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/reconciliation/${issue.id}`, { json: { status, note: note || null } })
      onChanged()
    } catch (e) {
      setError(problemText(e))
      setBusy(false)
    }
  }

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-semibold">{issueLabels[issue.issue] ?? issue.issue}</h2>
          <p className="text-sm text-muted">
            {issue.orderId ? <Link to={`/admin/orders/${issue.orderId}`} className="font-mono text-brand-600 hover:underline">{issue.orderReference}</Link> : 'No order'}
            {issue.userEmail && ` · ${issue.userEmail}`} · {formatDateTime(issue.createdAt)}
          </p>
        </div>
        <StatusBadge status={issue.status === 'open' ? 'pending' : issue.status === 'investigating' ? 'processing' : issue.status} />
      </div>
      {issue.details && (
        <dl className="mt-3 grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-xl bg-surface-2 p-3 text-xs">
          {Object.entries(issue.details).map(([k, v]) => (<Fragment key={k}><dt className="text-muted">{k.replaceAll('_', ' ')}</dt><dd className="font-mono">{String(v)}</dd></Fragment>))}
        </dl>
      )}
      {open ? (
        <div className="mt-4 flex flex-wrap items-center gap-2">
          <Input className="min-w-60 flex-1" placeholder="What did you find or do?" value={note} onChange={(e) => setNote(e.target.value)} aria-label="Resolution note" />
          {issue.status === 'open' && <Button size="sm" variant="secondary" onClick={() => set('investigating')} disabled={busy}>Investigating</Button>}
          <Button size="sm" onClick={() => set('resolved')} disabled={busy || !note.trim()}>Resolve</Button>
          <Button size="sm" variant="ghost" onClick={() => set('ignored')} disabled={busy || !note.trim()}>Ignore</Button>
        </div>
      ) : (
        <p className="mt-3 text-sm"><span className="text-muted">{issue.resolvedBy} · {formatDateTime(issue.resolvedAt)}:</span> {issue.resolutionNote}</p>
      )}
      {error && <p role="alert" className="mt-2 text-sm text-danger">{error}</p>}
    </Card>
  )
}

export default function ReconciliationPage() {
  const [tab, setTab] = useState<'open' | 'closed'>('open')
  const [state, reload] = useApi<IssueView[]>(`/api/admin/reconciliation?status=${tab}`)
  return (
    <>
      <PageHeader title="Payment issues" description="Payments that could not be applied automatically. Access is never granted for these until a person checks them." />
      <div className="mb-4 flex gap-2">
        {(['open', 'closed'] as const).map((t) => (
          <Button key={t} size="sm" variant={tab === t ? 'primary' : 'secondary'} onClick={() => setTab(t)}>{t === 'open' ? 'Needs attention' : 'Closed'}</Button>
        ))}
      </div>
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load payment issues.</p>}
      {state.status === 'ok' && state.data.length === 0 && (
        <EmptyState icon={ShieldCheck} title={tab === 'open' ? 'Nothing needs attention' : 'No closed issues yet'} />
      )}
      {state.status === 'ok' && <div className="flex flex-col gap-4">{state.data.map((i) => <IssueCard key={i.id} issue={i} onChanged={reload} />)}</div>}
    </>
  )
}
