import type { LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'

export function EmptyState({ icon: Icon, title, children }: { icon: LucideIcon; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-[var(--radius-card)] border border-dashed border-border px-6 py-12 text-center">
      <span className="grid size-12 place-items-center rounded-2xl bg-brand-500/10 text-brand-500">
        <Icon className="size-6" />
      </span>
      <h3 className="font-semibold">{title}</h3>
      {children && <div className="max-w-sm text-sm text-muted">{children}</div>}
    </div>
  )
}
