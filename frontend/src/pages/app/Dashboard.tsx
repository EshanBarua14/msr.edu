import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { motion, type Variants } from 'motion/react'
import { BellRing, BookOpen, CreditCard, GraduationCap, ListChecks, PlayCircle } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { StatusBadge } from '@/components/billing/StatusBadge'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { AttemptRow } from '@/components/learn/AttemptRow'
import { CourseProgressRow } from '@/components/learn/CourseProgressRow'
import { EmptyState } from '@/components/ui/EmptyState'
import { expiringSoon, formatDate, type Membership, type OrderView } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import type { MyLearning } from '@/lib/learning'
import { useApi } from '@/lib/useApi'

const container: Variants = { hidden: {}, show: { transition: { staggerChildren: 0.06 } } }
const item: Variants = { hidden: { opacity: 0, y: 12 }, show: { opacity: 1, y: 0 } }
const EXPIRING_DAYS = 14
const link = 'text-sm font-medium text-brand-600 hover:underline'

function Section({ title, action, children, className }: { title: string; action?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <motion.section variants={item} className={className}>
      <Card className="h-full">
        <div className="mb-4 flex items-center justify-between gap-2">
          <h2 className="font-semibold">{title}</h2>
          {action}
        </div>
        {children}
      </Card>
    </motion.section>
  )
}

export default function Dashboard() {
  const { profile } = useAuth()
  const firstName = profile?.fullName?.split(' ')[0]
  const [learning] = useApi<MyLearning>('/api/me/learning')
  const courses = learning.data?.courses ?? []
  const current = courses[0]
  const attempts = learning.data?.recentAttempts ?? []
  const [membership] = useApi<Membership>('/api/me/membership')
  const [orders] = useApi<OrderView[]>('/api/me/orders')
  const plans = (membership.data?.subscriptions ?? []).filter((s) => s.status === 'active' || s.status === 'grace')
  const expiring = expiringSoon(membership.data?.access ?? [], EXPIRING_DAYS)
  const recent = (orders.data ?? []).slice(0, 3)

  return (
    <motion.div variants={container} initial="hidden" animate="show" className="grid gap-5 lg:grid-cols-3">
      <motion.div variants={item} className="lg:col-span-3">
        <div className="relative overflow-hidden rounded-[var(--radius-card)] bg-gradient-to-br from-brand-600 via-brand-500 to-accent-500 p-6 text-white sm:p-8">
          <div aria-hidden className="absolute -top-16 -right-16 size-64 rounded-full bg-white/10 blur-2xl" />
          <p className="text-sm text-white/80">Welcome back</p>
          <h1 className="mt-1 text-2xl font-bold sm:text-3xl">{firstName ? `Hi, ${firstName}` : 'Hi there'}</h1>
          <p className="mt-2 max-w-lg text-white/85">Pick up where you left off, or explore a new course.</p>
          <Link to="/courses" className={buttonClass('secondary', 'md', 'mt-6 border-0 bg-white text-brand-700 hover:bg-white/90')}>
            Browse courses
          </Link>
        </div>
      </motion.div>

      <Section title="Continue learning" className="lg:col-span-2">
        {current ? (
          <CourseProgressRow c={current} />
        ) : (
          <EmptyState icon={PlayCircle} title="Nothing in progress">
            Start a course or a free preview and it will show up here.
          </EmptyState>
        )}
      </Section>

      <Section title="Membership" action={plans.length > 0 && <Link to="/app/subscription" className={link}>Details</Link>}>
        {plans.length > 0 ? (
          <ul className="flex flex-col gap-3 text-sm">
            {plans.map((p) => (
              <li key={p.id} className="flex items-start justify-between gap-2">
                <span><span className="font-medium">{p.planName}</span><span className="block text-xs text-muted">Until {formatDate(p.periodEnd)}</span></span>
                <StatusBadge status={p.status} />
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon={GraduationCap} title="No active membership">
            <Link to="/pricing" className="font-medium text-brand-600 hover:underline">See plans</Link>
          </EmptyState>
        )}
      </Section>

      <Section
        title="My courses"
        className="lg:col-span-2"
        action={courses.length > 0 && <Link to="/app/courses" className="text-sm font-medium text-brand-600 hover:underline">View all</Link>}
      >
        {courses.length > 0 ? (
          <div className="flex flex-col gap-1">
            {courses.slice(0, 3).map((c) => <CourseProgressRow key={c.courseId} c={c} />)}
          </div>
        ) : (
          <EmptyState icon={BookOpen} title="No courses started yet" />
        )}
      </Section>

      <Section title="Expiring access">
        {expiring.length > 0 ? (
          <ul className="flex flex-col gap-3 text-sm">
            {expiring.map((a) => (
              <li key={a.id} className="flex items-center justify-between gap-2">
                <span className="font-medium">{a.title}</span>
                <span className="text-xs text-warning">Ends {formatDate(a.expiresAt)}</span>
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon={BellRing} title="Nothing expiring soon" />
        )}
      </Section>

      <Section
        title="Quiz results"
        action={attempts.length > 0 && <Link to="/app/quizzes" className="text-sm font-medium text-brand-600 hover:underline">All</Link>}
      >
        {attempts.length > 0 ? (
          <div className="-mx-3 flex flex-col">
            {attempts.slice(0, 3).map((a) => <AttemptRow key={a.id} a={a} />)}
          </div>
        ) : (
          <EmptyState icon={ListChecks} title="No quiz attempts yet" />
        )}
      </Section>

      <Section
        title="Recent transactions"
        className="lg:col-span-2"
        action={<Link to="/app/transactions" className={link}>View all</Link>}
      >
        {recent.length > 0 ? (
          <ul className="flex flex-col divide-y divide-border text-sm">
            {recent.map((o) => (
              <li key={o.reference} className="flex items-center justify-between gap-3 py-2.5">
                <span>
                  <span className="font-medium">{o.items.map((i) => i.name).join(', ')}</span>
                  <span className="block text-xs text-muted">{formatDate(o.paidAt ?? o.createdAt)} · {o.reference}</span>
                </span>
                <span className="flex items-center gap-3"><span className="tabular-nums">{formatMoney(o.totalMinor, o.currency)}</span><StatusBadge status={o.status} /></span>
              </li>
            ))}
          </ul>
        ) : (
          <EmptyState icon={CreditCard} title="No transactions yet">
            Receipts appear here once a payment is verified.
          </EmptyState>
        )}
      </Section>
    </motion.div>
  )
}
