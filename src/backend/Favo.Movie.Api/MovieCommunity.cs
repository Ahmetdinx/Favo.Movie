using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Favo.Movie.Api;

public sealed record MoviePreference(int[] Genres, string[] Criteria);
public sealed record MovieEntry(Guid UserId, int MovieId, string State, Dictionary<string, int>? Scores, DateTimeOffset UpdatedAtUtc)
{
    public decimal? Overall => MovieRecommendations.ValidScores(Scores)
        ? Scores!.Sum(p => p.Value * (p.Key == "pace" ? 10m : 15m)) / 100m : null;
}
public sealed record MovieFeedback(Guid UserId, int MovieId, string Choice);
public sealed record FilmAvatar(string AvatarId);
public sealed record IdentityUser(Guid UserId, string Email);
public sealed record ReaderSettings(string? Nickname, bool IsPublic, string AvatarId);
public sealed record NetworkMember(Guid Id, string Nickname);
public sealed record MovieNetwork(NetworkMember[] Following, NetworkMember[] Followers);
public sealed record MovieViewer(Guid UserId, string? Nickname, bool IsPublic);
public sealed record FilmSuggestion(MovieCard Movie, string Shelf, string[] Reasons, int RatingCount, Dictionary<string, decimal> PriorityScores, decimal Rank);

// Remote validation keeps the shared identity signing secret out of Movie.
public sealed class SharedIdentity(HttpClient http)
{
    public async Task<T> Get<T>(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new MovieProviderException("unauthorized", "Lütfen yeniden giriş yap.", 401);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new MovieProviderException("not_found", "Profil bulunamadı veya gizli.", 404);
        if (!response.IsSuccessStatusCode) throw new MovieProviderException("identity_unavailable", "Favo hesap servisine ulaşılamıyor.", 503);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new MovieProviderException("identity_unavailable", "Hesap bilgisi alınamadı.", 503);
    }
    public static string Token(HttpContext context)
    {
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var value) || value.Scheme != "Bearer" || string.IsNullOrWhiteSpace(value.Parameter))
            throw new MovieProviderException("unauthorized", "Giriş yapmalısın.", 401);
        return value.Parameter;
    }
    public async Task<MovieViewer> Viewer(HttpContext context, CancellationToken ct)
    {
        var token = Token(context);
        var user = await Get<IdentityUser>("api/identity/me", token, ct);
        var settings = await Get<ReaderSettings>("api/readers/me", token, ct);
        if (user.UserId == Guid.Empty) throw new MovieProviderException("unauthorized", "Geçersiz oturum.", 401);
        return new(user.UserId, settings.Nickname, settings.IsPublic);
    }
}

// SQLite's primary key and transaction make concurrent updates/restarts safe.
// Owned film data stays separate from the shared identity and follow graph.
public sealed class MovieStore
{
    private readonly string connectionString;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public MovieStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS documents(kind TEXT NOT NULL, owner TEXT NOT NULL, item TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(kind,owner,item));";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    public T? Get<T>(string kind, string owner, string item = "")
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM documents WHERE kind=$k AND owner=$o AND item=$i";
        cmd.Parameters.AddWithValue("$k",kind); cmd.Parameters.AddWithValue("$o",owner); cmd.Parameters.AddWithValue("$i",item);
        return cmd.ExecuteScalar() is string json ? JsonSerializer.Deserialize<T>(json, Json) : default;
    }
    public T[] All<T>(string kind, string? owner = null)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM documents WHERE kind=$k" + (owner is null ? "" : " AND owner=$o");
        cmd.Parameters.AddWithValue("$k",kind); if (owner is not null) cmd.Parameters.AddWithValue("$o",owner);
        using var reader = cmd.ExecuteReader(); List<T> rows = [];
        while(reader.Read()) rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), Json)!);
        return rows.ToArray();
    }
    public void Save<T>(string kind, string owner, string item, T value)
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO documents VALUES($k,$o,$i,$v) ON CONFLICT(kind,owner,item) DO UPDATE SET payload=excluded.payload";
        cmd.Parameters.AddWithValue("$k",kind); cmd.Parameters.AddWithValue("$o",owner); cmd.Parameters.AddWithValue("$i",item); cmd.Parameters.AddWithValue("$v",JsonSerializer.Serialize(value,Json)); cmd.ExecuteNonQuery();
    }
    public void Delete(string kind, string owner, string item)
    {
        using var db = Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM documents WHERE kind=$k AND owner=$o AND item=$i";
        cmd.Parameters.AddWithValue("$k",kind); cmd.Parameters.AddWithValue("$o",owner); cmd.Parameters.AddWithValue("$i",item); cmd.ExecuteNonQuery();
    }
    public string Avatar(Guid id) => Get<FilmAvatar>("avatar",id.ToString())?.AvatarId ?? "projector";
    public MoviePreference Preferences(Guid id) => Get<MoviePreference>("preference",id.ToString()) ?? new([],[]);
}

