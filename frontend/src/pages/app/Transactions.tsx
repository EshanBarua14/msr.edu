import { Link } from 'react-router-dom'
import { CreditCard, Receipt } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { formatDateTime, isFinal, type OrderView } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'

/** Orders and their payment status. With `receiptsOnly`, the paid ones with a receipt. */
export default function Transactions({ receiptsOnly = false }: { receiptsOnly?: boolean }) {
  const [state] = useApi<OrderView[]>('/api/me/orders')
  const rows = state.status === 'ok' ? state.data.filter((o) => !receiptsOnly || o.receiptNumber) : []
  return (
    <>
      <PageHeader
        title={receiptsOnly ? 'Receipts' : 'Transactions'}
        description={receiptsOnly ? 'A receipt is issued for every confirmed payment.' : 'Every order you have placed and what happened to it.'}
      />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load your orders.</p>}
      {state.status === 'ok' && rows.length === 0 && (
        <EmptyState icon={receiptsOnly ? Receipt : CreditCard} title={receiptsOnly ? 'No receipts yet' : 'No transactions yet'} />
      )}
      {rows.length > 0 && (
        <Card className="overflow-x-auto p-0">
          <table className="w-full min-w-[40rem] text-sm">
            <thead className="border-b border-border text-left text-xs text-muted">
              <tr>
                <th className="px-5 py-3 font-medium">Order</th><th className="px-3 py-3 font-medium">Date</th>
                <th className="px-3 py-3 text-right font-medium">Total</th><th className="px-3 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium">Receipt</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((o) => (
                <tr key={o.reference} className="border-b border-border last:border-0">
                  <td className="px-5 py-3">
                    <p className="font-medium">{o.items.map((i) => i.name).join(', ')}</p>
                    <p className="font-mono text-xs text-muted">{o.reference}</p>
                  </td>
                  <td className="px-3 py-3 text-muted">{formatDateTime(o.paidAt ?? o.createdAt)}</td>
                  <td className="px-3 py-3 text-right tabular-nums">
                    {formatMoney(o.totalMinor, o.currency)}
                    {o.refundedMinor > 0 && <p className="text-xs text-muted">−{formatMoney(o.refundedMinor, o.currency)} refunded</p>}
                  </td>
                  <td className="px-3 py-3">
                    {isFinal(o.status) ? <StatusBadge status={o.status} /> : (
                      <Link to={`/checkout/return/${o.reference}`}><StatusBadge status={o.status} /></Link>
                    )}
                  </td>
                  <td className="px-5 py-3">
                    {o.receiptNumber ? <Link to={`/app/receipts/${o.receiptNumber}`} className="text-brand-600 hover:underline">{o.receiptNumber}</Link> : <span className="text-muted">—</span>}
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
