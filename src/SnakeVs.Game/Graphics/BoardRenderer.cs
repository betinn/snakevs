using Microsoft.Xna.Framework;
using SnakeVs.Core;

namespace SnakeVs.Game.Graphics;

/// <summary>Maps grid cells to virtual screen space.</summary>
public readonly record struct BoardView(RectF Area, float Cell)
{
    public Vector2 CellCenter(Int2 c) => new(Area.X + (c.X + 0.5f) * Cell, Area.Y + (c.Y + 0.5f) * Cell);

    public Vector2 CellCenter(Vector2 c) => new(Area.X + (c.X + 0.5f) * Cell, Area.Y + (c.Y + 0.5f) * Cell);

    public static BoardView Fit(RectF available, int columns, int rows)
    {
        var cell = MathF.Floor(MathF.Min(available.W / columns, available.H / rows));
        var w = cell * columns;
        var h = cell * rows;
        return new BoardView(new RectF(MathF.Round(available.X + (available.W - w) / 2f), MathF.Round(available.Y + (available.H - h) / 2f), w, h), cell);
    }
}

/// <summary>Draws a <see cref="GameSession"/>: board, obstacles, pickups and smoothly animated snakes.</summary>
public static class BoardRenderer
{
    public static void Draw(Canvas c, GameSession session, BoardView view, float time, float alpha = 1f, float shake = 0f)
    {
        if (shake > 0)
        {
            var dx = MathF.Sin(time * 91f) * shake;
            var dy = MathF.Cos(time * 73f) * shake;
            view = view with { Area = view.Area.Offset(dx, dy) };
        }

        DrawBackground(c, session.Board, view, alpha);
        DrawObstacles(c, session.Board, view, alpha);
        DrawApple(c, view, session.Apple, time, Palette.Apple, Palette.AppleShine, alpha);

        if (session.Golden is { } golden)
        {
            var blink = golden.RemainingMs < 2000 && MathF.Sin(time * 22f) < 0;
            if (!blink) DrawApple(c, view, golden.Cell, time, Palette.Golden, Palette.GoldenShine, alpha, sparkle: true);
            c.DrawArc(view.CellCenter(golden.Cell), view.Cell * 0.62f, MathF.Max(2f, view.Cell * 0.07f), (float)golden.Fraction, Palette.Golden * (0.55f * alpha));
        }

        if (session.PowerUp is { } power)
        {
            var blink = power.RemainingMs < 2500 && MathF.Sin(time * 20f) < 0;
            if (!blink) DrawPowerUp(c, view, power.Kind, view.CellCenter(power.Cell), time, alpha);
        }

        // Rival first so the player is always on top.
        for (var i = session.Snakes.Count - 1; i >= 0; i--)
        {
            DrawSnake(c, session.Snakes[i], view, session.StepAlpha, time, alpha);
        }
    }

    public static void DrawBackground(Canvas c, Board board, BoardView view, float alpha)
    {
        var area = view.Area;
        c.FillRoundRect(area.Inflate(6), 14, Palette.BoardBorder * alpha);
        c.FillRect(area, Palette.BoardBackground * alpha);
        for (var y = 0; y < board.Height; y++)
        {
            for (var x = (y % 2); x < board.Width; x += 2)
            {
                c.FillRect(area.X + x * view.Cell, area.Y + y * view.Cell, view.Cell, view.Cell, Palette.BoardCellAlt * alpha);
            }
        }
    }

    public static void DrawObstacles(Canvas c, Board board, BoardView view, float alpha)
    {
        foreach (var o in board.Obstacles)
        {
            DrawObstacleCell(c, view.Area.X + o.X * view.Cell, view.Area.Y + o.Y * view.Cell, view.Cell, alpha);
        }
    }

    public static void DrawObstacleCell(Canvas c, float x, float y, float cell, float alpha)
    {
        var inset = MathF.Max(1f, cell * 0.06f);
        var radius = cell * 0.18f;
        var r = new RectF(x + inset, y + inset, cell - inset * 2, cell - inset * 2);
        c.FillRoundRect(r.Offset(0, cell * 0.08f), radius, Palette.ObstacleShadow * alpha);
        c.FillRoundRect(r, radius, Palette.Obstacle * alpha);
        c.FillRoundRect(new RectF(r.X + cell * 0.12f, r.Y + cell * 0.1f, r.W - cell * 0.24f, cell * 0.16f), cell * 0.08f, Palette.ObstacleTop * alpha);
    }

