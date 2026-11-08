import { useCallback, useEffect, useRef, useState } from 'react'
import { identityRequest, publishSessionEvent, subscribeSessionEvents } from './sessionTransport'

export type Session = { userId: string; email: string; accessToken: string; expiresAtUtc: string }

export function useSession() {
  const [session, setSession] = useState<Session | null>(null)
  const [initializing, setInitializing] = useState(true)
  const [error, setError] = useState('')
  const pending = useRef<Promise<void> | null>(null)
  const epoch = useRef(0)
  const currentSession = useRef<Session | null>(null)
  const refresh = useCallback(() => {
    if (pending.current) return pending.current
    const current = epoch.current
    const request = (async () => {
      try {
        const response = await identityRequest('/api/identity/refresh', { method: 'POST' })
        if (response.status !== 401 && !response.ok) throw Error('Oturum kontrol edilemedi. Kitap servisini kontrol edip tekrar dene.')
        const value = response.ok ? await response.json() as Session : null
        if (epoch.current === current) { currentSession.current = value; setSession(value); setError('') }
      } catch (failure) {
        if (epoch.current === current) { currentSession.current = null; setSession(null); setError(failure instanceof Error ? failure.message : 'Bağlantı kurulamadı.') }
      } finally { setInitializing(false); pending.current = null }
    })()
    pending.current = request
    return request
  }, [])
  useEffect(() => {
    void refresh()
    const unsubscribe = subscribeSessionEvents(action => {
      epoch.current++
      currentSession.current = null
      setSession(null)
      if (action === 'login') void (pending.current ?? Promise.resolve()).then(refresh)
    })
    const onPageShow = (event: PageTransitionEvent) => { if (event.persisted) void refresh() }
    window.addEventListener('pageshow', onPageShow)
    return () => { unsubscribe(); window.removeEventListener('pageshow', onPageShow) }
  }, [refresh])
  async function login(email: string, password: string) {
    const response = await identityRequest('/api/identity/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.trim(), password }),
    })
    if (!response.ok) throw Error(response.status === 401 ? 'E-posta veya parola hatalı.' : response.status === 429 ? 'Çok fazla deneme yapıldı. Biraz sonra tekrar dene.' : 'Giriş tamamlanamadı. Lütfen tekrar dene.')
    const value = await response.json() as Session
    epoch.current++
    currentSession.current = value; setSession(value); setError(''); publishSessionEvent('login')
  }
  async function logout() {
    const response = await identityRequest('/api/identity/logout', { method: 'POST' })
    if (!response.ok) throw Error('Çıkış tamamlanamadı. Bağlantını kontrol edip tekrar dene.')
    epoch.current++
    currentSession.current = null; setSession(null); setError(''); publishSessionEvent('logout')
  }
  const request = useCallback(async (path: string, init: RequestInit = {}) => {
    const user = currentSession.current?.userId
    const send = () => {
      const headers = new Headers(init.headers)
      if (currentSession.current) headers.set('Authorization', 'Bearer ' + currentSession.current.accessToken)
      return fetch(path, { ...init, headers, cache: 'no-store' })
    }
    const response = await send()
    if (response.status !== 401 || !user || init.signal?.aborted) return response
    await refresh()
    return currentSession.current?.userId === user && !init.signal?.aborted ? send() : response
  }, [refresh])
  return { session, initializing, error, refresh, login, logout, request }
}

