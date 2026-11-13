using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>
/// Title screen: the difficulty picker, one button per mode and the records for the difficulty picked. Two CPU snakes
/// battle in the background as an attract mode.
/// </summary>
public sealed class MenuScene : Scene
{
    private readonly ButtonList _buttons = new();
    private readonly Button _journey;
    private readonly Button _classic;
    private readonly Button _levels;
    private readonly Button _versus;
    private readonly Button? _ranking;
    private readonly Button _options;
    private readonly Button _help;

    /// <summary>The three segments of the difficulty picker, indexed by <see cref="Difficulty"/>. Drawn by this scene.</summary>
    private readonly Button[] _difficulties = new Button[3];
    private GameSession _attract;
    private BoardView _attractView;
    private float _restartTimer;
    private float _titleY;
    private float _pickerCaptionY;
    private float _footerY;

    public MenuScene(SnakeGame game) : base(game)
    {
        var t = game.Text;
        _journey = _buttons.Add(new Button(t.Journey, ButtonStyle.Primary));
        _classic = _buttons.Add(new Button(t.Classic));
        _levels = _buttons.Add(new Button(t.Levels));
        _versus = _buttons.Add(new Button(t.Versus) { Accent = Palette.RivalHead });
        if (game.Platform.Leaderboard is not null) _ranking = _buttons.Add(new Button(t.Ranking) { Accent = Palette.Gold });
        _options = _buttons.Add(new Button(t.Options));
        _help = _buttons.Add(new Button(t.HowToPlay));
        _difficulties[(int)Difficulty.Easy] = new Button(t.Easy);
        _difficulties[(int)Difficulty.Normal] = new Button(t.Normal);
        _difficulties[(int)Difficulty.Hard] = new Button(t.Hard);
        _attract = NewAttract();
    }

    public override void Layout()
    {
        var safe = Safe;
        _attractView = BoardView.Fit(safe.Inflate(-24), Rules.BoardWidth, Rules.BoardHeight);

        var width = MathF.Min(560f, safe.W - 80f);
        var count = _buttons.Buttons.Count;
        var height = count > 6 ? 84f : 92f;
        var gap = count > 6 ? 14f : 18f;
        var stack = height * count + gap * (count - 1);
        var titleBlock = 330f;
        const float pickerCaption = 32f;
        const float pickerHeight = 72f;
        var pickerBlock = pickerCaption + pickerHeight + gap + 6f;
        var footer = 110f;
        var free = safe.H - titleBlock - pickerBlock - stack - footer;
        var top = safe.Y + MathF.Max(30f, free * 0.45f);

        _titleY = top + 120f;
        _pickerCaptionY = top + titleBlock + 12f;
        var segment = width / 3f;
        for (var i = 0; i < 3; i++)
        {
            _difficulties[i].Bounds = new RectF(safe.Center.X - width / 2f + i * segment, top + titleBlock + pickerCaption, segment, pickerHeight);
        }

        _buttons.LayoutColumn(safe.Center.X, top + titleBlock + pickerBlock, width, height, gap);
        _footerY = MathF.Min(safe.Bottom - 70f, top + titleBlock + pickerBlock + stack + 60f + MathF.Max(0f, free * 0.2f));
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

        // Difficulty: tap a segment, or left/right on the keyboard.
        var current = (int)Game.Save.Difficulty;
        var wanted = current;
        if (input.KeyDirection == Direction.Left) wanted = Math.Max(0, current - 1);
        else if (input.KeyDirection == Direction.Right) wanted = Math.Min(2, current + 1);
        for (var i = 0; i < 3; i++)
        {
            if (_difficulties[i].WasClicked(input)) wanted = i;
        }

        if (wanted != current)
        {
            Game.Save.Difficulty = (Difficulty)wanted;
            Game.PlaySound(Sfx.Click);
            Game.Vibrate(8);
            Game.Persist();
            return;
        }

        var clicked = _buttons.Update(input);
        if (clicked is null) return;
        Game.PlaySound(Sfx.Click);

        if (clicked == _journey) Game.SetScene(RunScene.Create(Game, GameMode.Journey));
        else if (clicked == _classic) Game.SetScene(RunScene.Create(Game, GameMode.Endless));
        else if (clicked == _levels) Game.SetScene(RunScene.Create(Game, GameMode.Levels));
        else if (clicked == _versus) Game.SetScene(RunScene.Create(Game, GameMode.Versus));
        else if (clicked == _ranking && Game.Platform.Leaderboard is { } board) Game.SetScene(new RankingScene(Game, board, GameMode.Journey, Game.Save.Difficulty));
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

        DrawDifficultyPicker(c);
        _buttons.Draw(c, Font, Game.Input, Time);

        var save = Game.Save;
        var t = Game.Text;
        var journeyBest = save.GetBest(GameMode.Journey, save.Difficulty);
        var classicBest = save.GetBest(GameMode.Endless, save.Difficulty);
        var levelsBest = save.GetBest(GameMode.Levels, save.Difficulty);
        var line1 = $"{t.Journey} {journeyBest}  -  {t.Classic} {classicBest}  -  {t.Levels} {levelsBest}";
        var line2 = $"VS CPU {t.WinsLosses} {save.VersusWins}/{save.VersusLosses}";
        Font.DrawCentered(c, line1, new Vector2(cx, _footerY), Font.FitScale(line1, Safe.W - 60, 3f), Palette.Gold);
        Font.DrawCentered(c, line2, new Vector2(cx, _footerY + 38), Font.FitScale(line2, Safe.W - 60, 2.5f), Palette.TextDim);
        Font.DrawCentered(c, "V" + SnakeGame.VersionName, new Vector2(Safe.Right - 60, Safe.Bottom - 24), 2f, Palette.TextDim * 0.7f, shadow: false);
    }

    /// <summary>One track with three segments; the one in use is filled with its color (green, gold, red).</summary>
    private void DrawDifficultyPicker(Canvas c)
    {
        var input = Game.Input;
        var first = _difficulties[0].Bounds;
        var track = new RectF(first.X, first.Y, first.W * 3f, first.H);
        Font.DrawCentered(c, Game.Text.Difficulty, new Vector2(track.Center.X, _pickerCaptionY), 2.5f, Palette.TextDim);
        c.FillRoundRect(track.Offset(0, 6), 20, Palette.TextShadow * 0.6f);
        c.FillRoundRect(track, 20, Palette.Button);

        for (var i = 0; i < 3; i++)
        {
            var button = _difficulties[i];
            var selected = (int)Game.Save.Difficulty == i;
            var r = button.Bounds.Inflate(button.IsPressed(input) ? -9 : -6);
            if (selected) c.FillRoundRect(r, 15, DifficultyColor((Difficulty)i));
            else if (button.IsPressed(input)) c.FillRoundRect(r, 15, Palette.ButtonPressed);
            var scale = Font.FitScale(button.Label, r.W - 16, 3.5f);
            Font.DrawCentered(c, button.Label, r.Center, scale, selected ? Palette.ButtonPrimaryText : Palette.TextDim, shadow: false);
        }
    }

    private static Color DifficultyColor(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Easy => Palette.PlayerHead,
        Difficulty.Hard => Palette.Danger,
        _ => Palette.Gold,
    };

    private static GameSession NewAttract()
    {
        var s = new GameSession(GameMode.Versus, Difficulty.Normal, Environment.TickCount);
        s.Player.IsAiControlled = true;
        s.Player.AiMistakeChance = 0.03;
        return s;
    }
}
