using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>The game screen for all modes: HUD, board, optional d-pad and the pause/result overlays.</summary>
public sealed class PlayScene : Scene
{
    private const float HudHeight = 112f;
    private const float DyingSeconds = 1.1f;
    private const float BannerSeconds = 1.9f;

    private readonly GameMode _mode;
    private readonly bool _autopilot;
    private readonly GameSession _session;
    private readonly Particles _particles = new();
    private readonly FloatingTexts _texts = new();
    private readonly Button _pause = new(string.Empty) { Icon = Icons.Pause };
    private readonly ButtonList _pauseMenu = new();
    private readonly ButtonList _resultMenu = new();
    private readonly Button _resume;
    private readonly Button _restart;
    private readonly Button _pauseToMenu;
    private readonly Button _again;
    private readonly Button _resultToMenu;
    private readonly RectF[] _pad = new RectF[4];

    private Phase _phase = Phase.Ready;
    private Phase _phaseBeforePause;
    private float _phaseTime;
    private float _shake;
    private BoardView _view;
    private RectF _hud;
    private RectF _panel;
    private bool _newRecord;
    private int _bestBefore;
    private int _eatCombo;
    private float _comboTimer;

    public PlayScene(SnakeGame game, GameMode mode, bool autopilot = false, int startLevel = 1) : base(game)
    {
        _mode = mode;
        _autopilot = autopilot;
        _session = new GameSession(mode, game.Save.Difficulty, Environment.TickCount, startLevel);
        if (autopilot)
        {
            _session.Player.IsAiControlled = true;
            _session.Player.AiMistakeChance = 0;
            _phase = Phase.Playing;
        }

        _bestBefore = game.Save.GetBest(mode, game.Save.Difficulty);

        var t = game.Text;
        _resume = _pauseMenu.Add(new Button(t.Resume, ButtonStyle.Primary));
        _restart = _pauseMenu.Add(new Button(t.Restart));
        _pauseToMenu = _pauseMenu.Add(new Button(t.Menu));
        _again = _resultMenu.Add(new Button(mode == GameMode.Versus ? t.Rematch : t.PlayAgain, ButtonStyle.Primary));
        _resultToMenu = _resultMenu.Add(new Button(t.Menu));
    }

    private enum Phase
    {
        Ready,
        Playing,
        Paused,
        Dying,
        LevelBanner,
        Result,
    }

    public GameSession Session => _session;

    private bool UseButtons => Game.Save.Controls == ControlScheme.Buttons && !_autopilot;

    public override void Enter()
    {
        Game.Save.GamesPlayed++;
    }

    public override void Layout()
    {
        var safe = Safe.Inflate(-16);
        var wide = safe.W > safe.H * 0.8f;
        var padSize = 0f;
        var padArea = new RectF();

        if (UseButtons)
        {
            padSize = MathF.Min(118f, MathF.Min(safe.W, safe.H) * 0.13f);
            if (wide)
            {
                var w = padSize * 3 + 40;
                padArea = new RectF(safe.Right - w, safe.Y, w, safe.H);
                safe = safe with { W = safe.W - w };
            }
            else
            {
                var h = padSize * 3 + 24;
                padArea = new RectF(safe.X, safe.Bottom - h, safe.W, h);
                safe = safe with { H = safe.H - h };
            }
        }

        var boardArea = new RectF(safe.X + 6, safe.Y + HudHeight + 6, safe.W - 12, safe.H - HudHeight - 12);
        // Fit centers the board in the area below the HUD, which also centers the HUD + board block.
        _view = BoardView.Fit(boardArea, Rules.BoardWidth, Rules.BoardHeight);
        _hud = new RectF(_view.Area.X, _view.Area.Y - HudHeight, _view.Area.W, HudHeight - 12);

        if (UseButtons)
        {
            var center = wide
                ? padArea.Center
                : new Vector2(padArea.Center.X, MathF.Min(padArea.Center.Y, _view.Area.Bottom + 18 + padSize * 1.5f + 6));
            var gap = 8f;
            _pad[(int)Direction.Up] = RectF.FromCenter(center + new Vector2(0, -padSize - gap), padSize, padSize);
            _pad[(int)Direction.Down] = RectF.FromCenter(center + new Vector2(0, padSize + gap), padSize, padSize);
            _pad[(int)Direction.Left] = RectF.FromCenter(center + new Vector2(-padSize - gap, 0), padSize, padSize);
            _pad[(int)Direction.Right] = RectF.FromCenter(center + new Vector2(padSize + gap, 0), padSize, padSize);
        }

        _pause.Bounds = new RectF(_hud.Right - 84, _hud.Y + 6, 84, 84);

        var pw = MathF.Min(600f, Screen.X - 60f);
        var ph = 640f;
        _panel = RectF.FromCenter(Safe.Center, pw, ph);
        var bw = pw - 100f;
        _pauseMenu.LayoutColumn(_panel.Center.X, _panel.Y + 200, bw, 96, 24);
        _resultMenu.LayoutColumn(_panel.Center.X, _panel.Bottom - 96 * 2 - 24 - 50, bw, 96, 24);
    }

