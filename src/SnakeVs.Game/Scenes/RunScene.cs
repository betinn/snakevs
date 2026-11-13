using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.Online;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>
/// What every game screen shares, whatever the mode: steering, the d-pad, the ready/pause/result overlays, records
/// and the online ranking. <see cref="PlayScene"/> runs the classic modes, <see cref="JourneyScene"/> the journey.
/// </summary>
public abstract class RunScene : Scene
{
    protected const float HudHeight = 112f;
    private const float DyingSeconds = 1.1f;

    private readonly Button _pause = new(string.Empty) { Icon = Icons.Pause };
    private readonly ButtonList _pauseMenu = new();
    private readonly ButtonList _resultMenu = new();
    private readonly Button _resume;
    private readonly Button _restart;
    private readonly Button _pauseToMenu;
    private readonly Button _again;
    private readonly Button _resultToMenu;
    private readonly Button _retrySave;
    private readonly RectF[] _pad = new RectF[4];
    private RunPhase _phaseBeforePause;
    private RectF _panel;

    // Online ranking: check whether the run made the top, then the nickname dialog (opened by itself), then the
    // upload. The name is kept so a failed upload retries without asking again.
    private Task<ScoreEntry[]>? _qualify;
    private bool _nameDue;
    private Task<string?>? _askName;
    private Task<SubmitResult>? _submit;
    private string? _submitName;

    protected RunScene(SnakeGame game, GameMode mode, bool autopilot) : base(game)
    {
        Mode = mode;
        Autopilot = autopilot;
        if (autopilot) Phase = RunPhase.Playing;
        BestBefore = game.Save.GetBest(mode, game.Save.Difficulty);

        var t = game.Text;
        _resume = _pauseMenu.Add(new Button(t.Resume, ButtonStyle.Primary));
        _restart = _pauseMenu.Add(new Button(t.Restart));
        _pauseToMenu = _pauseMenu.Add(new Button(t.Menu));
        _again = _resultMenu.Add(new Button(mode == GameMode.Versus ? t.Rematch : t.PlayAgain, ButtonStyle.Primary));
        _resultToMenu = _resultMenu.Add(new Button(t.Menu));
        _retrySave = new Button(t.SaveFailed) { Accent = Palette.Gold };
    }

    protected enum RunPhase
    {
        Ready,
        Playing,
        Paused,
        Dying,

        /// <summary>A stop between two parts of a run (the "level clear" banner of the levels mode).</summary>
        Interlude,
        Result,
    }

    public GameMode Mode { get; }

    /// <summary>The CPU plays (automated captures): no d-pad, no ranking, no waiting for the first input.</summary>
    protected bool Autopilot { get; }

    protected Particles Particles { get; } = new();
    protected FloatingTexts Texts { get; } = new();
    protected RunPhase Phase { get; private set; } = RunPhase.Ready;
    protected float PhaseTime { get; private set; }
    protected float Shake { get; set; }
    protected BoardView View { get; private set; }
    protected RectF Hud { get; private set; }
    protected RectF PauseBounds => _pause.Bounds;
    protected int BestBefore { get; }
    protected bool NewRecord { get; set; }
    protected int EatCombo { get; set; }
    protected float ComboTimer { get; set; }

    protected abstract Snake Player { get; }
    protected abstract Difficulty RunDifficulty { get; }

    /// <summary>Title of the "ready" band shown before the first move.</summary>
    protected abstract string ReadyTitle { get; }

    protected virtual Color ReadyColor => Palette.Accent;

    /// <summary>Second line under the score in the result panel (apples, level, stage...).</summary>
    protected abstract string ResultDetail { get; }

    protected bool UseButtons => Game.Save.Controls == ControlScheme.Buttons && !Autopilot;

    private ILeaderboard? OnlineRanking => Autopilot ? null : Game.Platform.Leaderboard;

