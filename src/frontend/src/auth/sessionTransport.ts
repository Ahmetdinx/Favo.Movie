// Shared verbatim with Favo.Books. Tokens stay in memory; only events cross tabs.
const lockName = 'favo.identity.session.v1'
const channelName = 'favo.identity.events.v1'
const eventKey = 'favo.identity.event.v1'
const documentId = crypto.randomUUID()
export type SessionEvent = 'login' | 'logout'

export async function identityRequest(path: string, init: RequestInit = {}) {
  if (!navigator.locks) throw new Error('Güvenli ortak giriş için güncel Chrome, Edge veya Firefox kullanın.')
  return navigator.locks.request(lockName, () => fetch(path, {
    ...init, credentials: 'include', cache: 'no-store',
  }))
}

export function publishSessionEvent(action: SessionEvent) {
  const event = { action, id: crypto.randomUUID(), source: documentId }
  if (typeof BroadcastChannel !== 'undefined') {
    const channel = new BroadcastChannel(channelName)
    channel.postMessage(event)
    channel.close()
  } else {
    try { localStorage.setItem(eventKey, JSON.stringify(event)); localStorage.removeItem(eventKey) } catch { /* pageshow revalidates */ }
  }
}

export function subscribeSessionEvents(listener: (action: SessionEvent) => void) {
  const receive = (value: unknown) => {
    if (typeof value !== 'object' || value === null || !('action' in value)) return
    if ('source' in value && value.source === documentId) return
    if (value.action === 'login' || value.action === 'logout') listener(value.action)
  }
  if (typeof BroadcastChannel !== 'undefined') {
    const channel = new BroadcastChannel(channelName)
    channel.onmessage = event => receive(event.data)
    return () => channel.close()
  }
  const onStorage = (event: StorageEvent) => {
    if (event.key !== eventKey || !event.newValue) return
    try { receive(JSON.parse(event.newValue)) } catch { /* ignore unrelated data */ }
  }
  window.addEventListener('storage', onStorage)
  return () => window.removeEventListener('storage', onStorage)
}

