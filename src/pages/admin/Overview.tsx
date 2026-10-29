import { motion, type Variants } from 'motion/react'
import { AlertTriangle, ArrowDownLeft, Banknote, CheckCircle2, Circle, Clock, CreditCard, RotateCcw, Ticket, XCircle } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { hasAnyRole } from '@/auth/roles'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { businessDefaults, formatMoney } from '@/lib/business'
import { env } from '@/lib/env'

const container: Variants = { hidden: {}, show: { transition: { staggerChildren: 0.05 } } }
const item: Variants = { hidden: { opacity: 0, y: 10 }, show: { opacity: 1, y: 0 } }

// Figures will come from verified transaction records (server-side views), never client analytics.
const kpis = [
  { label: 'Verified payments', value: '0', icon: CheckCircle2 },
  { label: 'Gross sales', value: formatMoney(0), icon: Banknote },
  { label: 'Refunds', value: formatMoney(0), icon: ArrowDownLeft },
  { label: 'Net sales', value: formatMoney(0), icon: CreditCard },
  { label: 'Subscription revenue', value: formatMoney(0), icon: Ticket },
  { label: 'Pending payments', value: '0', icon: Clock },
  { label: 'Failed payments', value: '0', icon: XCircle },
  { label: 'Refunds awaiting action', value: '0', icon: RotateCcw },
]

const setup = [
  { label: 'Connect Supabase project (URL + anon key)', done: env.hasSupabase },
  { label: 'Enable Google provider in Supabase Auth', done: false },
  { label: `Confirm business defaults: ${businessDefaults.currency}, ${businessDefaults.subscriptionRenewal} renewal, ${businessDefaults.courseAccess} course access`, done: false },
  { label: 'Choose and verify a payment gateway', done: false },
  { label: 'Connect Google Drive for course resources', done: false },
]

export default function AdminOverview() {
  const { roles } = useAuth()
  const showFinance = hasAnyRole(roles, ['finance_manager'])

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

        <div className="grid gap-6 lg:grid-cols-2">
          <motion.div variants={item}>
            <Card className="h-full">
              <h2 className="font-semibold">Setup checklist</h2>
              <ul className="mt-4 flex flex-col gap-3">
                {setup.map((s) => (
                  <li key={s.label} className="flex items-start gap-3 text-sm">
                    {s.done ? <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-success" /> : <Circle className="mt-0.5 size-4 shrink-0 text-muted" />}
                    <span className={s.done ? 'text-muted line-through' : ''}>{s.label}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </motion.div>
          <motion.div variants={item}>
            <Card className="h-full">
              <h2 className="font-semibold">Reconciliation</h2>
              <div className="mt-4 flex items-center gap-3 rounded-2xl bg-surface-2 p-4 text-sm text-muted">
                <AlertTriangle className="size-4 shrink-0" /> No unresolved payment discrepancies.
              </div>
            </Card>
          </motion.div>
        </div>
      </motion.div>
    </>
  )
}
