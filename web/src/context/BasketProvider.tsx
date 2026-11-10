import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api } from '../api'
import type { Basket } from '../types'
import { BasketContext, type BasketApi } from './contexts'

export function BasketProvider({ children }: { children: ReactNode }) {
  const [basket, setBasket] = useState<Basket | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(undefined)

  const refresh = useCallback(async () => {
    try {
      const b = await api.basket()
      setBasket(b)
      setError(undefined)
    } catch (e) {
      setError(e)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const value = useMemo<BasketApi>(
    () => ({
      basket,
      count: basket?.items.reduce((s, i) => s + i.quantity, 0) ?? 0,
      loading,
      error,
      refresh,
      setBasket,
    }),
    [basket, loading, error, refresh],
  )

  return <BasketContext.Provider value={value}>{children}</BasketContext.Provider>
}
