import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError, googleLoginUrl } from '@/lib/api'
import type { Role } from './roles'

export interface Profile {
  id: string
  email: string | null
  fullName: string | null
  avatarUrl: string | null
}

interface Providers {
  google: boolean
  devLogin: boolean
}

type Status = 'loading' | 'signed_out' | 'signed_in'

interface AuthState {
  status: Status
  profile: Profile | null
  roles: Role[]
  providers: Providers | null
  /** Reload the session from the API (e.g. after the OAuth redirect). */
  refresh: () => Promise<void>
  signInWithGoogle: (returnTo?: string) => void
  signOut: () => Promise<void>
  /** Development API only: sign in with chosen roles, no Google needed. */
  devSignIn: (roles: Role[]) => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

interface MeResponse extends Profile {
  roles: Role[]
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>('loading')
  const [profile, setProfile] = useState<Profile | null>(null)
  const [roles, setRoles] = useState<Role[]>([])
  const [providers, setProviders] = useState<Providers | null>(null)

  const refresh = useCallback(async () => {
    try {
      const me = await api<MeResponse>('/api/auth/me')
      setProfile({ id: me.id, email: me.email, fullName: me.fullName, avatarUrl: me.avatarUrl })
      setRoles(me.roles)
      setStatus('signed_in')
    } catch (e) {
      if (!(e instanceof ApiError) || e.status !== 401) console.error('Could not load session', e)
      setProfile(null)
      setRoles([])
      setStatus('signed_out')
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    Promise.all([
      api<MeResponse>('/api/auth/me').catch(() => null),
      api<Providers>('/api/auth/providers').catch(() => ({ google: false, devLogin: false })),
    ]).then(([me, p]) => {
      if (cancelled) return
      setProviders(p)
      if (me) {
        setProfile({ id: me.id, email: me.email, fullName: me.fullName, avatarUrl: me.avatarUrl })
        setRoles(me.roles)
        setStatus('signed_in')
      } else {
        setStatus('signed_out')
      }
    })
    return () => {
      cancelled = true
    }
  }, [])

  const signInWithGoogle = useCallback((returnTo?: string) => {
    window.location.assign(googleLoginUrl(returnTo))
  }, [])

  const signOut = useCallback(async () => {
    await api('/api/auth/logout', { method: 'POST' })
    setProfile(null)
    setRoles([])
    setStatus('signed_out')
  }, [])

  const devSignIn = useCallback(
    async (r: Role[]) => {
      await api('/api/auth/dev-login', { json: { email: `${r.at(-1) ?? 'learner'}@dev.local`, roles: r } })
      await refresh()
    },
    [refresh],
  )

  const value = useMemo<AuthState>(
    () => ({ status, profile, roles, providers, refresh, signInWithGoogle, signOut, devSignIn }),
    [status, profile, roles, providers, refresh, signInWithGoogle, signOut, devSignIn],
  )
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

// oxlint-disable-next-line react/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside <AuthProvider>')
  return ctx
}
