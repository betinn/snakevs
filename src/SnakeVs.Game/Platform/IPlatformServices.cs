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

    /// <summary>Folder where the save file lives. Must be writable and private to the app.</summary>
    string DataDirectory { get; }

    SafeInsets GetSafeInsets();

    void Vibrate(int milliseconds);

    /// <summary>Returns true once for every system back gesture/button since the last call.</summary>
    bool ConsumeBackRequest();

    /// <summary>Leaves the game (Android: sends the task to the background).</summary>
    void ExitApp();
}

/// <summary>Start-up options, mostly used by the desktop build for automated captures.</summary>
public sealed class GameOptions
{
    /// <summary>When set, runs a scripted tour of the screens and saves PNG captures here, then exits.</summary>
    public string? TourDirectory { get; init; }

    /// <summary>When set, renders store art (icons and feature graphic) here, then exits.</summary>
    public string? StoreArtDirectory { get; init; }
}
