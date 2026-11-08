extern alias movie;
extern alias books;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Favo.Movie.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Favo.Books.Modules.Identity.Persistence;
using Favo.Books.Modules.Catalog.Persistence;
using Favo.Books.Modules.Reading.Persistence;
using Favo.Books.Modules.Profiles.Persistence;
using Favo.Books.Modules.Community.Persistence;

var booksRoot=Environment.GetEnvironmentVariable("FAVO_BOOKS_ROOT")!;
var movieRoot=Environment.GetEnvironmentVariable("FAVO_MOVIE_ROOT")!;
var databasePath=Path.Combine(Path.GetTempPath(),"favo-movie-tests",Guid.NewGuid().ToString(),"test.db");
await using var database=new PostgreSqlBuilder("postgres:18.4-alpine3.24").WithDatabase("movie_checks").WithUsername("postgres").WithPassword("test-only").Build();
await database.StartAsync();
await using var booksFactory=new BooksFactory(booksRoot,database.GetConnectionString());
booksFactory.UseKestrel(5118);
using(var scope=booksFactory.Services.CreateScope()){
 await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
 await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
 await scope.ServiceProvider.GetRequiredService<ReadingDbContext>().Database.MigrateAsync();
 await scope.ServiceProvider.GetRequiredService<ProfilesDbContext>().Database.MigrateAsync();
 await scope.ServiceProvider.GetRequiredService<CommunityDbContext>().Database.MigrateAsync();
}
await using var movieFactory=new MovieFactory(movieRoot,databasePath);
if(args.Contains("--serve"))movieFactory.UseKestrel(5131);
using var anonymous=movieFactory.CreateClient();
var checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
async Task<JsonElement> Read(HttpClient c,string path){var r=await c.GetAsync(path);r.EnsureSuccessStatusCode();return await r.Content.ReadFromJsonAsync<JsonElement>();}
async Task<(HttpClient Books,HttpClient Movie,Guid Id)> User(string name){
 var client=new HttpClient{BaseAddress=new Uri("http://localhost:5118")};
 (await client.PostAsJsonAsync("/api/identity/register",new{email=name+"@example.test",nickname=name,password="Cinema-Test-Only-2026!"})).EnsureSuccessStatusCode();
 var r=await client.PostAsJsonAsync("/api/identity/login",new{email=name+"@example.test",password="Cinema-Test-Only-2026!"});r.EnsureSuccessStatusCode();var login=await r.Content.ReadFromJsonAsync<JsonElement>();
 client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",login.GetProperty("accessToken").GetString());
 (await client.PutAsJsonAsync("/api/readers/me",new{nickname=name,isPublic=true})).EnsureSuccessStatusCode();
 var film=movieFactory.CreateClient();film.DefaultRequestHeaders.Authorization=client.DefaultRequestHeaders.Authorization;
 return(client,film,login.GetProperty("userId").GetGuid());
}
var ada=await User("movieada");var deniz=await User("moviedeniz");var ece=await User("movieece");var can=await User("moviecan");
var scores=MovieRecommendations.Criteria.Keys.ToDictionary(k=>k,_=>10);
Check((await anonymous.GetAsync("/api/movies/community/me")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous profile denied");
Check((await anonymous.PutAsJsonAsync("/api/movies/community/avatar",new{avatarId="noir"})).StatusCode==HttpStatusCode.Unauthorized,"Anonymous mutation denied");
anonymous.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","forged");
Check((await anonymous.GetAsync("/api/movies/community/me")).StatusCode==HttpStatusCode.Unauthorized,"Forged bearer rejected by shared identity");
Check((await ada.Movie.PutAsJsonAsync("/api/movies/community/avatar",new{avatarId="unknown"})).StatusCode==HttpStatusCode.BadRequest,"Unknown avatar rejected");
(await ada.Movie.PutAsJsonAsync("/api/movies/community/avatar",new{avatarId="space"})).EnsureSuccessStatusCode();
Check((await Read(ada.Movie,"/api/movies/community/me")).GetProperty("avatarId").GetString()=="space","Film avatar persisted");
Check((await Read(deniz.Movie,"/api/movies/community/me")).GetProperty("avatarId").GetString()=="projector","Avatar is account scoped");
Check((await Read(ada.Books,"/api/readers/me")).GetProperty("avatarId").GetString()!="space","Books avatar untouched");
Check((await ada.Movie.PutAsJsonAsync("/api/movies/community/preferences",new{genres=new[]{18},criteria=new[]{"theme","theme"}})).StatusCode==HttpStatusCode.BadRequest,"Duplicate criteria rejected");
(await ada.Movie.PutAsJsonAsync("/api/movies/community/preferences",new{genres=new[]{18},criteria=new[]{"theme","visuals","pace"}})).EnsureSuccessStatusCode();
Check((await ada.Movie.PutAsJsonAsync("/api/movies/community/entry/42",new{state="watched",scores=new{theme=11}})).StatusCode==HttpStatusCode.BadRequest,"Incomplete/out of range rating rejected");
Check((await ada.Movie.PutAsJsonAsync("/api/movies/community/entry/42",new{state="watching",scores})).StatusCode==HttpStatusCode.BadRequest,"Unwatched rating rejected");
foreach(var u in new[]{deniz,ece,can})(await u.Movie.PutAsJsonAsync("/api/movies/community/entry/42",new{state="watched",scores})).EnsureSuccessStatusCode();
(await deniz.Movie.PutAsJsonAsync("/api/movies/community/entry/43",new{state="watching"})).EnsureSuccessStatusCode();
(await deniz.Books.PutAsync("/api/readers/movieada/follow",null)).EnsureSuccessStatusCode();
(await ada.Books.PutAsync("/api/readers/moviedeniz/follow",null)).EnsureSuccessStatusCode();
var home=await Read(ada.Movie,"/api/movies/community/home");
Check(home.GetProperty("reviews").GetArrayLength()==1,"Follower film rating visible");
Check(home.GetProperty("watching").GetArrayLength()==1,"Following film activity visible");
var suggestion=home.GetProperty("suggestions").EnumerateArray().Single(s=>s.GetProperty("movie").GetProperty("id").GetInt32()==42);
Check(suggestion.GetProperty("shelf").GetString()=="perfect","Three ratings above nine plus genre and three priorities form perfect match");
Check(suggestion.GetProperty("reasons").EnumerateArray().Any(r=>r.GetString()!.Contains("takipçin")),"Follower rating contributes explained signal");
(await deniz.Books.PutAsJsonAsync("/api/readers/me",new{nickname="moviedeniz",isPublic=false})).EnsureSuccessStatusCode();
home=await Read(ada.Movie,"/api/movies/community/home");
Check(home.GetProperty("reviews").GetArrayLength()==0 && home.GetProperty("watching").GetArrayLength()==0,"Private profile removed from both activity feeds");
Check((await ada.Movie.GetAsync("/api/movies/community/profile/moviedeniz")).StatusCode==HttpStatusCode.NotFound,"Private film profile denied");
Check((await ada.Books.GetAsync("/api/readers/moviedeniz/identity")).StatusCode==HttpStatusCode.NotFound,"Shared public identity respects privacy");
(await deniz.Books.PutAsJsonAsync("/api/readers/me",new{nickname="moviedeniz",isPublic=true})).EnsureSuccessStatusCode();
(await deniz.Books.DeleteAsync("/api/readers/movieada/follow")).EnsureSuccessStatusCode();
Check((await Read(ada.Movie,"/api/movies/community/home")).GetProperty("reviews").GetArrayLength()==0,"Unfollow removes follower reviews");
(await ada.Movie.PutAsJsonAsync("/api/movies/community/feedback/42",new{choice="dismissed"})).EnsureSuccessStatusCode();
Check(!(await Read(ada.Movie,"/api/movies/community/home")).GetProperty("suggestions").EnumerateArray().Any(s=>s.GetProperty("movie").GetProperty("id").GetInt32()==42),"Dismissed movie excluded");
(await ada.Movie.DeleteAsync("/api/movies/community/feedback/42")).EnsureSuccessStatusCode();
(await ada.Movie.PutAsJsonAsync("/api/movies/community/entry/42",new{state="watched",scores})).EnsureSuccessStatusCode();
Check(!(await Read(ada.Movie,"/api/movies/community/home")).GetProperty("suggestions").EnumerateArray().Any(s=>s.GetProperty("movie").GetProperty("id").GetInt32()==42),"Watched/rated movie excluded");
(await ada.Movie.DeleteAsync("/api/movies/community/entry/42")).EnsureSuccessStatusCode();
Check((await Read(deniz.Movie,"/api/movies/community/me")).GetProperty("entries").GetArrayLength()==2,"Delete cannot affect another user's films");
Check(new MovieStore(databasePath).Avatar(ada.Id)=="space","Data survives new store instance");
Check((await Read(deniz.Movie,"/api/movies/community/directory?query=movieada")).GetProperty("items")[0].GetProperty("avatarId").GetString()=="space","Search uses saved movie avatar");
Check((await Read(ada.Movie,"/api/movies/community/directory?kind=following")).GetProperty("items").GetArrayLength()==1,"Movie follows list uses shared graph");
var weighted=MovieRecommendations.Criteria.Keys.ToDictionary(k=>k,_=>10);weighted["pace"]=1;
Check(new MovieEntry(ada.Id,42,"watched",weighted,DateTimeOffset.UtcNow).Overall==9.1m,"Same weighting as Books: pace ten percent, other criteria fifteen");
var store=movieFactory.Services.GetRequiredService<MovieStore>();
var films=store.All<MovieDetails>("movie");
var entries=store.All<MovieEntry>("entry");
var pref=new MoviePreference([18],["theme","visuals","pace"]);
Check(MovieRecommendations.Rank(films,entries.Where(e=>e.UserId!=can.Id).ToArray(),[],ada.Id,pref,[]).All(s=>s.Shelf!="perfect"),"Fewer than three ratings never claims perfect");
var nine=entries.Select(e=>e with{Scores=e.Scores is null?null:MovieRecommendations.Criteria.Keys.ToDictionary(k=>k,_=>9)}).ToArray();
Check(MovieRecommendations.Rank(films,nine,[],ada.Id,pref,[]).All(s=>s.Shelf!="perfect"),"Exactly nine does not meet above-nine threshold");
Check(MovieRecommendations.Rank(films,entries,[],ada.Id,new([35],["theme","visuals","pace"]),[]).Any(s=>s.Shelf=="discovery"),"Strong out-of-genre film uses discovery shelf");
Check(MovieRecommendations.Rank(films,[],[],ada.Id,new([35],[]),[]).Length==0,"Unrelated genre without evidence excluded");
Console.WriteLine($"PASS: {checks} Movie community integration checks. Disposable databases and fake TMDB only.");
if(args.Contains("--serve")){
 (await deniz.Books.PutAsync("/api/readers/movieada/follow",null)).EnsureSuccessStatusCode();
 Console.WriteLine("PREVIEW READY: Movie5131 Books5118; movieada@example.test / Cinema-Test-Only-2026!");
 using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};try{await Task.Delay(Timeout.Infinite,stop.Token);}catch(OperationCanceledException){}
}

sealed class BooksFactory(string root,string connection):WebApplicationFactory<books::Program>{
 protected override void ConfigureWebHost(IWebHostBuilder b){b.UseContentRoot(Path.Combine(root,"src/backend/Api/Favo.Books.Api"));b.UseEnvironment("Testing");b.UseSetting("ConnectionStrings:Database",connection);b.UseSetting("Jwt:Secret","movie-isolated-testing-secret-at-least-32-characters");b.UseSetting("Jwt:Issuer","MovieTests");b.UseSetting("Jwt:Audience","MovieTests");b.UseSetting("Jwt:ExpirationMinutes","60");b.UseSetting("Catalog:ImportJobs:WorkerEnabled","false");b.UseSetting("Google:ClientId","");b.UseSetting("Logging:LogLevel:Default","Warning");}
}
sealed class MovieFactory(string root,string database):WebApplicationFactory<movie::Program>{
 protected override void ConfigureWebHost(IWebHostBuilder b){b.UseContentRoot(Path.Combine(root,"src/backend/Favo.Movie.Api"));b.UseEnvironment("Development");b.UseSetting("Identity:ApiOrigin","http://localhost:5118/");b.UseSetting("Movie:DatabasePath",database);b.UseSetting("Tmdb:ReadAccessToken","test-only");b.UseSetting("Logging:LogLevel:Default","Warning");b.ConfigureServices(s=>s.AddHttpClient<MovieCatalog>().ConfigurePrimaryHttpMessageHandler(()=>new FakeMovies()));}
}
sealed class FakeMovies:HttpMessageHandler{
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
 var path=request.RequestUri!.AbsolutePath; object body;
 if(path.Contains("genre/movie"))body=new{genres=new[]{new{id=18,name="Dram"},new{id=35,name="Komedi"}}};
 else if(path.Contains("discover"))body=new{page=1,total_pages=1,total_results=2,results=new[]{new{id=42,title="Test Filmi · Son Seans",original_title="Last Screening",adult=false,vote_count=200,vote_average=8},new{id=43,title="Test Filmi · Gece Yolculuğu",original_title="Night Journey",adult=false,vote_count=100,vote_average=7}}};
 else {var id=int.Parse(path.Split('/').Last());body=new{id,title=id==42?"Test Filmi · Son Seans":"Test Filmi · Gece Yolculuğu",original_title="Cinema test fixture",overview="Yalnızca otomatik ve görsel test için oluşturulmuş film kaydı.",release_date="2026-01-01",runtime=110,genres=new[]{new{id=18,name="Dram"}},credits=new{crew=new[]{new{name="Test Yönetmeni",job="Director"}},cast=Array.Empty<object>()},videos=new{results=Array.Empty<object>()},vote_count=100,vote_average=8,adult=false};}
 return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(body)});
 }
}
