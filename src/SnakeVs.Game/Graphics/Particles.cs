using Microsoft.Xna.Framework;

namespace SnakeVs.Game.Graphics;

/// <summary>Small fixed-size particle pool (no allocations while playing).</summary>
public sealed class Particles
{
    private readonly Particle[] _items = new Particle[400];
    private readonly Random _rng = new();
    private int _next;

    public void Burst(Vector2 position, Color color, int count, float speed, float size, float life = 0.6f)
    {
        for (var i = 0; i < count; i++)
        {
            var angle = (float)(_rng.NextDouble() * MathHelper.TwoPi);
            var s = speed * (0.35f + (float)_rng.NextDouble() * 0.65f);
            ref var p = ref _items[_next];
            _next = (_next + 1) % _items.Length;
            p.Position = position;
            p.Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * s;
            p.Color = color;
            p.Size = size * (0.6f + (float)_rng.NextDouble() * 0.6f);
            p.Life = life * (0.7f + (float)_rng.NextDouble() * 0.5f);
            p.MaxLife = p.Life;
        }
    }

    public void Clear()
    {
        for (var i = 0; i < _items.Length; i++) _items[i].Life = 0;
    }

    public void Update(float dt)
    {
        var drag = MathF.Pow(0.04f, dt);
        for (var i = 0; i < _items.Length; i++)
        {
            ref var p = ref _items[i];
            if (p.Life <= 0) continue;
            p.Life -= dt;
            p.Position += p.Velocity * dt;
            p.Velocity *= drag;
        }
    }

    public void Draw(Canvas canvas)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            ref var p = ref _items[i];
            if (p.Life <= 0) continue;
            var t = p.Life / p.MaxLife;
            canvas.FillCircle(p.Position, p.Size * (0.4f + 0.6f * t), p.Color * t);
        }
    }

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public Color Color;
        public float Size;
        public float Life;
        public float MaxLife;
    }
}

/// <summary>Floating "+10" style labels.</summary>
public sealed class FloatingTexts
{
    private readonly List<Item> _items = [];

    public void Add(string text, Vector2 position, Color color, float scale = 3f)
    {
        _items.Add(new Item { Text = text, Position = position, Color = color, Scale = scale, Life = 0.9f });
    }

    public void Clear() => _items.Clear();

    public void Update(float dt)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.Life -= dt;
            item.Position.Y -= 60f * dt;
            if (item.Life <= 0) _items.RemoveAt(i);
            else _items[i] = item;
        }
    }

    public void Draw(Canvas canvas, PixelFont font)
    {
        foreach (var item in _items)
        {
            var alpha = MathHelper.Clamp(item.Life / 0.4f, 0f, 1f);
            font.DrawCentered(canvas, item.Text, item.Position, item.Scale, item.Color, true, alpha);
        }
    }

    private struct Item
    {
        public string Text;
        public Vector2 Position;
        public Color Color;
        public float Scale;
        public float Life;
    }
}