public static class MovieRecommendations
{
    public static readonly Dictionary<string,string> Criteria = new() {
        ["theme"]="Konu / Fikir", ["visuals"]="Görüntü / Sinematografi", ["characters"]="Oyunculuk / Karakterler",
        ["structure"]="Senaryo / Kurgu", ["pace"]="Tempo / Sürükleyicilik", ["impact"]="Duygusal Etki", ["experience"]="Genel İzleme Deneyimi" };
    public static readonly string[] Avatars = ["projector", "director", "ticket", "popcorn", "noir", "space"];
    public static bool ValidScores(Dictionary<string,int>? scores) => scores is not null && scores.Count == 7 && scores.All(p=>Criteria.ContainsKey(p.Key) && p.Value is >=1 and <=10);
    public static bool ValidPreferences(MoviePreference p) => p.Genres is not null && p.Criteria is not null && p.Genres.Length <=5 && p.Criteria.Length <=3 && p.Genres.All(g=>g>0)
        && p.Genres.Distinct().Count()==p.Genres.Length && p.Criteria.Distinct().Count()==p.Criteria.Length && p.Criteria.All(c=>c is not null && Criteria.ContainsKey(c));
    public static FilmSuggestion[] Rank(MovieDetails[] films, MovieEntry[] entries, MovieFeedback[] feedback, Guid user, MoviePreference preference, HashSet<Guid> followers)
    {
        var own = entries.Where(e=>e.UserId==user).ToArray();
        var ratings = entries.Where(e=>ValidScores(e.Scores)).ToArray();
        var excluded = own.Where(e=>e.State is "watching" or "watched" || e.Scores is not null).Select(e=>e.MovieId).Concat(feedback.Where(f=>f.UserId==user).Select(f=>f.MovieId)).ToHashSet();
        var liked = films.Where(f=>own.Any(e=>e.MovieId==f.Movie.Id && e.Overall>=7)).ToArray();
        var interested = films.Where(f=>feedback.Any(e=>e.UserId==user && e.MovieId==f.Movie.Id && e.Choice=="interested")).ToArray();
        List<FilmSuggestion> result=[];
        foreach(var film in films.Where(f=>!excluded.Contains(f.Movie.Id)))
        {
            List<string> reasons=[]; decimal score=0;
            var genre=film.Genres.FirstOrDefault(g=>preference.Genres.Contains(g.Id));
            if(genre is not null){score+=4;reasons.Add($"Seçtiğin {genre.Name} türünde.");}
            if(liked.Any(f=>f.Genres.Select(g=>g.Id).Intersect(film.Genres.Select(g=>g.Id)).Any())){score+=2;reasons.Add("Beğendiğin filmlerle ortak türü var.");}
            if(liked.Any(f=>f.Directors.Intersect(film.Directors).Any())){score+=3;reasons.Add("Beğendiğin bir yönetmenin filmi.");}
            if(interested.Any(f=>f.Directors.Intersect(film.Directors).Any() || f.Genres.Select(g=>g.Id).Intersect(film.Genres.Select(g=>g.Id)).Any())){score+=1;reasons.Add("İlgini çeken filmlerle benzer.");}
            var votes=ratings.Where(e=>e.MovieId==film.Movie.Id).ToArray();
            score+=(votes.Sum(e=>e.Overall!.Value)+25)/(votes.Length+5)/10;
            var priority=new Dictionary<string,decimal>();
            foreach(var criterion in preference.Criteria)
            {
                if(votes.Length<3) continue;
                var avg=votes.Average(e=>(decimal)e.Scores![criterion]); priority[criterion]=avg;
                score+=((avg*votes.Length+25)/(votes.Length+5)-5)/preference.Criteria.Length;
                if(avg>=7) reasons.Add($"{Criteria[criterion]}: {avg:0.0}/10 ({votes.Length} değerlendirme).");
            }
            var followerVotes=votes.Where(e=>followers.Contains(e.UserId) && e.Overall>=7).ToArray();
            if(followerVotes.Length>0){score+=Math.Min(2,followerVotes.Length*.5m);reasons.Add($"{followerVotes.Length} takipçin beğendi.");}
            var strong=preference.Criteria.Length>0 && priority.Count==preference.Criteria.Length && priority.Values.All(v=>v>9);
            var shelf=genre is not null && preference.Criteria.Length==3 && strong ? "perfect" : genre is null && preference.Genres.Length>0 && film.Genres.Length>0 && strong ? "discovery" : "personal";
            if(preference.Genres.Length>0 && genre is null && shelf!="discovery") continue;
            if(reasons.Count==0) reasons.Add("Keşif önerisi; kişisel sinyaller henüz yeterli değil.");
            result.Add(new(film.Movie,shelf,reasons.ToArray(),votes.Length,priority,score));
        }
        var sorted=result.OrderByDescending(r=>r.Rank).ThenBy(r=>r.Movie.Id).ToArray();
        return sorted.Where(r=>r.Shelf=="perfect").Take(6).Concat(sorted.Where(r=>r.Shelf=="personal").Take(6)).Concat(sorted.Where(r=>r.Shelf=="discovery").Take(3)).ToArray();
    }
}
