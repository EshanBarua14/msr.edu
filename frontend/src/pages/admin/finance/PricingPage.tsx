import { useState, type FormEvent } from 'react'
import { Tag } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Price } from '@/components/billing/Price'
import { Badge } from '@/components/ui/Badge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field, Input, Select } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { useApi } from '@/lib/useApi'
import { accessLabel, type CoursePrice, formatDate, fromLocalInput, type SaleStatus, toLocalInput, toMajor, toMinor } from '@/lib/billing'

function PriceEditor({ row, onSaved, onCancel }: { row: CoursePrice; onSaved: () => void; onCancel: () => void }) {
  const [price, setPrice] = useState(toMajor(row.priceMinor))
  const [promo, setPromo] = useState(toMajor(row.promoPriceMinor))
  const [promoStarts, setPromoStarts] = useState(toLocalInput(row.promoStartsAt))
  const [promoEnds, setPromoEnds] = useState(toLocalInput(row.promoEndsAt))
  const [days, setDays] = useState(row.accessDays?.toString() ?? '')
  const [status, setStatus] = useState<SaleStatus>(row.saleStatus ?? 'active')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function save(e: FormEvent) {
    e.preventDefault()
    const priceMinor = toMinor(price)
    const promoMinor = promo.trim() ? toMinor(promo) : null
    if (priceMinor == null || (promo.trim() && promoMinor == null)) return setError('Enter amounts like 1500 or 1499.50.')
    setBusy(true)
    setError(null)
    try {
      await api(`/api/admin/courses/${row.courseId}/price`, {
        method: 'PUT',
        json: {
          priceMinor, promoPriceMinor: promoMinor, promoStartsAt: fromLocalInput(promoStarts), promoEndsAt: fromLocalInput(promoEnds),
          accessDays: days.trim() ? Number(days) : null, status,
        },
      })
      onSaved()
    } catch (err) {
      setError(problemText(err))
      setBusy(false)
    }
  }

  return (
    <form onSubmit={save} className="grid gap-4 bg-surface-2 px-5 py-4 sm:grid-cols-3">
      <Field label={`Price (${row.currency})`}>{(id) => <Input id={id} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required autoFocus />}</Field>
      <Field label="Access days" hint="Empty = lifetime">{(id) => <Input id={id} type="number" min={1} value={days} onChange={(e) => setDays(e.target.value)} />}</Field>
      <Field label="Sale status">
        {(id) => (
          <Select id={id} value={status} onChange={(e) => setStatus(e.target.value as SaleStatus)}>
            <option value="active">On sale</option><option value="inactive">Not on sale</option><option value="archived">Archived</option>
          </Select>
        )}
      </Field>
      <Field label="Promotional price" hint="Optional, below the regular price">{(id) => <Input id={id} inputMode="decimal" value={promo} onChange={(e) => setPromo(e.target.value)} />}</Field>
      <Field label="Promotion starts">{(id) => <Input id={id} type="datetime-local" value={promoStarts} onChange={(e) => setPromoStarts(e.target.value)} disabled={!promo.trim()} />}</Field>
      <Field label="Promotion ends">{(id) => <Input id={id} type="datetime-local" value={promoEnds} onChange={(e) => setPromoEnds(e.target.value)} disabled={!promo.trim()} />}</Field>
      {error && <p role="alert" className="text-sm text-danger sm:col-span-3">{error}</p>}
      <div className="flex gap-2 sm:col-span-3">
        <Button type="submit" size="sm" disabled={busy}>{busy && <Spinner className="size-4" />}Save price</Button>
        <Button variant="ghost" size="sm" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  )
}

export default function PricingPage() {
  const [state, reload] = useApi<CoursePrice[]>('/api/admin/pricing')
  const [editing, setEditing] = useState<string | null>(null)
  return (
    <>
      <PageHeader title="Course pricing" description="Set what each course costs. Past orders keep the price they were sold at." />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load prices.</p>}
      {state.status === 'ok' && state.data.length === 0 && <EmptyState icon={Tag} title="No courses yet">Create a course first, then price it here.</EmptyState>}
      {state.status === 'ok' && state.data.length > 0 && (
        <Card className="p-0">
          <ul>
            {state.data.map((c) => (
              <li key={c.courseId} className="border-b border-border last:border-0">
                <div className="flex flex-wrap items-center gap-3 px-5 py-3 text-sm">
                  <div className="min-w-48 flex-1">
                    <p className="font-medium">{c.title}</p>
                    <p className="text-xs text-muted">/{c.slug} · course {c.courseStatus}</p>
                  </div>
                  {c.isFree ? <Badge tone="success">Free course</Badge> : c.priceMinor == null ? <Badge>No price</Badge> : (
                    <div className="text-right">
                      <Price priceMinor={c.promoPriceMinor ?? c.priceMinor} regularMinor={c.priceMinor} currency={c.currency} />
                      <p className="text-xs text-muted">
                        {accessLabel(c.accessDays)}{c.promoPriceMinor != null && c.promoEndsAt && ` · promo until ${formatDate(c.promoEndsAt)}`}
                      </p>
                    </div>
                  )}
                  {c.saleStatus && <StatusBadge status={c.saleStatus === 'inactive' ? 'not_on_sale' : c.saleStatus} />}
                  {!c.isFree && editing !== c.courseId && (
                    <Button variant="secondary" size="sm" onClick={() => setEditing(c.courseId)}>{c.priceMinor == null ? 'Set price' : 'Edit'}</Button>
                  )}
                </div>
                {editing === c.courseId && (
                  <PriceEditor row={c} onCancel={() => setEditing(null)} onSaved={() => { setEditing(null); reload() }} />
                )}
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  )
}
