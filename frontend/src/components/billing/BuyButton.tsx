import { useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '@/auth/AuthProvider'
import { Button } from '@/components/ui/Button'
import type { Variant } from '@/components/ui/buttonClass'
import { Spinner } from '@/components/ui/Spinner'
import { problemText } from '@/lib/admin'
import { api } from '@/lib/api'
import type { CheckoutResult } from '@/lib/billing'

/**
 * Starts checkout for a product. The server sets the price; the browser only says what to buy.
 * Signed-out visitors are sent to sign in first and come back here.
 */
export function BuyButton({ productId, children, variant = 'primary', className }: {
  productId: string; children: React.ReactNode; variant?: Variant; className?: string
}) {
  const { status } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function buy() {
    if (status !== 'signed_in') {
      navigate(`/login?next=${encodeURIComponent(location.pathname)}`)
      return
    }
    setBusy(true)
    setError(null)
    try {
      const { redirectUrl } = await api<CheckoutResult>('/api/checkout', { json: { productId } })
      // Real gateways host their own payment page; the sandbox page is ours.
      if (redirectUrl.startsWith('/')) navigate(redirectUrl)
      else window.location.assign(redirectUrl)
    } catch (e) {
      setError(problemText(e))
      setBusy(false)
    }
  }

  return (
    <div className={className}>
      <Button variant={variant} className="w-full" onClick={buy} disabled={busy}>
        {busy && <Spinner className="size-4" />}
        {children}
      </Button>
      {error && <p role="alert" className="mt-2 text-center text-sm text-danger">{error}</p>}
    </div>
  )
}
