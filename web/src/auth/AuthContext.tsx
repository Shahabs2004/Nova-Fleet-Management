import { useQueryClient } from '@tanstack/react-query'
import { createContext, type ReactNode, useCallback, useContext, useEffect, useState } from 'react'
import { ApiError, api } from '../lib/api'
import type { CurrentUser } from '../lib/types'

interface AuthState {
  user: CurrentUser | null
  loading: boolean
  refresh: () => Promise<void>
  loginLocal: (username: string, password: string) => Promise<void>
  loginLdap: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: string) => boolean
}

const AuthContext = createContext<AuthState | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)

  const clearPrivateCache = useCallback(async () => {
    await queryClient.cancelQueries()
    queryClient.clear()
  }, [queryClient])

  const refresh = useCallback(async () => {
    try {
      const me = await api.get<CurrentUser>('/api/auth/me')
      setUser(me)
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) {
        return
      }
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect -- Restore the initial session after mount.
    void refresh()
    const handler = () => {
      void clearPrivateCache().finally(() => setUser(null))
    }
    window.addEventListener('nf-unauthorized', handler)
    return () => window.removeEventListener('nf-unauthorized', handler)
  }, [refresh, clearPrivateCache])

  const login = useCallback(
    async (path: string, username: string, password: string) => {
      const result = await api.post<CurrentUser>(path, { username, password })
      if (result.username !== user?.username) {
        await clearPrivateCache()
      }
      setUser(result)
    },
    [user?.username, clearPrivateCache],
  )

  const loginLocal = useCallback((u: string, p: string) => login('/api/auth/login-local', u, p), [login])
  const loginLdap = useCallback((u: string, p: string) => login('/api/auth/login-ldap', u, p), [login])

  const logout = useCallback(async () => {
    try {
      await api.post('/api/auth/logout')
    } finally {
      await clearPrivateCache()
      setUser(null)
    }
  }, [clearPrivateCache])

  const can = useCallback(
    (permission: string) => Boolean(user?.permissions?.includes(permission)),
    [user],
  )

  return (
    <AuthContext.Provider value={{ user, loading, refresh, loginLocal, loginLdap, logout, can }}>
      {children}
    </AuthContext.Provider>
  )
}

// oxlint-disable-next-line react/only-export-components -- The provider and its consumer share one context module.
export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
