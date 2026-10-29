/** Shapes from backend/src/MsrEdu.Api/Endpoints/OpsEndpoints.cs and AdminEndpoints.cs. Amounts are minor units. */
import type { Role } from '@/auth/roles'

export interface RevenueRow {
  period: string; payments: number; grossMinor: number; refundedMinor: number; netMinor: number; courseMinor: number; subscriptionMinor: number
}
export interface RevenueReport {
  currency: string; from: string; to: string; group: 'day' | 'month'; totals: RevenueRow; rows: RevenueRow[]
  products: { name: string; kind: 'course' | 'subscription_plan'; sales: number; grossMinor: number }[]
}

export interface IssueView {
  id: string; issue: string; status: string; details: Record<string, unknown> | null; orderId: string | null; orderReference: string | null
  userEmail: string | null; createdAt: string; resolvedBy: string | null; resolvedAt: string | null; resolutionNote: string | null
}

export interface AuditRow {
  id: number; createdAt: string; actor: string | null; action: 'insert' | 'update' | 'delete'; entityType: string; entityId: string | null
  before: Record<string, unknown> | null; after: Record<string, unknown> | null
}
export interface AuditPage { rows: AuditRow[]; next: number | null }

export interface SettingView { key: string; value: unknown; description: string | null; isPublic: boolean; updatedAt: string; updatedBy: string | null }

export interface SetupStatus {
  database: boolean; googleSignIn: boolean; defaultsConfirmed: boolean; gateway: string | null; gatewayIsSandbox: boolean; drive: boolean; ai: boolean
}

export interface AdminUser { id: string; email: string; fullName: string | null; lastLoginAt: string | null; roles: Role[] }

/** Fields an audit row changed: for updates only what differs, for inserts and deletes every field. */
export function changedFields(row: Pick<AuditRow, 'before' | 'after'>) {
  const keys = new Set([...Object.keys(row.before ?? {}), ...Object.keys(row.after ?? {})])
  return [...keys]
    .filter((k) => !row.before || !row.after || JSON.stringify(row.before[k]) !== JSON.stringify(row.after[k]))
    .filter((k) => k !== 'updated_at')
    .map((k) => ({ field: k, before: row.before?.[k], after: row.after?.[k] }))
}

/** Short x-axis label for a report period ("2026-10-29" → "29 Oct", "2026-10" → "Oct 26"). */
export function periodLabel(period: string) {
  const [y, m, d] = period.split('-').map(Number)
  const month = new Date(Date.UTC(y, m - 1, 1)).toLocaleString('en-GB', { month: 'short', timeZone: 'UTC' })
  return d ? `${d} ${month}` : `${month} ${String(y).slice(2)}`
}

/** A tidy axis maximum: 1, 2 or 5 times a power of ten, at or above `max`. */
export function niceMax(max: number) {
  if (max <= 0) return 1
  const p = 10 ** Math.floor(Math.log10(max))
  return ([1, 2, 5, 10].find((m) => m * p >= max) ?? 10) * p
}

export const issueLabels: Record<string, string> = {
  amount_mismatch: 'Amount or currency did not match',
  paid_after_close: 'Paid after the order was closed',
  paid_after_expiry: 'Paid after the order expired',
}

/** Business settings the owner can edit, with how to edit each. */
export type SettingKind =
  | { type: 'choice'; options: [string, string][] }
  | { type: 'number'; min: number; max: number; nullable?: boolean; unit: string }
  | { type: 'toggle' }
  | { type: 'currency' }
  | { type: 'currencies' }

export const settingForms: Record<string, { label: string; kind: SettingKind }> = {
  'currency.default': { label: 'Default currency', kind: { type: 'currency' } },
  'currency.allowed': { label: 'Currencies admins may price in', kind: { type: 'currencies' } },
  'subscription.renewal_mode': { label: 'Membership renewal', kind: { type: 'choice', options: [['manual', 'Manual: learner pays again'], ['auto', 'Automatic (needs a gateway that supports it)']] } },
  'subscription.grace_days': { label: 'Grace period after a membership ends', kind: { type: 'number', min: 0, max: 90, unit: 'days' } },
  'course.default_access_days': { label: 'Default access for a course purchase', kind: { type: 'number', min: 1, max: 3650, nullable: true, unit: 'days (empty = lifetime)' } },
  'refund.access_policy': { label: 'Access after a full refund', kind: { type: 'choice', options: [['revoke_immediately', 'Remove access immediately'], ['revoke_after_days', 'Remove access after some days']] } },
  'refund.revoke_after_days': { label: 'Days of access kept after a refund', kind: { type: 'number', min: 0, max: 365, unit: 'days' } },
  'discounts.enabled': { label: 'Coupon codes at checkout', kind: { type: 'toggle' } },
  'bank_transfer.enabled': { label: 'Manual bank transfer', kind: { type: 'toggle' } },
  'order.expiry_minutes': { label: 'Unpaid orders expire after', kind: { type: 'number', min: 5, max: 1440, unit: 'minutes' } },
  'admin.model': { label: 'Admin model', kind: { type: 'choice', options: [['single_owner', 'Single owner does everything'], ['separate_roles', 'Separate finance, content and support roles']] } },
}

export const roleLabels: Record<Role, string> = {
  owner_admin: 'Owner', content_manager: 'Content', finance_manager: 'Finance', support_staff: 'Support', learner: 'Learner',
}
