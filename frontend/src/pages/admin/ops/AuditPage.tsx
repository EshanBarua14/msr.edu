import { useState } from 'react'
import { FileClock } from 'lucide-react'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Input, Select } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { api } from '@/lib/api'
import { formatDateTime } from '@/lib/billing'
import { type AuditPage as Page, type AuditRow, changedFields } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

const show = (v: unknown) => (v === undefined ? '' : v === null ? 'null' : typeof v === 'object' ? JSON.stringify(v) : String(v))

function Row({ row }: { row: AuditRow }) {
  const fields = changedFields(row)
  return (
    <li className="border-b border-border px-5 py-3 last:border-0">
      <details>
        <summary className="flex cursor-pointer flex-wrap items-center gap-3 text-sm">
          <Badge tone={row.action === 'insert' ? 'success' : row.action === 'delete' ? 'danger' : 'brand'}>{row.action}</Badge>
          <span className="font-medium">{row.entityType.replaceAll('_', ' ')}</span>
          <span className="font-mono text-xs text-muted">{row.entityId}</span>
          <span className="ml-auto text-xs text-muted">{row.actor ?? 'system'} · {formatDateTime(row.createdAt)}</span>
        </summary>
        <div className="mt-3 overflow-x-auto">
          <table className="w-full text-xs">
            <thead className="text-left text-muted"><tr><th className="py-1 pr-3 font-medium">Field</th>{row.action !== 'insert' && <th className="py-1 pr-3 font-medium">Before</th>}{row.action !== 'delete' && <th className="py-1 font-medium">After</th>}</tr></thead>
            <tbody className="font-mono">
              {fields.map((f) => (
                <tr key={f.field} className="border-t border-border">
                  <td className="py-1 pr-3 font-sans">{f.field}</td>
                  {row.action !== 'insert' && <td className="max-w-80 truncate py-1 pr-3 text-muted">{show(f.before)}</td>}
                  {row.action !== 'delete' && <td className="max-w-80 truncate py-1">{show(f.after)}</td>}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </li>
  )
}

export default function AuditPage() {
  const [entities] = useApi<string[]>('/api/admin/audit/entities')
  const [entity, setEntity] = useState('')
  const [actor, setActor] = useState('')
  const [applied, setApplied] = useState('')
  const query = new URLSearchParams({ ...(entity && { entity }), ...(applied && { actor: applied }) }).toString()
  const [state] = useApi<Page>(`/api/admin/audit${query ? `?${query}` : ''}`)
  const [more, setMore] = useState<{ query: string; rows: AuditRow[]; next: number | null } | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)

  // Older pages belong to the filter they were loaded for.
  const extra = more?.query === query ? more : null
  const rows = [...(state.data?.rows ?? []), ...(extra?.rows ?? [])]
  const next = extra ? extra.next : state.data?.next ?? null


  async function loadMore() {
    if (next == null) return
    setLoadingMore(true)
    const page = await api<Page>(`/api/admin/audit?${new URLSearchParams({ ...(entity && { entity }), ...(applied && { actor: applied }), before: String(next) })}`)
    setMore({ query, rows: [...(extra?.rows ?? []), ...page.rows], next: page.next })
    setLoadingMore(false)
  }

  return (
    <>
      <PageHeader title="Audit log" description="Every change to roles, settings, prices, orders, payments, refunds and access, with who made it. Entries can't be edited or deleted." />
      <div className="mb-4 flex flex-wrap gap-3">
        <div className="w-56">
          <Select value={entity} onChange={(e) => setEntity(e.target.value)} aria-label="Filter by record type">
            <option value="">All records</option>
            {entities.data?.map((e) => <option key={e} value={e}>{e.replaceAll('_', ' ')}</option>)}
          </Select>
        </div>
        <form className="min-w-60 flex-1" onSubmit={(e) => { e.preventDefault(); setApplied(actor.trim()) }}>
          <Input placeholder="Changed by (email), press Enter" value={actor} onChange={(e) => setActor(e.target.value)} aria-label="Filter by who made the change" />
        </form>
      </div>
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load the audit log.</p>}
      {state.status === 'ok' && rows.length === 0 && <EmptyState icon={FileClock} title="No entries match" />}
      {rows.length > 0 && <Card className="p-0"><ul>{rows.map((r) => <Row key={r.id} row={r} />)}</ul></Card>}
      {next != null && (
        <Button variant="secondary" className="mt-4" onClick={loadMore} disabled={loadingMore}>{loadingMore && <Spinner className="size-4" />}Load older entries</Button>
      )}
    </>
  )
}
