using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace Favo.Movie.Api;

public sealed record CatalogQuery(string Query, int Page, int? Year, int? Genre, string Sort)
{
    public bool IsValid() => Query.Length <= 120 && Page is >= 1 and <= 500 && (Year is null or >= 1888 and <= 2100)
        && (Genre is null or > 0) && Sort is "popular" or "top" && (Query.Length == 0 || (Genre is null && Sort == "popular"));
}
public sealed record GenreInfo(int Id, string Name);
public sealed record MovieCard(int Id, string Title, string OriginalTitle, string? PosterUrl, string? BackdropUrl, string? ReleaseDate, decimal? TmdbScore, int VoteCount);
public sealed record MoviePage(MovieCard[] Items, int Page, int TotalPages, int TotalResults);
public sealed record CastMember(string Name, string Character);
public sealed record MovieDetails(MovieCard Movie, string Overview, int? Runtime, GenreInfo[] Genres, string[] Directors, CastMember[] Cast, string? TrailerUrl, string? ImdbUrl);
public sealed class MovieProviderException(string code, string message, int status) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class MovieCatalog(HttpClient http, IConfiguration config, IMemoryCache cache)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private async Task<T> Fetch<T>(string path, CancellationToken ct)
    {
        var token = config["Tmdb:ReadAccessToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new MovieProviderException("catalog_not_configured", "Film kataloğu henüz bağlantıya hazır değil.", 503);
        var key = "tmdb:" + path;
        if (cache.TryGetValue<T>(key, out var cached) && cached is not null) return cached;
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new MovieProviderException("not_found", "Film bulunamadı.", 404);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new MovieProviderException("provider_busy", "Katalog şu an yoğun. Biraz sonra tekrar dene.", 503);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new MovieProviderException("catalog_credentials", "Film kataloğu bağlantısı yapılandırılmalı.", 503);
        if (!response.IsSuccessStatusCode) throw new MovieProviderException("provider_unavailable", "Film kataloğuna şu anda ulaşılamıyor.", 502);
        T value;
        try { value = (await response.Content.ReadFromJsonAsync<T>(Json, ct)) ?? throw new JsonException(); }
        catch (JsonException) { throw new MovieProviderException("provider_response", "Film bilgileri şu anda okunamıyor.", 502); }
        cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return value;
    }
    public async Task<GenreInfo[]> Genres(CancellationToken ct) => (await Fetch<GenreResponse>("genre/movie/list?language=tr-TR", ct)).Genres;
    public async Task<MoviePage> Browse(CatalogQuery query, CancellationToken ct)
    {
        if (!query.IsValid()) throw new ArgumentException("Invalid movie query.", nameof(query));
        var path = query.Query.Length > 0
            ? "search/movie?query=" + Uri.EscapeDataString(query.Query)
            : "discover/movie?include_video=false&sort_by=" + (query.Sort == "top" ? "vote_average.desc&vote_count.gte=200" : "popularity.desc");
        path += "&language=tr-TR&include_adult=false&page=" + query.Page;
        if (query.Year is int year) path += "&primary_release_year=" + year;
        if (query.Genre is int genre) path += "&with_genres=" + genre;
        var result = await Fetch<TmdbPage>(path, ct);
        return new(result.Results.Where(m => !m.Adult && m.Id > 0).Select(Card).ToArray(), result.Page, Math.Min(500, result.TotalPages), result.TotalResults);
    }
    public async Task<MovieDetails> Details(int id, CancellationToken ct)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var movie = await Fetch<TmdbMovie>($"movie/{id}?language=tr-TR&append_to_response=credits,videos", ct);
        if (movie.Adult || movie.Id <= 0) throw new MovieProviderException("not_found", "Film bulunamadı.", 404);
        var video = movie.Videos.Results.FirstOrDefault(v => v.Site == "YouTube" && v.Type == "Trailer" && v.Official && Regex.IsMatch(v.Key, "^[a-zA-Z0-9_-]{1,40}$"));
        return new(Card(movie), movie.Overview ?? "", movie.Runtime > 0 ? movie.Runtime : null, movie.Genres,
            movie.Credits.Crew.Where(p => p.Job == "Director").Select(p => p.Name).Distinct().ToArray(),
            movie.Credits.Cast.Take(12).Select(p => new CastMember(p.Name, p.Character)).ToArray(),
            video is null ? null : "https://www.youtube.com/watch?v=" + video.Key,
            movie.ImdbId is not null && Regex.IsMatch(movie.ImdbId, "^tt[0-9]+$") ? "https://www.imdb.com/title/" + movie.ImdbId + "/" : null);
    }
    public static string? Image(string? path, string size) => path is not null && Regex.IsMatch(path, "^/[a-zA-Z0-9._-]+$")
        ? "https://image.tmdb.org/t/p/" + size + path : null;
    private static MovieCard Card(TmdbMovie m) => new(m.Id, string.IsNullOrWhiteSpace(m.Title) ? m.OriginalTitle : m.Title, m.OriginalTitle,
        Image(m.PosterPath, "w500"), Image(m.BackdropPath, "w1280"), string.IsNullOrWhiteSpace(m.ReleaseDate) ? null : m.ReleaseDate, m.VoteCount > 0 ? m.VoteAverage : null, m.VoteCount);
    public sealed class GenreResponse { public GenreInfo[] Genres { get; set; } = []; }
    public sealed class TmdbPage { public TmdbMovie[] Results { get; set; } = []; public int Page { get; set; } public int TotalPages { get; set; } public int TotalResults { get; set; } }
    public sealed class TmdbMovie {
        public int Id { get; set; } public string Title { get; set; } = ""; public string OriginalTitle { get; set; } = "";
        public string? PosterPath { get; set; } public string? BackdropPath { get; set; } public string? ReleaseDate { get; set; }
        public decimal VoteAverage { get; set; } public int VoteCount { get; set; } public bool Adult { get; set; }
        public string? Overview { get; set; } public int Runtime { get; set; } public GenreInfo[] Genres { get; set; } = [];
        public string? ImdbId { get; set; } public Credits Credits { get; set; } = new(); public Videos Videos { get; set; } = new();
    }
    public sealed class Credits { public Person[] Cast { get; set; } = []; public Person[] Crew { get; set; } = []; }
    public sealed class Person { public string Name { get; set; } = ""; public string Job { get; set; } = ""; public string Character { get; set; } = ""; }
    public sealed class Videos { public Video[] Results { get; set; } = []; }
    public sealed class Video { public string Key { get; set; } = ""; public string Site { get; set; } = ""; public string Type { get; set; } = ""; public bool Official { get; set; } }
}
