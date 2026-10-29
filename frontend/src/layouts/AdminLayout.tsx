import { Link } from 'react-router-dom'
import { GraduationCap } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { hasAnyRole } from '@/auth/roles'
import { Badge } from '@/components/ui/Badge'
import { buttonClass } from '@/components/ui/buttonClass'
import { adminNav } from './adminNav'
import { Shell, type NavSection } from './Shell'

export function AdminLayout() {
  const { roles } = useAuth()
  const sections: NavSection[] = adminNav
    .map((s) => ({ title: s.title, items: s.items.filter((i) => hasAnyRole(roles, i.roles)) }))
    .filter((s) => s.items.length > 0)

  return (
    <Shell
      sections={sections}
      home="/admin"
      badge={<Badge tone="brand">Admin</Badge>}
      topRight={
        <Link to="/app" className={buttonClass('secondary', 'sm')}>
          <GraduationCap className="size-4" /> Learner view
        </Link>
      }
    />
  )
}
