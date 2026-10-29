/**
 * Placeholder business defaults pending owner confirmation (blueprint section 18).
 * The authoritative values live in the `app_settings` table; these are UI fallbacks only
 * and must never be used to compute a charge.
 */
export const businessDefaults = {
  currency: 'BDT',
  subscriptionRenewal: 'manual' as 'manual' | 'auto',
  courseAccess: 'lifetime' as 'lifetime' | 'fixed_days',
  couponsEnabled: false,
  adminModel: 'single_owner' as 'single_owner' | 'separate_roles',
}

/** Amounts are stored in minor units (paisa for BDT). */
export function formatMoney(amountMinor: number, currency = businessDefaults.currency, locale = 'en-BD') {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, minimumFractionDigits: 0 }).format(
    amountMinor / 100,
  )
}