    public override void OnFocusLost()
    {
        if (_phase is Phase.Playing or Phase.Ready) EnterPause();
    }

    /// <summary>Opens the pause overlay (used by the automated tour too).</summary>
    public void EnterPause()
    {
        if (_phase == Phase.Paused) return;
        _phaseBeforePause = _phase;
        SetPhase(Phase.Paused);
    }

    /// <summary>Ends the run immediately (used by the automated tour).</summary>
    public void ForceGameOver()
    {
        if (_phase == Phase.Ready) SetPhase(Phase.Playing);
        _session.KillPlayer();
        ProcessEvents();
        CheckSessionState();
    }

    public override void Update(float dt)
    {
        _phaseTime += dt;
        _shake = MathF.Max(0f, _shake - dt * 30f);
        _comboTimer -= dt;
        if (_comboTimer <= 0) _eatCombo = 0;
        _particles.Update(dt);
        _texts.Update(dt);

        var input = Game.Input;
        switch (_phase)
        {
            case Phase.Ready:
                if (input.Back || input.PauseKey || _pause.WasClicked(input))
                {
                    EnterPause();
                    break;
                }

                var first = ReadSteering(out var tappedBoard);
                if (first is not null || tappedBoard)
                {
                    if (first is { } d) _session.Player.QueueTurn(d);
                    SetPhase(Phase.Playing);
                }

                break;

            case Phase.Playing:
                if (input.Back || input.PauseKey || _pause.WasClicked(input))
                {
                    EnterPause();
                    break;
                }

                ReadSteering(out _);
                _session.Update(dt * 1000.0);
                ProcessEvents();
                CheckSessionState();
                break;

            case Phase.Paused:
                var choice = _pauseMenu.Update(input);
                if (input.Back || input.PauseKey || choice == _resume)
                {
                    Game.PlaySound(Sfx.Click);
                    SetPhase(_phaseBeforePause == Phase.Ready ? Phase.Ready : Phase.Playing);
                }
                else if (choice == _restart)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(new PlayScene(Game, _mode));
                }
                else if (choice == _pauseToMenu)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(new MenuScene(Game));
                }

                break;

            case Phase.Dying:
                if (_phaseTime >= DyingSeconds) ShowResult();
                break;

            case Phase.LevelBanner:
                if (_phaseTime >= BannerSeconds)
                {
                    _session.AdvanceLevel();
                    _session.ClearEvents();
                    _particles.Clear();
                    SetPhase(_autopilot ? Phase.Playing : Phase.Ready);
                }

                break;

