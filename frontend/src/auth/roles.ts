export const ROLES = ['owner_admin', 'content_manager', 'finance_manager', 'support_staff', 'learner'] as const
export type Role = (typeof ROLES)[number]

export const STAFF_ROLES: readonly Role[] = ['owner_admin', 'content_manager', 'finance_manager', 'support_staff']

export const isStaff = (roles: readonly Role[]) => roles.some((r) => STAFF_ROLES.includes(r))

/** owner_admin implicitly satisfies every role check. */
export const hasAnyRole = (roles: readonly Role[], required: readonly Role[]) =>
  roles.includes('owner_admin') || required.some((r) => roles.includes(r))

/** Where a user lands after sign-in when no explicit return path is given. */
export const homeFor = (roles: readonly Role[]) => (isStaff(roles) ? '/admin' : '/app')

/** Only allow same-origin relative return paths, to avoid open redirects. */
export function safeReturnPath(raw: string | null | undefined): string | null {
  if (!raw || !raw.startsWith('/') || raw.startsWith('//') || raw.startsWith('/\\')) return null
  return raw
}
