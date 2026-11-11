using System.Net.Http.Json;
using Microsoft.JSInterop;
using SnakeVs.Core;
using SnakeVs.Game.Localization;
using SnakeVs.Game.Online;

namespace SnakeVs.Web;

/// <summary>Online ranking through the /api/scores server (src/SnakeVs.Api), with an HTML name dialog (native keyboard on phones).</summary>
internal sealed class HttpLeaderboard(HttpClient http, IJSRuntime js) : ILeaderboard
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    public async Task<string?> AskNameAsync(string? suggested, Strings text) =>
        await js.InvokeAsync<string?>("snakevs.askName", suggested ?? string.Empty, text.NamePrompt, text.NameSave, text.NameCancel, Leaderboard.NameMaxLength);

    public async Task<ScoreEntry[]> GetTopAsync(GameMode mode, Difficulty difficulty, int take)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var entries = await http.GetFromJsonAsync($"api/scores/{mode}/{difficulty}?take={take}", LeaderboardJson.Default.ScoreEntryArray, cts.Token);
        return entries ?? [];
    }

    public async Task<SubmitResult> SubmitAsync(ScoreSubmission submission)
    {
        using var cts = new CancellationTokenSource(Timeout);
        using var response = await http.PostAsJsonAsync("api/scores", submission, LeaderboardJson.Default.ScoreSubmission, cts.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(LeaderboardJson.Default.SubmitResult, cts.Token)
            ?? throw new InvalidOperationException("Empty answer from the ranking.");
    }
}