    /// <summary>The right screen for a mode.</summary>
    public static RunScene Create(SnakeGame game, GameMode mode, bool autopilot = false, int startLevel = 1) =>
        mode == GameMode.Journey
            ? new JourneyScene(game, autopilot, startLevel)
            : new PlayScene(game, mode, autopilot, startLevel);

    /// <summary>Advances the run by one frame and reacts to what happened (sounds, particles, <see cref="EndRun"/>...).</summary>
    protected abstract void Simulate(float dt);

    protected abstract void KillPlayer();

    protected abstract ScoreSubmission BuildSubmission(string name);

    protected abstract void DrawHud(Canvas c);

    protected abstract void DrawBoard(Canvas c);

    protected virtual void DrawEffectsLayer(Canvas c)
    {
        Particles.Draw(c);
        Texts.Draw(c, Font);
    }

    protected virtual void UpdateInterlude()
    {
    }

    protected virtual void DrawInterlude(Canvas c)
    {
    }

    /// <summary>Stores the outcome of the run (records, statistics) and plays the closing jingle.</summary>
    protected virtual void RecordResult()
    {
        NewRecord = Game.Save.SubmitScore(Mode, Game.Save.Difficulty, Player.Score);
        Game.PlaySound(NewRecord ? Sfx.Win : Sfx.Lose);
    }

    /// <summary>Sound, particles and haptics for the events every mode has. <paramref name="pos"/> is where it happened, in particle space.</summary>
    protected void Feedback(GameEvent e, Vector2 pos)
    {
        var isPlayer = e.SnakeId == GameSession.PlayerId;
        var cell = View.Cell;
        switch (e.Type)
        {
            case GameEventType.AteApple:
                if (isPlayer)
                {
                    EatCombo++;
                    ComboTimer = 2.5f;
                }

                Game.PlaySound(Sfx.Eat, isPlayer ? 0.9f : 0.45f, isPlayer ? MathF.Min(0.6f, EatCombo * 0.06f) : -0.2f);
                Particles.Burst(pos, Palette.Apple, 14, cell * 5f, cell * 0.16f);
                Texts.Add($"+{e.Value}", pos - new Vector2(0, cell), isPlayer ? Palette.Text : Palette.RivalHead);
                if (isPlayer) Game.Vibrate(12);
                break;

            case GameEventType.AteGolden:
                Game.PlaySound(Sfx.Golden, isPlayer ? 1f : 0.5f);
                Particles.Burst(pos, Palette.Golden, 26, cell * 7f, cell * 0.2f, 0.8f);
                Texts.Add($"+{e.Value}", pos - new Vector2(0, cell), Palette.Gold, 4f);
                if (isPlayer) Game.Vibrate(30);
                break;

            case GameEventType.PowerUpCollected:
                var kind = (PowerUpKind)e.Value;
                Game.PlaySound(Sfx.PowerUp, isPlayer ? 0.9f : 0.45f);
                Particles.Burst(pos, Palette.PowerUp(kind), 22, cell * 6f, cell * 0.18f, 0.7f);
                Texts.Add(Game.Text.PowerUpName(kind), pos - new Vector2(0, cell), Palette.PowerUp(kind), 3f);
                if (isPlayer) Game.Vibrate(25);
                break;

            case GameEventType.ShieldUsed:
                Game.PlaySound(Sfx.Shield);
                Particles.Burst(pos, Palette.Shield, 24, cell * 6f, cell * 0.18f);
                Texts.Add(Game.Text.ShieldSaved, pos - new Vector2(0, cell), Palette.Shield, 3.5f);
                Shake = 6f;
                if (isPlayer) Game.Vibrate(60);
                break;

            case GameEventType.SnakeDied:
                Game.PlaySound(Sfx.Die, isPlayer ? 1f : 0.7f);
                var (head, _) = Palette.SnakeColors(e.SnakeId);
                Particles.Burst(pos, head, 40, cell * 9f, cell * 0.22f, 0.9f);
                Particles.Burst(pos, Color.White, 12, cell * 5f, cell * 0.12f, 0.5f);
                Shake = 12f;
                Game.Vibrate(isPlayer ? 300 : 80);
                break;

            case GameEventType.LevelCleared:
                Game.PlaySound(Sfx.LevelUp);
                Particles.Burst(pos, Palette.PlayerHead, 40, cell * 9f, cell * 0.2f, 1f);
                Game.Vibrate(80);
                break;

            case GameEventType.GoldenSpawned:
            case GameEventType.PowerUpSpawned:
                Game.PlaySound(Sfx.Spawn, 0.4f);
                Particles.Burst(pos, Color.White, 8, cell * 3f, cell * 0.1f, 0.4f);
                break;
        }
    }

