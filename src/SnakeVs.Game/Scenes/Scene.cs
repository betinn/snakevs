using Microsoft.Xna.Framework;
using SnakeVs.Game.Graphics;

namespace SnakeVs.Game.Scenes;

public abstract class Scene(SnakeGame game)
{
    protected SnakeGame Game { get; } = game;
    protected Vector2 Screen => Game.ScreenSize;
    protected RectF Safe => Game.SafeArea;
    protected PixelFont Font => Game.Font;
    protected float Time => Game.Time;

    public virtual void Enter()
    {
    }

    public virtual void Leave()
    {
    }

    /// <summary>Called whenever the screen size or safe area changes, and before the first frame.</summary>
    public virtual void Layout()
    {
    }

    /// <summary>App lost focus (home button, phone call, window switch).</summary>
    public virtual void OnFocusLost()
    {
    }

    public abstract void Update(float dt);

    public abstract void Draw(Canvas c);
}

/// <summary>Shared visual building blocks for screens.</summary>
public static class Ui
{
    /// <summary>Background with a slow drifting dot grid.</summary>
    public static void Backdrop(Canvas c, Vector2 size, float time)
    {
        c.FillRect(0, 0, size.X, size.Y, Palette.Background);
        const float spacing = 48f;
        var offset = (time * 8f) % spacing;
        for (var y = -spacing + offset; y < size.Y + spacing; y += spacing)
        {
            for (var x = 0f; x < size.X + spacing; x += spacing)
            {
                c.FillRect(x, y, 3, 3, Palette.BoardCellAlt);
            }
        }
    }

    public static void Panel(Canvas c, RectF r, float alpha = 1f)
    {
        c.FillRoundRect(r.Offset(0, 10), 30, Palette.TextShadow * (0.5f * alpha));
        c.FillRoundRect(r.Inflate(3), 32, Palette.PanelBorder * alpha);
        c.FillRoundRect(r, 30, Palette.Panel * alpha);
    }

    public static void Title(Canvas c, PixelFont font, string text, Vector2 center, float scale, Color color, float alpha = 1f) =>
        font.DrawCentered(c, text, center, scale, color, shadow: true, alpha: alpha);

    /// <summary>Applies an ease-out-back pop for panel entrances. t in [0, 1].</summary>
    public static float Pop(float t)
    {
        t = MathHelper.Clamp(t, 0f, 1f);
        const float s = 1.70158f;
        t -= 1f;
        return t * t * ((s + 1f) * t + s) + 1f;
    }
}
