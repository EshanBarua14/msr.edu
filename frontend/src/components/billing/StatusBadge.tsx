import { Badge } from '@/components/ui/Badge'
import { paymentTone, statusLabel } from '@/lib/billing'

export function StatusBadge({ status }: { status: string }) {
  return <Badge tone={paymentTone(status)}>{statusLabel(status)}</Badge>
}
