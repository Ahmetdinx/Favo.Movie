import { useEffect, useId, useRef, useState, type KeyboardEvent } from 'react'
import { CinemaAvatar, useCommunity } from './Community'
import './HeaderSearch.css'

export default function HeaderSearch() {
    const [query, setQuery] = useState('')
    const [open, setOpen] = useState(false)
    const [items, setItems] = useState<{nickname:string;avatarId?:string}[]>([])
    const [busy, setBusy] = useState(false)
    const [error, setError] = useState('')
    const [active, setActive] = useState(-1)
    const inputRef = useRef<HTMLInputElement>(null)
    const listId = useId()
    const { account, api } = useCommunity()
    const navigate = (path: string) => location.assign(import.meta.env.BASE_URL + path)
    const term = query.trim().toLowerCase()
    const valid = /^[a-z0-9_]{2,24}$/.test(term)
    const expanded = open && term.length >= 2

    useEffect(() => {
        setItems([]); setActive(-1); setError('')
        if (!open || !valid) { setBusy(false); return }
        const controller = new AbortController()
        setBusy(true)
        const timer = window.setTimeout(async () => {
            try {
                const path = account.session ? '/api/movies/community/directory?' : '/api/readers/?'
                const data = await api<{items:(string|{nickname:string;avatarId?:string})[]}>(path + new URLSearchParams({query:term}), {signal:controller.signal})
                if (!controller.signal.aborted) setItems(data.items.slice(0,5).map(item=>typeof item==='string'?{nickname:item}:item))
            } catch (e) {
                if (!controller.signal.aborted) setError(e instanceof Error ? e.message : 'Arama yapılamadı.')
            } finally { if (!controller.signal.aborted) setBusy(false) }
        }, 250)
        return () => { window.clearTimeout(timer); controller.abort() }
    }, [term, open, valid, api, account.session?.userId])

    function close() { setOpen(false); setActive(-1) }
    function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
        if (event.key === 'Escape') { event.preventDefault(); close() }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault(); setOpen(true)
            if (items.length) setActive(index => event.key === 'ArrowDown'
                ? (index + 1) % items.length : (index <= 0 ? items.length - 1 : index - 1))
        }
        if (event.key === 'Enter' && expanded && active >= 0 && items[active]) {
            event.preventDefault()
            navigate('people/' + items[active].nickname); close(); setQuery('')
        }
    }

    return <div className="header-reader-search" role="search" aria-label="Profil ara"
        onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) close() }}>
        <svg className="header-search-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7">
            <circle cx="10.5" cy="10.5" r="6.5" /><path d="m16 16 4.5 4.5" />
        </svg>
        <input ref={inputRef} type="text" role="combobox" aria-label="Kullanıcı adı ara"
            placeholder="Sinemasever ara…" autoComplete="off" spellCheck={false} maxLength={24}
            aria-expanded={expanded} aria-controls={expanded ? listId : undefined} aria-autocomplete="list"
            aria-activedescendant={expanded && active >= 0 ? listId + '-' + active : undefined}
            value={query} onFocus={() => setOpen(true)} onKeyDown={onKeyDown}
            onChange={event => { setQuery(event.target.value); setItems([]); setActive(-1); setError(''); setBusy(true); setOpen(true) }} />
        {query && <button type="button" className="header-search-clear" aria-label="Aramayı temizle"
            onClick={() => { setQuery(''); setItems([]); close(); inputRef.current?.focus() }}>×</button>}
        {expanded && <div className="header-reader-dropdown">
            <ul id={listId} role="listbox" aria-label="Açık profiller">
                {items.map(({nickname,avatarId}, index) => <li key={nickname} id={listId + '-' + index} role="option" aria-selected={active === index}>
                    <a href={import.meta.env.BASE_URL + 'people/' + nickname} tabIndex={-1} onMouseEnter={() => setActive(index)}
                        onClick={() => { close(); setQuery('') }}>
                        <CinemaAvatar id={avatarId}/>
                        <span>@{nickname}</span><span className="header-reader-arrow" aria-hidden="true">↗</span>
                    </a>
                </li>)}
            </ul>
            <div className="header-reader-status" role="status">
                {!valid ? 'En az 2 karakter; a-z, rakam veya alt çizgi kullan.' : busy ? 'Aranıyor…' : error ||
                    (items.length === 0 ? 'Açık profil bulunamadı.' : '')}
            </div>
        </div>}
    </div>
}
