import { Link, useParams } from 'react-router-dom'
import { ArrowLeft, Printer } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Logo } from '@/components/ui/Logo'
import { Spinner } from '@/components/ui/Spinner'
import { formatDateTime, type ReceiptView } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'
import { NotFound } from '@/pages/StatusPage'

export default function ReceiptPage() {
  const { number = '' } = useParams()
  const [state] = useApi<ReceiptView>(`/api/receipts/${encodeURIComponent(number)}`)
  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  if (state.status === 'error') return <NotFound />
  const r = state.data
  return (
    <div className="mx-auto max-w-2xl">
      <div className="mb-4 flex justify-between print:hidden">
        <Link to="/app/receipts" className="inline-flex items-center gap-1 text-sm text-muted hover:text-ink"><ArrowLeft className="size-4" />Receipts</Link>
        <Button variant="secondary" size="sm" onClick={() => window.print()}><Printer className="size-4" />Print or save as PDF</Button>
      </div>
      <Card className="print:border-0 print:shadow-none">
        <div className="flex items-start justify-between gap-4">
          <Logo />
          <div className="text-right">
            <p className="text-xs uppercase tracking-wide text-muted">Receipt</p>
            <p className="font-semibold">{r.number}</p>
            <p className="text-xs text-muted">{formatDateTime(r.issuedAt)}</p>
          </div>
        </div>
        <div className="mt-8 grid gap-4 text-sm sm:grid-cols-2">
          <div><p className="text-xs text-muted">Billed to</p><p className="font-medium">{r.customerName ?? r.customerEmail}</p><p className="text-muted">{r.customerEmail}</p></div>
          <div className="sm:text-right"><p className="text-xs text-muted">Order</p><p className="font-mono">{r.orderReference}</p><StatusBadge status={r.orderStatus} /></div>
        </div>
        <table className="mt-8 w-full text-sm">
          <thead className="border-b border-border text-left text-xs text-muted"><tr><th className="py-2 font-medium">Item</th><th className="py-2 text-right font-medium">Amount</th></tr></thead>
          <tbody>
            {r.items.map((i) => (
              <tr key={i.name} className="border-b border-border"><td className="py-3">{i.name}<span className="block text-xs text-muted">{i.kind === 'course' ? 'Course' : 'Membership'}</span></td><td className="py-3 text-right tabular-nums">{formatMoney(i.amountMinor, r.currency)}</td></tr>
            ))}
          </tbody>
          <tfoot>
            <tr><td className="pt-4 font-semibold">Paid</td><td className="pt-4 text-right font-semibold tabular-nums">{formatMoney(r.totalMinor, r.currency)}</td></tr>
            {r.refundedMinor > 0 && <tr><td className="pt-1 text-muted">Refunded</td><td className="pt-1 text-right tabular-nums text-muted">−{formatMoney(r.refundedMinor, r.currency)}</td></tr>}
          </tfoot>
        </table>
        <p className="mt-8 text-xs text-muted">
          Paid {formatDateTime(r.paidAt)} via {r.provider}{r.providerTxnId && <> · transaction <span className="font-mono">{r.providerTxnId}</span></>}
        </p>
      </Card>
    </div>
  )
}
