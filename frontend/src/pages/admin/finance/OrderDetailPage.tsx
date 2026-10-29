import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { AlertTriangle, ArrowLeft } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { Field, Input, Toggle } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { formatDateTime, type OrderDetail, statusLabel, toMajor, toMinor } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'
import { NotFound } from '@/pages/StatusPage'

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Card className="p-0">
      <h2 className="border-b border-border px-5 py-3 font-semibold">{title}</h2>
      <div className="px-5 py-3 text-sm">{children}</div>
    </Card>
  )
}

function RefundForm({ d, onDone }: { d: OrderDetail; onDone: () => void }) {
  const left = d.order.totalMinor - d.order.refundedMinor
  const [amount, setAmount] = useState(toMajor(left))
  const [reason, setReason] = useState('')
  const [manual, setManual] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    const amountMinor = toMinor(amount)
    if (amountMinor == null) return setError('Enter an amount like 500 or 499.50.')
    const full = amountMinor === left
    if (!window.confirm(`Refund ${formatMoney(amountMinor, d.order.currency)}?${full ? ' This is a full refund; access from this order will be removed per the refund policy.' : ''}`)) return
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/orders/${d.id}/refunds`, { json: { amountMinor, reason, manual } })
      onDone()
    } catch (err) {
      setError(problemText(err))
    }
    setBusy(false)
  }

  return (
    <form onSubmit={submit} className="grid gap-3 sm:grid-cols-[10rem_1fr]">
      <Field label="Amount">{(id) => <Input id={id} inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} required />}</Field>
      <Field label="Reason">{(id) => <Input id={id} value={reason} onChange={(e) => setReason(e.target.value)} required />}</Field>
      <div className="sm:col-span-2">
        <Toggle checked={manual} onChange={setManual} label="Already refunded outside the app (record only, no gateway call)" />
      </div>
      {error && <p role="alert" className="text-danger sm:col-span-2">{error}</p>}
      <div className="sm:col-span-2">
        <Button type="submit" variant="danger" size="sm" disabled={busy || !reason.trim()}>{busy && <Spinner className="size-4" />}Refund</Button>
        <span className="ml-3 text-xs text-muted">Up to {formatMoney(left, d.order.currency)}</span>
      </div>
    </form>
  )
}

export default function OrderDetailPage() {
  const { id = '' } = useParams()
  const { roles } = useAuth()
  const [state, reload] = useApi<OrderDetail>(`/api/admin/orders/${id}`)
  if (state.status === 'loading') return <Spinner className="text-brand-500" />
  if (state.status === 'error') return <NotFound />
  const d = state.data
  const o = d.order
  const canRefund = (roles.includes('owner_admin') || roles.includes('finance_manager')) && ['successful', 'partially_refunded'].includes(o.status)

  return (
    <>
      <Link to="/admin/orders" className="mb-3 inline-flex items-center gap-1 text-sm text-muted hover:text-ink"><ArrowLeft className="size-4" />Orders</Link>
      <PageHeader title={o.reference} description={`${d.userEmail} · ${o.items.map((i) => i.name).join(', ')}`} actions={<StatusBadge status={o.status} />} />
      <div className="grid gap-5 lg:grid-cols-2">
        <Section title="Summary">
          <dl className="grid grid-cols-2 gap-y-2">
            <dt className="text-muted">Total</dt><dd className="text-right font-semibold tabular-nums">{formatMoney(o.totalMinor, o.currency)}</dd>
            <dt className="text-muted">Refunded</dt><dd className="text-right tabular-nums">{formatMoney(o.refundedMinor, o.currency)}</dd>
            <dt className="text-muted">Created</dt><dd className="text-right">{formatDateTime(o.createdAt)}</dd>
            <dt className="text-muted">Paid</dt><dd className="text-right">{formatDateTime(o.paidAt)}</dd>
            <dt className="text-muted">Receipt</dt><dd className="text-right">{o.receiptNumber ? <Link className="text-brand-600 hover:underline" to={`/app/receipts/${o.receiptNumber}`}>{o.receiptNumber}</Link> : '—'}</dd>
          </dl>
        </Section>
        <Section title="Access granted">
          {d.access.length === 0 ? <p className="text-muted">None.</p> : (
            <ul className="flex flex-col gap-2">
              {d.access.map((a) => (
                <li key={a.id} className="flex items-center justify-between gap-3">
                  <span>{a.title}<span className="block text-xs text-muted">{a.expiresAt ? `until ${formatDateTime(a.expiresAt)}` : 'no end date'}{a.statusReason && ` · ${a.statusReason}`}</span></span>
                  <StatusBadge status={a.status} />
                </li>
              ))}
            </ul>
          )}
        </Section>
        {d.issues.length > 0 && (
          <div className="lg:col-span-2">
            <Card className="border-warning/40 bg-warning/5">
              <p className="flex items-center gap-2 font-semibold text-warning"><AlertTriangle className="size-4" />Needs reconciliation</p>
              <ul className="mt-2 text-sm">{d.issues.map((i) => <li key={i.id}>{statusLabel(i.issue)} · {i.status} · {formatDateTime(i.createdAt)}</li>)}</ul>
            </Card>
          </div>
        )}
        <div className="lg:col-span-2">
          <Section title="Payment attempts">
            <ul className="flex flex-col divide-y divide-border">
              {d.transactions.map((t) => (
                <li key={t.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
                  <span>
                    {t.provider} · <span className="font-mono text-xs">{t.providerTxnId ?? 'not registered'}</span>
                    <span className="block text-xs text-muted">{formatDateTime(t.createdAt)}{t.verifiedAt && ` · verified ${formatDateTime(t.verifiedAt)}`}{t.failureReason && ` · ${t.failureReason}`}</span>
                  </span>
                  <span className="flex items-center gap-3"><span className="tabular-nums">{formatMoney(t.amountMinor, t.currency)}</span><StatusBadge status={t.status} /></span>
                </li>
              ))}
            </ul>
          </Section>
        </div>
        <div className="lg:col-span-2">
          <Section title="Webhook events">
            {d.events.length === 0 ? <p className="text-muted">No events received yet.</p> : (
              <ul className="flex flex-col divide-y divide-border">
                {d.events.map((e) => (
                  <li key={e.id} className="py-2">
                    <details>
                      <summary className="flex cursor-pointer flex-wrap items-center justify-between gap-2">
                        <span>{e.eventType} <span className="font-mono text-xs text-muted">{e.providerEventId}</span></span>
                        <span className="text-xs text-muted">{formatDateTime(e.receivedAt)} · {e.processingError ?? (e.processedAt ? 'applied' : 'not processed')}</span>
                      </summary>
                      <pre className="mt-2 overflow-x-auto rounded-xl bg-surface-2 p-3 text-xs">{JSON.stringify(e.payload, null, 2)}</pre>
                    </details>
                  </li>
                ))}
              </ul>
            )}
          </Section>
        </div>
        <div className="lg:col-span-2">
          <Section title="Refunds">
            {d.refunds.length > 0 && (
              <ul className="mb-4 flex flex-col divide-y divide-border">
                {d.refunds.map((r) => (
                  <li key={r.id} className="flex flex-wrap items-center justify-between gap-3 py-2">
                    <span>{r.reason}<span className="block text-xs text-muted">{formatDateTime(r.createdAt)}{r.isManual ? ' · recorded manually' : r.providerRefundId && ` · ${r.providerRefundId}`}</span></span>
                    <span className="flex items-center gap-3"><span className="tabular-nums">{formatMoney(r.amountMinor, r.currency)}</span><StatusBadge status={r.status} /></span>
                  </li>
                ))}
              </ul>
            )}
            {canRefund ? <RefundForm key={o.refundedMinor} d={d} onDone={reload} /> : d.refunds.length === 0 && <p className="text-muted">Only paid orders can be refunded.</p>}
          </Section>
        </div>
      </div>
    </>
  )
}