    /// <summary>The run is over: the death animation plays, then the result panel opens.</summary>
    protected void EndRun() => SetPhase(RunPhase.Dying);

    protected void BeginInterlude() => SetPhase(RunPhase.Interlude);

    protected void EndInterlude() => SetPhase(Autopilot ? RunPhase.Playing : RunPhase.Ready);

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
        View = BoardView.Fit(boardArea, Rules.BoardWidth, Rules.BoardHeight);
        Hud = new RectF(View.Area.X, View.Area.Y - HudHeight, View.Area.W, HudHeight - 12);

        if (UseButtons)
        {
            var center = wide
                ? padArea.Center
                : new Vector2(padArea.Center.X, MathF.Min(padArea.Center.Y, View.Area.Bottom + 18 + padSize * 1.5f + 6));
            var gap = 8f;
            _pad[(int)Direction.Up] = RectF.FromCenter(center + new Vector2(0, -padSize - gap), padSize, padSize);
            _pad[(int)Direction.Down] = RectF.FromCenter(center + new Vector2(0, padSize + gap), padSize, padSize);
            _pad[(int)Direction.Left] = RectF.FromCenter(center + new Vector2(-padSize - gap, 0), padSize, padSize);
            _pad[(int)Direction.Right] = RectF.FromCenter(center + new Vector2(padSize + gap, 0), padSize, padSize);
        }

        _pause.Bounds = new RectF(Hud.Right - 84, Hud.Y + 6, 84, 84);

