/** Shapes from backend/src/MsrEdu.Api/Endpoints/PaymentEndpoints.cs and FinanceEndpoints.cs. Amounts are minor units. */

export type PaymentStatus =
  | 'created' | 'pending' | 'processing' | 'successful' | 'failed' | 'cancelled' | 'expired'
  | 'refund_pending' | 'partially_refunded' | 'refunded'

export type BillingPeriod = 'month' | 'year' | 'custom_days'
export type SaleStatus = 'active' | 'inactive' | 'archived'

export interface PriceInfo { productId: string; priceMinor: number; regularMinor: number; currency: string; accessDays: number | null }

export interface PlanOffer {
  id: string; slug: string; name: string; description: string | null; productId: string; priceMinor: number; regularMinor: number
  currency: string; billingPeriod: BillingPeriod; periodDays: number | null; allCourses: boolean; courses: string[]; categories: string[]
}

export interface PaymentsStatus { enabled: boolean; gateway: string | null; gatewayName: string | null; isSandbox: boolean }
export interface CheckoutResult { reference: string; redirectUrl: string }

export interface OrderLine { name: string; kind: 'course' | 'subscription_plan'; amountMinor: number; courseSlug: string | null }
export interface OrderView {
  reference: string; status: PaymentStatus; totalMinor: number; currency: string; createdAt: string; paidAt: string | null
  expiresAt: string | null; items: OrderLine[]; receiptNumber: string | null; refundedMinor: number
}

export interface SubscriptionView { id: string; planName: string; status: string; periodStart: string | null; periodEnd: string | null; graceUntil: string | null }
export interface AccessView {
  id: string; kind: 'course' | 'plan'; title: string; courseSlug: string | null; source: string; status: string
  statusReason: string | null; startsAt: string; expiresAt: string | null
}
export interface Membership { subscriptions: SubscriptionView[]; access: AccessView[] }

export interface ReceiptView {
  number: string; issuedAt: string; orderReference: string; customerName: string | null; customerEmail: string; items: OrderLine[]
  totalMinor: number; currency: string; provider: string; providerTxnId: string | null; paidAt: string | null; orderStatus: PaymentStatus
  refundedMinor: number
}

export interface SandboxPayment { providerTxnId: string; orderReference: string; description: string; amountMinor: number; currency: string; status: PaymentStatus }

// ------------------------------------------------------------------ admin
export interface CoursePrice {
  courseId: string; title: string; slug: string; courseStatus: string; isFree: boolean; productId: string | null; priceMinor: number | null
  promoPriceMinor: number | null; promoStartsAt: string | null; promoEndsAt: string | null; accessDays: number | null
  saleStatus: SaleStatus | null; currency: string
}

export interface PlanFields {
  slug: string; name: string; description: string | null; priceMinor: number; billingPeriod: BillingPeriod; periodDays: number | null
  graceDays: number; allCourses: boolean; courseIds: string[]; categoryIds: string[]; status: SaleStatus; position: number
}
export interface PlanEditor extends PlanFields { id: string; currency: string; activeSubscribers: number }

export interface OrderRow {
  id: string; reference: string; userEmail: string; status: PaymentStatus; totalMinor: number; currency: string
  createdAt: string; paidAt: string | null; items: string; provider: string | null
}
export interface TxnRow {
  id: string; provider: string; method: string; providerTxnId: string | null; amountMinor: number; currency: string
  status: PaymentStatus; failureReason: string | null; verifiedAt: string | null; createdAt: string
}
export interface EventRow {
  id: number; provider: string; providerEventId: string; eventType: string; signatureValid: boolean | null
  receivedAt: string; processedAt: string | null; processingError: string | null; payload: unknown
}
export interface RefundRow {
  id: string; amountMinor: number; currency: string; reason: string; status: string; isManual: boolean
  providerRefundId: string | null; createdAt: string; completedAt: string | null
}
export interface OrderDetail {
  id: string; userEmail: string; userId: string; order: OrderView; transactions: TxnRow[]; events: EventRow[]
  refunds: RefundRow[]; access: AccessView[]; issues: { id: string; issue: string; status: string; createdAt: string }[]
}
export interface AdminEntitlement {
  id: string; userId: string; userEmail: string; kind: 'course' | 'plan'; title: string; source: string; status: string
  statusReason: string | null; grantReason: string | null; startsAt: string; expiresAt: string | null
}

// ------------------------------------------------------------------ helpers
type Tone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger'

export function paymentTone(status: string): Tone {
  switch (status) {
    case 'successful': case 'active': case 'completed': return 'success'
    case 'pending': case 'processing': case 'created': case 'refund_pending': case 'grace': case 'scheduled': return 'warning'
    case 'failed': case 'revoked': return 'danger'
    case 'partially_refunded': case 'refunded': return 'brand'
    default: return 'neutral'
  }
}

/** "partially_refunded" → "Partially refunded". */
export const statusLabel = (status: string) => (status.charAt(0).toUpperCase() + status.slice(1)).replaceAll('_', ' ')

export const isFinal = (status: PaymentStatus) => !['created', 'pending', 'processing'].includes(status)

export function periodLabel(period: BillingPeriod, days: number | null) {
  return period === 'month' ? 'per month' : period === 'year' ? 'per year' : `every ${days ?? '?'} days`
}

export const accessLabel = (days: number | null) => (days ? `${days} days of access` : 'Lifetime access')

/** Admin forms take prices in major units (taka); the API stores minor units. Returns null for blank or invalid. */
export function toMinor(major: string): number | null {
  const v = major.trim()
  if (!/^\d+(\.\d{1,2})?$/.test(v)) return null
  return Math.round(Number(v) * 100)
}

export const toMajor = (minor: number | null | undefined) => (minor == null ? '' : String(minor / 100))

export const formatDate = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' }) : '—'

export const formatDateTime = (iso: string | null | undefined) =>
  iso ? new Date(iso).toLocaleString('en-GB', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—'

/** ISO → value for <input type="datetime-local"> in the viewer's time zone, and back. */
export function toLocalInput(iso: string | null) {
  if (!iso) return ''
  const d = new Date(iso)
  return new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}
export const fromLocalInput = (v: string) => (v ? new Date(v).toISOString() : null)
