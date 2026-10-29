import { Construction } from 'lucide-react'
import { EmptyState } from '@/components/ui/EmptyState'
import { PageHeader } from '@/components/ui/PageHeader'

export default function Placeholder({ title, description, phase }: { title: string; description?: string; phase?: string }) {
  return (
    <>
      <PageHeader title={title} description={description} />
      <EmptyState icon={Construction} title="Coming soon">
        This section is planned{phase ? ` for ${phase}` : ''}.
      </EmptyState>
    </>
  )
}
