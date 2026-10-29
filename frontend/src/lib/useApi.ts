import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from './api'

export type ApiState<T> =
  | { status: 'loading'; data?: undefined; error?: undefined }
  | { status: 'ok'; data: T; error?: undefined }
  | { status: 'error'; data?: undefined; error: ApiError }

/** GET a resource; `reload` refetches without clearing what is on screen. Pass null to skip. */
export function useApi<T>(path: string | null) {
  const [state, setState] = useState<ApiState<T>>({ status: 'loading' })
  const [tick, setTick] = useState(0)

  useEffect(() => {
    if (!path) return
    let live = true
    api<T>(path).then(
      (data) => live && setState({ status: 'ok', data }),
      (e: unknown) => live && setState({ status: 'error', error: e instanceof ApiError ? e : new ApiError(0, null) }),
    )
    return () => {
      live = false
    }
  }, [path, tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])
  return [state, reload] as const
}
