namespace Favo.Movie.Api;

public static class CommunityEndpoints
{
    public sealed record EntryRequest(string State, Dictionary<string,int>? Scores);
    public sealed record FeedbackRequest(string Choice);
    public sealed record NamePage(string[] Items, bool HasMore);
    public sealed record MemberPage(NetworkMember[] Items, bool HasMore);
    public static void MapMovieCommunity(this RouteGroupBuilder movies)
    {
        movies.MapGet("/community/options", () => Results.Ok(new { criteria=MovieRecommendations.Criteria, avatars=MovieRecommendations.Avatars }));
        var group=movies.MapGroup("/community");
        group.AddEndpointFilter(async (context,next)=>{
            var identity=context.HttpContext.RequestServices.GetRequiredService<SharedIdentity>();
            context.HttpContext.Items["viewer"]=await identity.Viewer(context.HttpContext,context.HttpContext.RequestAborted);
            return await next(context);
        });
        group.MapGet("/me", (HttpContext ctx,MovieStore store)=>{
            var v=Viewer(ctx);return Results.Ok(new {v.Nickname,v.IsPublic,avatarId=store.Avatar(v.UserId),preferences=store.Preferences(v.UserId),entries=Entries(store,v.UserId),feedback=store.All<MovieFeedback>("feedback",v.UserId.ToString()).Select(f=>new{f.MovieId,f.Choice,title=store.Get<MovieDetails>("movie","",f.MovieId.ToString())?.Movie.Title})});
        });
        group.MapGet("/directory", async(string? query,string? kind,int? page,HttpContext ctx,SharedIdentity identity,MovieStore store,CancellationToken ct)=>{
            var number=page??1; if(number<1 || number>500)return Results.BadRequest();
            string[] names;bool hasMore;var token=SharedIdentity.Token(ctx);
            if(kind is "followers" or "following"){
                var network=await identity.Get<MovieNetwork>("api/readers/me/network-members",token,ct);
                var members=(kind=="followers"?network.Followers:network.Following).OrderBy(m=>m.Nickname,StringComparer.Ordinal).Skip((number-1)*20).Take(21).ToArray();
                return Results.Ok(new {items=members.Take(20).Select(m=>new{m.Nickname,avatarId=store.Avatar(m.Id)}),hasMore=members.Length>20});
            }
            if(kind is not null || string.IsNullOrWhiteSpace(query) || !System.Text.RegularExpressions.Regex.IsMatch(query,"^[a-zA-Z0-9_]{2,24}$"))return Results.BadRequest(new{message="En az iki karakterle kullanıcı adı ara."});
            var result=await identity.Get<NamePage>($"api/readers/?query={Uri.EscapeDataString(query)}&page={number}",token,ct);names=result.Items;hasMore=result.HasMore;
            var visible=new List<object>();
            foreach(var name in names){try{var member=await identity.Get<NetworkMember>("api/readers/"+Uri.EscapeDataString(name)+"/identity",token,ct);visible.Add(new{member.Nickname,avatarId=store.Avatar(member.Id)});}catch(MovieProviderException e)when(e.Status==404){}}
            return Results.Ok(new{items=visible,hasMore});
        });
        group.MapPut("/avatar", (FilmAvatar value,HttpContext ctx,MovieStore store)=>{
            if(!MovieRecommendations.Avatars.Contains(value.AvatarId))return Results.BadRequest(new {message="Listeden bir avatar seç."});
            store.Save("avatar",Viewer(ctx).UserId.ToString(),"",value);return Results.NoContent();
        });
        group.MapPut("/preferences", async (MoviePreference value,HttpContext ctx,MovieStore store,MovieCatalog catalog,CancellationToken ct)=>{
            if(!MovieRecommendations.ValidPreferences(value))return Results.BadRequest(new {message="En fazla beş tür ve üç farklı öncelik seç."});
            if(value.Genres.Length>0 && value.Genres.Except((await catalog.Genres(ct)).Select(g=>g.Id)).Any())return Results.BadRequest(new {message="Geçerli türleri seç."});
            store.Save("preference",Viewer(ctx).UserId.ToString(),"",value);return Results.NoContent();
        });
        group.MapGet("/entry/{id:int}", (int id,HttpContext ctx,MovieStore store)=>Results.Ok(new {entry=store.Get<MovieEntry>("entry",Viewer(ctx).UserId.ToString(),id.ToString())}));
        group.MapPut("/entry/{id:int}", async (int id,EntryRequest value,HttpContext ctx,MovieStore store,MovieCatalog catalog,CancellationToken ct)=>{
            if(id<=0 || value.State is not ("watchlist" or "watching" or "watched") || (value.Scores is not null && (!MovieRecommendations.ValidScores(value.Scores) || value.State!="watched")))
                return Results.BadRequest(new {message="İzleme durumunu ve 1–10 arasındaki yedi puanı kontrol et."});
            var film=store.Get<MovieDetails>("movie","",id.ToString()) ?? await catalog.Details(id,ct);
            store.Save("movie","",id.ToString(),film);
            store.Save("entry",Viewer(ctx).UserId.ToString(),id.ToString(),new MovieEntry(Viewer(ctx).UserId,id,value.State,value.Scores,DateTimeOffset.UtcNow));
            return Results.NoContent();
        });
        group.MapDelete("/entry/{id:int}", (int id,HttpContext ctx,MovieStore store)=>{store.Delete("entry",Viewer(ctx).UserId.ToString(),id.ToString());return Results.NoContent();});
        group.MapPut("/feedback/{id:int}", async (int id,FeedbackRequest value,HttpContext ctx,MovieStore store,MovieCatalog catalog,CancellationToken ct)=>{
            if(id<=0 || value.Choice is not ("interested" or "dismissed"))return Results.BadRequest();
            var film=store.Get<MovieDetails>("movie","",id.ToString()) ?? await catalog.Details(id,ct); store.Save("movie","",id.ToString(),film);
            store.Save("feedback",Viewer(ctx).UserId.ToString(),id.ToString(),new MovieFeedback(Viewer(ctx).UserId,id,value.Choice));return Results.NoContent();
        });
        group.MapDelete("/feedback/{id:int}", (int id,HttpContext ctx,MovieStore store)=>{store.Delete("feedback",Viewer(ctx).UserId.ToString(),id.ToString());return Results.NoContent();});
        group.MapGet("/profile/{nickname}", async (string nickname,HttpContext ctx,MovieStore store,SharedIdentity identity,CancellationToken ct)=>{
            var path="api/readers/"+Uri.EscapeDataString(nickname)+"/identity";
            var member=await identity.Get<NetworkMember>(path,SharedIdentity.Token(ctx),ct);
            var entries=Entries(store,member.Id);
            var current=await identity.Get<NetworkMember>(path,SharedIdentity.Token(ctx),ct);
            if(current.Id!=member.Id)return Results.NotFound();
            return Results.Ok(new {member.Nickname,avatarId=store.Avatar(member.Id),entries});
        });
        group.MapGet("/home", async (HttpContext ctx,MovieStore store,SharedIdentity identity,MovieCatalog catalog,CancellationToken ct)=>{
            var viewer=Viewer(ctx); var token=SharedIdentity.Token(ctx); var preference=store.Preferences(viewer.UserId);
            // Import a bounded candidate pool from selected genres plus general discovery.
            // Existing indexed films remain usable when the provider is temporarily unavailable.
            string? catalogNotice=null;
            using var discoveryBudget=CancellationTokenSource.CreateLinkedTokenSource(ct);
            discoveryBudget.CancelAfter(TimeSpan.FromSeconds(15));
            try{
                var genres=preference.Genres.Cast<int?>().Prepend(null);
                var ids=new HashSet<int>();
                foreach(var genre in genres){var page=await catalog.Browse(new("",1,null,genre,"popular"),discoveryBudget.Token); foreach(var m in page.Items.Take(6))ids.Add(m.Id);}
                await Parallel.ForEachAsync(ids.Where(id=>store.Get<MovieDetails>("movie","",id.ToString()) is null),new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=discoveryBudget.Token},async(id,token)=>{
                    store.Save("movie","",id.ToString(),await catalog.Details(id,token));
                });
            }catch(MovieProviderException e){catalogNotice=e.Message;}
            catch(HttpRequestException){catalogNotice="Film kataloğuna şu anda ulaşılamıyor.";}
            catch(OperationCanceledException) when(!ct.IsCancellationRequested){catalogNotice="Film kataloğu yanıt vermedi.";}
            var entries=store.All<MovieEntry>("entry"); var films=store.All<MovieDetails>("movie"); var map=films.ToDictionary(f=>f.Movie.Id);
            // Refresh visibility/follows immediately before composing personal activity.
            var network=await identity.Get<MovieNetwork>("api/readers/me/network-members",token,ct);
            var following=network.Following.ToDictionary(m=>m.Id); var followers=network.Followers.ToDictionary(m=>m.Id);
            object[] Activity(Dictionary<Guid,NetworkMember> people,bool reviews)=>entries.Where(e=>people.ContainsKey(e.UserId) && map.ContainsKey(e.MovieId) && (reviews ? e.Overall is not null : e.State=="watching"))
                .OrderByDescending(e=>e.UpdatedAtUtc).Take(12).Select(e=>(object)new {people[e.UserId].Nickname,avatarId=store.Avatar(e.UserId),movie=map[e.MovieId].Movie,e.Overall,e.UpdatedAtUtc}).ToArray();
            var suggestions=MovieRecommendations.Rank(films,entries,store.All<MovieFeedback>("feedback",viewer.UserId.ToString()),viewer.UserId,preference,followers.Keys.ToHashSet());
            return Results.Ok(new {preferences=preference,suggestions,watching=Activity(following,false),reviews=Activity(followers,true),catalogNotice});
        });
    }
    private static MovieViewer Viewer(HttpContext ctx)=>(MovieViewer)ctx.Items["viewer"]!;
    private static object[] Entries(MovieStore store,Guid user)=>store.All<MovieEntry>("entry",user.ToString()).OrderByDescending(e=>e.UpdatedAtUtc)
        .Select(e=>new {entry=new{e.MovieId,e.State,e.Scores,e.Overall,e.UpdatedAtUtc},movie=store.Get<MovieDetails>("movie","",e.MovieId.ToString())?.Movie}).Where(e=>e.movie is not null).Cast<object>().ToArray();
}
