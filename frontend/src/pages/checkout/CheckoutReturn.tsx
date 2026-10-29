import { useEffect } from 'react'
import { Link, useParams } from 'react-router-dom'
import { motion } from 'motion/react'
import { CheckCircle2, Clock, XCircle } from 'lucide-react'
import { AccessPrompt } from '@/components/learn/AccessPrompt'
import { buttonClass } from '@/components/ui/buttonClass'
import { Card } from '@/components/ui/Card'
import { FullPageSpinner, Spinner } from '@/components/ui/Spinner'
import { isFinal, type OrderView } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'

/**
 * Where learners land after paying. Coming back here proves nothing: the page polls the order until the
 * gateway's verified webhook has settled it.
 */
export default function CheckoutReturn() {
  const { reference = '' } = useParams()
  const [state, reload] = useApi<OrderView>(`/api/orders/${encodeURIComponent(reference)}`)
  const waiting = state.status === 'ok' && !isFinal(state.data.status)

  useEffect(() => {
    if (!waiting) return
    const timer = setInterval(reload, 2000)
    return () => clearInterval(timer)
  }, [waiting, reload])

  if (state.status === 'loading') return <FullPageSpinner />
  if (state.status === 'error') return <AccessPrompt status={state.error.status} />
  const o = state.data
  const paid = o.status === 'successful'
  const course = o.items.find((i) => i.courseSlug)?.courseSlug
  const Icon = paid ? CheckCircle2 : waiting ? Clock : XCircle

  return (
    <div className="mx-auto max-w-md px-4 py-12">
      <motion.div initial={{ opacity: 0, scale: 0.98 }} animate={{ opacity: 1, scale: 1 }}>
        <Card className="text-center">
          <span className={`mx-auto grid size-14 place-items-center rounded-2xl ${paid ? 'bg-success/12 text-success' : waiting ? 'bg-warning/12 text-warning' : 'bg-danger/12 text-danger'}`}>
            <Icon className="size-7" />
          </span>
          <h1 className="mt-4 text-xl font-semibold">
            {paid ? 'Payment confirmed' : waiting ? 'Waiting for confirmation' : o.status === 'cancelled' ? 'Payment cancelled' : o.status === 'expired' ? 'Order expired' : 'Payment not completed'}
          </h1>
          <p className="mt-2 text-sm text-muted">
            {paid
              ? 'Your access is ready and a receipt has been issued.'
              : waiting
                ? 'We unlock access once the payment provider confirms. This usually takes a few seconds.'
                : 'Nothing was charged for this order. You can try again any time.'}
          </p>
          {waiting && <Spinner className="mx-auto mt-4 text-brand-500" />}
          <dl className="mt-6 grid grid-cols-2 gap-y-2 rounded-2xl bg-surface-2 p-4 text-left text-sm">
            <dt className="text-muted">Order</dt><dd className="text-right font-mono text-xs leading-5">{o.reference}</dd>
            <dt className="text-muted">{o.items.length === 1 ? 'Item' : 'Items'}</dt><dd className="text-right">{o.items.map((i) => i.name).join(', ')}</dd>
            <dt className="text-muted">Total</dt><dd className="text-right font-semibold tabular-nums">{formatMoney(o.totalMinor, o.currency)}</dd>
            {o.receiptNumber && (<><dt className="text-muted">Receipt</dt><dd className="text-right">{o.receiptNumber}</dd></>)}
          </dl>
          <div className="mt-6 flex flex-col gap-2">
            {paid && course && <Link to={`/courses/${course}`} className={buttonClass()}>Start learning</Link>}
            {paid && !course && <Link to="/app/subscription" className={buttonClass()}>View membership</Link>}
            {paid && o.receiptNumber && <Link to={`/app/receipts/${o.receiptNumber}`} className={buttonClass('secondary')}>View receipt</Link>}
            {!paid && !waiting && <Link to={course ? `/courses/${course}` : '/pricing'} className={buttonClass()}>Try again</Link>}
            <Link to="/app/transactions" className={buttonClass('ghost', 'sm')}>All transactions</Link>
          </div>
        </Card>
      </motion.div>
    </div>
  )
}
