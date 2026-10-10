using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SnakeVs.Core;
using SnakeVs.Game.Graphics;

namespace SnakeVs.Game;

/// <summary>
/// Renders launcher icons and the Play Store graphics with the same drawing code as the game,
/// so the brand stays consistent. Run the desktop build with --store-art &lt;dir&gt;.
/// </summary>
public static class StoreArt
{
    private static readonly (string Folder, int DoubleDensity)[] Densities =
    [
        ("mipmap-mdpi", 2),
        ("mipmap-hdpi", 3),
        ("mipmap-xhdpi", 4),
        ("mipmap-xxhdpi", 6),
        ("mipmap-xxxhdpi", 8),
    ];

    // Snake path in a 5x5 grid, head first, forming an S.
    private static readonly Vector2[] IconSnake =
    [
        new(3, 1), new(3, 2), new(2, 2), new(1, 2), new(1, 3), new(1, 4), new(2, 4), new(3, 4),
    ];

    public static void RenderAll(SnakeGame game, string directory)
    {
        var device = game.GraphicsDevice;
        var canvas = game.Canvas;
        Directory.CreateDirectory(directory);

        Render(device, canvas, 512, 512, Path.Combine(directory, "store", "icon-512.png"), Color.Transparent,
            c => DrawIcon(c, 512, IconShape.Square));
        Render(device, canvas, 1024, 500, Path.Combine(directory, "store", "feature-graphic-1024x500.png"), Palette.Background,
            c => DrawFeature(c, game.Font, 1024, 500));

        foreach (var (folder, k) in Densities)
        {
            // k is twice the density: mdpi = 1x, hdpi = 1.5x, xhdpi = 2x, xxhdpi = 3x, xxxhdpi = 4x.
            var legacy = 48 * k / 2;
            var adaptive = 108 * k / 2;
            var res = Path.Combine(directory, "android", folder);

            Render(device, canvas, legacy, legacy, Path.Combine(res, "ic_launcher.png"), Color.Transparent,
                c => DrawIcon(c, legacy, IconShape.Rounded));
            Render(device, canvas, legacy, legacy, Path.Combine(res, "ic_launcher_round.png"), Color.Transparent,
                c => DrawIcon(c, legacy, IconShape.Circle));
            Render(device, canvas, adaptive, adaptive, Path.Combine(res, "ic_launcher_foreground.png"), Color.Transparent,
                c => DrawAdaptiveForeground(c, adaptive, monochrome: false));
            Render(device, canvas, adaptive, adaptive, Path.Combine(res, "ic_launcher_monochrome.png"), Color.Transparent,
                c => DrawAdaptiveForeground(c, adaptive, monochrome: true));
        }
    }

    private enum IconShape
    {
        Square,
        Rounded,
        Circle,
    }

    private static void DrawIcon(Canvas c, float size, IconShape shape)
    {
        var full = new RectF(0, 0, size, size);
        switch (shape)
        {
            case IconShape.Square:
                c.FillRect(full, Palette.Background);
                break;
            case IconShape.Rounded:
                c.FillRoundRect(full.Inflate(-size * 0.04f), size * 0.2f, Palette.Background);
                break;
            case IconShape.Circle:
                c.FillCircle(full.Center, size * 0.46f, Palette.Background);
                break;
        }

        var inner = shape == IconShape.Square ? 0.74f : 0.62f;
        DrawIconArt(c, RectF.FromCenter(full.Center, size * inner, size * inner), withBoard: true, monochrome: false);
    }

    private static void DrawAdaptiveForeground(Canvas c, float size, bool monochrome)
    {
        // Adaptive icons: 108dp canvas, only the central 66dp circle is guaranteed visible.
        var zone = size * 66f / 108f;
        var art = RectF.FromCenter(new Vector2(size / 2f), zone * 0.86f, zone * 0.86f);
        DrawIconArt(c, art, withBoard: !monochrome, monochrome);
    }

    private static void DrawIconArt(Canvas c, RectF area, bool withBoard, bool monochrome)
    {
        var view = BoardView.Fit(area, 5, 5);
        if (withBoard)
        {
            var b = view.Area.Inflate(view.Cell * 0.12f);
            c.FillRoundRect(b, view.Cell * 0.5f, Palette.BoardBackground);
            for (var y = 0; y < 5; y++)
            {
                for (var x = (y % 2); x < 5; x += 2)
                {
                    var cell = new RectF(view.Area.X + x * view.Cell, view.Area.Y + y * view.Cell, view.Cell, view.Cell);
                    if (x == 0 || y == 0 || x == 4 || y == 4) continue;
                    c.FillRect(cell, Palette.BoardCellAlt);
                }
            }
        }

        var head = monochrome ? Color.White : Palette.PlayerHead;
        var tail = monochrome ? Color.White : Palette.PlayerTail;
        BoardRenderer.DrawPath(c, IconSnake, view, head, tail, 1f);
        if (monochrome)
        {
            c.FillRoundRect(RectF.FromCenter(view.CellCenter(IconSnake[0]), view.Cell * 0.92f, view.Cell * 0.92f), view.Cell * 0.34f, Color.White);
            c.FillCircle(view.CellCenter(new Vector2(3, 0)), view.Cell * 0.36f, Color.White);
            return;
        }

        BoardRenderer.DrawHead(c, view.CellCenter(IconSnake[0]), view.Cell, head, Direction.Up, true, false, false, 1f, 0f);
        BoardRenderer.DrawApple(c, view, new Int2(3, 0), 0f, Palette.Apple, Palette.AppleShine, 1f);
    }

