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
}

/// <summary>Something that happened during a step. The presentation layer turns these into sound, particles and haptics.</summary>
/// <param name="Type">What happened.</param>
/// <param name="Cell">Where it happened.</param>
/// <param name="SnakeId">Snake involved, or -1.</param>
/// <param name="Value">Points earned, power-up kind, etc.</param>
public readonly record struct GameEvent(GameEventType Type, Int2 Cell, int SnakeId = -1, int Value = 0);