    public static void DrawApple(Canvas c, BoardView view, Int2 cell, float time, Color body, Color shine, float alpha, bool sparkle = false)
    {
        var center = view.CellCenter(cell);
        var s = view.Cell;
        var pulse = 1f + MathF.Sin(time * 5f) * 0.05f;
        var r = s * 0.36f * pulse;
        c.DrawGlow(center, s * 1.1f, body * (0.28f * alpha));
        c.FillRect(center.X - s * 0.03f, center.Y - r - s * 0.12f, s * 0.07f, s * 0.16f, Palette.Stem * alpha);
        c.FillCircle(center + new Vector2(s * 0.13f, -r - s * 0.04f), s * 0.11f, Palette.Leaf * alpha);
        c.FillCircle(center + new Vector2(0, s * 0.03f), r, body * alpha);
        c.FillCircle(center + new Vector2(-r * 0.38f, -r * 0.3f), r * 0.26f, shine * (0.75f * alpha));

        if (!sparkle) return;
        var k = (MathF.Sin(time * 6f) + 1f) / 2f;
        var p = center + new Vector2(r * 0.9f, -r * 0.9f);
        var len = s * (0.12f + 0.12f * k);
        c.FillRect(p.X - len, p.Y - 1, len * 2, 2, Color.White * alpha);
        c.FillRect(p.X - 1, p.Y - len, 2, len * 2, Color.White * alpha);
    }

    public static void DrawPowerUp(Canvas c, BoardView view, PowerUpKind kind, Vector2 center, float time, float alpha)
    {
        var s = view.Cell;
        var color = Palette.PowerUp(kind);
        center.Y += MathF.Sin(time * 4f) * s * 0.06f;
        var size = s * 0.86f;
        c.DrawGlow(center, s * 1.2f, color * (0.35f * alpha));
        c.FillRoundRect(RectF.FromCenter(center, size, size), s * 0.22f, color * alpha);
        c.FillRoundRect(RectF.FromCenter(center, size * 0.78f, size * 0.78f), s * 0.16f, Color.Lerp(color, Color.White, 0.25f) * alpha);
        c.BitmapCentered(Icons.PowerUp(kind), center, s * 0.085f, Palette.Pupil * alpha);
    }

    public static void DrawSnake(Canvas c, Snake snake, BoardView view, float stepAlpha, float time, float alpha)
    {
        if (snake.Length == 0) return;
        var (headColor, tailColor) = Palette.SnakeColors(snake.Id);

        var opacity = alpha;
        if (snake.IsGhost)
        {
            var ending = snake.GhostMs < 1500 && MathF.Sin(time * 25f) < 0;
            opacity *= ending ? 0.8f : 0.45f;
        }

        if (!snake.Alive)
        {
            var flash = MathF.Sin(time * 30f) > 0;
            headColor = flash ? Color.White : Palette.TextDim;
            tailColor = flash ? Palette.TextDim : Palette.ObstacleTop;
            stepAlpha = 1f;
        }

        // Build the visual path: interpolated head, static body cells, interpolated tail end.
        var body = snake.Body;
        var n = body.Count;
        Span<Vector2> points = stackalloc Vector2[n + 1];
        var count = 0;

        var head = ToVec(body[0]);
        if (n > 1 && snake.Alive && body[0].IsAdjacentTo(body[1]))
        {
            head = Vector2.Lerp(ToVec(body[1]), head, stepAlpha);
        }

        points[count++] = head;
        for (var i = 1; i < n; i++) points[count++] = ToVec(body[i]);
        if (snake.Alive && snake.PreviousTail != body[^1] && snake.PreviousTail.IsAdjacentTo(body[^1]))
        {
            points[count++] = Vector2.Lerp(ToVec(snake.PreviousTail), ToVec(body[^1]), stepAlpha);
        }

        DrawPath(c, points[..count], view, headColor, tailColor, opacity);
        DrawHead(c, view.CellCenter(points[0]), view.Cell, headColor, snake.Heading, snake.Alive, snake.Shields > 0, snake.HasDouble, opacity, time);
    }

