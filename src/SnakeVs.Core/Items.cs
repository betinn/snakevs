namespace SnakeVs.Core;

/// <summary>A pickup that disappears after a while (golden apple or power-up).</summary>
public sealed class TimedItem
{
    public TimedItem(Int2 cell, double lifetimeMs, PowerUpKind kind = default)
    {
        Cell = cell;
        LifetimeMs = lifetimeMs;
        RemainingMs = lifetimeMs;
        Kind = kind;
    }

    public Int2 Cell { get; }
    public PowerUpKind Kind { get; }
    public double LifetimeMs { get; }
    public double RemainingMs { get; internal set; }

    /// <summary>1 when just spawned, 0 when about to vanish.</summary>
    public double Fraction => LifetimeMs <= 0 ? 0 : Math.Clamp(RemainingMs / LifetimeMs, 0, 1);
}

public enum SessionState
{
    Playing,
    LevelCleared,
    GameOver,
    MatchOver,
}

public enum MatchOutcome
{
    None,
    PlayerWon,
    RivalWon,
    Draw,
}

public readonly record struct SpawnPoint(Int2 Head, Direction Heading);
