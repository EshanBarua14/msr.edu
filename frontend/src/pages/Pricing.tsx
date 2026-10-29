import { BadgeCheck, BookOpenCheck, Ticket } from 'lucide-react'
import { Card } from '@/components/ui/Card'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'

export default function Pricing() {
  return (
    <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
      <PageHeader title="Pricing" description="Buy a single course, or become a member for access to every eligible course." />
      <div className="grid gap-5 md:grid-cols-2">
        <Card>
          <BookOpenCheck className="size-6 text-brand-500" />
          <h2 className="mt-3 text-lg font-semibold">Individual courses</h2>
          <p className="mt-1 text-sm text-muted">One payment, with the access period shown on each course before checkout.</p>
        </Card>
        <Card>
          <BadgeCheck className="size-6 text-brand-500" />
          <h2 className="mt-3 text-lg font-semibold">Membership</h2>
          <p className="mt-1 text-sm text-muted">Monthly or yearly plans covering every course included in the plan.</p>
        </Card>
      </div>
      <div className="mt-8">
        <EmptyState icon={Ticket} title="Plans are not on sale yet">
          Prices are set by the MSR.edu team and will appear here once published.
        </EmptyState>
      </div>
    </div>
  )
}