    /// <summary>Draws a snake body through cell-space points (head first) as one continuous rounded tube.</summary>
    public static void DrawPath(Canvas c, ReadOnlySpan<Vector2> points, BoardView view, Color headColor, Color tailColor, float opacity)
    {
        var count = points.Length;
        var cell = view.Cell;
        var size = cell * 0.82f;
        var radius = cell * 0.3f;

        for (var i = count - 1; i >= 0; i--)
        {
            var t = count <= 1 ? 0f : i / (float)(count - 1);
            var color = Color.Lerp(headColor, tailColor, t) * opacity;
            var p = view.CellCenter(points[i]);

            if (i > 0)
            {
                var prev = points[i - 1];
                var d = prev - points[i];
                if (MathF.Abs(d.X) + MathF.Abs(d.Y) <= 1.01f)
                {
                    var q = view.CellCenter(prev);
                    var minX = MathF.Min(p.X, q.X) - size / 2f;
                    var minY = MathF.Min(p.Y, q.Y) - size / 2f;
                    var maxX = MathF.Max(p.X, q.X) + size / 2f;
                    var maxY = MathF.Max(p.Y, q.Y) + size / 2f;
                    var bridge = new RectF(minX, minY, maxX - minX, maxY - minY);
                    c.FillRoundRect(bridge, radius, color);
                    continue;
                }
            }

            c.FillRoundRect(RectF.FromCenter(p, size, size), radius, color);
        }
    }

    public static void DrawHead(Canvas c, Vector2 center, float cell, Color color, Direction heading, bool alive, bool shield, bool doublePoints, float opacity, float time)
    {
        c.DrawGlow(center, cell * 1.4f, color * (0.22f * opacity));
        c.FillRoundRect(RectF.FromCenter(center, cell * 0.92f, cell * 0.92f), cell * 0.34f, color * opacity);

        var forward = DirVec(heading);
        var side = new Vector2(-forward.Y, forward.X);
        var eyeBase = center + forward * (cell * 0.12f);
        var eyeR = cell * 0.14f;
        for (var sign = -1f; sign <= 1f; sign += 2f)
        {
            var eye = eyeBase + side * (cell * 0.2f * sign);
            c.FillCircle(eye, eyeR, Palette.Eye * opacity);
            if (alive)
            {
                c.FillCircle(eye + forward * (eyeR * 0.35f), eyeR * 0.55f, Palette.Pupil * opacity);
            }
            else
            {
                c.Line(eye - new Vector2(eyeR * 0.6f), eye + new Vector2(eyeR * 0.6f), MathF.Max(1.5f, cell * 0.05f), Palette.Pupil * opacity);
                c.Line(eye + new Vector2(-eyeR * 0.6f, eyeR * 0.6f), eye + new Vector2(eyeR * 0.6f, -eyeR * 0.6f), MathF.Max(1.5f, cell * 0.05f), Palette.Pupil * opacity);
            }
        }

        if (shield)
        {
            var pulse = 1f + MathF.Sin(time * 6f) * 0.06f;
            c.DrawRing(center, cell * 0.78f * pulse, Palette.Shield * (0.9f * opacity));
        }

        if (doublePoints)
        {
            var k = (MathF.Sin(time * 8f) + 1f) / 2f;
            c.FillCircle(center - forward * (cell * 0.05f) + side * (cell * 0.55f), cell * (0.06f + 0.04f * k), Palette.Double * opacity);
            c.FillCircle(center - forward * (cell * 0.05f) - side * (cell * 0.55f), cell * (0.06f + 0.04f * (1 - k)), Palette.Double * opacity);
        }
    }

    private static Vector2 ToVec(Int2 p) => new(p.X, p.Y);

    public static Vector2 DirVec(Direction d) => d switch
    {
        Direction.Up => new Vector2(0, -1),
        Direction.Right => new Vector2(1, 0),
        Direction.Down => new Vector2(0, 1),
        _ => new Vector2(-1, 0),
    };
}

