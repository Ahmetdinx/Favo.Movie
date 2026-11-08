import { useId, useRef, useState, type FormEvent } from 'react'
import type { useSession } from './useSession'

export default function AccountDialog({ account }: { account: ReturnType<typeof useSession> }) {
  const id = useId()
  const dialog = useRef<HTMLDialogElement>(null)
  const { session, initializing, error, refresh, login, logout } = account
  const [busy, setBusy] = useState(false)
  const [formError, setFormError] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    setBusy(true); setFormError('')
    try {
      await login(String(data.get('email')), String(data.get('password')))
      form.reset(); dialog.current?.close()
    } catch (failure) { setFormError(failure instanceof Error ? failure.message : 'Giriş tamamlanamadı.') }
    finally { setBusy(false) }
  }
  async function signOut() {
    setBusy(true); setFormError('')
    try { await logout(); dialog.current?.close() }
    catch (failure) { setFormError(failure instanceof Error ? failure.message : 'Çıkış tamamlanamadı.') }
    finally { setBusy(false) }
  }
  return <>
    <button className="account-link" disabled={initializing} onClick={() => { setFormError(''); dialog.current?.showModal() }}>
      {initializing ? 'Oturum kontrol ediliyor…' : session ? 'Hesabım' : 'Giriş yap'} <span aria-hidden="true">↗</span>
    </button>
    <dialog ref={dialog} aria-labelledby={id + '-title'} onClick={event => { if (event.target === dialog.current && !busy) dialog.current.close() }}>
      <div className="dialog-body"><button className="close" aria-label="Kapat" onClick={() => dialog.current?.close()}>×</button>
        <span className="dialog-mark brand-mark" /><p className="eyebrow">TEK HESAP. SENİN FAVORİLERİN.</p>
        <h2 id={id + '-title'}>{session ? 'Yeniden merhaba.' : 'Favo’ya giriş yap.'}</h2>
        {session ? <>
          <p className="account-email">{session.email}</p><p>Film dünyasına hoş geldin.</p>
          <a className="primary" href="/film/">Filmleri keşfet <span aria-hidden="true">→</span></a>
          <a className="profile-link" href="/film/profile">Film profilim</a>
          <button className="signout" disabled={busy} onClick={() => void signOut()}>{busy ? 'Çıkış yapılıyor…' : 'Favo’dan çıkış yap'}</button>
        </> : <>
          <p>Mevcut Favo Kitap hesabını kullanabilirsin.</p>
          <form className="login-form" onSubmit={event => void submit(event)}>
            <label htmlFor={id + '-email'}>E-posta</label><input id={id + '-email'} name="email" type="email" autoComplete="username" required maxLength={320} disabled={busy} />
            <label htmlFor={id + '-password'}>Parola</label><input id={id + '-password'} name="password" type="password" autoComplete="current-password" required maxLength={128} disabled={busy} />
            <button className="primary" type="submit" disabled={busy}>{busy ? 'Giriş yapılıyor…' : 'Giriş yap'}</button>
          </form>
          <p className="account-note">Hesabın yok mu? <a href="/kitap/register">Favo hesabı oluştur</a></p>
        </>}
        {(formError || error) && <p role="alert" className="auth-error">{formError || error}</p>}
        {error && <button className="profile-link" onClick={() => void refresh()}>Oturumu tekrar kontrol et</button>}
      </div>
    </dialog>
  </>
}

