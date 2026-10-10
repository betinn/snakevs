using Microsoft.Xna.Framework;

namespace SnakeVs.Game.Graphics;

/// <summary>Float rectangle in virtual (design) units.</summary>
public readonly record struct RectF(float X, float Y, float W, float H)
{
    public float Left => X;
    public float Top => Y;
    public float Right => X + W;
    public float Bottom => Y + H;
    public Vector2 Center => new(X + W / 2f, Y + H / 2f);
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(W, H);

    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < X + W && p.Y < Y + H;

    public RectF Inflate(float amount) => new(X - amount, Y - amount, W + amount * 2, H + amount * 2);

    public RectF Offset(float dx, float dy) => new(X + dx, Y + dy, W, H);

    public static RectF FromCenter(Vector2 center, float w, float h) => new(center.X - w / 2f, center.Y - h / 2f, w, h);
}
