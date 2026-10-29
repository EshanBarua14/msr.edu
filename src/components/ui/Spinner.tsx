import { cn } from '@/lib/cn'

export function Spinner({ className }: { className?: string }) {
  return (
    <span
      role="status"
      aria-label="Loading"
      className={cn('inline-block size-5 animate-spin rounded-full border-2 border-current border-t-transparent', className)}
    />
  )
}

export function FullPageSpinner() {
  return (
    <div className="grid min-h-dvh place-items-center text-brand-500">
      <Spinner className="size-8" />
    </div>
  )
}
