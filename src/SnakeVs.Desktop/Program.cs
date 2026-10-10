using SnakeVs.Game;
using SnakeVs.Game.Platform;

namespace SnakeVs.Desktop;

public static class Program
{
    /// <summary>
    /// Usage:
    ///   SnakeVs                      play
    ///   SnakeVs --tour &lt;dir&gt;        capture every screen at several resolutions, then exit
    ///   SnakeVs --store-art &lt;dir&gt;   render launcher icons and store graphics, then exit
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        string? tour = null;
        string? art = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--tour") tour = Path.GetFullPath(args[i + 1]);
            if (args[i] == "--store-art") art = Path.GetFullPath(args[i + 1]);
        }

        // Automated runs use a throw-away save folder so they never touch real records.
        var dataDir = tour is not null || art is not null
            ? Path.Combine(Path.GetTempPath(), "SnakeVs-automation-" + Environment.ProcessId)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnakeVs");

        var platform = new DesktopPlatform(dataDir);
        using var game = new SnakeGame(platform, new GameOptions { TourDirectory = tour, StoreArtDirectory = art });
        platform.Game = game;
        game.Run();
        return 0;
    }
}

internal sealed class DesktopPlatform(string dataDirectory) : IPlatformServices
{
    public SnakeGame? Game { get; set; }

    public bool IsMobile => false;

    public string DataDirectory => dataDirectory;

    public SafeInsets GetSafeInsets() => SafeInsets.None;

    public void Vibrate(int milliseconds)
    {
    }

    public bool ConsumeBackRequest() => false;

    public void ExitApp() => Game?.Exit();
}
