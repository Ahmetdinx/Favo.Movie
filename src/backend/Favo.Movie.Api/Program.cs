using Favo.Movie.Api;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);
var identityOrigin = builder.Configuration["Identity:ApiOrigin"] ?? (builder.Environment.IsDevelopment() ? "http://localhost:5117/" : throw new InvalidOperationException("Configure Identity:ApiOrigin."));
if (!Uri.TryCreate(identityOrigin, UriKind.Absolute, out var identityUri) || identityUri.UserInfo.Length>0 || identityUri.Query.Length>0 || identityUri.Fragment.Length>0 || identityUri.AbsolutePath!="/" || (identityUri.Scheme!="https" && !(builder.Environment.IsDevelopment() && identityUri.Scheme=="http" && identityUri.IsLoopback)))
    throw new InvalidOperationException("Identity:ApiOrigin must be a trusted HTTPS origin (loopback HTTP allowed in Development).");
builder.Services.AddHttpClient<SharedIdentity>(client=>{client.BaseAddress=identityUri;client.Timeout=TimeSpan.FromSeconds(10);})
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
builder.Services.AddSingleton(new MovieStore(builder.Configuration["Movie:DatabasePath"] ?? Path.Combine(builder.Environment.ContentRootPath,"App_Data","movie.db")));
builder.Services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 }));
builder.Services.AddHttpClient<MovieCatalog>(client => {
    client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
var app = builder.Build();
app.MapGet("/", () => Results.Ok(new { service = "Favo.Movie.Api", status = "running" }));
var movies = app.MapGroup("/api/movies");
movies.AddEndpointFilter(async (context, next) => {
    context.HttpContext.Response.Headers.CacheControl = "no-store";
    try { return await next(context); }
    catch (MovieProviderException e) { return Results.Json(new { code = e.Code, message = e.Message }, statusCode: e.Status); }
    catch (HttpRequestException) { return Results.Json(new { code = "provider_unavailable", message = "Film kataloğuna şu anda ulaşılamıyor." }, statusCode: 502); }
    catch (OperationCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested) {
        return Results.Json(new { code = "provider_timeout", message = "Film kataloğu yanıt vermedi. Tekrar dene." }, statusCode: 504);
    }
});
movies.MapGet("/status", (IConfiguration configuration) => Results.Ok(new { configured = !string.IsNullOrWhiteSpace(configuration["Tmdb:ReadAccessToken"]) }));
movies.MapGet("/genres", async (MovieCatalog catalog, CancellationToken ct) => Results.Ok(await catalog.Genres(ct)));
movies.MapGet("/", async (string? query, int? page, int? year, int? genre, string? sort, MovieCatalog catalog, CancellationToken ct) => {
    var request = new CatalogQuery(query?.Trim() ?? "", page ?? 1, year, genre, sort ?? "popular");
    if (!request.IsValid()) return Results.BadRequest(new { code = "invalid_query", message = "Arama ve filtre değerlerini kontrol et." });
    return Results.Ok(await catalog.Browse(request, ct));
});
movies.MapGet("/{id:int}", async (int id, MovieCatalog catalog, CancellationToken ct) =>
    id <= 0 ? Results.BadRequest() : Results.Ok(await catalog.Details(id, ct)));
movies.MapMovieCommunity();
app.Run();
public partial class Program { }
