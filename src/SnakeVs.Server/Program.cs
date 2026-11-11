using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using SnakeVs.Core;
using SnakeVs.Server;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration.GetSection("Leaderboard");

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, LeaderboardJson.Default);
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddSingleton(_ =>
{
    var path = config["DatabasePath"] ?? Path.Combine("App_Data", "scores.db");
    return new ScoreStore(Path.Combine(builder.Environment.ContentRootPath, path));
});

// Behind nginx/IIS on the same machine the client address comes in X-Forwarded-For (only loopback proxies are trusted).
builder.Services.Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("submit", http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("SubmitsPerMinute", 6), Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("read", http => RateLimitPartition.GetFixedWindowLimiter(
        ClientKey(http),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = config.GetValue("ReadsPerMinute", 120), Window = TimeSpan.FromMinutes(1) }));
});

// Only needed when the game is hosted somewhere else (static host) and calls this API across origins.
var allowedOrigins = config.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).WithMethods("GET", "POST").WithHeaders("Content-Type")));

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.Use((http, next) =>
{
    // "/" is the game. Rewritten before routing so the static asset endpoint of index.html serves it.
    if (http.Request.Path == "/") http.Request.Path = "/index.html";
    return next(http);
});
app.UseRouting();
app.UseCors();
app.UseRateLimiter();

// The game itself (SnakeVs.Web): index.html, the .NET runtime and assemblies, precompressed (br/gzip),
// fingerprinted files cached for a year, everything else revalidated.
app.MapStaticAssets();

var api = app.MapGroup("/api/scores");

api.MapGet("/{mode}/{difficulty}", async (string mode, string difficulty, int? take, ScoreStore store, CancellationToken ct) =>
{
    if (!TryParseBoard(mode, difficulty, out var m, out var d)) return Results.NotFound();
    var count = Math.Clamp(take ?? Leaderboard.DefaultTake, 1, Leaderboard.MaxTake);
    return Results.Ok(await store.TopAsync(m, d, count, ct));
}).RequireRateLimiting("read");

api.MapPost("/", async (ScoreSubmission submission, ScoreStore store, CancellationToken ct) =>
{
    var name = Leaderboard.NormalizeName(submission.Name);
    if (name is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Invalid name."] });
    if (!Leaderboard.IsPlausible(submission)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["score"] = ["Invalid score."] });

    var result = await store.AddAsync(submission, name, ct);
    return Results.Created($"/api/scores/{submission.Mode}/{submission.Difficulty}", result);
}).RequireRateLimiting("submit");

// Moderation: DELETE /api/scores/{id} with header "X-Admin-Key". Disabled unless Leaderboard:AdminKey is set.
api.MapDelete("/{id:long}", async (long id, HttpRequest request, ScoreStore store, CancellationToken ct) =>
{
    var adminKey = config["AdminKey"];
    if (string.IsNullOrEmpty(adminKey)) return Results.NotFound();
    var given = request.Headers["X-Admin-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(adminKey))) return Results.Unauthorized();
    return await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
});

app.Run();

static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static bool TryParseBoard(string mode, string difficulty, out GameMode m, out Difficulty d)
{
    d = default;
    return Enum.TryParse(mode, ignoreCase: true, out m) && Leaderboard.IsRanked(m)
        && Enum.TryParse(difficulty, ignoreCase: true, out d) && Enum.IsDefined(d)
        && !int.TryParse(mode, out _) && !int.TryParse(difficulty, out _);
}

/// <summary>Entry point type, visible to the integration tests.</summary>
public partial class Program;
