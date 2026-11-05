import { createContext, type ReactNode, useCallback, useContext, useEffect, useState } from 'react'
import { api } from '../lib/api'
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
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)

  const refresh = useCallback(async () => {
    try {
      const me = await api.get<CurrentUser>('/api/auth/me')
      setUser(me)
    } catch {
      setUser(null)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
    const handler = () => setUser(null)
    window.addEventListener('nf-unauthorized', handler)
    return () => window.removeEventListener('nf-unauthorized', handler)
  }, [refresh])

  const login = useCallback(
    async (path: string, username: string, password: string) => {
      const result = await api.post<CurrentUser>(path, { username, password })
      setUser(result)
    },
    [],
  )

  const loginLocal = useCallback((u: string, p: string) => login('/api/auth/login-local', u, p), [login])
  const loginLdap = useCallback((u: string, p: string) => login('/api/auth/login-ldap', u, p), [login])

  const logout = useCallback(async () => {
    try {
      await api.post('/api/auth/logout')
    } finally {
      setUser(null)
    }
  }, [])

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

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
