using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>Rules, controls and a legend of every pickup.</summary>
public sealed class HelpScene : Scene
{
    private const float RowHeight = 74f;
    private readonly Button _back;
    private float _top;
    private float _scale = 1f;

    public HelpScene(SnakeGame game) : base(game)
    {
        _back = new Button(game.Text.Back, ButtonStyle.Primary) { Focused = false };
    }

    private static float ContentHeight => 100 + 5 * 34 + 20 + 2 * 34 + 30 + 8 * RowHeight;

    public override void Layout()
    {
        var available = Safe.H - 140f - 40f;
        _scale = MathF.Min(1f, available / ContentHeight);
        _top = Safe.Y + MathF.Max(20f, (available - ContentHeight * _scale) / 2f);
        var width = MathF.Min(560f, Safe.W - 80f);
        _back.Bounds = new RectF(Safe.Center.X - width / 2f, Safe.Bottom - 130f, width, 96f);
    }

    public override void Update(float dt)
    {
        var input = Game.Input;
        if (input.Back || input.Confirm || _back.WasClicked(input))
        {
            Game.PlaySound(Sfx.Click);
            Game.SetScene(new MenuScene(Game));
        }
    }

    public override void Draw(Canvas c)
    {
        Ui.Backdrop(c, Screen, Time);
        var t = Game.Text;
        var k = _scale;
        var cx = Safe.Center.X;
        var y = _top;

        Ui.Title(c, Font, t.HowToPlay, new Vector2(cx, y + 40 * k), Font.FitScale(t.HowToPlay, Safe.W - 60, 8f * k), Palette.Accent);
        y += 100 * k;

        foreach (var line in t.HelpBasics)
        {
            Font.DrawCentered(c, line, new Vector2(cx, y), Font.FitScale(line, Safe.W - 60, 3f * k), Palette.Text);
            y += 34 * k;
        }

        y += 20 * k;
        foreach (var line in t.HelpVersus)
        {
            Font.DrawCentered(c, line, new Vector2(cx, y), Font.FitScale(line, Safe.W - 60, 3f * k), Palette.RivalHead);
            y += 34 * k;
        }

        y += 30 * k;
        var cell = 52f * k;
        var listW = MathF.Min(620f, Safe.W - 40f);
        var left = cx - listW / 2f;
        var view = new BoardView(new RectF(left, 0, cell, cell), cell);

        void Row(Action<Vector2> icon, string name, string help, Color color)
        {
            var center = new Vector2(left + cell / 2f, y + cell / 2f);
            icon(center);
            var textX = left + cell + 24 * k;
            var maxW = listW - cell - 30 * k;
            Font.Draw(c, name, new Vector2(textX, y + 4 * k), Font.FitScale(name, maxW, 3f * k), color);
            Font.Draw(c, help, new Vector2(textX, y + 34 * k), Font.FitScale(help, maxW, 2.5f * k), Palette.TextDim);
            y += RowHeight * k;
        }

        Row(p => BoardRenderer.DrawApple(c, view with { Area = RectF.FromCenter(p, cell, cell) }, new Int2(0, 0), Time, Palette.Apple, Palette.AppleShine, 1f),
            t.Apples, $"+{Rules.AppleScore} {t.Score}", Palette.Apple);
        Row(p => BoardRenderer.DrawApple(c, view with { Area = RectF.FromCenter(p, cell, cell) }, new Int2(0, 0), Time, Palette.Golden, Palette.GoldenShine, 1f, sparkle: true),
            t.GoldenName, t.GoldenHelp, Palette.Gold);
        foreach (var kind in Enum.GetValues<PowerUpKind>())
        {
            Row(p => BoardRenderer.DrawPowerUp(c, view, kind, p, Time, 1f), t.PowerUpName(kind), t.PowerUpHelp[(int)kind], Palette.PowerUp(kind));
        }

        Row(p => BoardRenderer.DrawObstacleCell(c, p.X - cell / 2f, p.Y - cell / 2f, cell, 1f), t.ObstacleName, t.ObstacleHelp, Palette.ObstacleTop);

        _back.Draw(c, Font, Game.Input, Time);
    }
}
