import { Link } from 'react-router-dom'
import { BookOpen, CreditCard, GraduationCap, LayoutDashboard, ListChecks, Receipt, Shield } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { isStaff } from '@/auth/roles'
import { buttonClass } from '@/components/ui/buttonClass'
import { Shell, type NavSection } from './Shell'

const sections: NavSection[] = [
  {
    items: [
      { to: '/app', label: 'Dashboard', icon: LayoutDashboard, end: true },
      { to: '/app/courses', label: 'My courses', icon: BookOpen },
      { to: '/app/quizzes', label: 'Quiz results', icon: ListChecks },
    ],
  },
  {
    title: 'Billing',
    items: [
      { to: '/app/subscription', label: 'Membership', icon: GraduationCap },
      { to: '/app/transactions', label: 'Transactions', icon: CreditCard },
      { to: '/app/receipts', label: 'Receipts', icon: Receipt },
    ],
  },
]

export function LearnerLayout() {
  const { roles } = useAuth()
  return (
    <Shell
      sections={sections}
      home="/app"
      topRight={
        isStaff(roles) && (
          <Link to="/admin" className={buttonClass('secondary', 'sm')}>
            <Shield className="size-4" /> Admin
          </Link>
        )
      }
    />
  )
}
