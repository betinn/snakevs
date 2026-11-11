using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using SnakeVs.Core;

namespace SnakeVs.Server.Tests;

/// <summary>Runs the real server in memory against a throw-away SQLite file.</summary>
public sealed class ScoresApiTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"snakevs-test-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _http;

    public ScoresApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Leaderboard:DatabasePath", _dbPath);
            b.UseSetting("Leaderboard:SubmitsPerMinute", "1000");
            b.UseSetting("Leaderboard:AdminKey", "secret");
        });
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }) File.Delete(f);
    }

    private static ScoreSubmission Run(string name, int score, GameMode mode = GameMode.Endless, Difficulty difficulty = Difficulty.Normal) =>
        new(mode, difficulty, name, score, Apples: 20, Level: 1, Steps: 2000);

    private async Task<SubmitResult> Post(ScoreSubmission s)
    {
        var response = await _http.PostAsJsonAsync("/api/scores", s, LeaderboardJson.Default.ScoreSubmission);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync(LeaderboardJson.Default.SubmitResult))!;
    }

    private async Task<ScoreEntry[]> Top(string board) =>
        (await _http.GetFromJsonAsync($"/api/scores/{board}", LeaderboardJson.Default.ScoreEntryArray))!;

    [Fact]
    public async Task Submit_ReturnsRank_AndTopIsOrdered()
    {
        Assert.Equal(1, (await Post(Run("ana", 100))).Rank);
        Assert.Equal(1, (await Post(Run("beto", 300))).Rank);
        var third = await Post(Run("caio", 100));
        Assert.Equal(3, third.Rank); // ties go to whoever got there first
        Assert.Equal(3, third.Total);

        var top = await Top("endless/normal");
        Assert.Equal(["BETO", "ANA", "CAIO"], top.Select(e => e.Name));
        Assert.Equal([1, 2, 3], top.Select(e => e.Rank));
    }

    [Fact]
    public async Task Boards_AreSeparatedByModeAndDifficulty()
    {
        await Post(Run("ana", 100, GameMode.Endless, Difficulty.Easy));
        await Post(Run("beto", 100, GameMode.Endless, Difficulty.Hard));

        Assert.Single(await Top("endless/easy"));
        Assert.Single(await Top("ENDLESS/HARD"));
        Assert.Empty(await Top("levels/easy"));
    }

    [Fact]
    public async Task Take_IsClamped()
    {
        for (var i = 0; i < 12; i++) await Post(Run("p" + i, 10 + i));
        Assert.Equal(Leaderboard.DefaultTake, (await Top("endless/normal")).Length);
        Assert.Equal(3, (await Top("endless/normal?take=3")).Length);
        Assert.Equal(12, (await Top("endless/normal?take=999")).Length);
    }

    [Theory]
    [InlineData("versus/normal")]
    [InlineData("endless/impossible")]
    [InlineData("2/1")]
    public async Task UnknownBoards_Are404(string board)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/scores/{board}")).StatusCode);
    }

    [Fact]
    public async Task InvalidSubmissions_AreRejected()
    {
        async Task Rejected(ScoreSubmission s) =>
            Assert.Equal(HttpStatusCode.BadRequest, (await _http.PostAsJsonAsync("/api/scores", s, LeaderboardJson.Default.ScoreSubmission)).StatusCode);

        await Rejected(Run("@@@", 100));
        await Rejected(Run("", 100));
        await Rejected(Run("ana", 9_999_999));
        await Rejected(Run("ana", 10, GameMode.Versus));
        Assert.Empty(await Top("endless/normal"));
    }

    [Fact]
    public async Task StoredNames_AreNormalized()
    {
        await Post(Run("  <b>zé</b>   lagarto ", 50));
        Assert.Equal("BZÉBLAGARTO", (await Top("endless/normal"))[0].Name.Replace(" ", ""));
    }

    [Fact]
    public async Task Delete_NeedsTheAdminKey()
    {
        var id = (await Post(Run("ana", 100))).Id;

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.DeleteAsync($"/api/scores/{id}")).StatusCode);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/scores/{id}");
        request.Headers.Add("X-Admin-Key", "secret");
        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(request)).StatusCode);
        Assert.Empty(await Top("endless/normal"));
    }

    [Fact]
    public async Task Submissions_AreRateLimited()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Leaderboard:DatabasePath", _dbPath);
            b.UseSetting("Leaderboard:SubmitsPerMinute", "2");
        });
        using var http = factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add((await http.PostAsJsonAsync("/api/scores", Run("ana", 10), LeaderboardJson.Default.ScoreSubmission)).StatusCode);
        }

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests], codes);
    }

    [Fact]
    public async Task ServesTheGame()
    {
        var response = await _http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("theCanvas", await response.Content.ReadAsStringAsync());
    }
}
