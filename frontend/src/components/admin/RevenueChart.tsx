import { useState } from 'react'
import { formatMoney } from '@/lib/business'
import { niceMax, periodLabel, type RevenueRow } from '@/lib/ops'

const W = 720, H = 240, PAD = { top: 12, right: 8, bottom: 28, left: 64 }

/** Net sales per period as bars on one axis, with a hover tooltip. The table below the chart carries every value. */
export function RevenueChart({ rows, currency }: { rows: RevenueRow[]; currency: string }) {
  const [hover, setHover] = useState<number | null>(null)
  const max = niceMax(Math.max(...rows.map((r) => r.netMinor), 0))
  const plotW = W - PAD.left - PAD.right
  const plotH = H - PAD.top - PAD.bottom
  const slot = plotW / Math.max(rows.length, 1)
  const barW = Math.max(2, Math.min(28, slot - 2))
  const y = (v: number) => PAD.top + plotH - (Math.max(v, 0) / max) * plotH
  const ticks = [0, 0.5, 1].map((t) => t * max)
  const labelEvery = Math.ceil(rows.length / 8)
  const h = hover != null ? rows[hover] : null

  return (
    <div className="relative">
      <svg viewBox={`0 0 ${W} ${H}`} className="h-auto w-full" role="img" aria-label="Net sales by period" onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PAD.left} x2={W - PAD.right} y1={y(t)} y2={y(t)} stroke="var(--color-border)" strokeWidth={1} />
            <text x={PAD.left - 8} y={y(t) + 4} textAnchor="end" fontSize={11} fill="var(--color-muted)" className="tabular-nums">
              {formatMoney(t, currency)}
            </text>
          </g>
        ))}
        {rows.map((r, i) => {
          const x = PAD.left + i * slot + (slot - barW) / 2
          const top = y(r.netMinor)
          const height = PAD.top + plotH - top
          return (
            <g key={r.period}>
              {height > 0 && (
                // Rounded data end, square at the baseline: a rounded rect plus a square cap over its lower corners.
                <g fill="var(--color-brand-500)" opacity={hover == null || hover === i ? 1 : 0.45}>
                  <rect x={x} y={top} width={barW} height={height} rx={Math.min(4, barW / 2, height)} />
                  {height > 4 && <rect x={x} y={top + height / 2} width={barW} height={height / 2} />}
                </g>
              )}
              {i % labelEvery === 0 && (
                <text x={x + barW / 2} y={H - 8} textAnchor="middle" fontSize={11} fill="var(--color-muted)">{periodLabel(r.period)}</text>
              )}
              <rect x={PAD.left + i * slot} y={PAD.top} width={slot} height={plotH} fill="transparent" onMouseEnter={() => setHover(i)} />
            </g>
          )
        })}
        <line x1={PAD.left} x2={W - PAD.right} y1={PAD.top + plotH} y2={PAD.top + plotH} stroke="var(--color-muted)" strokeWidth={1} />
      </svg>
      {h && hover != null && (
        <div
          className="pointer-events-none absolute top-2 rounded-xl border border-border bg-surface px-3 py-2 text-xs shadow-lg"
          style={{ left: `${Math.min(80, ((PAD.left + hover * slot) / W) * 100)}%` }}
        >
          <p className="font-semibold">{periodLabel(h.period)}</p>
          <p className="tabular-nums">Net {formatMoney(h.netMinor, currency)}</p>
          <p className="text-muted tabular-nums">{h.payments} payments · {formatMoney(h.refundedMinor, currency)} refunded</p>
        </div>
      )}
    </div>
  )
}
