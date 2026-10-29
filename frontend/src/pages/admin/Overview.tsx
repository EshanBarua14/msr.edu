import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { motion, type Variants } from 'motion/react'
import { AlertTriangle, ArrowDownLeft, Banknote, CheckCircle2, Circle, Clock, CreditCard, RotateCcw, Ticket, XCircle } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { hasAnyRole } from '@/auth/roles'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { api } from '@/lib/api'
import { businessDefaults, formatMoney } from '@/lib/business'
import type { SetupStatus } from '@/lib/ops'
import { useApi } from '@/lib/useApi'

const container: Variants = { hidden: {}, show: { transition: { staggerChildren: 0.05 } } }
const item: Variants = { hidden: { opacity: 0, y: 10 }, show: { opacity: 1, y: 0 } }

/** Server-computed from verified transaction records, never client analytics. */
interface Overview {
  verifiedPayments: number
  grossMinor: number
  refundedMinor: number
  netMinor: number
  subscriptionMinor: number
  courseMinor: number
  pendingPayments: number
  failedPayments: number
  refundsAwaitingAction: number
  openReconciliation: number
  currency: string
}

export default function AdminOverview() {
  const { roles, providers } = useAuth()
  const showFinance = hasAnyRole(roles, ['finance_manager'])
  const [data, setData] = useState<Overview | null>(null)
  const [error, setError] = useState(false)
  const [setupState] = useApi<SetupStatus>('/api/admin/setup')
  const isOwner = roles.includes('owner_admin')

  useEffect(() => {
    if (!showFinance) return
    api<Overview>('/api/admin/overview').then(setData, () => setError(true))
  }, [showFinance])

  const money = (v?: number) => (data ? formatMoney(v ?? 0, data.currency) : '—')
  const count = (v?: number) => (data ? String(v ?? 0) : '—')
  const kpis = [
    { label: 'Verified payments', value: count(data?.verifiedPayments), icon: CheckCircle2 },
    { label: 'Gross sales', value: money(data?.grossMinor), icon: Banknote },
    { label: 'Refunds', value: money(data?.refundedMinor), icon: ArrowDownLeft },
    { label: 'Net sales', value: money(data?.netMinor), icon: CreditCard },
    { label: 'Subscription revenue', value: money(data?.subscriptionMinor), icon: Ticket },
    { label: 'Pending payments', value: count(data?.pendingPayments), icon: Clock },
    { label: 'Failed payments', value: count(data?.failedPayments), icon: XCircle },
    { label: 'Refunds awaiting action', value: count(data?.refundsAwaitingAction), icon: RotateCcw },
  ]

  const status = setupState.data
  const setup = [
    { label: 'API connected to PostgreSQL', done: status?.database ?? providers !== null },
    { label: 'Google sign-in configured on the API', done: status?.googleSignIn ?? providers?.google ?? false },
    {
      label: `Confirm business defaults: ${businessDefaults.currency}, ${businessDefaults.subscriptionRenewal} renewal, ${businessDefaults.courseAccess} course access`,
      done: status?.defaultsConfirmed ?? false,
      to: isOwner ? '/admin/settings' : undefined,
    },
    {
      label: status?.gatewayIsSandbox ? 'Choose a real payment gateway (test payments only for now)' : 'Choose and verify a payment gateway',
      done: !!status?.gateway && !status.gatewayIsSandbox,
    },
    { label: 'Connect Google Drive for course resources', done: status?.drive ?? false },
    { label: 'Add an AI key for quiz question drafts (optional)', done: status?.ai ?? false },
  ]

  return (
    <>
      <PageHeader title="Overview" description="Business and content health at a glance." />
      <motion.div variants={container} initial="hidden" animate="show" className="flex flex-col gap-6">
        {showFinance && (
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            {kpis.map((k) => (
              <motion.div key={k.label} variants={item}>
                <Card className="p-5">
                  <div className="flex items-center justify-between text-muted">
                    <span className="text-xs font-medium">{k.label}</span>
                    <k.icon className="size-4" />
                  </div>
                  <p className="mt-3 text-2xl font-bold tabular-nums">{k.value}</p>
                </Card>
              </motion.div>
            ))}
          </div>
        )}
        {error && <p className="text-sm text-danger">Could not load payment figures.</p>}

        <div className="grid gap-6 lg:grid-cols-2">
          <motion.div variants={item}>
            <Card className="h-full">
              <h2 className="font-semibold">Setup checklist</h2>
              <ul className="mt-4 flex flex-col gap-3">
                {setup.map((s) => (
                  <li key={s.label} className="flex items-start gap-3 text-sm">
                    {s.done ? <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" /> : <Circle className="mt-0.5 size-4 shrink-0 text-muted" />}
                    {s.to && !s.done ? (
                      <Link to={s.to} className="text-brand-600 hover:underline">{s.label}</Link>
                    ) : (
                      <span className={s.done ? 'text-muted line-through' : ''}>{s.label}</span>
                    )}
                  </li>
                ))}
              </ul>
            </Card>
          </motion.div>
          <motion.div variants={item}>
            <Card className="h-full">
              <h2 className="font-semibold">Reconciliation</h2>
              <div className="mt-4 flex items-center gap-3 rounded-2xl bg-surface-2 p-4 text-sm text-muted">
                <AlertTriangle className="size-4 shrink-0" />
                {data && data.openReconciliation > 0 ? (
                  <Link to="/admin/reconciliation" className="text-warning hover:underline">
                    {data.openReconciliation} payment {data.openReconciliation === 1 ? 'issue needs' : 'issues need'} attention
                  </Link>
                ) : (
                  'No unresolved payment discrepancies.'
                )}
              </div>
            </Card>
          </motion.div>
        </div>
      </motion.div>
    </>
  )
}
