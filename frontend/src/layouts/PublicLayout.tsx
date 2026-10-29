import { Link, NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '@/auth/AuthProvider'
import { homeFor } from '@/auth/roles'
import { buttonClass } from '@/components/ui/buttonClass'
import { Logo } from '@/components/ui/Logo'
import { cn } from '@/lib/cn'

const year = new Date().getFullYear()

const links = [
  { to: '/courses', label: 'Courses' },
  { to: '/pricing', label: 'Pricing' },
]

export function PublicLayout() {
  const { status, roles } = useAuth()
  return (
    <div className="flex min-h-dvh flex-col">
      <header className="sticky top-0 z-30 border-b border-border/60 bg-bg/75 backdrop-blur-lg">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <Logo />
          <nav className="hidden items-center gap-1 sm:flex">
            {links.map((l) => (
              <NavLink
                key={l.to}
                to={l.to}
                className={({ isActive }) =>
                  cn('rounded-full px-4 py-2 text-sm font-medium text-muted hover:text-ink', isActive && 'text-ink')
                }
              >
                {l.label}
              </NavLink>
            ))}
          </nav>
          {status === 'signed_in' ? (
            <Link to={homeFor(roles)} className={buttonClass('primary', 'sm')}>
              Dashboard
            </Link>
          ) : (
            <Link to="/login" className={buttonClass('primary', 'sm')}>
              Sign in
            </Link>
          )}
        </div>
      </header>
      <main className="flex-1">
        <Outlet />
      </main>
      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-8 text-sm text-muted sm:px-6">
          <span>© {year} MSR.edu</span>
          <div className="flex gap-4">
            <Link to="/terms" className="hover:text-ink">Terms</Link>
            <Link to="/privacy" className="hover:text-ink">Privacy</Link>
            <Link to="/refunds" className="hover:text-ink">Refund policy</Link>
          </div>
        </div>
      </footer>
    </div>
  )
}
