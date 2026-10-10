using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SnakeVs.Game.Graphics;

/// <summary>
/// Immediate mode 2D drawing on top of SpriteBatch. All shapes come from a few textures generated at start-up,
/// so the game ships without a content pipeline. Colors are premultiplied (use <c>color * alpha</c> for fades).
/// </summary>
public sealed class Canvas : IDisposable
{
    private const int CircleSize = 256;
    private const int GlowSize = 128;

    public Canvas(GraphicsDevice device)
    {
        Device = device;
        Batch = new SpriteBatch(device);
        Pixel = new Texture2D(device, 1, 1);
        Pixel.SetData([Color.White]);
        Circle = CreateDisc(device, CircleSize, 0f);
        Ring = CreateDisc(device, CircleSize, 0.82f);
        Glow = CreateGlow(device, GlowSize);
    }

    public GraphicsDevice Device { get; }
    public SpriteBatch Batch { get; }
    public Texture2D Pixel { get; }
    public Texture2D Circle { get; }
    public Texture2D Ring { get; }
    public Texture2D Glow { get; }

    public void Begin(Matrix transform) =>
        Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, null, transform);

    public void End() => Batch.End();

    public void FillRect(float x, float y, float w, float h, Color color)
    {
        if (w <= 0 || h <= 0) return;
        Batch.Draw(Pixel, new Vector2(x, y), null, color, 0f, Vector2.Zero, new Vector2(w, h), SpriteEffects.None, 0f);
    }

    public void FillRect(RectF r, Color color) => FillRect(r.X, r.Y, r.W, r.H, color);

    public void StrokeRect(RectF r, float thickness, Color color)
    {
        FillRect(r.X, r.Y, r.W, thickness, color);
        FillRect(r.X, r.Bottom - thickness, r.W, thickness, color);
        FillRect(r.X, r.Y + thickness, thickness, r.H - thickness * 2, color);
        FillRect(r.Right - thickness, r.Y + thickness, thickness, r.H - thickness * 2, color);
    }

    /// <summary>Rounded rectangle built from a cross of rects and four quarter discs (no overlap, so alpha stays even).</summary>
    public void FillRoundRect(RectF r, float radius, Color color)
    {
        radius = MathF.Min(radius, MathF.Min(r.W, r.H) / 2f);
        if (radius < 0.75f)
        {
            FillRect(r, color);
            return;
        }

        FillRect(r.X + radius, r.Y, r.W - radius * 2, r.H, color);
        FillRect(r.X, r.Y + radius, radius, r.H - radius * 2, color);
        FillRect(r.Right - radius, r.Y + radius, radius, r.H - radius * 2, color);

        var half = CircleSize / 2;
        var scale = new Vector2(radius / half);
        Batch.Draw(Circle, new Vector2(r.X, r.Y), new Rectangle(0, 0, half, half), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        Batch.Draw(Circle, new Vector2(r.Right - radius, r.Y), new Rectangle(half, 0, half, half), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        Batch.Draw(Circle, new Vector2(r.X, r.Bottom - radius), new Rectangle(0, half, half, half), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        Batch.Draw(Circle, new Vector2(r.Right - radius, r.Bottom - radius), new Rectangle(half, half, half, half), color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
    }

    public void FillCircle(Vector2 center, float radius, Color color)
    {
        if (radius <= 0) return;
        var origin = new Vector2(CircleSize / 2f);
        Batch.Draw(Circle, center, null, color, 0f, origin, radius / (CircleSize / 2f), SpriteEffects.None, 0f);
    }

    /// <summary>Ring whose stroke is about 18% of the radius.</summary>
    public void DrawRing(Vector2 center, float radius, Color color)
    {
        if (radius <= 0) return;
        var origin = new Vector2(CircleSize / 2f);
        Batch.Draw(Ring, center, null, color, 0f, origin, radius / (CircleSize / 2f), SpriteEffects.None, 0f);
    }

    /// <summary>Partial ring drawn as a chain of dots, used for countdown timers.</summary>
    public void DrawArc(Vector2 center, float radius, float thickness, float fraction, Color color)
    {
        fraction = MathHelper.Clamp(fraction, 0f, 1f);
        var steps = (int)MathF.Max(6, 48 * fraction);
        for (var i = 0; i <= steps; i++)
        {
            var a = -MathHelper.PiOver2 + MathHelper.TwoPi * fraction * i / steps;
            FillCircle(center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius, thickness / 2f, color);
        }
    }

    public void DrawGlow(Vector2 center, float radius, Color color)
    {
        if (radius <= 0) return;
        var origin = new Vector2(GlowSize / 2f);
        Batch.Draw(Glow, center, null, color, 0f, origin, radius / (GlowSize / 2f), SpriteEffects.None, 0f);
    }

    public void Line(Vector2 a, Vector2 b, float thickness, Color color)
    {
        var d = b - a;
        var length = d.Length();
        if (length <= 0.01f) return;
        var angle = MathF.Atan2(d.Y, d.X);
        Batch.Draw(Pixel, a, null, color, angle, new Vector2(0f, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0f);
    }

    /// <summary>Draws a bitmap described by rows of '#' characters, each pixel <paramref name="px"/> units wide.</summary>
    public void Bitmap(string[] rows, Vector2 topLeft, float px, Color color)
    {
        for (var y = 0; y < rows.Length; y++)
        {
            var row = rows[y];
            var x = 0;
            while (x < row.Length)
            {
                if (row[x] != '#')
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < row.Length && row[x] == '#') x++;
                FillRect(topLeft.X + start * px, topLeft.Y + y * px, (x - start) * px, px, color);
            }
        }
    }

    public void BitmapCentered(string[] rows, Vector2 center, float px, Color color)
    {
        var w = rows.Max(r => r.Length) * px;
        var h = rows.Length * px;
        Bitmap(rows, new Vector2(center.X - w / 2f, center.Y - h / 2f), px, color);
    }

    public void Dispose()
    {
        Batch.Dispose();
        Pixel.Dispose();
        Circle.Dispose();
        Ring.Dispose();
        Glow.Dispose();
    }

    private static Texture2D CreateDisc(GraphicsDevice device, int size, float innerRatio)
    {
        var data = new Color[size * size];
        var r = size / 2f;
        var inner = r * innerRatio;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5f - r;
                var dy = y + 0.5f - r;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                var outerA = MathHelper.Clamp(r - dist, 0f, 1f);
                var innerA = innerRatio > 0 ? MathHelper.Clamp(dist - inner, 0f, 1f) : 1f;
                var a = outerA * innerA;
                data[y * size + x] = new Color(a, a, a, a);
            }
        }

        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }

    private static Texture2D CreateGlow(GraphicsDevice device, int size)
    {
        var data = new Color[size * size];
        var r = size / 2f;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x + 0.5f - r;
                var dy = y + 0.5f - r;
                var t = MathHelper.Clamp(1f - MathF.Sqrt(dx * dx + dy * dy) / r, 0f, 1f);
                var a = t * t;
                data[y * size + x] = new Color(a, a, a, a);
            }
        }

        var tex = new Texture2D(device, size, size);
        tex.SetData(data);
        return tex;
    }
}