    private static void DrawFeature(Canvas c, PixelFont font, float w, float h)
    {
        c.FillRect(0, 0, w, h, Palette.Background);
        for (var y = 12f; y < h; y += 40f)
        {
            for (var x = 12f; x < w; x += 40f) c.FillRect(x, y, 3, 3, Palette.BoardCellAlt);
        }

        // Right: a slice of a match.
        var view = BoardView.Fit(new RectF(w * 0.5f, 40, w * 0.47f, h - 80), 12, 10);
        var area = view.Area;
        c.FillRoundRect(area.Inflate(6), 14, Palette.BoardBorder);
        c.FillRect(area, Palette.BoardBackground);
        for (var y = 0; y < 10; y++)
        {
            for (var x = (y % 2); x < 12; x += 2) c.FillRect(area.X + x * view.Cell, area.Y + y * view.Cell, view.Cell, view.Cell, Palette.BoardCellAlt);
        }

        foreach (var o in new[] { new Int2(5, 4), new Int2(6, 4), new Int2(5, 5), new Int2(6, 5), new Int2(1, 8), new Int2(2, 8), new Int2(3, 8), new Int2(10, 1) })
        {
            BoardRenderer.DrawObstacleCell(c, area.X + o.X * view.Cell, area.Y + o.Y * view.Cell, view.Cell, 1f);
        }

        Vector2[] green = [new(3, 3), new(3, 4), new(3, 5), new(3, 6), new(4, 6), new(5, 6), new(6, 6), new(7, 6), new(7, 7), new(7, 8)];
        Vector2[] pink = [new(8, 2), new(7, 2), new(6, 2), new(5, 2), new(5, 1), new(4, 1), new(3, 1), new(2, 1)];
        BoardRenderer.DrawPath(c, pink, view, Palette.RivalHead, Palette.RivalTail, 1f);
        BoardRenderer.DrawHead(c, view.CellCenter(pink[0]), view.Cell, Palette.RivalHead, Direction.Right, true, false, false, 1f, 0f);
        BoardRenderer.DrawPath(c, green, view, Palette.PlayerHead, Palette.PlayerTail, 1f);
        BoardRenderer.DrawHead(c, view.CellCenter(green[0]), view.Cell, Palette.PlayerHead, Direction.Up, true, true, false, 1f, 0.3f);
        BoardRenderer.DrawApple(c, view, new Int2(1, 2), 0.3f, Palette.Apple, Palette.AppleShine, 1f);
        BoardRenderer.DrawApple(c, view, new Int2(10, 6), 0.9f, Palette.Golden, Palette.GoldenShine, 1f, sparkle: true);
        BoardRenderer.DrawPowerUp(c, view, PowerUpKind.Ghost, view.CellCenter(new Int2(9, 4)), 0f, 1f);
        BoardRenderer.DrawPowerUp(c, view, PowerUpKind.Slow, view.CellCenter(new Int2(1, 5)), 0f, 1f);

        // Left: logo.
        var cx = w * 0.25f;
        font.DrawCentered(c, "SNAKE", new Vector2(cx, 160), 15f, Palette.PlayerHead);
        var badge = RectF.FromCenter(new Vector2(cx, 290), 170, 92);
        c.FillRoundRect(badge.Offset(0, 8), 22, Palette.TextShadow * 0.6f);
        c.FillRoundRect(badge, 22, Palette.RivalHead);
        font.DrawCentered(c, "VS", badge.Center, 10f, Palette.Pupil, shadow: false);
        font.DrawCentered(c, "OBSTÁCULOS + PODERES", new Vector2(cx, 395), 3.2f, Palette.TextDim);
    }

    private static void Render(GraphicsDevice device, Canvas canvas, int width, int height, string path, Color clear, Action<Canvas> draw)
    {
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        device.SetRenderTarget(target);
        device.Clear(clear);
        canvas.Begin(Matrix.Identity);
        draw(canvas);
        canvas.End();
        device.SetRenderTarget(null);

        // Render targets hold premultiplied colors; PNG wants straight alpha.
        var data = new Color[width * height];
        target.GetData(data);
        for (var i = 0; i < data.Length; i++)
        {
            var p = data[i];
            if (p.A is 0 or 255) continue;
            var a = p.A / 255f;
            data[i] = new Color((byte)Math.Min(255, p.R / a), (byte)Math.Min(255, p.G / a), (byte)Math.Min(255, p.B / a), p.A);
        }

        using var output = new Texture2D(device, width, height);
        output.SetData(data);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        output.SaveAsPng(stream, width, height);
    }
}
