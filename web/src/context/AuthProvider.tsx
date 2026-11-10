import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { clearAuthSession, getStoredUser, saveAuthSession } from '../api'
import type { AuthResponse, UserProfile } from '../types'
import { AuthContext, type AuthApi } from './contexts'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(() => getStoredUser())

  const setSession = useCallback((auth: AuthResponse) => {
    saveAuthSession(auth)
    setUser(auth.user)
  }, [])

  const logout = useCallback(() => {
    clearAuthSession()
    setUser(null)
  }, [])

  const value = useMemo<AuthApi>(
    () => ({
      user,
      isAdmin: user?.role === 'Admin',
      setSession,
      logout,
    }),
    [user, setSession, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

