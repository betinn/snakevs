using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.Input;
using SnakeVs.Game.Localization;
using SnakeVs.Game.Platform;
using SnakeVs.Game.Scenes;
using SnakeVs.Game.Storage;

namespace SnakeVs.Game;

/// <summary>
/// Game host shared by every platform. Renders a resolution independent "virtual" screen that always contains
/// a 720x1280 design box and grows along the longer axis, so there are no black bars on any aspect ratio.
/// </summary>
public sealed class SnakeGame : Microsoft.Xna.Framework.Game
{
    public const float DesignWidth = 720f;
    public const float DesignHeight = 1280f;
    public const string VersionName = "1.0.0";

    private const float FadeSpeed = 7f;

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameOptions _options;
    private Scene? _scene;
    private Scene? _pending;
    private float _fade;
    private bool _fadingOut;
    private Point _lastBackBuffer;
    private SafeInsets _lastInsets;
    private AutoTour? _tour;

    public SnakeGame(IPlatformServices platform, GameOptions? options = null)
    {
        Platform = platform;
        _options = options ?? new GameOptions();

        _graphics = new GraphicsDeviceManager(this)
        {
            SynchronizeWithVerticalRetrace = true,
        };

        if (platform.IsMobile)
        {
            _graphics.IsFullScreen = true;
            _graphics.SupportedOrientations = DisplayOrientation.Portrait;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = 540;
            _graphics.PreferredBackBufferHeight = 960;
            Window.AllowUserResizing = true;
            Window.Title = "Snake VS";
        }

        IsMouseVisible = !platform.IsMobile;
        IsFixedTimeStep = false;

        Store = new SaveStore(platform.DataDirectory);
        Save = Store.Load();
        Text = Strings.ForCulture(CultureInfo.CurrentUICulture);
    }

    public IPlatformServices Platform { get; }
    public SaveStore Store { get; }
    public SaveData Save { get; }
    public Strings Text { get; }
    public PixelFont Font { get; } = new();
    public InputManager Input { get; } = new();
    public Canvas Canvas { get; private set; } = null!;
    public SoundBank Sound { get; private set; } = null!;

    /// <summary>Size of the virtual screen in design units.</summary>
    public Vector2 ScreenSize { get; private set; } = new(DesignWidth, DesignHeight);

    /// <summary>Back buffer pixels per design unit.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>Part of the virtual screen not covered by notches or system bars.</summary>
    public RectF SafeArea { get; private set; } = new(0, 0, DesignWidth, DesignHeight);

    /// <summary>Seconds since start, for animations.</summary>
    public float Time { get; private set; }

    public Scene? CurrentScene => _scene;

    public void SetScene(Scene scene, bool instant = false)
    {
        if (instant || _scene is null)
        {
            SwapScene(scene);
            _fade = instant ? 0f : 1f;
            _fadingOut = false;
            return;
        }

        _pending = scene;
        _fadingOut = true;
    }

    public void PlaySound(Sfx sfx, float volume = 1f, float pitch = 0f) => Sound.Play(sfx, volume, pitch);

    public void Vibrate(int milliseconds)
    {
        if (Save.VibrationOn) Platform.Vibrate(milliseconds);
    }

    public void Persist() => Store.Save(Save);

    public void ApplySettings() => Sound.Enabled = Save.SoundOn;

    public void Quit()
    {
        Persist();
        if (Platform.IsMobile) Platform.ExitApp();
        else Exit();
    }

    protected override void Initialize()
    {
        base.Initialize();
        Window.ClientSizeChanged += (_, _) => _lastBackBuffer = Point.Zero;
    }

    protected override void LoadContent()
    {
        Canvas = new Canvas(GraphicsDevice);
        Sound = new SoundBank();
        ApplySettings();
        UpdateMetrics(force: true);

        if (_options.StoreArtDirectory is { } artDir)
        {
            StoreArt.RenderAll(this, artDir);
            Exit();
            return;
        }

        SetScene(new MenuScene(this), instant: true);
        if (_options.TourDirectory is { } tourDir) _tour = new AutoTour(this, tourDir);
    }

    protected override void OnDeactivated(object sender, EventArgs args)
    {
        base.OnDeactivated(sender, args);
        _scene?.OnFocusLost();
        Persist();
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        Persist();
        base.OnExiting(sender, args);
    }

    protected override void Update(GameTime gameTime)
    {
        var dt = MathHelper.Clamp((float)gameTime.ElapsedGameTime.TotalSeconds, 0f, 0.1f);
        Time += dt;
        UpdateMetrics(force: false);

        Input.Update(gameTime.TotalGameTime.TotalSeconds, Scale, Platform.ConsumeBackRequest(), !Platform.IsMobile);

        if (_fadingOut)
        {
            Input.Consume();
            _fade += dt * FadeSpeed;
            if (_fade >= 1f && _pending is not null)
            {
                SwapScene(_pending);
                _pending = null;
                _fade = 1f;
                _fadingOut = false;
            }
        }
        else if (_fade > 0f)
        {
            _fade = MathF.Max(0f, _fade - dt * FadeSpeed);
        }

        _scene?.Update(dt);
        _tour?.Update(dt);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(Palette.Background);
        Canvas.Begin(Matrix.CreateScale(Scale));
        _scene?.Draw(Canvas);
        if (_fade > 0f) Canvas.FillRect(0, 0, ScreenSize.X, ScreenSize.Y, Palette.Background * _fade);
        Canvas.End();
        base.Draw(gameTime);
    }

    /// <summary>Renders the current scene at an arbitrary resolution and saves it as PNG (store screenshots, tests).</summary>
    public void CaptureScene(string path, int width, int height)
    {
        if (_scene is null) return;
        var saved = (ScreenSize, Scale, SafeArea);
        ApplyMetrics(width, height, SafeInsets.None);
        _scene.Layout();

        using (var target = new RenderTarget2D(GraphicsDevice, width, height))
        {
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(Palette.Background);
            Canvas.Begin(Matrix.CreateScale(Scale));
            _scene.Draw(Canvas);
            Canvas.End();
            GraphicsDevice.SetRenderTarget(null);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var stream = File.Create(path);
            target.SaveAsPng(stream, width, height);
        }

        (ScreenSize, Scale, SafeArea) = saved;
        _scene.Layout();
    }

    /// <summary>Points the metrics at an arbitrary target size (used by store art rendering).</summary>
    internal void ApplyMetrics(int width, int height, SafeInsets insets)
    {
        Scale = MathF.Min(width / DesignWidth, height / DesignHeight);
        ScreenSize = new Vector2(width / Scale, height / Scale);
        SafeArea = new RectF(
            insets.Left / Scale,
            insets.Top / Scale,
            ScreenSize.X - (insets.Left + insets.Right) / Scale,
            ScreenSize.Y - (insets.Top + insets.Bottom) / Scale);
    }

    private void UpdateMetrics(bool force)
    {
        var vp = GraphicsDevice.Viewport;
        var size = new Point(vp.Width, vp.Height);
        var insets = Platform.GetSafeInsets();
        if (!force && size == _lastBackBuffer && insets == _lastInsets) return;
        if (size.X <= 0 || size.Y <= 0) return;

        _lastBackBuffer = size;
        _lastInsets = insets;
        ApplyMetrics(size.X, size.Y, insets);
        _scene?.Layout();
    }

    private void SwapScene(Scene scene)
    {
        _scene?.Leave();
        _scene = scene;
        _scene.Layout();
        _scene.Enter();
        Input.Consume();
    }
}
