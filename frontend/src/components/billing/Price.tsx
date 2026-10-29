import { formatMoney } from '@/lib/business'

/** Current price, with the regular price struck through while a promotion runs. */
export function Price({ priceMinor, regularMinor, currency, className }: {
  priceMinor: number; regularMinor: number; currency: string; className?: string
}) {
  return (
    <span className={className}>
      <span className="font-bold tabular-nums">{formatMoney(priceMinor, currency)}</span>
      {regularMinor > priceMinor && (
        <span className="ml-2 text-sm font-normal text-muted line-through tabular-nums">{formatMoney(regularMinor, currency)}</span>
      )}
    </span>
  )
}
