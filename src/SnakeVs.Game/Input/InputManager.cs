using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using SnakeVs.Core;

namespace SnakeVs.Game.Input;

/// <summary>
/// Unifies touch, mouse, keyboard and gamepad into simple per-frame signals in virtual coordinates:
/// taps, swipes (emitted while the finger is still moving, for responsive steering), presses and keys.
/// </summary>
public sealed class InputManager
{
    private const float SwipeThreshold = 42f;
    private const float TapSlop = 26f;
    private const double TapMaxSeconds = 0.6;
    private const int MouseId = -1000;

    private readonly List<Vector2> _taps = [];
    private readonly List<Vector2> _presses = [];
    private readonly List<Direction> _swipes = [];

    private KeyboardState _prevKeys;
    private GamePadState _prevPad;
    private MouseState _prevMouse;

    private int? _activeId;
    private Vector2 _start;
    private Vector2 _anchor;
    private double _startTime;
    private bool _swiped;
    private float _maxTravel;

    public IReadOnlyList<Vector2> Taps => _taps;
    public IReadOnlyList<Vector2> Presses => _presses;
    public IReadOnlyList<Direction> Swipes => _swipes;

    /// <summary>Current pointer position while a finger or mouse button is down.</summary>
    public Vector2? Pointer { get; private set; }

    /// <summary>Where the current press started.</summary>
    public Vector2? PressOrigin { get; private set; }

    public Direction? KeyDirection { get; private set; }
    public bool Back { get; private set; }
    public bool Confirm { get; private set; }
    public bool PauseKey { get; private set; }
    public bool MenuUp { get; private set; }
    public bool MenuDown { get; private set; }

    /// <summary>True if any input happened this frame (used to skip idle screens).</summary>
    public bool AnyActivity => _taps.Count > 0 || _swipes.Count > 0 || KeyDirection is not null || Confirm;

    public void Update(double totalSeconds, float scale, bool platformBack, bool mouseEnabled)
    {
        _taps.Clear();
        _presses.Clear();
        _swipes.Clear();
        KeyDirection = null;

        var keys = Keyboard.GetState();
        var pad = GamePad.GetState(PlayerIndex.One);

        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
        bool PadPressed(Buttons b) => pad.IsButtonDown(b) && !_prevPad.IsButtonDown(b);

        if (Pressed(Keys.Up) || Pressed(Keys.W) || PadPressed(Buttons.DPadUp) || PadPressed(Buttons.LeftThumbstickUp)) KeyDirection = Direction.Up;
        else if (Pressed(Keys.Down) || Pressed(Keys.S) || PadPressed(Buttons.DPadDown) || PadPressed(Buttons.LeftThumbstickDown)) KeyDirection = Direction.Down;
        else if (Pressed(Keys.Left) || Pressed(Keys.A) || PadPressed(Buttons.DPadLeft) || PadPressed(Buttons.LeftThumbstickLeft)) KeyDirection = Direction.Left;
        else if (Pressed(Keys.Right) || Pressed(Keys.D) || PadPressed(Buttons.DPadRight) || PadPressed(Buttons.LeftThumbstickRight)) KeyDirection = Direction.Right;

        MenuUp = KeyDirection == Direction.Up;
        MenuDown = KeyDirection == Direction.Down;
        Confirm = Pressed(Keys.Enter) || Pressed(Keys.Space) || PadPressed(Buttons.A) || PadPressed(Buttons.Start);
        Back = platformBack || Pressed(Keys.Escape) || PadPressed(Buttons.Back) || PadPressed(Buttons.B);
        PauseKey = Pressed(Keys.P) || PadPressed(Buttons.Start);

        _prevKeys = keys;
        _prevPad = pad;

        foreach (var touch in TouchPanel.GetState())
        {
            var pos = touch.Position / scale;
            switch (touch.State)
            {
                case TouchLocationState.Pressed:
                    PointerDown(touch.Id, pos, totalSeconds);
                    break;
                case TouchLocationState.Moved:
                    PointerMove(touch.Id, pos);
                    break;
                case TouchLocationState.Released:
                case TouchLocationState.Invalid:
                    PointerUp(touch.Id, pos, totalSeconds);
                    break;
            }
        }

        if (mouseEnabled)
        {
            var mouse = Mouse.GetState();
            var pos = new Vector2(mouse.X, mouse.Y) / scale;
            var down = mouse.LeftButton == ButtonState.Pressed;
            var wasDown = _prevMouse.LeftButton == ButtonState.Pressed;
            if (down && !wasDown) PointerDown(MouseId, pos, totalSeconds);
            else if (down) PointerMove(MouseId, pos);
            else if (wasDown) PointerUp(MouseId, pos, totalSeconds);
            _prevMouse = mouse;
        }
    }

    /// <summary>Clears all signals so the next scene does not react to the input that opened it.</summary>
    public void Consume()
    {
        _taps.Clear();
        _presses.Clear();
        _swipes.Clear();
        KeyDirection = null;
        Back = Confirm = PauseKey = MenuUp = MenuDown = false;
        _swiped = true;
    }

    private void PointerDown(int id, Vector2 pos, double time)
    {
        if (_activeId is not null) return;
        _activeId = id;
        _start = pos;
        _anchor = pos;
        _startTime = time;
        _swiped = false;
        _maxTravel = 0;
        Pointer = pos;
        PressOrigin = pos;
        _presses.Add(pos);
    }

    private void PointerMove(int id, Vector2 pos)
    {
        if (_activeId != id) return;
        Pointer = pos;
        _maxTravel = MathF.Max(_maxTravel, Vector2.Distance(_start, pos));

        var delta = pos - _anchor;
        if (delta.Length() < SwipeThreshold) return;

        var dir = MathF.Abs(delta.X) > MathF.Abs(delta.Y)
            ? (delta.X > 0 ? Direction.Right : Direction.Left)
            : (delta.Y > 0 ? Direction.Down : Direction.Up);
        _swipes.Add(dir);
        _anchor = pos;
        _swiped = true;
    }

    private void PointerUp(int id, Vector2 pos, double time)
    {
        if (_activeId != id) return;
        PointerMove(id, pos);
        if (!_swiped && _maxTravel < TapSlop && time - _startTime < TapMaxSeconds) _taps.Add(pos);
        _activeId = null;
        Pointer = null;
        PressOrigin = null;
    }
}
