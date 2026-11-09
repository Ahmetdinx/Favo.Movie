# Favo.Movie

Favo Film'in ilk dikey dilimi: ortak Favo oturumu, sinema temalı katalog, TMDB arama/keşif ve film detayları. .NET 10 API + React/TypeScript/Vite.

## Kapsam

- Favo portalından `/go/movie` → `/film/` geçişi.
- Mevcut Favo Identity oturumu; yeni kullanıcı tablosu, parola veya JWT anahtarı yok.
- Türkçe öncelikli TMDB katalog sorguları, yıl/tür filtreleri, popüler/yüksek puan sıralaması ve sayfalama. Adla aramada TMDB'nin desteklediği yıl filtresi kullanılabilir; tür ve sıralama keşif moduna aittir.
- Afiş, film görseli, süre, türler, yönetmen, oyuncular, mevcutsa resmi YouTube fragmanı ve IMDb bağlantısı.
- Kaynak puanı açıkça TMDB olarak gösterilir. Eksik afiş ve eksik Türkçe özet için ayrı durumlar.
- Sunucuda 10 dakika / en fazla 500 sorgu önbelleği. API anahtarı yoksa 503 + anlaşılır katalog durumu; uydurma film veya puan eklenmez.

## Film topluluğu

- Profil bağlantısı seçilen sinema avatarıdır. Altı özgün SVG avatarı vardır; Kitap avatarını değiştirmez.
- Kullanıcı arama, takip / takipten çıkma ve takipçi listeleri ortak Favo ağına bağlıdır.
- Kullanıcı adı ve profilin herkese açık olması Kitap ile ortaktır. Film profilinin ayarlarından düzenlenebilir. Gizli kullanıcılar başka profillerde, aramada ve sosyal akışta görünmez.
- İzleyeceğim / izliyorum / izledim listeleri ve yedi ölçütlü değerlendirmeler kalıcı olarak kaydedilir. Puanlar 1–10; tempo %10, diğer altı ölçüt %15 ağırlıktadır.
- Ana sayfada takip edilenlerin izledikleri ve takipçilerin film değerlendirmeleri iki sütunda görünür.
- En fazla beş tür ve üç öncelik seçilir. Kitap sıralaması uyarlanmıştır: tür +4, beğenilen tür +2, yönetmen +3, ilgi geri bildirimi +1. Topluluk puanında 5 nötr ön oy kullanılır. Öncelik kanıtı en az 3 değerlendirme ister. Üç önceliğin her biri kesinlikle 9'un üzerinde ve tür eşleşiyorsa 4/4 rafına girer; güçlü tür dışı eşleşmeler keşif rafına girer. Takipçi beğenileri ayrıca en fazla +2 katkı sağlar. TMDB puanı Favo puanı yerine kullanılmaz.
- İzlenen, izlenmekte olan, puanlanan veya öneri geri bildirimi verilen filmler yeniden önerilmez. Geri bildirimler profilde geri alınabilir.

Film verileri SQLite `src/backend/Favo.Movie.Api/App_Data/movie.db` dosyasında, kullanıcı GUID ve TMDB film kimliğiyle tutulur. GUID doğrulaması her istekte ortak Identity API'den yapılır; JWT anahtarı kopyalanmaz. Takip ilişkileri ikinci kez kaydedilmez. Film metadata havuzu açılan/kaydedilen filmler ve ana sayfada tür başına getirilen sınırlı keşif sonuçlarından oluşur; bütün TMDB arşivinin tarandığı iddia edilmez. İzleme/değerlendirme yalnızca sunucunun doğruladığı film için yazılır.

**Kitap API ve Movie API yeniden başlatılmalıdır.** Kitap'a eklenen iki okuma ucu: `/api/readers/me/network-members` ve `/api/readers/{nickname}/identity`. Movie'de geliştirme varsayılanı `Identity:ApiOrigin=http://localhost:5117/`; başlatıcı farklı Books origin'ini otomatik geçirir. Üretimde `Identity__ApiOrigin` güvenilen Books HTTPS origin'i, `Movie__DatabasePath` kalıcı disk yolu olmalıdır. SQLite dosyasını yedekleyin; Git veya dağıtım paketine eklemeyin. Çok sunuculu dağıtım için ortak veritabanına geçiş gerekir.

## TMDB bağlantısı

TMDB hesabının API ayarlarından **API Read Access Token** alın. Anahtarı chate veya GitHub'a koymadan bu proje kökünde kendi terminalinizde kaydedin:

```powershell
dotnet user-secrets set "Tmdb:ReadAccessToken" "BURAYA_READ_ACCESS_TOKEN" --project src/backend/Favo.Movie.Api
```

Üretimde `Tmdb__ReadAccessToken` secret/environment değişkeni kullanılır. API anahtarı frontend ortam değişkeni değildir. Ticari olmayan kullanım kaynak gösterimi gerektirir; ticari kullanım için TMDB lisansı alınmalıdır. Onaylı TMDB logosu ve resmi atıf bildirimi uygulamanın Veri kaynağı bölümünde bulunmalıdır.

Resmi belgeler: https://developer.themoviedb.org/docs/faq, https://developer.themoviedb.org/docs/authentication-application

## Çalıştırma

Favo ana projesindeki `Start-Favo.ps1`, projeyi Favo'nun yanındaki `Favo.Movie` klasöründe veya `~/source/repos` altında bulur, Movie API'yi 5130 portunda başlatır ve ön yüzü `/film/` altında sunar. Ana adres: http://localhost:5175/film/

Farklı konum için Favo başlatıcısına `-MovieRoot 'PROJE_YOLU'` verin. Kitap API ortak hesap kaynağı olarak çalışmaya devam eder.

Movie backend'i ayrı çalıştırmak için:

```powershell
dotnet run --project src/backend/Favo.Movie.Api
```

Frontend bağımsız geliştirmesi `npm --prefix src/frontend run dev` ile 5176 portundadır. Ortak oturum doğrulaması daima 5175 origin'i altındaki Film/Kitap/Favo üzerinden yapılmalıdır.

## Kontroller

```powershell
dotnet build Favo.Movie.slnx
dotnet run --project tests/Favo.Movie.Checks
npm --prefix src/frontend ci
npm --prefix src/frontend run build
npm --prefix src/frontend run lint
```

Kontroller sahte HTTP yanıtlarıyla filtre doğrulama, kaynak eşleme, kodlama, önbellek, eksik anahtar ve upstream hatalarını sınar. Gerçek TMDB bağlantısı anahtar sağlanmadan doğrulanmış sayılmaz.

Topluluk entegrasyon testi Docker'da geçici PostgreSQL ve ayrı SQLite kullanır; kişisel veritabanına dokunmaz:

```powershell
$env:FAVO_BOOKS_ROOT=(Resolve-Path ..\Favo.Books).Path
$env:FAVO_MOVIE_ROOT=$PWD.Path
dotnet run --project tests/Favo.Movie.CommunityChecks -c Release -p:BooksRoot=$env:FAVO_BOOKS_ROOT
```

Bu test ortak giriş, hesap ayrımı, avatar kaydı, takip/gizlilik, öneri eşikleri, puan ağırlıkları ve kalıcılığı doğrular. TMDB yanıtları yalnızca test host'unda sahtedir; test filmleri normal uygulamaya eklenmez. Test projesi BooksRoot gerektirdiği için ana çözümün varsayılan derlemesine dahil edilmez.
