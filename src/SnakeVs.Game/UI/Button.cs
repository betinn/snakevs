using Microsoft.Xna.Framework;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.Input;

namespace SnakeVs.Game.UI;

public enum ButtonStyle
{
    Normal,
    Primary,
    Ghost,
}

/// <summary>Tappable rounded button. Fires on release (tap) inside the bounds, or on Enter when focused.</summary>
public sealed class Button
{
    public Button(string label, ButtonStyle style = ButtonStyle.Normal)
    {
        Label = label;
        Style = style;
    }

    public string Label { get; set; }
    public ButtonStyle Style { get; set; }
    public RectF Bounds { get; set; }
    public bool Focused { get; set; }
    public string[]? Icon { get; set; }
    public Color? Accent { get; set; }

    public bool IsPressed(InputManager input) =>
        input.Pointer is { } p && input.PressOrigin is { } o && Bounds.Contains(p) && Bounds.Contains(o);

    public bool WasClicked(InputManager input)
    {
        foreach (var tap in input.Taps)
        {
            if (Bounds.Inflate(6).Contains(tap)) return true;
        }

        return false;
    }

    public void Draw(Canvas c, PixelFont font, InputManager input, float time, float alpha = 1f)
    {
        var pressed = IsPressed(input);
        var r = pressed ? Bounds.Inflate(-3) : Bounds;
        var radius = MathF.Min(22f, r.H * 0.3f);

        Color bg, fg;
        switch (Style)
        {
            case ButtonStyle.Primary:
                bg = pressed ? Palette.ButtonPrimaryPressed : Palette.ButtonPrimary;
                fg = Palette.ButtonPrimaryText;
                break;
            case ButtonStyle.Ghost:
                bg = pressed ? Palette.ButtonPressed : Color.Transparent;
                fg = Palette.TextDim;
                break;
            default:
                bg = pressed ? Palette.ButtonPressed : Palette.Button;
                fg = Palette.Text;
                break;
        }

        if (Style != ButtonStyle.Ghost)
        {
            c.FillRoundRect(r.Offset(0, 6), radius, Palette.TextShadow * (0.6f * alpha));
        }

        c.FillRoundRect(r, radius, bg * alpha);

        if (Focused)
        {
            var pulse = 0.55f + 0.45f * MathF.Sin(time * 6f);
            var ring = r.Inflate(4);
            c.FillRoundRect(new RectF(ring.X, ring.Y, ring.W, 3), 1, (Accent ?? Palette.Accent) * (pulse * alpha));
            c.FillRoundRect(new RectF(ring.X, ring.Bottom - 3, ring.W, 3), 1, (Accent ?? Palette.Accent) * (pulse * alpha));
        }

        if (Accent is { } accent && Style == ButtonStyle.Normal)
        {
            c.FillRoundRect(new RectF(r.X + 10, r.Y + r.H * 0.25f, 8, r.H * 0.5f), 4, accent * alpha);
        }

        var scale = font.FitScale(Label, r.W - 40, MathF.Min(5f, MathF.Floor(r.H / 14f * 2f) / 2f));
        if (Icon is not null)
        {
            var px = scale;
            var iconW = Icon[0].Length * px;
            var textW = font.Measure(Label, scale).X;
            var total = iconW + 14 + textW;
            var x = r.Center.X - total / 2f;
            c.BitmapCentered(Icon, new Vector2(x + iconW / 2f, r.Center.Y), px, fg * alpha);
            font.Draw(c, Label, new Vector2(MathF.Round(x + iconW + 14), MathF.Round(r.Center.Y - 3.5f * scale)), scale, fg * alpha);
        }
        else
        {
            font.DrawCentered(c, Label, r.Center, scale, fg, shadow: false, alpha: alpha);
        }
    }
}

/// <summary>Vertical list of buttons with keyboard/gamepad focus navigation.</summary>
public sealed class ButtonList
{
    private readonly List<Button> _buttons = [];
    private int _focus = -1;

    public IReadOnlyList<Button> Buttons => _buttons;

    public Button Add(Button b)
    {
        _buttons.Add(b);
        return b;
    }

    public void Clear()
    {
        _buttons.Clear();
        _focus = -1;
    }

    /// <summary>Lays buttons out top to bottom, centered on <paramref name="centerX"/>.</summary>
    public float LayoutColumn(float centerX, float top, float width, float height, float gap)
    {
        var y = top;
        foreach (var b in _buttons)
        {
            b.Bounds = new RectF(centerX - width / 2f, y, width, height);
            y += height + gap;
        }

        return y - gap;
    }

    /// <summary>Returns the clicked button (tap or Enter on the focused one), or null.</summary>
    public Button? Update(InputManager input)
    {
        if (_buttons.Count == 0) return null;
        if (input.MenuDown) _focus = (_focus + 1) % _buttons.Count;
        if (input.MenuUp) _focus = _focus <= 0 ? _buttons.Count - 1 : _focus - 1;
        for (var i = 0; i < _buttons.Count; i++) _buttons[i].Focused = i == _focus;

        if (input.Confirm && _focus >= 0) return _buttons[_focus];
        foreach (var b in _buttons)
        {
            if (b.WasClicked(input)) return b;
        }

        return null;
    }

    public void Draw(Canvas c, PixelFont font, InputManager input, float time, float alpha = 1f)
    {
        foreach (var b in _buttons) b.Draw(c, font, input, time, alpha);
    }
}
