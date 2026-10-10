namespace SnakeVs.Core;

public enum GameMode
{
    Classic,
    Versus,
}

public enum Difficulty
{
    Easy,
    Normal,
    Hard,
}

public enum PowerUpKind
{
    /// <summary>Slows the whole game down for a while.</summary>
    Slow,

    /// <summary>Lets the snake pass through obstacles, bodies and borders.</summary>
    Ghost,

    /// <summary>Doubles the points earned by the snake that picked it.</summary>
    Double,

    /// <summary>Cuts a few segments off the tail.</summary>
    Shrink,

    /// <summary>Absorbs one fatal collision.</summary>
    Shield,
}

/// <summary>All the tuning numbers of the game in one place.</summary>
public static class Rules
{
    public const int BoardWidth = 20;
    public const int BoardHeight = 30;
    public const int StartLength = 4;

    public const int ApplesPerLevel = 8;
    public const int VersusTargetApples = 15;

    public const double LevelSpeedupMs = 7;
    public const double MinStepMs = 55;
    public const double MaxFrameMs = 250;

    public const double SlowFactor = 1.75;
    public const double SlowDurationMs = 7000;
    public const double GhostDurationMs = 6000;
    public const double DoubleDurationMs = 10000;
    public const double ShieldGhostMs = 1500;
    public const int MaxShields = 1;
    public const int ShrinkAmount = 4;
    public const int MinLength = 3;

    public const double GoldenLifetimeMs = 6500;
    public const double PowerUpLifetimeMs = 9000;
    public const double GoldenChance = 0.12;
    public const double PowerUpChance = 0.28;

    public const int AppleScore = 10;
    public const int AppleScorePerLevel = 2;
    public const int GoldenScore = 50;
    public const int PowerUpScore = 5;
    public const int LevelBonus = 100;

    public static double BaseStepMs(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 170,
        Difficulty.Normal => 130,
        Difficulty.Hard => 95,
        _ => 130,
    };

    public static double StepMs(GameMode mode, Difficulty difficulty, int level)
    {
        var baseMs = BaseStepMs(difficulty);
        if (mode == GameMode.Versus) return baseMs;
        return Math.Max(MinStepMs, baseMs - (level - 1) * LevelSpeedupMs);
    }

    public static int AppleValue(int level) => AppleScore + (level - 1) * AppleScorePerLevel;

    /// <summary>Chance per decision that a CPU snake picks a random move instead of the best one.</summary>
    public static double AiMistakeChance(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 0.16,
        Difficulty.Normal => 0.05,
        _ => 0.0,
    };
}
