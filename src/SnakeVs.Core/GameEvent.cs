namespace SnakeVs.Core;

public enum GameEventType
{
    AteApple,
    AteGolden,
    GoldenSpawned,
    GoldenExpired,
    PowerUpSpawned,
    PowerUpExpired,
    PowerUpCollected,
    ShieldUsed,
    SnakeDied,
    LevelCleared,
    GameOver,
    MatchOver,

    // Journey mode.
    /// <summary>The stage's apples are done and its portal opened. Cell: middle of the portal.</summary>
    GateOpened,

    /// <summary>The player went through the portal. Value: bonus earned for the stage left behind.</summary>
    StageEntered,

    /// <summary>A hazard cut the tail. Cell: where it was cut. Value: segments lost.</summary>
    SnakeCut,
    LaserFired,
    MeteorImpact,

    /// <summary>A bug or the rival snake died. SnakeId is the rival's id for the rival, -1 for a bug. Value: points earned.</summary>
    EnemyDied,
    AppleStolen,
    Teleported,
    SpecterStunned,
}

/// <summary>Something that happened during a step. The presentation layer turns these into sound, particles and haptics.</summary>
/// <param name="Type">What happened.</param>
/// <param name="Cell">Where it happened.</param>
/// <param name="SnakeId">Snake involved, or -1.</param>
/// <param name="Value">Points earned, power-up kind, etc.</param>
public readonly record struct GameEvent(GameEventType Type, Int2 Cell, int SnakeId = -1, int Value = 0);
