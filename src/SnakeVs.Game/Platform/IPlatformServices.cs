using SnakeVs.Game.Online;
using SnakeVs.Game.Storage;

namespace SnakeVs.Game.Platform;

/// <summary>Screen area hidden by notches, rounded corners or system bars, in back buffer pixels.</summary>
public readonly record struct SafeInsets(int Left, int Top, int Right, int Bottom)
{
    public static readonly SafeInsets None = new(0, 0, 0, 0);
}

/// <summary>What the shared game code needs from the host platform.</summary>
public interface IPlatformServices
{
    bool IsMobile { get; }

    /// <summary>True when the mouse state should be polled every frame (desktop). Android only uses touch; the web queues mouse events itself.</summary>
    bool HasMouse { get; }

    /// <summary>Back buffer pixels per touch/mouse coordinate unit. 1 everywhere except the web (device pixel ratio).</summary>
    float PointerScale { get; }

    /// <summary>Where records and settings are kept (private app folder, browser storage...).</summary>
    ISaveStorage Storage { get; }

    /// <summary>Online ranking, or null when the platform has none (Android, desktop).</summary>
    ILeaderboard? Leaderboard { get; }

    SafeInsets GetSafeInsets();

    void Vibrate(int milliseconds);

    /// <summary>Returns true once for every system back gesture/button since the last call.</summary>
    bool ConsumeBackRequest();

    /// <summary>Leaves the game (Android: sends the task to the background; desktop: closes; web: leaves the page).</summary>
    void ExitApp();
}

/// <summary>Start-up options, mostly used by the desktop build for automated captures.</summary>
public sealed class GameOptions
{
    /// <summary>When set, runs a scripted tour of the screens and saves PNG captures here, then exits.</summary>
    public string? TourDirectory { get; init; }

    /// <summary>When set, renders store art (icons and feature graphic) here, then exits.</summary>
    public string? StoreArtDirectory { get; init; }

    /// <summary>
    /// Development aid: opens the journey directly at this stage instead of the menu. With <see cref="DemoAutopilot"/>
    /// the CPU plays. Used to look at a late stage without playing up to it.
    /// </summary>
    public int? DemoStage { get; init; }

    public bool DemoAutopilot { get; init; }

    /// <summary>UI language tag (e.g. "pt-BR"). When null, the current UI culture decides.</summary>
    public string? Language { get; init; }
}
