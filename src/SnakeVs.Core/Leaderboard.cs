using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SnakeVs.Core;

/// <summary>A finished run sent to the online ranking. <see cref="Steps"/> is the simulation step count of the run.</summary>
public sealed record ScoreSubmission(GameMode Mode, Difficulty Difficulty, string Name, int Score, int Apples, int Level, long Steps);

/// <summary>One line of a ranking.</summary>
public sealed record ScoreEntry(long Id, int Rank, string Name, int Score, int Apples, int Level, DateTime CreatedUtc);

/// <summary>Answer to a submission: the new entry and its position in its ranking.</summary>
public sealed record SubmitResult(long Id, int Rank, int Total);

/// <summary>
/// Rules of the online ranking. The server (src/SnakeVs.Api, Rust) has a port of NormalizeName/IsPlausible; both are
/// held to tests/fixtures/leaderboard.json (see LeaderboardFixtureTests). Change here, regenerate, fix the server.
/// </summary>
public static class Leaderboard
{
    public const int NameMaxLength = 12;

    /// <summary>
    /// Size of the top the game shows, and the one a run has to enter for the nickname box to open. The game always
    /// asks the server for this many.
    /// </summary>
    public const int TopSize = 5;

    /// <summary>Entries the server sends when a request does not say how many (part of the server contract).</summary>
    public const int DefaultTake = 10;
    public const int MaxTake = 50;

    /// <summary>Accented capitals the pixel font can draw (lower case is converted).</summary>
    private const string AccentedLetters = "ÁÀÂÃÉÊÍÓÔÕÚÜÇÑ";

    /// <summary>Ranked modes. Versus ends in a win or a loss, not a score worth ranking.</summary>
    public static bool IsRanked(GameMode mode) => mode is GameMode.Endless or GameMode.Levels or GameMode.Journey;

    /// <summary>
    /// Upper case, letters/digits/space/.-_ only (what the pixel font draws), single spaces, at most
    /// <see cref="NameMaxLength"/> characters. Returns null when nothing valid is left.
    /// </summary>
    public static string? NormalizeName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new StringBuilder(NameMaxLength);
        var pendingSpace = false;
        foreach (var ch in raw.Normalize(NormalizationForm.FormC))
        {
            var c = char.ToUpperInvariant(ch);
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (!IsNameChar(c)) continue;
            if (pendingSpace)
            {
                if (sb.Length + 1 >= NameMaxLength) break;
                sb.Append(' ');
                pendingSpace = false;
            }

            if (sb.Length >= NameMaxLength) break;
            sb.Append(c);
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    public static bool IsNameChar(char c) =>
        c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' || AccentedLetters.Contains(c);

    /// <summary>
    /// Cheap sanity check of a submitted run against the game rules (the server cannot replay the run, so this only
    /// rejects impossible numbers). Bonuses only spawn when an apple is eaten, so each apple brings at most one golden
    /// apple and one power-up, all doubled at best.
    /// </summary>
    public static bool IsPlausible(ScoreSubmission s)
    {
        if (!IsRanked(s.Mode) || !Enum.IsDefined(s.Difficulty)) return false;
        if (s.Score <= 0 || s.Apples < 0 || s.Level < 1 || s.Steps < 1) return false;

        const int boardCells = Rules.BoardWidth * Rules.BoardHeight;
        if (s.Apples > s.Steps) return false;

        long topLevel;
        Int128 levelBonus = 0;
        if (s.Mode == GameMode.Endless)
        {
            if (s.Apples > boardCells || s.Level != 1) return false;
            topLevel = Rules.EndlessSpeedLevel(s.Apples);
        }
        else if (s.Mode == GameMode.Levels)
        {
            // Levels: 8 apples per level, a run ends in the level it reached. Clearing level l pays LevelBonus * l.
            if (s.Level > s.Apples / Rules.ApplesPerLevel + 1) return false;
            topLevel = s.Level;
            levelBonus = (Int128)Rules.LevelBonus * (s.Level - 1) * s.Level / 2;
        }
        else
        {
            // Journey: Level is the stage reached. Every stage left behind took at least one apple and paid
            // JourneyStageBonus * its number. A stage has at most JourneyMaxApplesPerStage apples and one rival
            // snake, worth its bonus plus the golden apple it leaves behind (doubled at best).
            if (s.Level > (long)s.Apples + 1 || s.Apples > (long)s.Level * Rules.JourneyMaxApplesPerStage) return false;
            topLevel = s.Level;
            levelBonus = (Int128)Rules.JourneyStageBonus * (s.Level - 1) * s.Level / 2
                + (Int128)s.Level * 2 * (Rules.JourneyRivalBonus + Rules.GoldenScore);
        }

        // 128-bit math: huge (fake) inputs must not overflow. Same formula as src/SnakeVs.Api/src/rules.rs.
        var appleValue = Rules.AppleScore + (topLevel - 1) * Rules.AppleScorePerLevel;
        Int128 perApple = 2 * ((Int128)appleValue + Rules.GoldenScore + Rules.PowerUpScore);
        return s.Score <= (Int128)s.Apples * perApple + levelBonus;
    }
}

/// <summary>JSON contract of the ranking API, source generated so the trimmed web build needs no reflection.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ScoreSubmission))]
[JsonSerializable(typeof(ScoreEntry[]))]
[JsonSerializable(typeof(SubmitResult))]
public sealed partial class LeaderboardJson : JsonSerializerContext;
