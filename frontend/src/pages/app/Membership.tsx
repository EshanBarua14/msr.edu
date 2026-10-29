import { Link } from 'react-router-dom'
import { GraduationCap, KeyRound } from 'lucide-react'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { formatDate, statusLabel, type Membership as MembershipData } from '@/lib/billing'
import { useApi } from '@/lib/useApi'

export default function Membership() {
  const [state] = useApi<MembershipData>('/api/me/membership')
  return (
    <>
      <PageHeader
        title="Membership"
        description="Your plans and everything you have access to."
        actions={<Link to="/pricing" className={buttonClass('secondary', 'sm')}>See plans</Link>}
      />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load your membership.</p>}
      {state.status === 'ok' && (
        <div className="flex flex-col gap-8">
          <section>
            <h2 className="mb-3 font-semibold">Plans</h2>
            {state.data.subscriptions.length === 0 ? (
              <EmptyState icon={GraduationCap} title="No membership yet">
                A membership unlocks every course in its plan. <Link to="/pricing" className="text-brand-600 hover:underline">Compare plans</Link>
              </EmptyState>
            ) : (
              <div className="grid gap-4 md:grid-cols-2">
                {state.data.subscriptions.map((s) => (
                  <Card key={s.id}>
                    <div className="flex items-start justify-between gap-3">
                      <h3 className="font-semibold">{s.planName}</h3>
                      <StatusBadge status={s.status} />
                    </div>
                    <p className="mt-2 text-sm text-muted">
                      {formatDate(s.periodStart)} to {formatDate(s.periodEnd)}
                      {s.graceUntil && s.graceUntil !== s.periodEnd && <> · grace until {formatDate(s.graceUntil)}</>}
                    </p>
                    <p className="mt-3 text-xs text-muted">Renewal is manual: buy the plan again before it ends to extend it.</p>
                  </Card>
                ))}
              </div>
            )}
          </section>
          <section>
            <h2 className="mb-3 font-semibold">Access</h2>
            {state.data.access.length === 0 ? (
              <EmptyState icon={KeyRound} title="Nothing unlocked yet">Free courses and previews are always open.</EmptyState>
            ) : (
              <Card className="overflow-x-auto p-0">
                <table className="w-full min-w-[36rem] text-sm">
                  <thead className="border-b border-border text-left text-xs text-muted">
                    <tr><th className="px-5 py-3 font-medium">Course or plan</th><th className="px-3 py-3 font-medium">How</th><th className="px-3 py-3 font-medium">Status</th><th className="px-5 py-3 font-medium">Ends</th></tr>
                  </thead>
                  <tbody>
                    {state.data.access.map((a) => (
                      <tr key={a.id} className="border-b border-border last:border-0">
                        <td className="px-5 py-3 font-medium">
                          {a.courseSlug ? <Link to={`/courses/${a.courseSlug}`} className="hover:text-brand-600">{a.title}</Link> : a.title}
                        </td>
                        <td className="px-3 py-3 text-muted">{statusLabel(a.source)}</td>
                        <td className="px-3 py-3"><StatusBadge status={a.status} /></td>
                        <td className="px-5 py-3 text-muted">{a.expiresAt ? formatDate(a.expiresAt) : 'Never'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </Card>
            )}
          </section>
        </div>
      )}
    </>
  )
}
