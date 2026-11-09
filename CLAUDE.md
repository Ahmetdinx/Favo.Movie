# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Favo Film; TMDB kataloğu, izleme listeleri, yedi ölçütlü değerlendirme, öneriler ve ortak takip ağı sunan uygulamadır.

## Komutlar

```powershell
dotnet build Favo.Movie.slnx
dotnet run --project tests/Favo.Movie.Checks      # 21 katalog kontrolü, sahte TMDB, Docker gerekmez
dotnet run --project src/backend/Favo.Movie.Api
npm --prefix src/frontend ci
npm --prefix src/frontend run build
npm --prefix src/frontend run lint
```

Topluluk entegrasyon testleri Docker ve Favo.Books kaynak kodunu gerektirir; `Favo.Movie.slnx` derlemesine dahil değildir:

```powershell
$env:FAVO_BOOKS_ROOT='C:/Projects/Favo.Books'
$env:FAVO_MOVIE_ROOT='C:/Projects/Favo.Movie'
dotnet run --project tests/Favo.Movie.CommunityChecks -c Release -p:BooksRoot=C:/Projects/Favo.Books
```

Kontrol projeleri çalıştırılabilir programlardır, `dotnet test` bulmaz. Tek test filtresi yoktur; ilk başarısız kontrolde durur.

TMDB için `dotnet user-secrets set "Tmdb:ReadAccessToken" "<token>" --project src/backend/Favo.Movie.Api` gerekir. Token yoksa katalog 503 ve açık bir durum döner; uydurma film veya puan eklenmez. Gerçek TMDB bağlantısı henüz doğrulanmadı.

## Mimari

Tek bir API projesi ve katmansız dosyalardan oluşur; EF Core yoktur.

- `MovieCatalog.cs`: TMDB istemcisi, 10 dakika / en fazla 500 sorguluk bellek önbelleği. Sıralama, tür ve yıl filtreleri keşif modundadır; adla aramada yalnızca yıl filtresi desteklenir.
- `MovieCommunity.cs`: `MovieStore` (ham SQL ile SQLite), `SharedIdentity`, öneri hesapları.
- `CommunityEndpoints.cs`: `/api/movies/community` uçları.
- Veritabanı `App_Data/movie.db` dosyasıdır; `Movie:DatabasePath` ile değişir, Git'e ve yayın paketine girmez. Çok sunuculu dağıtım için ortak veritabanına geçmek gerekir.

### Kimlik

Movie JWT doğrulamaz ve kullanıcı tutmaz. Gelen bearer token'ı `SharedIdentity` ile her istekte `Identity:ApiOrigin` adresindeki Books API'ye iletir (geliştirmede `http://localhost:5117/`). Nickname, görünürlük ve takip ilişkileri Books'tadır; `/api/readers/me`, `/api/readers/me/network-members` ve `/api/readers/{nickname}/identity` uçlarına bağımlıdır. Film verisi kullanıcı GUID'si ve TMDB film kimliğiyle saklanır. Film avatarı Kitap avatarından bağımsızdır.

### Öneri ve puan kuralları

Kitap uygulamasıyla aynı düzen uyarlanmıştır, testler bu değerleri zorlar:

- Yedi ölçüt 1-10; tempo %10, diğerleri %15. Topluluk puanında 5 değerinde 5 nötr ön oy vardır.
- Skor: tür +4, beğenilen tür +2, yönetmen +3, ilgi geri bildirimi +1, takipçi beğenileri en fazla +2.
- Öncelik kanıtı en az 3 değerlendirme ister; güçlü raf için üç önceliğin her biri kesinlikle 9'un üzerinde olmalıdır.
- TMDB puanı Favo puanı yerine kullanılmaz. İzlenen, izlenmekte olan, puanlanan veya geri bildirim verilen filmler yeniden önerilmez.
- Yazmalar yalnızca sunucunun doğruladığı filmler için yapılır.

### Ön yüz

Vite tabanı `/film/`dir. Ortak oturum `http://localhost:5175/film/` üzerinden test edilir; bağımsız 5176 yalnızca arayüz geliştirme içindir. `src/frontend/src/auth/sessionTransport.ts`, Favo ve Favo.Books ile birebir aynı kalmalıdır. Görsel kimlik koyu bordo/altındır; gezinme düzeni `Favo/UX_CONVENTIONS.md` ile uyumludur.
