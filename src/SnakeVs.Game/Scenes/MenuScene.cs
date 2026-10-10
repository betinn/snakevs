using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>Title screen. Two CPU snakes battle in the background as an attract mode.</summary>
public sealed class MenuScene : Scene
{
    private readonly ButtonList _buttons = new();
    private readonly Button _classic;
    private readonly Button _levels;
    private readonly Button _versus;
    private readonly Button _options;
    private readonly Button _help;
    private GameSession _attract;
    private BoardView _attractView;
    private float _restartTimer;
    private float _titleY;
    private float _footerY;

    public MenuScene(SnakeGame game) : base(game)
    {
        var t = game.Text;
        _classic = _buttons.Add(new Button(t.Classic, ButtonStyle.Primary));
        _levels = _buttons.Add(new Button(t.Levels));
        _versus = _buttons.Add(new Button(t.Versus) { Accent = Palette.RivalHead });
        _options = _buttons.Add(new Button(t.Options));
        _help = _buttons.Add(new Button(t.HowToPlay));
        _attract = NewAttract();
    }

    public override void Layout()
    {
        var safe = Safe;
        _attractView = BoardView.Fit(safe.Inflate(-24), Rules.BoardWidth, Rules.BoardHeight);

        var width = MathF.Min(560f, safe.W - 80f);
        var height = 92f;
        var gap = 18f;
        var stack = height * 5 + gap * 4;
        var titleBlock = 330f;
        var footer = 110f;
        var free = safe.H - titleBlock - stack - footer;
        var top = safe.Y + MathF.Max(30f, free * 0.45f);

        _titleY = top + 120f;
        _buttons.LayoutColumn(safe.Center.X, top + titleBlock, width, height, gap);
        _footerY = MathF.Min(safe.Bottom - 70f, top + titleBlock + stack + 60f + MathF.Max(0f, free * 0.2f));
    }

    public override void Update(float dt)
    {
        var input = Game.Input;

        _attract.ClearEvents();
        _attract.Update(dt * 1000.0);
        if (_attract.State != SessionState.Playing)
        {
            _restartTimer += dt;
            if (_restartTimer > 1.2f)
            {
                _attract = NewAttract();
                _restartTimer = 0;
            }
        }

        if (input.Back)
        {
            Game.Quit();
            return;
        }

        var clicked = _buttons.Update(input);
        if (clicked is null) return;
        Game.PlaySound(Sfx.Click);

        if (clicked == _classic) Game.SetScene(new PlayScene(Game, GameMode.Endless));
        else if (clicked == _levels) Game.SetScene(new PlayScene(Game, GameMode.Levels));
        else if (clicked == _versus) Game.SetScene(new PlayScene(Game, GameMode.Versus));
        else if (clicked == _options) Game.SetScene(new OptionsScene(Game));
        else if (clicked == _help) Game.SetScene(new HelpScene(Game));
    }

    public override void Draw(Canvas c)
    {
        Ui.Backdrop(c, Screen, Time);
        BoardRenderer.Draw(c, _attract, _attractView, Time, alpha: 0.35f);
        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Background * 0.45f);

        var cx = Safe.Center.X;
        var bob = MathF.Sin(Time * 2f) * 6f;
        var titleScale = Font.FitScale("SNAKE", Safe.W - 60, 18f);
        Ui.Title(c, Font, "SNAKE", new Vector2(cx, _titleY - 40 + bob), titleScale, Palette.PlayerHead);

        var badge = RectF.FromCenter(new Vector2(cx, _titleY + 95 + bob), 190, 100);
        c.FillRoundRect(badge.Offset(0, 8), 24, Palette.TextShadow * 0.6f);
        c.FillRoundRect(badge, 24, Palette.RivalHead);
        Font.DrawCentered(c, "VS", badge.Center, 11f, Palette.Pupil, shadow: false);

        var tagScale = Font.FitScale(Game.Text.Tagline, Safe.W - 60, 3f);
        Font.DrawCentered(c, Game.Text.Tagline, new Vector2(cx, _titleY + 180), tagScale, Palette.TextDim);

        _buttons.Draw(c, Font, Game.Input, Time);

        var save = Game.Save;
        var t = Game.Text;
        var classicBest = save.GetBest(GameMode.Endless, save.Difficulty);
        var levelsBest = save.GetBest(GameMode.Levels, save.Difficulty);
        var line1 = $"{t.Classic} {classicBest}  -  {t.Levels} {levelsBest}";
        var line2 = $"VS CPU {t.WinsLosses} {save.VersusWins}/{save.VersusLosses}  -  {t.DifficultyName(save.Difficulty)}";
        Font.DrawCentered(c, line1, new Vector2(cx, _footerY), Font.FitScale(line1, Safe.W - 60, 3f), Palette.Gold);
        Font.DrawCentered(c, line2, new Vector2(cx, _footerY + 38), Font.FitScale(line2, Safe.W - 60, 2.5f), Palette.TextDim);
        Font.DrawCentered(c, "V" + SnakeGame.VersionName, new Vector2(Safe.Right - 60, Safe.Bottom - 24), 2f, Palette.TextDim * 0.7f, shadow: false);
    }

    private static GameSession NewAttract()
    {
        var s = new GameSession(GameMode.Versus, Difficulty.Normal, Environment.TickCount);
        s.Player.IsAiControlled = true;
        s.Player.AiMistakeChance = 0.03;
        return s;
    }
}
