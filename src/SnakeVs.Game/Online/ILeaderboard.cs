using SnakeVs.Core;
using SnakeVs.Game.Localization;

namespace SnakeVs.Game.Online;

/// <summary>
/// Online ranking offered by the platform (web only for now; null elsewhere). Tasks complete between frames and the
/// scenes poll them, so nothing here may block. Failures surface as faulted tasks.
/// </summary>
public interface ILeaderboard
{
    /// <summary>Asks the player for a name with the platform's own text input. Null when cancelled.</summary>
    Task<string?> AskNameAsync(string? suggested, Strings text);

    Task<ScoreEntry[]> GetTopAsync(GameMode mode, Difficulty difficulty, int take);

    Task<SubmitResult> SubmitAsync(ScoreSubmission submission);
}
