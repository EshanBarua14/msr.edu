import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { CreditCard, Search } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Input, Select } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { formatDateTime, type OrderRow } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'

const filters: [string, string][] = [
  ['', 'All orders'],
  ['pending,processing', 'Awaiting payment'],
  ['successful', 'Paid'],
  ['refund_pending,partially_refunded,refunded', 'Refunded'],
  ['failed,cancelled,expired', 'Failed, cancelled or expired'],
]

export default function OrdersPage() {
  const [params, setParams] = useSearchParams()
  const status = params.get('status') ?? ''
  const [q, setQ] = useState(params.get('q') ?? '')
  const query = new URLSearchParams({ ...(status && { status }), ...(params.get('q') && { q: params.get('q')! }) }).toString()
  const [state] = useApi<OrderRow[]>(`/api/admin/orders${query ? `?${query}` : ''}`)

  const update = (patch: Record<string, string>) => {
    const next = new URLSearchParams(params)
    Object.entries(patch).forEach(([k, v]) => (v ? next.set(k, v) : next.delete(k)))
    setParams(next, { replace: true })
  }

  return (
    <>
      <PageHeader title="Orders & transactions" description="Every checkout, its payment status and webhook history." />
      <div className="mb-4 flex flex-wrap gap-3">
        <form className="relative min-w-60 flex-1" onSubmit={(e) => { e.preventDefault(); update({ q: q.trim() }) }}>
          <Search className="absolute top-3 left-3 size-4 text-muted" />
          <Input className="pl-9" placeholder="Order reference or email" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Search orders" />
        </form>
        <div className="w-64">
          <Select value={status} onChange={(e) => update({ status: e.target.value })} aria-label="Filter by status">
            {filters.map(([v, label]) => <option key={v} value={v}>{label}</option>)}
          </Select>
        </div>
      </div>
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load orders.</p>}
      {state.status === 'ok' && state.data.length === 0 && <EmptyState icon={CreditCard} title="No orders match" />}
      {state.status === 'ok' && state.data.length > 0 && (
        <Card className="overflow-x-auto p-0">
          <table className="w-full min-w-[48rem] text-sm">
            <thead className="border-b border-border text-left text-xs text-muted">
              <tr>
                <th className="px-5 py-3 font-medium">Order</th><th className="px-3 py-3 font-medium">Learner</th>
                <th className="px-3 py-3 text-right font-medium">Total</th><th className="px-3 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium">Created</th>
              </tr>
            </thead>
            <tbody>
              {state.data.map((o) => (
                <tr key={o.id} className="border-b border-border last:border-0 hover:bg-surface-2">
                  <td className="px-5 py-3">
                    <Link to={`/admin/orders/${o.id}`} className="font-mono text-xs font-medium hover:text-brand-600">{o.reference}</Link>
                    <p className="text-xs text-muted">{o.items}</p>
                  </td>
                  <td className="px-3 py-3">{o.userEmail}</td>
                  <td className="px-3 py-3 text-right tabular-nums">{formatMoney(o.totalMinor, o.currency)}</td>
                  <td className="px-3 py-3"><StatusBadge status={o.status} /></td>
                  <td className="px-5 py-3 text-muted">{formatDateTime(o.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </>
  )
}
