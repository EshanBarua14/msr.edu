import { useState, type FormEvent } from 'react'
import { Plus, Ticket } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { Field, Input, Select, Textarea, Toggle } from '@/components/ui/Form'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import { type BillingPeriod, type CoursePrice, periodLabel, type PlanEditor, type PlanFields, type SaleStatus, toMajor, toMinor } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { slugify } from '@/lib/content'
import { useApi } from '@/lib/useApi'

const blank: PlanFields = {
  slug: '', name: '', description: null, priceMinor: 0, billingPeriod: 'month', periodDays: null, graceDays: 0,
  allCourses: true, courseIds: [], categoryIds: [], status: 'inactive', position: 0,
}

function PlanForm({ plan, courses, onDone }: { plan: PlanEditor | null; courses: CoursePrice[]; onDone: (saved: boolean) => void }) {
  const [f, setF] = useState<PlanFields>(plan ?? blank)
  const [price, setPrice] = useState(plan ? toMajor(plan.priceMinor) : '')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const set = (patch: Partial<PlanFields>) => setF((x) => ({ ...x, ...patch }))

  async function save(e: FormEvent) {
    e.preventDefault()
    const priceMinor = toMinor(price)
    if (priceMinor == null) return setError('Enter the price like 500 or 499.50.')
    setBusy(true)
    setError(null)
    try {
      const body = { ...f, slug: f.slug || slugify(f.name), priceMinor }
      await api(plan ? `/api/admin/plans/${plan.id}` : '/api/admin/plans', { method: plan ? 'PUT' : 'POST', json: body })
      onDone(true)
    } catch (err) {
      setError(problemText(err))
      setBusy(false)
    }
  }

  const toggleCourse = (id: string) =>
    set({ courseIds: f.courseIds.includes(id) ? f.courseIds.filter((c) => c !== id) : [...f.courseIds, id] })

  return (
    <Card className="mb-6">
      <form onSubmit={save} className="grid gap-4 sm:grid-cols-2">
        <Field label="Name">{(id) => <Input id={id} value={f.name} onChange={(e) => set({ name: e.target.value })} required autoFocus />}</Field>
        <Field label="Slug">{(id) => <Input id={id} value={f.slug} placeholder={slugify(f.name)} onChange={(e) => set({ slug: slugify(e.target.value) })} />}</Field>
        <Field label="Description" className="sm:col-span-2">{(id) => <Textarea id={id} className="min-h-16" value={f.description ?? ''} onChange={(e) => set({ description: e.target.value || null })} />}</Field>
        <Field label="Price per period">{(id) => <Input id={id} inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required />}</Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="Billing period">
            {(id) => (
              <Select id={id} value={f.billingPeriod} onChange={(e) => set({ billingPeriod: e.target.value as BillingPeriod })}>
                <option value="month">Monthly</option><option value="year">Yearly</option><option value="custom_days">Custom days</option>
              </Select>
            )}
          </Field>
          {f.billingPeriod === 'custom_days' ? (
            <Field label="Days">{(id) => <Input id={id} type="number" min={1} value={f.periodDays ?? ''} onChange={(e) => set({ periodDays: e.target.value ? Number(e.target.value) : null })} required />}</Field>
          ) : (
            <Field label="Grace days" hint="Access after period end">{(id) => <Input id={id} type="number" min={0} value={f.graceDays} onChange={(e) => set({ graceDays: Number(e.target.value) })} />}</Field>
          )}
        </div>
        <Field label="Status">
          {(id) => (
            <Select id={id} value={f.status} onChange={(e) => set({ status: e.target.value as SaleStatus })}>
              <option value="active">On sale</option><option value="inactive">Not on sale</option><option value="archived">Archived</option>
            </Select>
          )}
        </Field>
        <Field label="Order on pricing page">{(id) => <Input id={id} type="number" value={f.position} onChange={(e) => set({ position: Number(e.target.value) })} />}</Field>
        <div className="flex flex-col gap-3 sm:col-span-2">
          <Toggle checked={f.allCourses} onChange={(allCourses) => set({ allCourses })} label="Includes every published course" />
          {!f.allCourses && (
            <div className="grid max-h-64 gap-1 overflow-y-auto rounded-xl border border-border p-3 sm:grid-cols-2">
              {courses.map((c) => (
                <label key={c.courseId} className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={f.courseIds.includes(c.courseId)} onChange={() => toggleCourse(c.courseId)} />
                  {c.title}
                </label>
              ))}
              {f.categoryIds.length > 0 && <p className="text-xs text-muted sm:col-span-2">Also covers {f.categoryIds.length} whole categor{f.categoryIds.length === 1 ? 'y' : 'ies'}.</p>}
            </div>
          )}
        </div>
        {error && <p role="alert" className="text-sm text-danger sm:col-span-2">{error}</p>}
        <div className="flex gap-2 sm:col-span-2">
          <Button type="submit" disabled={busy}>{busy && <Spinner className="size-4" />}{plan ? 'Save plan' : 'Create plan'}</Button>
          <Button variant="ghost" onClick={() => onDone(false)}>Cancel</Button>
        </div>
      </form>
    </Card>
  )
}

export default function PlansPage() {
  const [state, reload] = useApi<PlanEditor[]>('/api/admin/plans')
  const [courses] = useApi<CoursePrice[]>('/api/admin/pricing')
  const [editing, setEditing] = useState<PlanEditor | 'new' | null>(null)
  const courseList = courses.status === 'ok' ? courses.data : []
  const done = (saved: boolean) => { setEditing(null); if (saved) reload() }

  return (
    <>
      <PageHeader
        title="Subscription plans"
        description="Memberships that unlock many courses. Price changes apply to new purchases only."
        actions={!editing && <Button onClick={() => setEditing('new')}><Plus className="size-4" />New plan</Button>}
      />
      {editing && <PlanForm key={editing === 'new' ? 'new' : editing.id} plan={editing === 'new' ? null : editing} courses={courseList} onDone={done} />}
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load plans.</p>}
      {state.status === 'ok' && state.data.length === 0 && !editing && (
        <EmptyState icon={Ticket} title="No plans yet">Create a monthly or yearly membership. It goes on sale when you set it to “On sale”.</EmptyState>
      )}
      {state.status === 'ok' && state.data.length > 0 && (
        <div className="grid gap-4 md:grid-cols-2">
          {state.data.map((p) => (
            <Card key={p.id}>
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="font-semibold">{p.name}</h2>
                  <p className="text-xs text-muted">/{p.slug}</p>
                </div>
                <StatusBadge status={p.status === 'inactive' ? 'not_on_sale' : p.status} />
              </div>
              <p className="mt-3"><span className="text-xl font-bold tabular-nums">{formatMoney(p.priceMinor, p.currency)}</span> <span className="text-sm text-muted">{periodLabel(p.billingPeriod, p.periodDays)}</span></p>
              <p className="mt-1 text-sm text-muted">
                {p.allCourses ? 'Every published course' : `${p.courseIds.length} courses${p.categoryIds.length ? `, ${p.categoryIds.length} categories` : ''}`}
                {' · '}{p.activeSubscribers} active {p.activeSubscribers === 1 ? 'member' : 'members'}
              </p>
              <Button variant="secondary" size="sm" className="mt-4" onClick={() => setEditing(p)}>Edit</Button>
            </Card>
          ))}
        </div>
      )}
    </>
  )
}
