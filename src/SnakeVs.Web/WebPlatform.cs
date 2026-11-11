using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Platform;
using Microsoft.Xna.Platform.Audio;
using Microsoft.Xna.Platform.Graphics;
using Microsoft.Xna.Platform.Input;
using SnakeVs.Game;
using SnakeVs.Game.Input;
using SnakeVs.Game.Online;
using SnakeVs.Game.Platform;
using SnakeVs.Game.Storage;

namespace SnakeVs.Web;

/// <summary>
/// Browser host. wwwroot/js/snakevs.js drives the frame loop through <see cref="Tick"/> and answers the
/// synchronous calls below (storage, haptics, safe area, back button). Browser state is read once per frame.
/// </summary>
public sealed class WebPlatform(IJSInProcessRuntime js) : IPlatformServices
{
    /// <summary>Base address of the ranking API (meta "snakevs-api" in index.html), or null when it is turned off.</summary>
    private readonly string? _apiBase = js.Invoke<string?>("snakevs.apiBase");

    private SnakeGame? _game;
    private GraphicsDeviceManager? _graphics;
    private SafeInsets _insets = SafeInsets.None;
    private bool _back;
    private HttpLeaderboard? _leaderboard;

    /// <summary>Only drives window setup in the shared code; the page is always a full window canvas.</summary>
    public bool IsMobile => false;

    /// <summary>
    /// Not polled: snakevs.js queues every mouse, touch and pen event (a quick tap can start and end inside one
    /// frame) and <see cref="Tick"/> hands them to the input manager. The engine's touch panel never sees touches.
    /// </summary>
    public bool HasMouse => false;

    public float PointerScale { get; private set; } = 1f;

    public ISaveStorage Storage { get; } = new LocalStorage(js);

    public ILeaderboard? Leaderboard => _leaderboard ??= _apiBase is null
        ? null
        : new HttpLeaderboard(new HttpClient { BaseAddress = new Uri(_apiBase) }, js);

    [JSInvokable]
    public void Tick()
    {
        // [canvas width, canvas height, pixel ratio x1000, inset left, top, right, bottom, back pressed,
        //  then (phase, pointer id, x, y) for every pointer event since the last frame, in CSS pixels]
        var f = js.Invoke<int[]>("snakevs.frameState");
        PointerScale = MathF.Max(0.1f, f[2] / 1000f);
        _insets = new SafeInsets(f[3], f[4], f[5], f[6]);
        _back |= f[7] != 0;

        if (_game is null)
        {
            RegisterEngine();
            _game = new SnakeGame(this, new GameOptions { Language = js.Invoke<string>("snakevs.language") });
            _game.Run();
            _graphics = (GraphicsDeviceManager?)_game.Services.GetService(typeof(IGraphicsDeviceManager));
        }

        for (var i = 8; i + 3 < f.Length; i += 4)
        {
            _game.Input.QueuePointer((PointerPhase)f[i], f[i + 1], new Vector2(f[i + 2], f[i + 3]));
        }

        FitBackBuffer(f[0], f[1]);
        _game.Tick();
    }

    /// <summary>Called when the tab is hidden or the page is being closed.</summary>
    [JSInvokable]
    public void Suspend() => _game?.Suspend();

    public SafeInsets GetSafeInsets() => _insets;

    public void Vibrate(int milliseconds) => js.InvokeVoid("snakevs.vibrate", milliseconds);

    public bool ConsumeBackRequest()
    {
        var back = _back;
        _back = false;
        return back;
    }

    public void ExitApp() => js.InvokeVoid("snakevs.exit");

    /// <summary>Plugs the browser back ends into KNI explicitly (no reflection lookup, safe with trimming).</summary>
    private static void RegisterEngine()
    {
        GameFactory.RegisterGameFactory(new ConcreteGameFactory());
        GraphicsFactory.RegisterGraphicsFactory(new ConcreteGraphicsFactory());
        InputFactory.RegisterInputFactory(new ConcreteInputFactory());
        AudioFactory.RegisterAudioFactory(new ConcreteAudioFactory());
    }

    /// <summary>Keeps the back buffer at the canvas size, which snakevs.js sets to CSS size x device pixel ratio.</summary>
    private void FitBackBuffer(int width, int height)
    {
        if (_graphics is null || width <= 0 || height <= 0) return;
        if (_graphics.PreferredBackBufferWidth == width && _graphics.PreferredBackBufferHeight == height) return;
        _graphics.PreferredBackBufferWidth = width;
        _graphics.PreferredBackBufferHeight = height;
        _graphics.ApplyChanges();
    }

    /// <summary>Save kept in the browser's localStorage (per site, survives reloads).</summary>
    private sealed class LocalStorage(IJSInProcessRuntime js) : ISaveStorage
    {
        public string? Read()
        {
            try
            {
                return js.Invoke<string?>("snakevs.storageRead");
            }
            catch (JSException)
            {
                return null;
            }
        }

        public void Write(string text)
        {
            try
            {
                js.InvokeVoid("snakevs.storageWrite", text);
            }
            catch (JSException)
            {
                // Private mode or quota exceeded: records stay in memory.
            }
        }
    }
}
