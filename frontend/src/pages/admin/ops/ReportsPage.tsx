import { useState } from 'react'
import { Download } from 'lucide-react'
import { RevenueChart } from '@/components/admin/RevenueChart'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { Field, Input, Select } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { formatMoney } from '@/lib/business'
import { periodLabel, type RevenueReport } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

export default function ReportsPage() {
  const [group, setGroup] = useState<'day' | 'month'>('day')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const query = new URLSearchParams({ group, ...(from && { from }), ...(to && { to }) }).toString()
  const [state] = useApi<RevenueReport>(`/api/admin/reports/revenue?${query}`)
  const r = state.data
  const money = (v: number) => (r ? formatMoney(v, r.currency) : '—')
  const csv = `/api/admin/reports/revenue.csv?${new URLSearchParams({ ...(from && { from }), ...(to && { to }) })}`

  return (
    <>
      <PageHeader
        title="Revenue reports"
        description="From verified payments and completed refunds only. Days are in Bangladesh time."
        actions={<a href={csv} className={buttonClass('secondary', 'sm')} download><Download className="size-4" />Export CSV</a>}
      />
      <div className="mb-5 flex flex-wrap items-end gap-3">
        <Field label="Group by" className="w-36">
          {(id) => (
            <Select id={id} value={group} onChange={(e) => setGroup(e.target.value as 'day' | 'month')}>
              <option value="day">Day</option><option value="month">Month</option>
            </Select>
          )}
        </Field>
        <Field label="From" className="w-44">{(id) => <Input id={id} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />}</Field>
        <Field label="To" className="w-44">{(id) => <Input id={id} type="date" value={to} onChange={(e) => setTo(e.target.value)} />}</Field>
        {(from || to) && <button className="h-10 text-sm text-brand-600 hover:underline" onClick={() => { setFrom(''); setTo('') }}>Last {group === 'day' ? '30 days' : '12 months'}</button>}
      </div>
      {state.status === 'error' && <p className="text-danger">Could not load the report. Check the dates (up to 2 years).</p>}
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {r && (
        <div className="flex flex-col gap-5">
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {[['Net sales', money(r.totals.netMinor)], ['Gross sales', money(r.totals.grossMinor)], ['Refunded', money(r.totals.refundedMinor)], ['Payments', String(r.totals.payments)]].map(([label, value]) => (
              <Card key={label}>
                <p className="text-sm text-muted">{label}</p>
                <p className="mt-1 text-2xl font-bold tabular-nums">{value}</p>
              </Card>
            ))}
          </div>
          <Card>
            <h2 className="mb-1 font-semibold">Net sales by {r.group}</h2>
            <p className="mb-4 text-xs text-muted">{periodLabel(r.from)} to {periodLabel(r.to)} · courses {money(r.totals.courseMinor)} · memberships {money(r.totals.subscriptionMinor)}</p>
            <RevenueChart rows={r.rows} currency={r.currency} />
          </Card>
          <div className="grid gap-5 lg:grid-cols-2">
            <Card className="overflow-x-auto p-0">
              <h2 className="px-5 pt-4 pb-2 font-semibold">By product</h2>
              {r.products.length === 0 ? <p className="px-5 pb-4 text-sm text-muted">No sales in this range.</p> : (
                <table className="w-full text-sm">
                  <thead className="border-b border-border text-left text-xs text-muted"><tr><th className="px-5 py-2 font-medium">Product</th><th className="px-3 py-2 text-right font-medium">Sales</th><th className="px-5 py-2 text-right font-medium">Gross</th></tr></thead>
                  <tbody>
                    {r.products.map((p) => (
                      <tr key={p.name + p.kind} className="border-b border-border last:border-0">
                        <td className="px-5 py-2">{p.name}<span className="block text-xs text-muted">{p.kind === 'course' ? 'Course' : 'Membership'}</span></td>
                        <td className="px-3 py-2 text-right tabular-nums">{p.sales}</td>
                        <td className="px-5 py-2 text-right tabular-nums">{money(p.grossMinor)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </Card>
            <Card className="max-h-[28rem] overflow-auto p-0">
              <h2 className="px-5 pt-4 pb-2 font-semibold">By {r.group}</h2>
              <table className="w-full text-sm">
                <thead className="sticky top-0 border-b border-border bg-surface text-left text-xs text-muted"><tr><th className="px-5 py-2 font-medium">Period</th><th className="px-3 py-2 text-right font-medium">Payments</th><th className="px-3 py-2 text-right font-medium">Refunded</th><th className="px-5 py-2 text-right font-medium">Net</th></tr></thead>
                <tbody>
                  {[...r.rows].reverse().filter((x) => x.payments || x.refundedMinor).map((x) => (
                    <tr key={x.period} className="border-b border-border last:border-0">
                      <td className="px-5 py-2">{periodLabel(x.period)}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{x.payments}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{money(x.refundedMinor)}</td>
                      <td className="px-5 py-2 text-right font-medium tabular-nums">{money(x.netMinor)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>
          </div>
        </div>
      )}
    </>
  )
}
