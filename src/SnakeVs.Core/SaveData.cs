using System.Globalization;
using System.Text;

namespace SnakeVs.Core;

public enum ControlScheme
{
    Swipe,
    Buttons,
}

/// <summary>Settings and records. Stored as simple "key=value" lines (no reflection, safe for trimming).</summary>
public sealed class SaveData
{
    private readonly Dictionary<string, int> _best = new(StringComparer.Ordinal);

    public Difficulty Difficulty { get; set; } = Difficulty.Normal;
    public bool SoundOn { get; set; } = true;
    public bool VibrationOn { get; set; } = true;
    public ControlScheme Controls { get; set; } = ControlScheme.Swipe;
    public int VersusWins { get; set; }
    public int VersusLosses { get; set; }
    public int VersusDraws { get; set; }
    public int GamesPlayed { get; set; }

    public int GetBest(GameMode mode, Difficulty difficulty) =>
        _best.TryGetValue(Key(mode, difficulty), out var v) ? v : 0;

    /// <summary>Records a score. Returns true when it beats the previous best.</summary>
    public bool SubmitScore(GameMode mode, Difficulty difficulty, int score)
    {
        if (score <= 0) return false;
        var key = Key(mode, difficulty);
        if (_best.TryGetValue(key, out var old) && old >= score) return false;
        _best[key] = score;
        return true;
    }

    public void RecordVersus(MatchOutcome outcome)
    {
        switch (outcome)
        {
            case MatchOutcome.PlayerWon: VersusWins++; break;
            case MatchOutcome.RivalWon: VersusLosses++; break;
            case MatchOutcome.Draw: VersusDraws++; break;
        }
    }

    public string Serialize()
    {
        var sb = new StringBuilder();
        sb.Append("version=1\n");
        sb.Append("difficulty=").Append((int)Difficulty).Append('\n');
        sb.Append("sound=").Append(SoundOn ? 1 : 0).Append('\n');
        sb.Append("vibration=").Append(VibrationOn ? 1 : 0).Append('\n');
        sb.Append("controls=").Append((int)Controls).Append('\n');
        sb.Append("vs.wins=").Append(VersusWins).Append('\n');
        sb.Append("vs.losses=").Append(VersusLosses).Append('\n');
        sb.Append("vs.draws=").Append(VersusDraws).Append('\n');
        sb.Append("games=").Append(GamesPlayed).Append('\n');
        foreach (var (key, value) in _best.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append("best.").Append(key).Append('=').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Parses saved text. Unknown or broken lines are ignored so a damaged file never crashes the game.</summary>
    public static SaveData Parse(string? text)
    {
        var data = new SaveData();
        if (string.IsNullOrWhiteSpace(text)) return data;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq];
            if (!int.TryParse(line[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;

            switch (key)
            {
                case "difficulty" when Enum.IsDefined(typeof(Difficulty), value):
                    data.Difficulty = (Difficulty)value;
                    break;
                case "sound":
                    data.SoundOn = value != 0;
                    break;
                case "vibration":
                    data.VibrationOn = value != 0;
                    break;
                case "controls" when Enum.IsDefined(typeof(ControlScheme), value):
                    data.Controls = (ControlScheme)value;
                    break;
                case "vs.wins":
                    data.VersusWins = Math.Max(0, value);
                    break;
                case "vs.losses":
                    data.VersusLosses = Math.Max(0, value);
                    break;
                case "vs.draws":
                    data.VersusDraws = Math.Max(0, value);
                    break;
                case "games":
                    data.GamesPlayed = Math.Max(0, value);
                    break;
                default:
                    if (key.StartsWith("best.", StringComparison.Ordinal) && value > 0)
                    {
                        // Before the endless mode existed, the level based game was called "classic".
                        const string legacy = "classic.";
                        var name = key[5..];
                        if (name.StartsWith(legacy, StringComparison.Ordinal)) name = "levels." + name[legacy.Length..];
                        data._best[name] = value;
                    }

                    break;
            }
        }

        return data;
    }

    private static string Key(GameMode mode, Difficulty difficulty) =>
        $"{mode.ToString().ToLowerInvariant()}.{difficulty.ToString().ToLowerInvariant()}";
}
