namespace SnakeVs.Core;

public enum GameMode
{
    /// <summary>Level after level: 8 apples clear a level and the next one brings new obstacles.</summary>
    Levels,

    /// <summary>The original snake: one open board, no end, the snake just keeps growing and the game keeps speeding up.</summary>
    Endless,

    Versus,

    /// <summary>One endless run through stages stacked on top of each other: clear a stage, a portal opens, the map scrolls on. No stops.</summary>
    Journey,
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
    public const int EndlessApplesPerSpeedStep = 5;

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

    /// <summary>
    /// Columns of a journey stage. Smaller than the classic board: the room is crossed in a couple of seconds and the
    /// cells are drawn bigger, so hazards are easier to read.
    /// </summary>
    public const int JourneyStageWidth = 16;

    /// <summary>Rows of a journey stage, counting the wall row at its top where the portal opens.</summary>
    public const int JourneyStageHeight = 24;
    public const int JourneyGateWidth = 4;
    public const int JourneyMaxApplesPerStage = 6;
    public const int JourneyStageBonus = 50;
    public const int JourneyRivalBonus = 50;

    /// <summary>The rival snake of a stage stops growing here: it is there to get in the way, not to fill the room.</summary>
    public const int JourneyRivalMaxLength = 8;
    public const double JourneySpeedupMs = 2;
    public const double JourneyMinStepMs = 80;

    /// <summary>
    /// Chance per step that the rival snake of a journey stage does not notice the player's body. That is how it can
    /// be beaten: get between it and the apple and, sooner or later, it runs into you.
    /// </summary>
    public static double JourneyRivalBlindChance(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 0.5,
        Difficulty.Normal => 0.3,
        _ => 0.15,
    };

    /// <summary>Steps a specter stays harmless after the player eats an apple.</summary>
    public const int SpecterStunTicks = 14;

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

    public static double JourneyStepMs(Difficulty difficulty, int stage) =>
        Math.Max(Math.Min(JourneyMinStepMs, BaseStepMs(difficulty)), BaseStepMs(difficulty) - (stage - 1) * JourneySpeedupMs);

    /// <summary>Apples that open the portal of a journey stage (stage numbers start at 1).</summary>
    /// <remarks>
    /// The introduction is quick on purpose: two apples while the first elements show up, three for the rest of it.
    /// The mixes start at four and go up one apple every eight stages.
    /// </remarks>
    public static int JourneyAppleGoal(int stage) => stage switch
    {
        <= 1 => 1,
        <= 5 => 2,
        <= 14 => 3,
        _ => Math.Min(JourneyMaxApplesPerStage, 4 + (stage - 15) / 8),
    };

    public static int AppleValue(int level) => AppleScore + (level - 1) * AppleScorePerLevel;

    /// <summary>Speed tier of an endless run: starts at 1 and goes up every few apples.</summary>
    public static int EndlessSpeedLevel(int applesEaten) => 1 + applesEaten / EndlessApplesPerSpeedStep;

    /// <summary>Chance per decision that a CPU snake picks a random move instead of the best one.</summary>
    public static double AiMistakeChance(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => 0.16,
        Difficulty.Normal => 0.05,
        _ => 0.0,
    };
}
