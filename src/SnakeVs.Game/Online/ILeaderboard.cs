using SnakeVs.Core;
using SnakeVs.Game.Localization;

namespace SnakeVs.Game.Online;

/// <summary>
/// Online ranking offered by the platform (web only for now; null elsewhere). Tasks complete between frames and the
/// scenes poll them, so nothing here may block. Failures surface as faulted tasks.
/// </summary>
public interface ILeaderboard
{
    /// <summary>
    /// Asks the player for a nickname with the platform's own text input, telling them the run of
    /// <paramref name="score"/> points made the top. Null when skipped.
    /// </summary>
    Task<string?> AskNameAsync(string? suggested, int score, Strings text);

    Task<ScoreEntry[]> GetTopAsync(GameMode mode, Difficulty difficulty, int take);

    Task<SubmitResult> SubmitAsync(ScoreSubmission submission);
}
