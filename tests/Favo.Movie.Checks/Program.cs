using System.Net;
using Favo.Movie.Api;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
foreach (var query in new[] { new CatalogQuery("", 0, null, null, "popular"), new("", 501, null, null, "popular"), new(new string('x',121), 1, null, null, "popular"), new("", 1, 1000, null, "popular"), new("", 1, null, -1, "popular"), new("",1,null,null,"invalid"), new("name",1,null,12,"popular") })
 Check(!query.IsValid(), "Invalid filter rejected");
Check(MovieCatalog.Image("//evil.test/x", "w500") is null, "Image host injection rejected");
Check(MovieCatalog.Image("/poster.jpg", "w500") == "https://image.tmdb.org/t/p/w500/poster.jpg", "Trusted image mapping");
using var memory = new MemoryCache(new MemoryCacheOptions { SizeLimit = 500 });
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Tmdb:ReadAccessToken"] = "test-only-token" }).Build();
var handler = new FakeTmdb();
using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.themoviedb.org/3/") };
var catalog = new MovieCatalog(http, config, memory);
var query1 = new CatalogQuery("A&B / İstanbul", 2, 2001, null, "popular");
var result = await catalog.Browse(query1, default);
Check(handler.Last!.Contains("query=A%26B%20%2F%20%C4%B0stanbul"), "Search text encoded");
Check(handler.Last.Contains("language=tr-TR") && handler.Last.Contains("primary_release_year=2001"), "Turkish language and year sent");
Check(result.Items.Length == 1 && result.Items[0].Id == 42, "Adult entries excluded");
Check(result.Items[0].TmdbScore == 8.2m && result.TotalPages == 500, "Score source preserved and pagination capped");
await catalog.Browse(query1, default);
Check(handler.Calls == 1, "Successful response cached");
await catalog.Browse(new("",1,null,18,"top"), default);
Check(handler.Last!.Contains("with_genres=18") && handler.Last.Contains("vote_count.gte=200"), "Discovery genre and credible vote threshold");
var detail = await catalog.Details(42, default);
Check(detail.Directors.SequenceEqual(new[] { "Test Director" }), "Director mapped from credits");
Check(detail.ImdbUrl == "https://www.imdb.com/title/tt1234567/", "IMDb identity linked without inventing IMDb rating");
Check(detail.TrailerUrl == "https://www.youtube.com/watch?v=abc_DEF-123", "Official trailer mapped");
config["Tmdb:ReadAccessToken"] = "";
try { await catalog.Browse(query1, default); throw new Exception("Expected setup error"); }
catch (MovieProviderException e) { Check(e.Code == "catalog_not_configured" && e.Status == 503, "Missing key is explicit even with cached data"); }
config["Tmdb:ReadAccessToken"] = "test-only-token";
handler.Status = HttpStatusCode.TooManyRequests;
try { await catalog.Details(500, default); throw new Exception("Expected upstream throttle"); }
catch (MovieProviderException e) { Check(e.Status == 503 && !e.Message.Contains("secret"), "Provider failure sanitized"); }
handler.Status = HttpStatusCode.NotFound;
try { await catalog.Details(501, default); throw new Exception("Expected missing film"); }
catch (MovieProviderException e) { Check(e.Status == 404, "Missing film mapped"); }
Console.WriteLine($"PASS: {count} Movie checks. No real TMDB key or network used.");
sealed class FakeTmdb : HttpMessageHandler {
 public string? Last; public int Calls; public HttpStatusCode Status = HttpStatusCode.OK;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
  Last = request.RequestUri!.AbsoluteUri; Calls++;
  if (request.Headers.Authorization?.ToString() != "Bearer test-only-token") throw new Exception("Missing server bearer");
  var json = Last.Contains("movie/42?") ? """
   {"id":42,"title":"Film","original_title":"Movie","runtime":110,"imdb_id":"tt1234567","genres":[{"id":18,"name":"Dram"}],"credits":{"crew":[{"job":"Director","name":"Test Director"}],"cast":[]},"videos":{"results":[{"site":"YouTube","type":"Trailer","official":true,"key":"abc_DEF-123"}]}}
   """ : """
   {"page":2,"total_pages":900,"total_results":18000,"results":[{"id":42,"title":"Film","original_title":"Movie","vote_average":8.2,"vote_count":300,"poster_path":"/poster.jpg"},{"id":43,"title":"Excluded","adult":true}]}
   """;
  return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Status == HttpStatusCode.OK ? json : "secret upstream error", System.Text.Encoding.UTF8, "application/json") });
 }
}