        var pw = MathF.Min(600f, Screen.X - 60f);
        var results = _resultMenu.Buttons.Count;
        var ph = 640f + (results - 2) * 120f;
        _panel = RectF.FromCenter(Safe.Center, pw, ph);
        var bw = pw - 100f;
        _pauseMenu.LayoutColumn(_panel.Center.X, _panel.Y + 200, bw, 96, 24);
        _resultMenu.LayoutColumn(_panel.Center.X, _panel.Bottom - 96 * results - 24 * (results - 1) - 50, bw, 96, 24);
    }

    public override void OnFocusLost()
    {
        if (Phase is RunPhase.Playing or RunPhase.Ready) EnterPause();
    }

    /// <summary>Opens the pause overlay (used by the automated tour too).</summary>
    public void EnterPause()
    {
        if (Phase == RunPhase.Paused) return;
        _phaseBeforePause = Phase;
        SetPhase(RunPhase.Paused);
    }

    /// <summary>Ends the run immediately (used by the automated tour).</summary>
    public void ForceGameOver()
    {
        if (Phase == RunPhase.Ready) SetPhase(RunPhase.Playing);
        KillPlayer();
        Simulate(0f);
    }

    public override void Update(float dt)
    {
        PhaseTime += dt;
        Shake = MathF.Max(0f, Shake - dt * 30f);
        ComboTimer -= dt;
        if (ComboTimer <= 0) EatCombo = 0;
        Particles.Update(dt);
        Texts.Update(dt);

        var input = Game.Input;
        switch (Phase)
        {
            case RunPhase.Ready:
                if (input.Back || input.PauseKey || _pause.WasClicked(input))
                {
                    EnterPause();
                    break;
                }

                var first = ReadSteering(out var tappedBoard);
                if (first is not null || tappedBoard)
                {
                    if (first is { } d) Player.QueueTurn(d);
                    SetPhase(RunPhase.Playing);
                }

                break;

            case RunPhase.Playing:
                if (input.Back || input.PauseKey || _pause.WasClicked(input))
                {
                    EnterPause();
                    break;
                }

                ReadSteering(out _);
                Simulate(dt);
                break;

            case RunPhase.Paused:
                var choice = _pauseMenu.Update(input);
                if (input.Back || input.PauseKey || choice == _resume)
                {
                    Game.PlaySound(Sfx.Click);
                    SetPhase(_phaseBeforePause == RunPhase.Ready ? RunPhase.Ready : RunPhase.Playing);
                }
                else if (choice == _restart)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(Create(Game, Mode));
                }
                else if (choice == _pauseToMenu)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(new MenuScene(Game));
                }

                break;

            case RunPhase.Dying:
                if (PhaseTime >= DyingSeconds) ShowResult();
                break;

            case RunPhase.Interlude:
                UpdateInterlude();
                break;

            case RunPhase.Result:
                if (UpdateSaveScore()) break;
                var pick = _resultMenu.Update(input);
                if (pick == _retrySave)
                {
                    Game.PlaySound(Sfx.Click);
                    if (OnlineRanking is { } ranking && _submitName is not null && _submit is null) Submit(ranking, _submitName);
                }
                else if (pick == _again)
                {
                    Game.PlaySound(Sfx.Click);
                    Game.SetScene(Create(Game, Mode));
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
            if (Phase == RunPhase.Playing && Player.QueueTurn(d)) Game.Vibrate(8);
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
            if (View.Area.Contains(tap)) tappedBoard = true;
        }

        // In the ready phase a direction is only accepted if it does not reverse the snake.
        if (Phase == RunPhase.Ready && first is { } f && !Player.CanTurnTo(f)) first = null;
        return first;
    }

    private void ShowResult()
    {
        RecordResult();
        Game.Persist();

        // Online ranking: a scoring run of a ranked mode is checked against the current top; if it makes it, the
        // nickname box opens by itself once the result panel is up.
        if (OnlineRanking is { } ranking && Leaderboard.IsRanked(Mode) && Player.Score > 0)
        {
            try
            {
                _qualify = ranking.GetTopAsync(Mode, RunDifficulty, Leaderboard.DefaultTake);
            }
            catch (Exception)
            {
                _qualify = null; // offline: the run simply is not ranked
            }
        }

        SetPhase(RunPhase.Result);
    }

    /// <summary>A run enters the top when there is room or it beats the last one (ties keep the older entry first).</summary>
    private bool MakesTop(ScoreEntry[] top) =>
        top.Length < Leaderboard.DefaultTake || Player.Score > top[^1].Score;

    private void Submit(ILeaderboard ranking, string name)
    {
        ShowRetry(false);
        try
        {
            _submit = ranking.SubmitAsync(BuildSubmission(name));
        }
        catch (Exception)
        {
            ShowRetry(true);
        }
    }

    /// <summary>Adds or removes the "error, try again" button above the result buttons.</summary>
    private void ShowRetry(bool show)
    {
        if (show == _resultMenu.Buttons.Contains(_retrySave)) return;
        _resultMenu.Clear();
        if (show) _resultMenu.Add(_retrySave);
        _resultMenu.Add(_again);
        _resultMenu.Add(_resultToMenu);
        Layout();
    }

    /// <summary>
    /// Follows the top check, the nickname dialog and the upload. Returns true while the dialog is open (input
    /// belongs to it).
    /// </summary>
    private bool UpdateSaveScore()
    {
        if (OnlineRanking is not { } ranking) return false;

        if (_qualify is { IsCompleted: true } qualify)
        {
            _qualify = null;
            if (qualify.IsCompletedSuccessfully) _nameDue = MakesTop(qualify.Result);
            else _ = qualify.Exception; // observed: offline, the run is just not ranked
        }

        // Let the result panel pop in first, so the player sees the score before typing.
        if (_nameDue && PhaseTime >= 0.7f)
        {
            _nameDue = false;
            try
            {
                Game.PlaySound(Sfx.Golden);
                _askName = ranking.AskNameAsync(Game.Save.PlayerName, Player.Score, Game.Text);
            }
            catch (Exception)
            {
                _askName = null;
            }
        }

        if (_askName is { } ask)
        {
            if (!ask.IsCompleted) return true;
            _askName = null;
            Game.Input.Consume();
            var name = ask.IsCompletedSuccessfully ? Leaderboard.NormalizeName(ask.Result) : null;
            if (name is null) return false;

            Game.Save.PlayerName = name;
            Game.Persist();
            _submitName = name;
            Submit(ranking, name);
            return false;
        }

        if (_submit is { IsCompleted: true } submit)
        {
            _submit = null;
            if (submit.IsCompletedSuccessfully)
            {
                var r = submit.Result;
                var sent = BuildSubmission(_submitName!);
                var mine = new ScoreEntry(r.Id, r.Rank, sent.Name, sent.Score, sent.Apples, sent.Level, DateTime.UtcNow);
                Game.PlaySound(Sfx.LevelUp);
                Game.SetScene(new RankingScene(Game, ranking, Mode, RunDifficulty, mine, r.Total, replayMode: Mode));
            }
            else
            {
                _ = submit.Exception; // observed: shown to the player as a retry button
                ShowRetry(true);
            }
        }

        return false;
    }

    private void SetPhase(RunPhase phase)
    {
        Phase = phase;
        PhaseTime = 0;
        Game.Input.Consume();
    }

    public override void Draw(Canvas c)
    {
        Ui.Backdrop(c, Screen, Time);
        DrawHud(c);
        _pause.Draw(c, Font, Game.Input, Time);
        DrawBoard(c);
        DrawEffectsLayer(c);
        if (UseButtons) DrawPad(c);

        switch (Phase)
        {
            case RunPhase.Ready:
                DrawReady(c);
                break;
            case RunPhase.Interlude:
                DrawInterlude(c);
                break;
            case RunPhase.Paused:
                DrawPauseOverlay(c);
                break;
            case RunPhase.Result:
                DrawResult(c);
                break;
        }
    }

    /// <summary>Score, best score and a caption on the left of the HUD (every mode but the duel).</summary>
    protected void DrawScore(Canvas c)
    {
        var t = Game.Text;
        var x = Hud.X + 4;
        var y = Hud.Y;
        Font.Draw(c, t.Score, new Vector2(x, y + 6), 2.5f, Palette.TextDim);
        Font.DrawShadowed(c, Player.Score.ToString(), new Vector2(x, y + 30), 6f, Palette.Text);
        var best = Math.Max(BestBefore, Player.Score);
        Font.Draw(c, $"{t.Best} {best}", new Vector2(x, y + 84), 2.5f, Palette.Gold);
    }

    /// <summary>Row of chips for the power-ups in effect.</summary>
    protected void DrawEffects(Canvas c, Vector2 center, Snake player, double slowMs)
    {
        Span<(PowerUpKind Kind, float Fraction)> active = stackalloc (PowerUpKind, float)[4];
        var n = 0;
        if (slowMs > 0) active[n++] = (PowerUpKind.Slow, (float)(slowMs / Rules.SlowDurationMs));
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
        var band = new RectF(View.Area.X, View.Area.Center.Y - 110, View.Area.W, 220);
        c.FillRect(band, Palette.Overlay);
        Font.DrawCentered(c, ReadyTitle, new Vector2(band.Center.X, band.Y + 52), Font.FitScale(ReadyTitle, band.W - 30, 5f), ReadyColor);
        var hint = UseButtons ? t.ArrowToStart : t.SwipeToStart;
        var alpha = 0.6f + 0.4f * MathF.Sin(Time * 5f);
        Font.DrawCentered(c, hint, new Vector2(band.Center.X, band.Y + 140), 3.5f, Palette.Text, alpha: alpha);

        if (!UseButtons)
        {
            // Little animated swipe hint above the snake's head.
            var head = ReadyHintAnchor;
            var k = (Time * 1.2f) % 1f;
            var from = head + new Vector2(0, -View.Cell * 2);
            var to = from + new Vector2(0, -View.Cell * 3);
            var p = Vector2.Lerp(from, to, k);
            c.FillCircle(p, View.Cell * 0.35f, Palette.Text * (0.8f * (1 - k)));
            c.DrawRing(p, View.Cell * 0.55f, Palette.Text * (0.5f * (1 - k)));
        }
    }

    /// <summary>Screen position of the player's head while waiting for the first move.</summary>
    protected virtual Vector2 ReadyHintAnchor => View.CellCenter(Player.Head);

    private void DrawPauseOverlay(Canvas c)
    {
        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Overlay);
        var k = Ui.Pop(PhaseTime * 4f);
        var r = RectF.FromCenter(_panel.Center, _panel.W * (0.85f + 0.15f * k), 560 * (0.85f + 0.15f * k));
        Ui.Panel(c, r);
        Font.DrawCentered(c, Game.Text.Paused, new Vector2(_panel.Center.X, _panel.Y + 120), 7f, Palette.Text);
        _pauseMenu.Draw(c, Font, Game.Input, Time);
    }

    private void DrawResult(Canvas c)
    {
        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Overlay * MathHelper.Clamp(PhaseTime * 4f, 0f, 1f));
        var k = Ui.Pop(PhaseTime * 3.5f);
        var r = RectF.FromCenter(_panel.Center, _panel.W * (0.8f + 0.2f * k), _panel.H * (0.8f + 0.2f * k));
        Ui.Panel(c, r);
        DrawResultBody(c, _panel.Center.X, _panel.Y, _panel.W);
        _resultMenu.Draw(c, Font, Game.Input, Time);
    }

    /// <summary>Upper half of the result panel. The default shows the score and the record.</summary>
    protected virtual void DrawResultBody(Canvas c, float cx, float top, float width)
    {
        var t = Game.Text;
        Font.DrawCentered(c, t.GameOver, new Vector2(cx, top + 80), Font.FitScale(t.GameOver, width - 60, 7f), Palette.Danger);
        Font.DrawCentered(c, t.Score, new Vector2(cx, top + 150), 3f, Palette.TextDim);
        Font.DrawCentered(c, Player.Score.ToString(), new Vector2(cx, top + 210), 9f, Palette.Text);
        if (NewRecord)
        {
            var a = 0.6f + 0.4f * MathF.Sin(Time * 8f);
            Font.DrawCentered(c, t.NewRecord, new Vector2(cx, top + 285), 4.5f, Palette.Gold, alpha: a);
        }
        else
        {
            var best = Game.Save.GetBest(Mode, RunDifficulty);
            Font.DrawCentered(c, $"{t.Best} {best}", new Vector2(cx, top + 285), 3.5f, Palette.Gold);
        }

        var detail = $"{ResultDetail}  -  {t.DifficultyName(RunDifficulty)}";
        if (_submit is not null)
        {
            var pulse = 0.6f + 0.4f * MathF.Sin(Time * 6f);
            Font.DrawCentered(c, t.Sending, new Vector2(cx, top + 330), 3f, Palette.Accent, alpha: pulse);
        }
        else
        {
            Font.DrawCentered(c, detail, new Vector2(cx, top + 330), Font.FitScale(detail, width - 60, 2.5f), Palette.TextDim);
        }
    }
}
