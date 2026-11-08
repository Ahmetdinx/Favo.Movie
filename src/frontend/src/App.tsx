import { useEffect, useState, type FormEvent } from 'react'
import AccountDialog from './auth/AccountDialog'
import HeaderSearch from './HeaderSearch'
import { CommunityProvider, CommunityHome, FilmProfile, ProfileLink, People, PublicProfile, MovieActions } from './Community'
import { useSession } from './auth/useSession'

type Film = { id: number; title: string; originalTitle: string; posterUrl: string | null; backdropUrl: string | null; releaseDate: string | null; tmdbScore: number | null; voteCount: number }
type Genre = { id: number; name: string }
type Page = { items: Film[]; page: number; totalPages: number; totalResults: number }
type Detail = { movie: Film; overview: string; runtime: number | null; genres: Genre[]; directors: string[]; cast: { name: string; character: string }[]; trailerUrl: string | null; imdbUrl: string | null }
type Failure = { code: string; message: string }
const base = import.meta.env.BASE_URL

function useData<T>(url: string) {
 const [data, setData] = useState<T | null>(null)
 const [error, setError] = useState<Failure | null>(null)
 const [loading, setLoading] = useState(true)
 const [revision, setRevision] = useState(0)
 useEffect(() => {
  const controller = new AbortController(); setLoading(true); setData(null); setError(null)
  void fetch(url, { signal: controller.signal }).then(async response => {
   const result = await response.json()
   if (!response.ok) throw { code: result.code ?? 'unavailable', message: result.message ?? 'Film bilgileri yüklenemedi.' }
   if (!controller.signal.aborted) setData(result)
  }).catch((failure: Failure) => { if (!controller.signal.aborted) setError({ code: failure.code ?? 'offline', message: failure.message ?? 'Bağlantı kurulamadı.' }) })
   .finally(() => { if (!controller.signal.aborted) setLoading(false) })
  return () => controller.abort()
 }, [url, revision])
 return { data, error, loading, retry: () => setRevision(n => n + 1) }
}
function Poster({ movie }: { movie: Film }) {
 const [failed, setFailed] = useState(false)
 return movie.posterUrl && !failed ? <img src={movie.posterUrl} alt="" loading="lazy" onError={() => setFailed(true)} />
  : <div className="poster-empty" aria-hidden="true"><span>F</span><small>FAVO FILM</small></div>
}
function Status({ error, retry }: { error: Failure; retry: () => void }) {
 const setup = error.code === 'catalog_not_configured' || error.code === 'catalog_credentials'
 return <section className="catalog-state" role={setup ? 'status' : 'alert'}><span className="state-symbol" aria-hidden="true">{setup ? '◷' : '↻'}</span><div><h2>{setup ? 'Perde açılmak üzere.' : 'Kısa bir ara.'}</h2><p>{setup ? 'Film kataloğu hazırlanıyor.' : error.message}</p></div><button className="outline-button" onClick={retry}>Tekrar dene <span aria-hidden="true">↗</span></button></section>
}
function Catalog() {
 const params = new URLSearchParams(location.search)
 const initialQuery = params.get('query') ?? ''
 const [query, setQuery] = useState(initialQuery)
 const request = useData<Page>('/api/movies/?' + params.toString())
 const genres = useData<Genre[]>('/api/movies/genres')
 function search(event: FormEvent<HTMLFormElement>) {
  event.preventDefault(); const form = new FormData(event.currentTarget); const next = new URLSearchParams()
  const term = String(form.get('query') ?? '').trim(); const year = String(form.get('year') ?? '')
  if (term) next.set('query', term)
  if (year) next.set('year', year)
  if (!term) { const genre = String(form.get('genre') ?? ''); if (genre) next.set('genre', genre); next.set('sort', String(form.get('sort') ?? 'popular')) }
  location.assign(base + 'catalog?' + next.toString())
 }
 function pageLink(page: number) { const next = new URLSearchParams(params); next.set('page', String(page)); return base + 'catalog?' + next.toString() }
 return <>
  <header className="page-heading"><p className="kicker">FİLM ARŞİVİ</p><h1>Katalog<em>.</em></h1></header>
  <section id="catalog" aria-labelledby="catalog-title"><div className="section-heading"><h2 id="catalog-title">{initialQuery ? 'Arama sonuçları' : 'Keşfet'}</h2>{request.data && <span>{request.data.totalResults.toLocaleString('tr-TR')} film</span>}</div>
   <form className="catalog-toolbar" onSubmit={search}><label className="search-label"><span className="sr-only">Film adı</span><input name="query" type="search" placeholder="Bir film ara…" value={query} onChange={event => setQuery(event.target.value)} maxLength={120}/></label>
    <label><span>Yıl</span><input name="year" type="number" min={1888} max={2100} placeholder="Tümü" defaultValue={params.get('year') ?? ''}/></label>
    <label><span>Tür</span><select name="genre" disabled={!!query.trim() || !genres.data} defaultValue={params.get('genre') ?? ''}><option value="">Tüm türler</option>{genres.data?.map(g => <option key={g.id} value={g.id}>{g.name}</option>)}</select></label>
    <label><span>Sıralama</span><select name="sort" disabled={!!query.trim()} defaultValue={params.get('sort') ?? 'popular'}><option value="popular">Popüler</option><option value="top">Yüksek puanlı</option></select></label>
    <button className="primary" type="submit">Keşfet <span aria-hidden="true">↗</span></button>
   </form>
   {request.loading && <div className="loading-grid" role="status" aria-label="Filmler yükleniyor">{Array.from({ length: 6 }, (_, i) => <div key={i}/>)}</div>}
   {request.error && <Status error={request.error} retry={() => { request.retry(); genres.retry() }}/>}
   {request.data && <>{!request.data.items.length ? <div className="catalog-state"><h3>Bu aramada film bulunamadı.</h3><a href={base + 'catalog'}>Filtreleri temizle ↗</a></div> : <div className="film-grid">{request.data.items.map(movie => <a className="film-card" href={base + 'movie/' + movie.id} key={movie.id}><div className="poster"><Poster movie={movie}/>{movie.tmdbScore !== null && <span className="score">★ {movie.tmdbScore.toFixed(1)} <small>TMDB</small></span>}<span className="poster-arrow" aria-hidden="true">↗</span></div><h3>{movie.title}</h3><p>{movie.releaseDate?.slice(0, 4) ?? 'Tarih belirtilmemiş'}</p></a>)}</div>}
    {request.data.totalPages > 1 && <nav className="pagination" aria-label="Katalog sayfaları">{request.data.page > 1 ? <a href={pageLink(request.data.page - 1)}>← Önceki</a> : <span/>}<span>{request.data.page} / {request.data.totalPages}</span>{request.data.page < request.data.totalPages ? <a href={pageLink(request.data.page + 1)}>Sonraki →</a> : <span/>}</nav>}</>}
  </section>
 </>
}
function MovieDetail({ id }: { id: string }) {
 const result = useData<Detail>('/api/movies/' + id)
 return <section className="detail"><a className="back-link" href={base + 'catalog'}>← Filmlere dön</a>{result.loading && <p role="status">Film yükleniyor…</p>}{result.error && <Status error={result.error} retry={result.retry}/>}{result.data && <>
  <div className="detail-hero">{result.data.movie.backdropUrl && <img className="backdrop" src={result.data.movie.backdropUrl} alt=""/>}<div className="detail-poster"><Poster movie={result.data.movie}/></div><div className="detail-title"><p className="kicker">{result.data.genres.map(g => g.name).join(' · ')}</p><h1>{result.data.movie.title}</h1>{result.data.movie.originalTitle !== result.data.movie.title && <p className="original-title">{result.data.movie.originalTitle}</p>}<div className="detail-meta"><span>{result.data.movie.releaseDate?.slice(0, 4)}</span>{result.data.runtime && <span>{result.data.runtime} dk</span>}{result.data.movie.tmdbScore !== null && <span>★ {result.data.movie.tmdbScore.toFixed(1)} <small>TMDB · {result.data.movie.voteCount.toLocaleString('tr-TR')} oy</small></span>}</div><div className="detail-links">{result.data.trailerUrl && <a className="primary" href={result.data.trailerUrl} target="_blank" rel="noopener noreferrer">Fragmanı izle ↗</a>}{result.data.imdbUrl && <a className="outline-button" href={result.data.imdbUrl} target="_blank" rel="noopener noreferrer">IMDb ↗</a>}</div></div></div>
  <MovieActions id={Number(id)}/><div className="detail-info"><section><h2>Hikâye</h2><p>{result.data.overview || 'Bu film için Türkçe özet henüz bulunmuyor.'}</p></section><section><h2>Yönetmen</h2><p>{result.data.directors.join(', ') || 'Bilgi bulunmuyor.'}</p></section></div>
  {!!result.data.cast.length && <section><h2>Oyuncular</h2><div className="cast-grid">{result.data.cast.map((person, i) => <article key={person.name + i}><span aria-hidden="true">{person.name.charAt(0)}</span><h3>{person.name}</h3><p>{person.character}</p></article>)}</div></section>}
 </>}</section>
}
function FilmShelf({ title, sort }: { title: string; sort: string }) {
 const result = useData<Page>('/api/movies/?sort=' + sort)
 return <section className="home-shelf"><div className="section-heading"><h2>{title}</h2><a className="text-link" href={base + 'catalog?sort=' + sort}>Tümünü gör ↗</a></div>
 {result.loading && <div className="loading-grid" role="status" aria-label="Filmler yükleniyor">{Array.from({length:5},(_,i)=><div key={i}/>)}</div>}
 {result.error && <Status error={result.error} retry={result.retry}/>}
 {result.data && (result.data.items.length ? <div className="film-grid">{result.data.items.slice(0,5).map(movie=><a className="film-card" key={movie.id} href={base+'movie/'+movie.id}><div className="poster"><Poster movie={movie}/>{movie.tmdbScore !== null && <span className="score">★ {movie.tmdbScore.toFixed(1)} <small>TMDB</small></span>}</div><h3>{movie.title}</h3><p>{movie.releaseDate?.slice(0,4)}</p></a>)}</div> : <p className="muted">Henüz gösterilecek film yok.</p>)}
 </section>
}
function Home() {
 return <>
  <section className="cinema-hero"><div className="hero-text"><p className="kicker"><span /> FAVO FILM</p><h1>Bir film.<br/><em>Bin başka hayat.</em></h1><a className="hero-link" href={base + 'catalog'}>Sıradaki filmini bul <span aria-hidden="true">↓</span></a></div><div className="cinema-art" aria-hidden="true"><div className="screen"><span>F</span><small>THE NEXT SCENE IS YOURS</small></div><div className="seats"><i/><i/><i/><i/><i/></div><span className="frame-number">01 / SİNEMA</span></div></section>
 <div className="discovery-tiles"><a href={base+'catalog?sort=popular'}><span className="tile-icon" aria-hidden="true">↗</span><div><small>ŞİMDİ KEŞFET</small><h2>Gündemdeki filmler</h2></div><span aria-hidden="true">→</span></a><a href={base+'catalog?sort=top'}><span className="tile-icon" aria-hidden="true">☆</span><div><small>TMDB SEÇKİSİ</small><h2>İz bırakan hikâyeler</h2></div><span aria-hidden="true">→</span></a></div>
 <CommunityHome/><FilmShelf title="Gündemdeki filmler" sort="popular"/>
 <section className="genre-section"><div className="section-heading"><h2>Hangi ruh hâlindesin?</h2></div><div className="genre-tiles">{[{id:18,name:'Biraz derinlik',label:'Dram',icon:'◐'},{id:878,name:'Başka dünyalar',label:'Bilim kurgu',icon:'✧'},{id:53,name:'Nefesini tut',label:'Gerilim',icon:'◈'},{id:35,name:'İyi gelsin',label:'Komedi',icon:'☀'}].map(g=><a key={g.id} href={base+'catalog?genre='+g.id}><span aria-hidden="true">{g.icon}</span><small>{g.label}</small><h3>{g.name}</h3><b aria-hidden="true">↗</b></a>)}</div></section>
 </>
}
export default function App() {
 const account = useSession()
 const path = location.pathname.slice(base.length).replace(/\/$/, '')
 const detail = /^movie\/([1-9]\d*)\/?$/.exec(path)
 return <CommunityProvider key={account.session?.userId ?? 'guest'} account={account}><a className="skip-link" href="#main">İçeriğe geç</a><header className="site-header"><a href={base} className="brand" aria-label="Favo Film ana sayfa"><span className="brand-mark"/><strong>favo</strong><span className="brand-label">Film</span></a><HeaderSearch/><nav aria-label="Ana gezinme"><a href="/">← Favo</a><a href={base} aria-current={path === '' ? 'page' : undefined}>Ana sayfa</a><a href={base + 'catalog'} aria-current={path === 'catalog' ? 'page' : undefined}>Katalog</a><ProfileLink/>{!account.session && <AccountDialog account={account}/>}</nav></header><main id="main">{detail ? <MovieDetail id={detail[1]}/> : path === 'people' ? <People/> : /^people\/[a-zA-Z0-9_]{2,24}$/.test(path) ? <PublicProfile nickname={path.split('/')[1]}/> : path === 'catalog' ? <Catalog/> : path === 'profile' ? <FilmProfile/> : path === '' || path === 'index.html' ? <Home/> : <section className="catalog-state"><h1>Bu sahne bulunamadı.</h1><a href={base}>Filmlere dön ↗</a></section>}</main><footer><a className="footer-brand" href="/">favo <span>Film</span></a><details><summary>Veri kaynağı</summary><p>Film bilgileri ve görseller TMDB tarafından sağlanır. TMDB puanı Favo kullanıcı puanı değildir.</p><a href="https://www.themoviedb.org/" target="_blank" rel="noopener noreferrer"><img className="tmdb-logo" src="https://www.themoviedb.org/assets/2/v4/logos/v2/blue_long_2-9665a76b1ae401a510ec1e0ca40ddcb3b0cfe45f1d51b77a308fea0845885648.svg" alt="TMDB" width="100" /></a><p lang="en">This product uses the TMDB API but is not endorsed or certified by TMDB.</p></details><span>© {new Date().getFullYear()} Favo</span></footer></CommunityProvider>
}
