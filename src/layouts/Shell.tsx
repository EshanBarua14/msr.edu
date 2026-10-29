import { useState, type ReactNode } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { AnimatePresence, motion } from 'motion/react'
import { LogOut, Menu, X, type LucideIcon } from 'lucide-react'
import { useAuth } from '@/auth/AuthProvider'
import { Logo } from '@/components/ui/Logo'
import { cn } from '@/lib/cn'

export interface NavItem {
  to: string
  label: string
  icon: LucideIcon
  end?: boolean
}
export interface NavSection {
  title?: string
  items: NavItem[]
}

/** Sidebar + topbar shell shared by the learner app and the admin console. */
export function Shell({ sections, home, badge, topRight }: { sections: NavSection[]; home: string; badge?: ReactNode; topRight?: ReactNode }) {
  const [open, setOpen] = useState(false)
  const { profile, signOut } = useAuth()
  const navigate = useNavigate()

  const nav = (
    <nav className="flex flex-col gap-6 p-4">
      {sections.map((s, i) => (
        <div key={s.title ?? i} className="flex flex-col gap-1">
          {s.title && <p className="px-3 pb-1 text-[11px] font-semibold tracking-wider text-muted uppercase">{s.title}</p>}
          {s.items.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium text-muted transition-colors hover:bg-surface-2 hover:text-ink',
                  isActive && 'bg-brand-500/10 text-brand-600 dark:text-brand-400',
                )
              }
            >
              <item.icon className="size-4 shrink-0" />
              {item.label}
            </NavLink>
          ))}
        </div>
      ))}
    </nav>
  )

  const handleSignOut = async () => {
    await signOut()
    navigate('/', { replace: true })
  }

  return (
    <div className="flex min-h-dvh">
      <aside className="sticky top-0 hidden h-dvh w-64 shrink-0 flex-col border-r border-border bg-surface lg:flex">
        <div className="flex h-16 items-center gap-2 border-b border-border px-5">
          <Logo to={home} />
          {badge}
        </div>
        <div className="flex-1 overflow-y-auto">{nav}</div>
      </aside>

      <AnimatePresence>
        {open && (
          <>
            <motion.div
              className="fixed inset-0 z-40 bg-black/40 lg:hidden"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              onClick={() => setOpen(false)}
            />
            <motion.aside
              className="fixed inset-y-0 left-0 z-50 w-72 overflow-y-auto border-r border-border bg-surface lg:hidden"
              initial={{ x: '-100%' }}
              animate={{ x: 0 }}
              exit={{ x: '-100%' }}
              transition={{ type: 'spring', stiffness: 380, damping: 36 }}
            >
              <div className="flex h-16 items-center justify-between border-b border-border px-5">
                <Logo to={home} />
                <button aria-label="Close menu" onClick={() => setOpen(false)} className="rounded-lg p-2 hover:bg-surface-2">
                  <X className="size-5" />
                </button>
              </div>
              {nav}
            </motion.aside>
          </>
        )}
      </AnimatePresence>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-30 flex h-16 items-center justify-between gap-3 border-b border-border bg-bg/80 px-4 backdrop-blur-lg sm:px-6">
          <button aria-label="Open menu" onClick={() => setOpen(true)} className="rounded-lg p-2 hover:bg-surface-2 lg:hidden">
            <Menu className="size-5" />
          </button>
          <div className="flex flex-1 items-center justify-end gap-3">
            {topRight}
            <div className="flex items-center gap-3">
              {profile?.avatarUrl ? (
                <img src={profile.avatarUrl} alt="" className="size-8 rounded-full" referrerPolicy="no-referrer" />
              ) : (
                <span className="grid size-8 place-items-center rounded-full bg-brand-500/15 text-sm font-bold text-brand-600">
                  {(profile?.fullName ?? profile?.email ?? '?').charAt(0).toUpperCase()}
                </span>
              )}
              <span className="hidden text-sm font-medium sm:inline">{profile?.fullName ?? profile?.email}</span>
            </div>
            <button
              onClick={handleSignOut}
              className="inline-flex items-center gap-2 rounded-full px-3 py-2 text-sm text-muted hover:bg-surface-2 hover:text-ink"
            >
              <LogOut className="size-4" />
              <span className="hidden sm:inline">Sign out</span>
            </button>
          </div>
        </header>
        <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-8 sm:px-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
