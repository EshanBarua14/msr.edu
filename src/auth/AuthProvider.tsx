import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { Session } from '@supabase/supabase-js'
import { supabase } from '@/lib/supabase'
import type { Role } from './roles'

export interface Profile {
  id: string
  email: string | null
  fullName: string | null
  avatarUrl: string | null
}

type Status = 'loading' | 'signed_out' | 'signed_in'

interface AuthState {
  status: Status
  profile: Profile | null
  roles: Role[]
  /** True when running without Supabase (local UI preview, dev builds only). */
  preview: boolean
  signInWithGoogle: (returnTo?: string) => Promise<void>
  signOut: () => Promise<void>
  /** Dev preview only: pretend to sign in with the given roles. */
  previewSignIn: (roles: Role[]) => void
}

const AuthContext = createContext<AuthState | null>(null)

const PREVIEW_KEY = 'msr.preview.roles'
const previewAllowed = !supabase && import.meta.env.DEV

const previewProfile: Profile = { id: 'preview', email: 'preview@msr.edu', fullName: 'Preview User', avatarUrl: null }

function readPreviewRoles(): Role[] | null {
  try {
    const raw = localStorage.getItem(PREVIEW_KEY)
    return raw ? (JSON.parse(raw) as Role[]) : null
  } catch {
    return null
  }
}

async function loadIdentity(session: Session): Promise<{ profile: Profile; roles: Role[] }> {
  const user = session.user
  const fallback: Profile = {
    id: user.id,
    email: user.email ?? null,
    fullName: (user.user_metadata?.full_name as string | undefined) ?? null,
    avatarUrl: (user.user_metadata?.avatar_url as string | undefined) ?? null,
  }
  if (!supabase) return { profile: fallback, roles: ['learner'] }

  const [{ data: p }, { data: r }] = await Promise.all([
    supabase.from('profiles').select('id, email, full_name, avatar_url').eq('id', user.id).maybeSingle(),
    supabase.from('user_roles').select('role_key').eq('user_id', user.id),
  ])
  const profile: Profile = p
    ? { id: p.id, email: p.email, fullName: p.full_name, avatarUrl: p.avatar_url }
    : fallback
  const roles = (r ?? []).map((row) => row.role_key as Role)
  return { profile, roles: roles.length ? roles : ['learner'] }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Without Supabase there is nothing to load: start from the (dev-only) preview state.
  const initialPreview = supabase ? null : previewAllowed ? readPreviewRoles() : null
  const [status, setStatus] = useState<Status>(supabase ? 'loading' : initialPreview ? 'signed_in' : 'signed_out')
  const [profile, setProfile] = useState<Profile | null>(initialPreview ? previewProfile : null)
  const [roles, setRoles] = useState<Role[]>(initialPreview ?? [])

  const applyPreview = useCallback((r: Role[] | null) => {
    if (!r) {
      setProfile(null)
      setRoles([])
      setStatus('signed_out')
      return
    }
    setProfile(previewProfile)
    setRoles(r)
    setStatus('signed_in')
  }, [])

  useEffect(() => {
    if (!supabase) return
    let cancelled = false
    const handle = async (session: Session | null) => {
      if (!session) {
        if (!cancelled) {
          setProfile(null)
          setRoles([])
          setStatus('signed_out')
        }
        return
      }
      const identity = await loadIdentity(session)
      if (!cancelled) {
        setProfile(identity.profile)
        setRoles(identity.roles)
        setStatus('signed_in')
      }
    }
    supabase.auth.getSession().then(({ data }) => handle(data.session))
    const { data: sub } = supabase.auth.onAuthStateChange((_event, session) => {
      // Defer Supabase calls out of the callback to avoid auth-lock deadlocks.
      setTimeout(() => handle(session), 0)
    })
    return () => {
      cancelled = true
      sub.subscription.unsubscribe()
    }
  }, [])

  const signInWithGoogle = useCallback(async (returnTo?: string) => {
    if (!supabase) throw new Error('Supabase is not configured. Set VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY.')
    const redirect = new URL('/auth/callback', window.location.origin)
    if (returnTo) redirect.searchParams.set('next', returnTo)
    const { error } = await supabase.auth.signInWithOAuth({
      provider: 'google',
      options: { redirectTo: redirect.toString(), queryParams: { prompt: 'select_account' } },
    })
    if (error) throw error
  }, [])

  const signOut = useCallback(async () => {
    if (supabase) await supabase.auth.signOut()
    else {
      try {
        localStorage.removeItem(PREVIEW_KEY)
      } catch {
        /* ignore */
      }
      applyPreview(null)
    }
  }, [applyPreview])

  const previewSignIn = useCallback(
    (r: Role[]) => {
      if (!previewAllowed) return
      try {
        localStorage.setItem(PREVIEW_KEY, JSON.stringify(r))
      } catch {
        /* ignore */
      }
      applyPreview(r)
    },
    [applyPreview],
  )

  const value = useMemo<AuthState>(
    () => ({ status, profile, roles, preview: previewAllowed, signInWithGoogle, signOut, previewSignIn }),
    [status, profile, roles, signInWithGoogle, signOut, previewSignIn],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// oxlint-disable-next-line react/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
