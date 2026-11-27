import { createContext, useContext } from 'react'
import type { CurrentUser } from '../lib/types'

export interface AuthState {
  user: CurrentUser | null
  loading: boolean
  refresh: () => Promise<void>
  loginLocal: (username: string, password: string) => Promise<void>
  loginLdap: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
  can: (permission: string) => boolean
}

export const AuthContext = createContext<AuthState | null>(null)

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within AuthProvider')
  return ctx
}