            case Phase.Result:
                var pick = _resultMenu.Update(input);
                if (pick == _again)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(new PlayScene(Game, _mode));
                }
                else if (pick == _resultToMenu || input.Back)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(new MenuScene(Game));
                }

                break;
        }
    }

    private Direction? ReadSteering(out bool tappedBoard)
    {
        var input = Game.Input;
        tappedBoard = false;
        Direction? first = null;

        void Steer(Direction d)
        {
            first ??= d;
            if (_phase == Phase.Playing && _session.Player.QueueTurn(d)) Game.Vibrate(8);
        }

        if (input.KeyDirection is { } key) Steer(key);

        if (UseButtons)
        {
            foreach (var press in input.Presses)
            {
                for (var i = 0; i < 4; i++)
                {
                    if (_pad[i].Inflate(10).Contains(press)) Steer((Direction)i);
                }
            }
        }
        else
        {
            foreach (var swipe in input.Swipes) Steer(swipe);
        }

        foreach (var tap in input.Taps)
        {
            if (_view.Area.Contains(tap)) tappedBoard = true;
        }

        // In the ready phase a direction is only accepted if it does not reverse the snake.
        if (_phase == Phase.Ready && first is { } f && !_session.Player.CanTurnTo(f)) first = null;
        return first;
    }

    private void CheckSessionState()
    {
        switch (_session.State)
        {
            case SessionState.GameOver:
            case SessionState.MatchOver:
                SetPhase(Phase.Dying);
                break;
            case SessionState.LevelCleared:
                SetPhase(Phase.LevelBanner);
                break;
        }
    }

    private void ShowResult()
    {
        var save = Game.Save;
        if (_mode != GameMode.Versus)
        {
            _newRecord = save.SubmitScore(_mode, save.Difficulty, _session.Player.Score);
            Game.PlaySound(_newRecord ? Sfx.Win : Sfx.Lose);
        }
        else
        {
            save.RecordVersus(_session.Outcome);
            _newRecord = false;
            Game.PlaySound(_session.Outcome == MatchOutcome.PlayerWon ? Sfx.Win : Sfx.Lose);
        }

        Game.Persist();
        SetPhase(Phase.Result);
    }

    private void SetPhase(Phase phase)
    {
        _phase = phase;
        _phaseTime = 0;
        Game.Input.Consume();
    }

    private void ProcessEvents()
    {
        foreach (var e in _session.Events)
        {
            var pos = _view.CellCenter(e.Cell);
            var isPlayer = e.SnakeId == GameSession.PlayerId;
            var cell = _view.Cell;
            switch (e.Type)
            {
                case GameEventType.AteApple:
                    if (isPlayer)
                    {
                        _eatCombo++;
                        _comboTimer = 2.5f;
                    }

                    Game.PlaySound(Sfx.Eat, isPlayer ? 0.9f : 0.45f, isPlayer ? MathF.Min(0.6f, _eatCombo * 0.06f) : -0.2f);
                    _particles.Burst(pos, Palette.Apple, 14, cell * 5f, cell * 0.16f);
                    _texts.Add($"+{e.Value}", pos - new Vector2(0, cell), isPlayer ? Palette.Text : Palette.RivalHead);
                    if (isPlayer) Game.Vibrate(12);
                    break;

                case GameEventType.AteGolden:
                    Game.PlaySound(Sfx.Golden, isPlayer ? 1f : 0.5f);
                    _particles.Burst(pos, Palette.Golden, 26, cell * 7f, cell * 0.2f, 0.8f);
                    _texts.Add($"+{e.Value}", pos - new Vector2(0, cell), Palette.Gold, 4f);
                    if (isPlayer) Game.Vibrate(30);
                    break;

                case GameEventType.PowerUpCollected:
                    var kind = (PowerUpKind)e.Value;
                    Game.PlaySound(Sfx.PowerUp, isPlayer ? 0.9f : 0.45f);
                    _particles.Burst(pos, Palette.PowerUp(kind), 22, cell * 6f, cell * 0.18f, 0.7f);
                    _texts.Add(Game.Text.PowerUpName(kind), pos - new Vector2(0, cell), Palette.PowerUp(kind), 3f);
                    if (isPlayer) Game.Vibrate(25);
                    break;

                case GameEventType.ShieldUsed:
                    Game.PlaySound(Sfx.Shield);
                    _particles.Burst(pos, Palette.Shield, 24, cell * 6f, cell * 0.18f);
                    _texts.Add(Game.Text.ShieldSaved, pos - new Vector2(0, cell), Palette.Shield, 3.5f);
                    _shake = 6f;
                    if (isPlayer) Game.Vibrate(60);
                    break;

                case GameEventType.SnakeDied:
                    Game.PlaySound(Sfx.Die, isPlayer ? 1f : 0.7f);
                    var (head, _) = Palette.SnakeColors(e.SnakeId);
                    _particles.Burst(pos, head, 40, cell * 9f, cell * 0.22f, 0.9f);
                    _particles.Burst(pos, Color.White, 12, cell * 5f, cell * 0.12f, 0.5f);
                    _shake = 12f;
                    Game.Vibrate(isPlayer ? 300 : 80);
                    break;

                case GameEventType.LevelCleared:
                    Game.PlaySound(Sfx.LevelUp);
                    _particles.Burst(pos, Palette.PlayerHead, 40, cell * 9f, cell * 0.2f, 1f);
                    Game.Vibrate(80);
                    break;

                case GameEventType.GoldenSpawned:
                case GameEventType.PowerUpSpawned:
                    Game.PlaySound(Sfx.Spawn, 0.4f);
                    _particles.Burst(pos, Color.White, 8, cell * 3f, cell * 0.1f, 0.4f);
                    break;
            }
        }

        _session.ClearEvents();
    }

    public override void Draw(Canvas c)
    {
        Ui.Backdrop(c, Screen, Time);
        DrawHud(c);
        BoardRenderer.Draw(c, _session, _view, Time, shake: _shake);
        _particles.Draw(c);
        _texts.Draw(c, Font);
        if (UseButtons) DrawPad(c);

        switch (_phase)
        {
            case Phase.Ready:
                DrawReady(c);
                break;
            case Phase.LevelBanner:
                DrawBanner(c);
                break;
            case Phase.Paused:
                DrawPauseOverlay(c);
                break;
            case Phase.Result:
                DrawResult(c);
                break;
        }
    }

    private void DrawHud(Canvas c)
    {
        var t = Game.Text;
        var player = _session.Player;
        var x = _hud.X + 4;
        var y = _hud.Y;

        if (_mode != GameMode.Versus)
        {
            Font.Draw(c, t.Score, new Vector2(x, y + 6), 2.5f, Palette.TextDim);
            Font.DrawShadowed(c, player.Score.ToString(), new Vector2(x, y + 30), 6f, Palette.Text);
            var best = Math.Max(_bestBefore, player.Score);
            Font.Draw(c, $"{t.Best} {best}", new Vector2(x, y + 84), 2.5f, Palette.Gold);
        }

        if (_mode == GameMode.Endless)
        {
            var cx = _hud.Center.X + 10;
            Font.DrawCentered(c, $"{t.Speed} {_session.SpeedLevel}", new Vector2(cx, y + 16), 3.5f, Palette.Text);
            Font.DrawCentered(c, $"{t.Apples} {player.ApplesEaten}", new Vector2(cx, y + 52), 2.5f, Palette.Apple);
            DrawEffects(c, new Vector2(cx, y + 84), player);
        }
        else if (_mode == GameMode.Levels)
        {
            var cx = _hud.Center.X + 10;
            Font.DrawCentered(c, $"{t.Level} {_session.Level}", new Vector2(cx, y + 16), 3.5f, Palette.Text);
            var pips = Rules.ApplesPerLevel;
            var spacing = 26f;
            var startX = cx - (pips - 1) * spacing / 2f;
            for (var i = 0; i < pips; i++)
            {
                var filled = i < _session.ApplesThisLevel;
                var p = new Vector2(startX + i * spacing, y + 50);
                c.FillCircle(p, 9f, filled ? Palette.Apple : Palette.BoardBorder);
                if (filled) c.FillCircle(p + new Vector2(-3, -3), 3f, Palette.AppleShine * 0.8f);
            }

            DrawEffects(c, new Vector2(cx, y + 84), player);
        }
        else
        {
            var rival = _session.Rival!;
            Font.Draw(c, t.You, new Vector2(x, y + 6), 2.5f, Palette.PlayerHead);
            Font.DrawShadowed(c, player.ApplesEaten.ToString(), new Vector2(x, y + 30), 6f, Palette.Text);

            var rx = _pause.Bounds.X - 20;
            var cpuLabel = Font.Measure(t.Cpu, 2.5f).X;
            Font.Draw(c, t.Cpu, new Vector2(rx - cpuLabel, y + 6), 2.5f, Palette.RivalHead);
            var rv = rival.ApplesEaten.ToString();
            var rvw = Font.Measure(rv, 6f).X;
            Font.DrawShadowed(c, rv, new Vector2(rx - rvw, y + 30), 6f, Palette.Text);

            var cx = (x + 110 + rx - 90) / 2f;
            var barW = MathF.Max(120f, rx - 90 - (x + 110));
            Font.DrawCentered(c, $"{t.Goal} {Rules.VersusTargetApples}", new Vector2(cx, y + 14), 2.5f, Palette.TextDim);
            DrawBar(c, new RectF(cx - barW / 2, y + 32, barW, 12), player.ApplesEaten / (float)Rules.VersusTargetApples, Palette.PlayerHead);
            DrawBar(c, new RectF(cx - barW / 2, y + 52, barW, 12), rival.ApplesEaten / (float)Rules.VersusTargetApples, Palette.RivalHead);
            DrawEffects(c, new Vector2(cx, y + 86), player);
        }

        _pause.Draw(c, Font, Game.Input, Time);
    }

    private static void DrawBar(Canvas c, RectF r, float fraction, Color color)
    {
        c.FillRoundRect(r, r.H / 2, Palette.BoardBorder);
        var w = MathF.Max(r.H, r.W * MathHelper.Clamp(fraction, 0f, 1f));
        if (fraction > 0) c.FillRoundRect(r with { W = w }, r.H / 2, color);
    }

    private void DrawEffects(Canvas c, Vector2 center, Snake player)
    {
        Span<(PowerUpKind Kind, float Fraction)> active = stackalloc (PowerUpKind, float)[4];
        var n = 0;
        if (_session.SlowMs > 0) active[n++] = (PowerUpKind.Slow, (float)(_session.SlowMs / Rules.SlowDurationMs));
        if (player.GhostMs > 0) active[n++] = (PowerUpKind.Ghost, (float)Math.Min(1, player.GhostMs / Rules.GhostDurationMs));
        if (player.DoubleMs > 0) active[n++] = (PowerUpKind.Double, (float)(player.DoubleMs / Rules.DoubleDurationMs));
        if (player.Shields > 0) active[n++] = (PowerUpKind.Shield, 1f);
        if (n == 0) return;

        const float chipW = 64f;
        const float gap = 8f;
        var x = center.X - (n * chipW + (n - 1) * gap) / 2f;
        for (var i = 0; i < n; i++)
        {
            var (kind, fraction) = active[i];
            var color = Palette.PowerUp(kind);
            var r = new RectF(x + i * (chipW + gap), center.Y - 13, chipW, 26);
            c.FillRoundRect(r, 8, Palette.Panel);
            c.FillRoundRect(r with { W = MathF.Max(16, r.W * fraction) }, 8, color * 0.35f);
            c.BitmapCentered(Icons.PowerUp(kind), new Vector2(r.X + 18, r.Center.Y), 2.4f, color);
            if (kind == PowerUpKind.Shield) Font.Draw(c, "1", new Vector2(r.X + 36, r.Y + 6), 2f, color);
        }
    }

    private void DrawPad(Canvas c)
    {
        var input = Game.Input;
        for (var i = 0; i < 4; i++)
        {
            var r = _pad[i];
            var pressed = input.Pointer is { } p && r.Inflate(10).Contains(p);
            c.FillRoundRect(r.Offset(0, 6), 24, Palette.TextShadow * 0.6f);
            c.FillRoundRect(pressed ? r.Inflate(-3) : r, 24, pressed ? Palette.ButtonPressed : Palette.Button);
            c.BitmapCentered(Icons.Arrow((Direction)i), r.Center, r.W * 0.07f, pressed ? Palette.Accent : Palette.Text);
        }
    }

    private void DrawReady(Canvas c)
    {
        var t = Game.Text;
        var band = new RectF(_view.Area.X, _view.Area.Center.Y - 110, _view.Area.W, 220);
        c.FillRect(band, Palette.Overlay);
        var title = _mode switch
        {
            GameMode.Versus => $"{t.Versus} - {t.DifficultyName(_session.Difficulty)}",
            GameMode.Endless => t.Classic,
            _ => $"{t.Level} {_session.Level}",
        };
        Font.DrawCentered(c, title, new Vector2(band.Center.X, band.Y + 52), 5f, _mode == GameMode.Versus ? Palette.RivalHead : Palette.Accent);
        var hint = UseButtons ? t.ArrowToStart : t.SwipeToStart;
        var alpha = 0.6f + 0.4f * MathF.Sin(Time * 5f);
        Font.DrawCentered(c, hint, new Vector2(band.Center.X, band.Y + 140), 3.5f, Palette.Text, alpha: alpha);

        if (!UseButtons)
        {
            // Little animated swipe hint above the snake's head.
            var head = _view.CellCenter(_session.Player.Head);
            var k = (Time * 1.2f) % 1f;
            var from = head + new Vector2(0, -_view.Cell * 2);
            var to = from + new Vector2(0, -_view.Cell * 3);
            var p = Vector2.Lerp(from, to, k);
            c.FillCircle(p, _view.Cell * 0.35f, Palette.Text * (0.8f * (1 - k)));
            c.DrawRing(p, _view.Cell * 0.55f, Palette.Text * (0.5f * (1 - k)));
        }
    }

    private void DrawBanner(Canvas c)
    {
        var t = Game.Text;
        var k = Ui.Pop(_phaseTime * 3f);
        var band = RectF.FromCenter(_view.Area.Center, _view.Area.W * k, 240);
        c.FillRect(band, Palette.Overlay);
        if (k < 0.6f) return;
        Font.DrawCentered(c, t.LevelCleared, new Vector2(band.Center.X, band.Y + 70), Font.FitScale(t.LevelCleared, band.W - 40, 6f), Palette.Accent);
        Font.DrawCentered(c, $"{t.Bonus} +{Rules.LevelBonus * _session.Level}", new Vector2(band.Center.X, band.Y + 140), 4f, Palette.Gold);
        Font.DrawCentered(c, $"{t.Level} {_session.Level + 1}", new Vector2(band.Center.X, band.Y + 195), 3f, Palette.TextDim);
    }

    private void DrawPauseOverlay(Canvas c)
    {
        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Overlay);
        var k = Ui.Pop(_phaseTime * 4f);
        var r = RectF.FromCenter(_panel.Center, _panel.W * (0.85f + 0.15f * k), 560 * (0.85f + 0.15f * k));
        Ui.Panel(c, r);
        Font.DrawCentered(c, Game.Text.Paused, new Vector2(_panel.Center.X, _panel.Y + 120), 7f, Palette.Text);
        _pauseMenu.Draw(c, Font, Game.Input, Time);
    }

    private void DrawResult(Canvas c)
    {
        var t = Game.Text;
        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Overlay * MathHelper.Clamp(_phaseTime * 4f, 0f, 1f));
        var k = Ui.Pop(_phaseTime * 3.5f);
        var r = RectF.FromCenter(_panel.Center, _panel.W * (0.8f + 0.2f * k), _panel.H * (0.8f + 0.2f * k));
        Ui.Panel(c, r);
        var cx = _panel.Center.X;
        var top = _panel.Y;

        if (_mode != GameMode.Versus)
        {
            Font.DrawCentered(c, t.GameOver, new Vector2(cx, top + 80), Font.FitScale(t.GameOver, _panel.W - 60, 7f), Palette.Danger);
            Font.DrawCentered(c, t.Score, new Vector2(cx, top + 150), 3f, Palette.TextDim);
            Font.DrawCentered(c, _session.Player.Score.ToString(), new Vector2(cx, top + 210), 9f, Palette.Text);
            if (_newRecord)
            {
                var a = 0.6f + 0.4f * MathF.Sin(Time * 8f);
                Font.DrawCentered(c, t.NewRecord, new Vector2(cx, top + 285), 4.5f, Palette.Gold, alpha: a);
            }
            else
            {
                var best = Game.Save.GetBest(_mode, _session.Difficulty);
                Font.DrawCentered(c, $"{t.Best} {best}", new Vector2(cx, top + 285), 3.5f, Palette.Gold);
            }

            var detail = _mode == GameMode.Endless ? $"{t.Apples} {_session.Player.ApplesEaten}" : $"{t.Level} {_session.Level}";
            Font.DrawCentered(c, $"{detail}  -  {t.DifficultyName(_session.Difficulty)}", new Vector2(cx, top + 330), 2.5f, Palette.TextDim);
        }
        else
        {
            var (title, color) = _session.Outcome switch
            {
                MatchOutcome.PlayerWon => (t.YouWin, Palette.PlayerHead),
                MatchOutcome.RivalWon => (t.CpuWins, Palette.RivalHead),
                _ => (t.Draw, Palette.Text),
            };
            Font.DrawCentered(c, title, new Vector2(cx, top + 80), Font.FitScale(title, _panel.W - 60, 7f), color);
            Font.DrawCentered(c, t.Apples, new Vector2(cx, top + 150), 3f, Palette.TextDim);
            var you = _session.Player.ApplesEaten.ToString();
            var cpu = _session.Rival!.ApplesEaten.ToString();
            Font.DrawCentered(c, you, new Vector2(cx - 110, top + 215), 9f, Palette.PlayerHead);
            Font.DrawCentered(c, "X", new Vector2(cx, top + 215), 5f, Palette.TextDim);
            Font.DrawCentered(c, cpu, new Vector2(cx + 110, top + 215), 9f, Palette.RivalHead);
            var save = Game.Save;
            Font.DrawCentered(c, $"{t.WinsLosses} {save.VersusWins}/{save.VersusLosses}", new Vector2(cx, top + 300), 3f, Palette.Gold);
        }

        _resultMenu.Draw(c, Font, Game.Input, Time);
    }
}
