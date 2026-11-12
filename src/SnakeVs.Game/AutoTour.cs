using SnakeVs.Core;
using SnakeVs.Game.Scenes;

namespace SnakeVs.Game;

/// <summary>
/// Scripted walk through every screen that saves captures at several phone/tablet resolutions, then exits.
/// Used to check layouts automatically and to produce Play Store screenshots (desktop only, via --tour).
/// </summary>
public sealed class AutoTour
{
    private static readonly (string Name, int W, int H)[] Sizes =
    [
        ("phone", 1080, 1920),
        ("tall", 1080, 2400),
        ("tablet", 1600, 2560),
        ("landscape", 1920, 1080),
    ];

    private readonly SnakeGame _game;
    private readonly string _directory;
    private readonly List<(float At, Action Action)> _steps = [];
    private float _time;
    private int _next;

    public AutoTour(SnakeGame game, string directory)
    {
        _game = game;
        _directory = directory;
        Directory.CreateDirectory(directory);

        var t = 0f;
        void At(float delay, Action action)
        {
            t += delay;
            _steps.Add((t, action));
        }

        At(1.5f, () => Shoot("01_menu"));
        At(0.1f, () => _game.SetScene(new OptionsScene(_game), instant: true));
        At(0.3f, () => Shoot("02_options"));
        At(0.1f, () => _game.SetScene(new HelpScene(_game), instant: true));
        At(0.5f, () => Shoot("03_help"));
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Endless), instant: true));
        At(0.4f, () => Shoot("04_ready"));
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Levels, autopilot: true, startLevel: 4), instant: true));
        At(7.0f, () => Shoot("05_levels"));
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Endless, autopilot: true), instant: true));
        At(14.0f, () => Shoot("05b_classic"));
        At(0.1f, () => (_game.CurrentScene as RunScene)?.EnterPause());
        At(0.5f, () => Shoot("06_pause"));
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Versus, autopilot: true), instant: true));
        At(9.0f, () => Shoot("07_versus"));
        At(0.1f, () => (_game.CurrentScene as RunScene)?.ForceGameOver());
        At(2.2f, () => Shoot("08_versus_result"));
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Levels, autopilot: true, startLevel: 2), instant: true));
        At(4.0f, () => (_game.CurrentScene as RunScene)?.ForceGameOver());
        At(2.2f, () => Shoot("09_gameover"));
        // The journey: the first stage, a few of the later ones, and a portal crossing caught mid-scroll.
        At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Journey), instant: true));
        At(0.4f, () => Shoot("11_journey_ready"));
        foreach (var stage in new[] { 1, 3, 4, 6, 7, 8, 10, 12, 14, 20 })
        {
            At(0.1f, () => _game.SetScene(RunScene.Create(_game, GameMode.Journey, autopilot: true, startLevel: stage), instant: true));
            At(stage == 1 ? 1.2f : 5.0f, () => Shoot($"12_journey_stage{stage:00}"));
        }

        At(0.1f, () =>
        {
            _game.Save.Controls = ControlScheme.Buttons;
            _game.SetScene(RunScene.Create(_game, GameMode.Endless), instant: true);
        });
        At(0.4f, () => Shoot("10_buttons"));
        At(0.2f, () =>
        {
            _game.Save.Controls = ControlScheme.Swipe;
            _game.Exit();
        });
    }

    public void Update(float dt)
    {
        _time += dt;
        while (_next < _steps.Count && _time >= _steps[_next].At)
        {
            _steps[_next].Action();
            _next++;
        }
    }

    private void Shoot(string name)
    {
        foreach (var (suffix, w, h) in Sizes)
        {
            _game.CaptureScene(Path.Combine(_directory, $"{name}_{suffix}.png"), w, h);
        }
    }
}
