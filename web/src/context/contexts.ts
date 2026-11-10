import { createContext, useContext } from 'react'
import type { AuthResponse, Basket, UserProfile } from '../types'

export type ToastKind = 'success' | 'error' | 'info'

export interface ToastApi {
  show: (message: string, kind?: ToastKind) => void
  success: (message: string) => void
  error: (message: string) => void
}

export const ToastContext = createContext<ToastApi | null>(null)

export function useToast(): ToastApi {
  const ctx = useContext(ToastContext)
  if (!ctx) throw new Error('useToast must be used inside ToastProvider')
  return ctx
}

export interface BasketApi {
  basket: Basket | null
  count: number
  loading: boolean
  error: unknown
  refresh: () => Promise<void>
  setBasket: (b: Basket | null) => void
}

export const BasketContext = createContext<BasketApi | null>(null)

export function useBasket(): BasketApi {
  const ctx = useContext(BasketContext)
  if (!ctx) throw new Error('useBasket must be used inside BasketProvider')
  return ctx
}

export interface AuthApi {
  user: UserProfile | null
  isAdmin: boolean
  setSession: (auth: AuthResponse) => void
  logout: () => void
}

export const AuthContext = createContext<AuthApi | null>(null)

export function useAuth(): AuthApi {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
