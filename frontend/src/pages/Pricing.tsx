import { Link } from 'react-router-dom'
import { motion } from 'motion/react'
import { BadgeCheck, BookOpenCheck, Check, Ticket } from 'lucide-react'
import { BuyButton } from '@/components/billing/BuyButton'
import { Price } from '@/components/billing/Price'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'
import { Spinner } from '@/components/ui/Spinner'
import { periodLabel, type PlanOffer } from '@/lib/billing'
import { useApi } from '@/lib/useApi'

function PlanCard({ plan, index }: { plan: PlanOffer; index: number }) {
  const covers = plan.allCourses ? ['Every published course'] : [...plan.categories.map((c) => `All ${c} courses`), ...plan.courses]
  return (
    <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: index * 0.05 }}>
      <Card className="flex h-full flex-col">
        <h2 className="text-lg font-semibold">{plan.name}</h2>
        {plan.description && <p className="mt-1 text-sm text-muted">{plan.description}</p>}
        <p className="mt-4">
          <Price className="text-3xl" priceMinor={plan.priceMinor} regularMinor={plan.regularMinor} currency={plan.currency} />
          <span className="ml-1 text-sm text-muted">{periodLabel(plan.billingPeriod, plan.periodDays)}</span>
        </p>
        <ul className="mt-4 flex flex-1 flex-col gap-2 text-sm">
          {covers.slice(0, 6).map((c) => (
            <li key={c} className="flex gap-2"><Check className="mt-0.5 size-4 shrink-0 text-success" />{c}</li>
          ))}
          {covers.length > 6 && <li className="text-muted">and {covers.length - 6} more</li>}
        </ul>
        <BuyButton productId={plan.productId} className="mt-6">Join {plan.name}</BuyButton>
        <p className="mt-2 text-center text-xs text-muted">Renews manually. Nothing is charged automatically.</p>
      </Card>
    </motion.div>
  )
}

export default function Pricing() {
  const [state] = useApi<PlanOffer[]>('/api/plans')
  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <PageHeader title="Pricing" description="Buy a single course, or become a member for access to every course in your plan." />
      {state.status === 'loading' && <Spinner className="text-brand-500" />}
      {state.status === 'error' && <p className="text-danger">Could not load plans.</p>}
      {state.status === 'ok' && state.data.length > 0 && (
        <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
          {state.data.map((p, i) => <PlanCard key={p.id} plan={p} index={i} />)}
        </div>
      )}
      {state.status === 'ok' && state.data.length === 0 && (
        <EmptyState icon={Ticket} title="Memberships are not on sale yet">
          Prices are set by the MSR.edu team and will appear here once published.
        </EmptyState>
      )}
      <div className="mt-10 grid gap-5 md:grid-cols-2">
        <Card>
          <BookOpenCheck className="size-6 text-brand-500" />
          <h2 className="mt-3 text-lg font-semibold">Individual courses</h2>
          <p className="mt-1 text-sm text-muted">One payment, with the access period shown on each course before checkout.</p>
          <Link to="/courses" className={buttonClass('secondary', 'sm', 'mt-4')}>Browse courses</Link>
        </Card>
        <Card>
          <BadgeCheck className="size-6 text-brand-500" />
          <h2 className="mt-3 text-lg font-semibold">Access after payment is confirmed</h2>
          <p className="mt-1 text-sm text-muted">
            Courses unlock as soon as the payment provider confirms your payment, and a receipt is issued automatically.
          </p>
        </Card>
      </div>
    </div>
  )
}
