using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;

namespace SnakeVs.Game.Scenes;

/// <summary>The game screen of the classic modes (endless, levels and the duel against the CPU).</summary>
public sealed class PlayScene : RunScene
{
    private const float BannerSeconds = 1.9f;

    private readonly GameSession _session;

    public PlayScene(SnakeGame game, GameMode mode, bool autopilot = false, int startLevel = 1) : base(game, mode, autopilot)
    {
        _session = new GameSession(mode, game.Save.Difficulty, Environment.TickCount, startLevel);
        if (autopilot)
        {
            _session.Player.IsAiControlled = true;
            _session.Player.AiMistakeChance = 0;
        }
    }

    public GameSession Session => _session;

    protected override Snake Player => _session.Player;

    protected override Difficulty RunDifficulty => _session.Difficulty;

    protected override string ReadyTitle => Mode switch
    {
        GameMode.Versus => $"{Game.Text.Versus} - {Game.Text.DifficultyName(_session.Difficulty)}",
        GameMode.Endless => Game.Text.Classic,
        _ => $"{Game.Text.Level} {_session.Level}",
    };

    protected override Color ReadyColor => Mode == GameMode.Versus ? Palette.RivalHead : Palette.Accent;

    protected override string ResultDetail => Mode == GameMode.Endless
        ? $"{Game.Text.Apples} {_session.Player.ApplesEaten}"
        : $"{Game.Text.Level} {_session.Level}";

    protected override void Simulate(float dt)
    {
        _session.Update(dt * 1000.0);
        ProcessEvents();
        switch (_session.State)
        {
            case SessionState.GameOver:
            case SessionState.MatchOver:
                EndRun();
                break;
            case SessionState.LevelCleared:
                BeginInterlude();
                break;
        }
    }

    protected override void KillPlayer() => _session.KillPlayer();

    protected override ScoreSubmission BuildSubmission(string name)
    {
        var p = _session.Player;
        return new ScoreSubmission(Mode, _session.Difficulty, name, p.Score, p.ApplesEaten, _session.Level, _session.StepCount);
    }

    protected override void UpdateInterlude()
    {
        if (PhaseTime < BannerSeconds) return;
        _session.AdvanceLevel();
        _session.ClearEvents();
        Particles.Clear();
        EndInterlude();
    }

    protected override void RecordResult()
    {
        if (Mode != GameMode.Versus)
        {
            base.RecordResult();
            return;
        }

        Game.Save.RecordVersus(_session.Outcome);
        NewRecord = false;
        Game.PlaySound(_session.Outcome == MatchOutcome.PlayerWon ? Sfx.Win : Sfx.Lose);
    }

    protected override void DrawBoard(Canvas c) => BoardRenderer.Draw(c, _session, View, Time, shake: Shake);

    private void ProcessEvents()
    {
        foreach (var e in _session.Events) Feedback(e, View.CellCenter(e.Cell));
        _session.ClearEvents();
    }

    protected override void DrawHud(Canvas c)
    {
        var t = Game.Text;
        var player = _session.Player;
        var x = Hud.X + 4;
        var y = Hud.Y;

        if (Mode != GameMode.Versus) DrawScore(c);

        if (Mode == GameMode.Endless)
        {
            var cx = Hud.Center.X + 10;
            Font.DrawCentered(c, $"{t.Speed} {_session.SpeedLevel}", new Vector2(cx, y + 16), 3.5f, Palette.Text);
            Font.DrawCentered(c, $"{t.Apples} {player.ApplesEaten}", new Vector2(cx, y + 52), 2.5f, Palette.Apple);
            DrawEffects(c, new Vector2(cx, y + 84), player, _session.SlowMs);
        }
        else if (Mode == GameMode.Levels)
        {
            var cx = Hud.Center.X + 10;
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

            DrawEffects(c, new Vector2(cx, y + 84), player, _session.SlowMs);
        }
        else
        {
            var rival = _session.Rival!;
            Font.Draw(c, t.You, new Vector2(x, y + 6), 2.5f, Palette.PlayerHead);
            Font.DrawShadowed(c, player.ApplesEaten.ToString(), new Vector2(x, y + 30), 6f, Palette.Text);

            var rx = PauseBounds.X - 20;
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
            DrawEffects(c, new Vector2(cx, y + 86), player, _session.SlowMs);
        }
    }

    private static void DrawBar(Canvas c, RectF r, float fraction, Color color)
    {
        c.FillRoundRect(r, r.H / 2, Palette.BoardBorder);
        var w = MathF.Max(r.H, r.W * MathHelper.Clamp(fraction, 0f, 1f));
        if (fraction > 0) c.FillRoundRect(r with { W = w }, r.H / 2, color);
    }

    protected override void DrawInterlude(Canvas c)
    {
        var t = Game.Text;
        var k = Ui.Pop(PhaseTime * 3f);
        var band = RectF.FromCenter(View.Area.Center, View.Area.W * k, 240);
        c.FillRect(band, Palette.Overlay);
        if (k < 0.6f) return;
        Font.DrawCentered(c, t.LevelCleared, new Vector2(band.Center.X, band.Y + 70), Font.FitScale(t.LevelCleared, band.W - 40, 6f), Palette.Accent);
        Font.DrawCentered(c, $"{t.Bonus} +{Rules.LevelBonus * _session.Level}", new Vector2(band.Center.X, band.Y + 140), 4f, Palette.Gold);
        Font.DrawCentered(c, $"{t.Level} {_session.Level + 1}", new Vector2(band.Center.X, band.Y + 195), 3f, Palette.TextDim);
    }

    protected override void DrawResultBody(Canvas c, float cx, float top, float width)
    {
        if (Mode != GameMode.Versus)
        {
            base.DrawResultBody(c, cx, top, width);
            return;
        }

        var t = Game.Text;
        var (title, color) = _session.Outcome switch
        {
            MatchOutcome.PlayerWon => (t.YouWin, Palette.PlayerHead),
            MatchOutcome.RivalWon => (t.CpuWins, Palette.RivalHead),
            _ => (t.Draw, Palette.Text),
        };
        Font.DrawCentered(c, title, new Vector2(cx, top + 80), Font.FitScale(title, width - 60, 7f), color);
        Font.DrawCentered(c, t.Apples, new Vector2(cx, top + 150), 3f, Palette.TextDim);
        var you = _session.Player.ApplesEaten.ToString();
        var cpu = _session.Rival!.ApplesEaten.ToString();
        Font.DrawCentered(c, you, new Vector2(cx - 110, top + 215), 9f, Palette.PlayerHead);
        Font.DrawCentered(c, "X", new Vector2(cx, top + 215), 5f, Palette.TextDim);
        Font.DrawCentered(c, cpu, new Vector2(cx + 110, top + 215), 9f, Palette.RivalHead);
        var save = Game.Save;
        Font.DrawCentered(c, $"{t.WinsLosses} {save.VersusWins}/{save.VersusLosses}", new Vector2(cx, top + 300), 3f, Palette.Gold);
    }
}
