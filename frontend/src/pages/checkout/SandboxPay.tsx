import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { CheckCircle2, FlaskConical, XCircle } from 'lucide-react'
import { AccessPrompt } from '@/components/learn/AccessPrompt'
import { Button } from '@/components/ui/Button'
import { Card } from '@/components/ui/Card'
import { FullPageSpinner, Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import type { SandboxPayment } from '@/lib/billing'
import { formatMoney } from '@/lib/business'
import { useApi } from '@/lib/useApi'

type Outcome = 'success' | 'fail' | 'cancel'

/**
 * Stands in for a real gateway's hosted payment page during development. Choosing an outcome makes the
 * sandbox send a signed webhook, which the server verifies like any other provider's.
 */
export default function SandboxPay() {
  const { txnId = '' } = useParams()
  const navigate = useNavigate()
  const [state] = useApi<SandboxPayment>(`/api/sandbox/payments/${encodeURIComponent(txnId)}`)
  const [busy, setBusy] = useState<Outcome | null>(null)
  const [error, setError] = useState<string | null>(null)

  if (state.status === 'loading') return <FullPageSpinner />
  if (state.status === 'error') return <AccessPrompt status={state.error.status} />
  const p = state.data

  async function choose(outcome: Outcome) {
    setBusy(outcome)
    setError(null)
    try {
      const { reference } = await api<{ reference: string }>(`/api/sandbox/payments/${encodeURIComponent(txnId)}`, { json: { outcome } })
      navigate(`/checkout/return/${reference}`, { replace: true })
    } catch (e) {
      setError(problemText(e))
      setBusy(null)
    }
  }

  const payable = p.status === 'pending' || p.status === 'expired'
  return (
    <div className="mx-auto max-w-md px-4 py-12">
      <Card className="text-center">
        <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-warning/12 text-warning">
          <FlaskConical className="size-6" />
        </span>
        <h1 className="mt-4 text-lg font-semibold">Sandbox payment</h1>
        <p className="mt-1 text-sm text-muted">Test mode. No real money moves. A real gateway's page appears here once one is set up.</p>
        <dl className="mt-6 grid grid-cols-2 gap-y-2 rounded-2xl bg-surface-2 p-4 text-left text-sm">
          <dt className="text-muted">Item</dt><dd className="text-right font-medium">{p.description}</dd>
          <dt className="text-muted">Order</dt><dd className="text-right font-mono text-xs leading-5">{p.orderReference}</dd>
          <dt className="text-muted">Amount</dt><dd className="text-right font-semibold tabular-nums">{formatMoney(p.amountMinor, p.currency)}</dd>
        </dl>
        {payable ? (
          <div className="mt-6 flex flex-col gap-2">
            <Button onClick={() => choose('success')} disabled={!!busy}>
              {busy === 'success' ? <Spinner className="size-4" /> : <CheckCircle2 className="size-4" />}Pay {formatMoney(p.amountMinor, p.currency)}
            </Button>
            <div className="grid grid-cols-2 gap-2">
              <Button variant="secondary" onClick={() => choose('fail')} disabled={!!busy}>
                {busy === 'fail' ? <Spinner className="size-4" /> : <XCircle className="size-4" />}Decline
              </Button>
              <Button variant="ghost" onClick={() => choose('cancel')} disabled={!!busy}>Cancel</Button>
            </div>
          </div>
        ) : (
          <Button className="mt-6 w-full" variant="secondary" onClick={() => navigate(`/checkout/return/${p.orderReference}`)}>
            See order status
          </Button>
        )}
        {error && <p role="alert" className="mt-3 text-sm text-danger">{error}</p>}
      </Card>
    </div>
  )
}
