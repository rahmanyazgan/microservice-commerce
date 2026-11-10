import { useCallback, useEffect, useRef, useState } from 'react'

export interface AsyncState<T> {
  data: T | undefined
  error: unknown
  loading: boolean
  reload: () => Promise<void>
  setData: (value: T | undefined) => void
}

/**
 * Fetches data on mount (and when deps change). If intervalMs is given,
 * re-fetches periodically. Pass `enabled=false` to stop polling.
 */
export function useAsync<T>(
  fetcher: () => Promise<T>,
  deps: unknown[] = [],
  intervalMs?: number,
  enabled = true,
): AsyncState<T> {
  const [data, setData] = useState<T | undefined>(undefined)
  const [error, setError] = useState<unknown>(undefined)
  const [loading, setLoading] = useState(true)
  const fetcherRef = useRef(fetcher)
  const mounted = useRef(true)

  useEffect(() => {
    fetcherRef.current = fetcher
  })

  const reload = useCallback(async () => {
    try {
      const result = await fetcherRef.current()
      if (!mounted.current) return
      setData(result)
      setError(undefined)
    } catch (e) {
      if (!mounted.current) return
      setError(e)
    } finally {
      if (mounted.current) setLoading(false)
    }
  }, [])

  useEffect(() => {
    mounted.current = true
    setLoading(true)
    void reload()
    return () => {
      mounted.current = false
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps)

  useEffect(() => {
    if (!intervalMs || !enabled) return
    const id = window.setInterval(() => void reload(), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs, enabled, reload])

  return { data, error, loading, reload, setData }
}
