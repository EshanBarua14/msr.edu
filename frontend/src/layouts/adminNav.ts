import {
  AlertTriangle, BadgePercent, BarChart3, BookOpen, Boxes, Clock, CreditCard, FileClock, FolderOpen,
  FolderTree, Gauge, Home, KeyRound, LayoutDashboard, ListChecks, Megaphone, Receipt, RotateCcw, Settings, ShieldAlert, Tag,
  Ticket, Users,
} from 'lucide-react'
import type { Role } from '@/auth/roles'
import type { NavItem } from './Shell'

export interface AdminNavItem extends NavItem {
  /** Roles (besides owner_admin) that may open this section. */
  roles: readonly Role[]
}
export interface AdminNavSection {
  title?: string
  items: AdminNavItem[]
}

const content: Role[] = ['content_manager']
const finance: Role[] = ['finance_manager']
const support: Role[] = ['support_staff']
const ownerOnly: Role[] = []

/** Admin console navigation, mirroring blueprint section 9. */
export const adminNav: AdminNavSection[] = [
  {
    items: [{ to: '/admin', label: 'Overview', icon: LayoutDashboard, end: true, roles: [...content, ...finance, ...support] }],
  },
  {
    title: 'People',
    items: [{ to: '/admin/users', label: 'Users & roles', icon: Users, roles: support }],
  },
  {
    title: 'Content',
    items: [
      { to: '/admin/courses', label: 'Courses & categories', icon: BookOpen, roles: content },
      { to: '/admin/modules', label: 'Modules & lessons', icon: FolderTree, roles: content },
      { to: '/admin/resources', label: 'Learning resources', icon: Boxes, roles: content },
      { to: '/admin/drive', label: 'Google Drive', icon: FolderOpen, roles: content },
      { to: '/admin/quizzes', label: 'Quizzes & questions', icon: ListChecks, roles: content },
    ],
  },
  {
    title: 'Commerce',
    items: [
      { to: '/admin/plans', label: 'Subscription plans', icon: Ticket, roles: finance },
      { to: '/admin/pricing', label: 'Course pricing', icon: Tag, roles: finance },
      { to: '/admin/promotions', label: 'Discounts & promotions', icon: BadgePercent, roles: finance },
      { to: '/admin/orders', label: 'Orders & transactions', icon: CreditCard, roles: [...finance, ...support] },
      { to: '/admin/pending', label: 'Pending payments', icon: Clock, roles: finance },
      { to: '/admin/refunds', label: 'Refunds & disputes', icon: RotateCcw, roles: finance },
      { to: '/admin/reconciliation', label: 'Payment issues', icon: ShieldAlert, roles: finance },
      { to: '/admin/entitlements', label: 'Learner entitlements', icon: KeyRound, roles: [...finance, ...support] },
      { to: '/admin/receipts', label: 'Receipts & invoices', icon: Receipt, roles: [...finance, ...support] },
      { to: '/admin/reports', label: 'Revenue reports', icon: BarChart3, roles: finance },
    ],
  },
  {
    title: 'Site',
    items: [
      { to: '/admin/announcements', label: 'Announcements', icon: Megaphone, roles: content },
      { to: '/admin/homepage', label: 'Homepage content', icon: Home, roles: content },
    ],
  },
  {
    title: 'System',
    items: [
      { to: '/admin/gateways', label: 'Payment gateways', icon: Gauge, roles: ownerOnly },
      { to: '/admin/settings', label: 'App settings', icon: Settings, roles: ownerOnly },
      { to: '/admin/audit', label: 'Audit logs', icon: FileClock, roles: finance },
      { to: '/admin/status', label: 'System status', icon: AlertTriangle, roles: ownerOnly },
    ],
  },
]

export const adminPages = adminNav.flatMap((s) => s.items).filter((i) => i.to !== '/admin')

