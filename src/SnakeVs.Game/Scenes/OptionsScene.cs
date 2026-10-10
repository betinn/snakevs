using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

public sealed class OptionsScene : Scene
{
    private readonly ButtonList _buttons = new();
    private readonly Button _difficulty;
    private readonly Button _sound;
    private readonly Button _vibration;
    private readonly Button _controls;
    private readonly Button _back;
    private float _titleY;

    public OptionsScene(SnakeGame game) : base(game)
    {
        _difficulty = _buttons.Add(new Button(string.Empty));
        _sound = _buttons.Add(new Button(string.Empty));
        _vibration = _buttons.Add(new Button(string.Empty));
        _controls = _buttons.Add(new Button(string.Empty));
        _back = _buttons.Add(new Button(game.Text.Back, ButtonStyle.Primary));
        RefreshLabels();
    }

    public override void Layout()
    {
        var width = MathF.Min(600f, Safe.W - 60f);
        const float height = 100f;
        const float gap = 22f;
        var stack = height * 5 + gap * 4 + 30;
        var top = Safe.Y + MathF.Max(40f, (Safe.H - stack - 200f) / 2f);
        _titleY = top + 70;
        _buttons.LayoutColumn(Safe.Center.X, top + 180, width, height, gap);
        _back.Bounds = _back.Bounds.Offset(0, 30);
    }

    public override void Update(float dt)
    {
        var input = Game.Input;
        if (input.Back)
        {
            GoBack();
            return;
        }

        var clicked = _buttons.Update(input);
        if (clicked is null) return;

        var save = Game.Save;
        if (clicked == _back)
        {
            GoBack();
            return;
        }

        if (clicked == _difficulty) save.Difficulty = (Difficulty)(((int)save.Difficulty + 1) % 3);
        else if (clicked == _sound) save.SoundOn = !save.SoundOn;
        else if (clicked == _vibration) save.VibrationOn = !save.VibrationOn;
        else if (clicked == _controls) save.Controls = save.Controls == ControlScheme.Swipe ? ControlScheme.Buttons : ControlScheme.Swipe;

        Game.ApplySettings();
        Game.PlaySound(Sfx.Click);
        if (clicked == _vibration) Game.Vibrate(60);
        Game.Persist();
        RefreshLabels();
    }

    private void GoBack()
    {
        Game.PlaySound(Sfx.Click);
        Game.Persist();
        Game.SetScene(new MenuScene(Game));
    }

    private void RefreshLabels()
    {
        var t = Game.Text;
        var s = Game.Save;
        _difficulty.Label = $"{t.Difficulty}: {t.DifficultyName(s.Difficulty)}";
        _difficulty.Accent = s.Difficulty switch
        {
            Difficulty.Easy => Palette.PlayerHead,
            Difficulty.Hard => Palette.Danger,
            _ => Palette.Gold,
        };
        _sound.Label = $"{t.Sound}: {(s.SoundOn ? t.On : t.Off)}";
        _sound.Accent = s.SoundOn ? Palette.PlayerHead : Palette.TextDim;
        _vibration.Label = $"{t.Vibration}: {(s.VibrationOn ? t.On : t.Off)}";
        _vibration.Accent = s.VibrationOn ? Palette.PlayerHead : Palette.TextDim;
        _controls.Label = $"{t.Controls}: {(s.Controls == ControlScheme.Swipe ? t.ControlSwipe : t.ControlButtons)}";
        _controls.Accent = Palette.Slow;
    }

    public override void Draw(Canvas c)
    {
        Ui.Backdrop(c, Screen, Time);
        Ui.Title(c, Font, Game.Text.Options, new Vector2(Safe.Center.X, _titleY), Font.FitScale(Game.Text.Options, Safe.W - 60, 9f), Palette.Accent);
        _buttons.Draw(c, Font, Game.Input, Time);
    }
}
