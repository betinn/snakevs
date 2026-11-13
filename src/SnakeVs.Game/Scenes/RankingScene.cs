using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.Input;
using SnakeVs.Game.Online;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>
/// Online top scores per mode and difficulty. Opened from the menu, or right after saving a score. In that case an
/// intro plays first: a card counts the score up and pops the position, then the new entry climbs the list from the
/// bottom to its place, pushing the rows it passes down. A tap skips ahead. The entry stays highlighted afterwards
/// (and is shown below the list when it did not make the top).
/// </summary>
public sealed class RankingScene : Scene
{
    private const int Rows = Leaderboard.TopSize;
    private const float CountSeconds = 1.0f;
    private const float RankPopAt = 1.15f;
    private const float CardSeconds = 2.4f;
    private const float CardFade = 0.3f;
    private const float FlashSeconds = 0.7f;

    private readonly ILeaderboard _board;
    private readonly ScoreEntry? _mine;
    private readonly int? _total;
    private readonly Particles _particles = new();
    private readonly (GameMode Mode, Difficulty Difficulty) _mineBoard;
    private readonly GameMode? _replayMode;
    private static readonly GameMode[] Modes = [GameMode.Journey, GameMode.Endless, GameMode.Levels];
    private readonly Button[] _modes = new Button[Modes.Length];
    private readonly Button[] _difficulties = new Button[3];
    private readonly ButtonList _footer = new();
    private readonly Button? _again;
    private readonly Button _back;

    private GameMode _mode;
    private Difficulty _difficulty;
    private Task<ScoreEntry[]>? _loading;
    private ScoreEntry[]? _entries;
    private bool _failed;
    private float _titleY;
    private RectF _list;
    private float _rowHeight;

    // Intro after saving a score. _introStart is the Time it began (negative: not started), _introPlayed keeps it to
    // the first load. The flags make each sound/burst fire once; the tick counters drive the count-up and climb ticks.
    private float _introStart = -1f;
    private bool _introPlayed;
    private bool _rankPopped;
    private bool _landed;
    private float _landedAt;
    private int _countTick = -1;
    private int _climbTick = int.MaxValue;

    /// <param name="mine">Entry just saved by the player, highlighted in the list.</param>
    /// <param name="total">Number of entries in that ranking, shown with the position.</param>
    /// <param name="replayMode">When set (coming from a finished run), offers "play again" in that mode.</param>
    public RankingScene(SnakeGame game, ILeaderboard board, GameMode mode, Difficulty difficulty, ScoreEntry? mine = null, int? total = null, GameMode? replayMode = null)
        : base(game)
    {
        _board = board;
        _total = total;
        _mode = Leaderboard.IsRanked(mode) ? mode : GameMode.Journey;
        _difficulty = difficulty;
        _mine = mine;
        _mineBoard = (_mode, difficulty);
        _replayMode = replayMode;

        var t = game.Text;
        _modes[0] = new Button(t.Journey);
        _modes[1] = new Button(t.Classic);
        _modes[2] = new Button(t.Levels);
        _difficulties[(int)Difficulty.Easy] = new Button(t.Easy);
        _difficulties[(int)Difficulty.Normal] = new Button(t.Normal);
        _difficulties[(int)Difficulty.Hard] = new Button(t.Hard);
        if (replayMode is not null) _again = _footer.Add(new Button(t.PlayAgain, ButtonStyle.Primary));
        _back = _footer.Add(new Button(replayMode is null ? t.Back : t.Menu, replayMode is null ? ButtonStyle.Primary : ButtonStyle.Normal));
        RefreshTabs();
        Load();
    }

    public override void Layout()
    {
        var width = MathF.Min(640f, Safe.W - 40f);
        var left = Safe.Center.X - width / 2f;
        var top = Safe.Y + MathF.Max(20f, (Safe.H - 1240f) / 2f);
        _titleY = top + 70f;

        const float gap = 14f;
        var tabsY = top + 140f;
        var third = (width - gap * 2) / 3f;
        for (var i = 0; i < _modes.Length; i++) _modes[i].Bounds = new RectF(left + i * (third + gap), tabsY, third, 72);
        for (var i = 0; i < 3; i++) _difficulties[i].Bounds = new RectF(left + i * (third + gap), tabsY + 72 + gap, third, 62);

        const float buttonH = 88f;
        const float buttonGap = 18f;
        var count = _footer.Buttons.Count;
        var footerTop = MathF.Min(Safe.Bottom - 30f, top + 1240f) - (count * buttonH + (count - 1) * buttonGap);
        _footer.LayoutColumn(Safe.Center.X, footerTop, width, buttonH, buttonGap);

        var listTop = tabsY + 72 + gap + 62 + 26;
        _list = new RectF(left, listTop, width, footerTop - 26 - listTop);
        _rowHeight = MathF.Min(108f, (_list.H - 24f) / (Rows + 1.5f));
    }

    public override void Update(float dt)
    {
        var input = Game.Input;
        PollLoading();
        _particles.Update(dt);

        if (input.Back)
        {
            GoToMenu();
            return;
        }

        if (IntroRunning)
        {
            UpdateIntro(input);
            return;
        }

        var tab = Array.IndexOf(Modes, _mode);
        if (input.KeyDirection == Direction.Left && tab > 0) SwitchTo(Modes[tab - 1], _difficulty);
        else if (input.KeyDirection == Direction.Right && tab < Modes.Length - 1) SwitchTo(Modes[tab + 1], _difficulty);
        for (var i = 0; i < _modes.Length; i++)
        {
            if (_modes[i].WasClicked(input)) SwitchTo(Modes[i], _difficulty);
        }

        for (var i = 0; i < 3; i++)
        {
            if (_difficulties[i].WasClicked(input)) SwitchTo(_mode, (Difficulty)i);
        }

        if (_failed)
        {
            foreach (var tap in input.Taps)
            {
                if (!_list.Contains(tap)) continue;
                Game.PlaySound(Sfx.Click);
                Load();
                break;
            }
        }

        var clicked = _footer.Update(input);
        if (clicked is null) return;
        Game.PlaySound(Sfx.Click);
        if (clicked == _again && _replayMode is { } replay) Game.SetScene(RunScene.Create(Game, replay));
        else GoToMenu();
    }

    public override void Draw(Canvas c)
    {
        var t = Game.Text;
        Ui.Backdrop(c, Screen, Time);
        Ui.Title(c, Font, t.Ranking, new Vector2(Safe.Center.X, _titleY), Font.FitScale(t.Ranking, Safe.W - 60, 9f), Palette.Gold);

        var input = Game.Input;
        foreach (var b in _modes) b.Draw(c, Font, input, Time);
        foreach (var b in _difficulties) b.Draw(c, Font, input, Time);

        Ui.Panel(c, _list);
        DrawList(c);
        _footer.Draw(c, Font, input, Time);
        _particles.Draw(c);
        if (IntroRunning) DrawCard(c);
    }

    // ---- Intro after saving a score ----------------------------------------------------------------------------

    private float IntroTime => Time - _introStart;

    private bool OnMineBoard => _mine is not null && _mineBoard == (_mode, _difficulty);

    private bool IntroRunning => _introStart >= 0 && !_landed;

    /// <summary>Index of the saved entry in the loaded list, or -1 when it is not there (did not make the top).</summary>
    private int MineIndex
    {
        get
        {
            if (!OnMineBoard || _entries is null) return -1;
            for (var i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].Id == _mine!.Id) return i;
            }

            return -1;
        }
    }

    private float ClimbSeconds
    {
        get
        {
            var i = MineIndex;
            return i < 0 ? 0f : MathHelper.Clamp((_entries!.Length - i) * 0.16f, 0.5f, 1.4f);
        }
    }

    /// <summary>Where the saved entry is drawn now, in rows from the top: from just below the list up to its place.</summary>
    private float MineSlot
    {
        get
        {
            var i = MineIndex;
            if (i < 0) return -1;
            if (_introStart < 0 || _landed) return i;
            var n = _entries!.Length;
            var k = MathHelper.Clamp((IntroTime - CardSeconds) / ClimbSeconds, 0f, 1f);
            k = k * k * (3f - 2f * k); // smoothstep: eases out of the bottom and into its place
            return n + (i - n) * k;
        }
    }

    private void StartIntroIfDue()
    {
        if (_introPlayed || !OnMineBoard || _entries is null) return;
        _introPlayed = true;
        _introStart = Time;
    }

    private void UpdateIntro(InputManager input)
    {
        var t = IntroTime;

        // Tap (or Enter) skips to the next step: card, then climb, then done.
        if (input.Taps.Count > 0 || input.Confirm)
        {
            if (t < CardSeconds - CardFade)
            {
                _countTick = int.MaxValue;
                _rankPopped = true;
                _introStart = Time - (CardSeconds - CardFade);
            }
            else
            {
                _introStart = Time - CardSeconds - ClimbSeconds;
            }

            t = IntroTime;
            input.Consume();
        }

        // Score count-up ticks, rising in pitch.
        if (t < CountSeconds)
        {
            var tick = (int)(t / 0.06f);
            if (tick != _countTick)
            {
                _countTick = tick;
                Game.PlaySound(Sfx.Eat, 0.35f, MathF.Min(0.7f, t / CountSeconds * 0.7f));
            }
        }

        if (!_rankPopped && t >= RankPopAt)
        {
            _rankPopped = true;
            Game.PlaySound(Sfx.Win);
            Game.Vibrate(40);
            var at = CardRect.Center + new Vector2(0, 90);
            _particles.Burst(at, Palette.Gold, 36, 900f, 9f, 0.9f);
            _particles.Burst(at, Palette.Accent, 18, 600f, 7f, 0.7f);
        }

        // Climb: a tick for every row passed.
        if (t >= CardSeconds)
        {
            var row = (int)MathF.Ceiling(MineSlot);
            if (row < _climbTick)
            {
                if (_climbTick != int.MaxValue) Game.PlaySound(Sfx.Eat, 0.5f, MathF.Min(0.8f, 0.2f + (_entries!.Length - row) * 0.06f));
                _climbTick = row;
            }
        }

        if (t >= CardSeconds + ClimbSeconds) Land();
    }

    private void Land()
    {
        _landed = true;
        _landedAt = Time;
        Game.PlaySound(Sfx.LevelUp);
        Game.Vibrate(30);
        var i = MineIndex;
        var y = i >= 0 ? RowY(i) : RowY(_entries?.Length ?? 0) + _rowHeight * 0.6f;
        var center = new Vector2(_list.Center.X, y + (_rowHeight - 6) / 2f);
        for (var k = -2; k <= 2; k++)
        {
            _particles.Burst(center + new Vector2(k * _list.W * 0.2f, 0), k % 2 == 0 ? Palette.Gold : Palette.Accent, 10, 500f, 6f, 0.7f);
        }
    }

    private RectF CardRect => RectF.FromCenter(Safe.Center, MathF.Min(560f, Safe.W - 60f), 500f);

    /// <summary>The intro card: score counting up, then the position popping in.</summary>
    private void DrawCard(Canvas c)
    {
        var t = Game.Text;
        var time = IntroTime;
        var fade = 1f - MathHelper.Clamp((time - (CardSeconds - CardFade)) / CardFade, 0f, 1f);
        if (fade <= 0f) return;

        c.FillRect(0, 0, Screen.X, Screen.Y, Palette.Overlay * (MathHelper.Clamp(time * 5f, 0f, 1f) * fade));
        var k = Ui.Pop(time * 3.5f);
        var card = CardRect;
        var r = RectF.FromCenter(card.Center, card.W * (0.8f + 0.2f * k), card.H * (0.8f + 0.2f * k));
        Ui.Panel(c, r, fade);
        var cx = card.Center.X;
        var top = card.Y;

        Font.DrawCentered(c, t.YourScore, new Vector2(cx, top + 70), Font.FitScale(t.YourScore, card.W - 60, 3f), Palette.TextDim, alpha: fade);
        var p = MathHelper.Clamp(time / CountSeconds, 0f, 1f);
        p = 1f - (1f - p) * (1f - p) * (1f - p); // ease-out: fast at first, settles on the score
        var shown = (int)MathF.Round(_mine!.Score * p);
        Font.DrawCentered(c, shown.ToString(), new Vector2(cx, top + 150), 9f, Palette.Text, alpha: fade);

        if (time < RankPopAt) return;
        var pop = Ui.Pop((time - RankPopAt) * 3f);
        Font.DrawCentered(c, t.Position, new Vector2(cx, top + 250), 3f, Palette.TextDim, alpha: fade);
        Font.DrawCentered(c, $"#{_mine.Rank}", new Vector2(cx, top + 340), MathF.Max(0.5f, 11f * pop), RankColor(_mine.Rank), alpha: fade);
        if (_total is { } total && total > 0)
        {
            Font.DrawCentered(c, $"{t.Of} {total}", new Vector2(cx, top + 435), 3f, Palette.TextDim, alpha: fade * MathHelper.Clamp(pop, 0f, 1f));
        }
    }

    private static Color RankColor(int rank) => rank switch
    {
        1 => Palette.Gold,
        2 => Palette.Text,
        3 => Palette.Shrink,
        _ => Palette.Accent,
    };

    private float RowY(float slot) => _list.Y + 12f + slot * _rowHeight;

    private void DrawList(Canvas c)
    {
        var t = Game.Text;
        var center = _list.Center;
        if (_failed)
        {
            Font.DrawCentered(c, t.RankingOffline, center - new Vector2(0, 30), Font.FitScale(t.RankingOffline, _list.W - 60, 3.5f), Palette.Danger);
            Font.DrawCentered(c, t.Retry, center + new Vector2(0, 30), 3f, Palette.TextDim, alpha: 0.6f + 0.4f * MathF.Sin(Time * 5f));
            return;
        }

        if (_entries is null)
        {
            Font.DrawCentered(c, t.Loading, center, 3.5f, Palette.TextDim, alpha: 0.6f + 0.4f * MathF.Sin(Time * 5f));
            return;
        }

        if (_entries.Length == 0)
        {
            Font.DrawCentered(c, t.RankingEmpty, center, Font.FitScale(t.RankingEmpty, _list.W - 60, 3.5f), Palette.TextDim);
        }

        // While the saved entry climbs, the rows below its place sit one slot up until it passes them.
        var mineIndex = MineIndex;
        var mineSlot = MineSlot;
        for (var i = 0; i < _entries.Length; i++)
        {
            if (i == mineIndex) continue;
            var slot = mineIndex >= 0 && i > mineIndex ? i - 1 + MathHelper.Clamp(i - mineSlot, 0f, 1f) : i;
            var e = _entries[i];
            DrawRow(c, e, RowY(slot), highlight: false, rank: e.Rank + (int)MathF.Round(slot) - i);
        }

        if (mineIndex >= 0)
        {
            // Hidden behind the card, then drawn above the others while it climbs, its position counting down.
            if (IntroRunning && IntroTime < CardSeconds - CardFade) return;
            var rank = Math.Max(_mine!.Rank, _mine.Rank + (int)MathF.Round(mineSlot) - mineIndex);
            DrawRow(c, _entries[mineIndex], RowY(mineSlot), highlight: true, rank: rank, lift: IntroRunning ? 1f : 0f);
            return;
        }

        // The run just saved did not make the top: show it under the list with its real position.
        if (OnMineBoard && !IntroRunning)
        {
            var y = RowY(_entries.Length);
            Font.DrawCentered(c, "...", new Vector2(center.X, y + _rowHeight * 0.3f), 3f, Palette.TextDim, shadow: false);
            DrawRow(c, _mine!, y + _rowHeight * 0.6f, highlight: true);
        }
    }

    private void DrawRow(Canvas c, ScoreEntry e, float y, bool highlight, int? rank = null, float lift = 0f)
    {
        var row = new RectF(_list.X + 12, y, _list.W - 24, _rowHeight - 6);
        if (lift > 0f)
        {
            // Lifted off the list while climbing: a bit bigger, with a drop shadow.
            row = row.Inflate(6f * lift);
            c.FillRoundRect(row.Offset(0, 8), 14, Palette.TextShadow * 0.6f);
            c.FillRoundRect(row, 14, Palette.Panel);
            c.FillRoundRect(row, 14, Palette.Accent * 0.35f);
        }
        else if (highlight)
        {
            c.FillRoundRect(row, 12, Palette.Accent * (0.18f + 0.08f * MathF.Sin(Time * 5f)));
            var flash = _landed ? 1f - (Time - _landedAt) / FlashSeconds : 0f;
            if (flash > 0f) c.FillRoundRect(row.Inflate(4f * flash), 14, Color.White * (0.5f * flash));
        }

        var scale = MathF.Min(5f, MathF.Floor(row.H / 11f * 2f) / 2f);
        var textY = MathF.Round(row.Center.Y - 3.5f * scale);
        var position = rank ?? e.Rank;
        var rankColor = position switch
        {
            1 => Palette.Gold,
            2 => Palette.Text,
            3 => Palette.Shrink,
            _ => Palette.TextDim,
        };

        Font.Draw(c, position.ToString(), new Vector2(row.X + 14, textY), scale, rankColor);

        var score = e.Score.ToString();
        var scoreW = Font.Measure(score, scale).X;
        Font.Draw(c, score, new Vector2(row.Right - 14 - scoreW, textY), scale, highlight ? Palette.Accent : Palette.Gold);

        var nameX = row.X + 14 + Font.Measure("000", scale).X + 22;
        var nameMax = row.Right - 14 - scoreW - 22 - nameX;
        var nameScale = MathF.Min(scale, Font.FitScale(e.Name, nameMax, scale));
        Font.Draw(c, e.Name, new Vector2(nameX, MathF.Round(row.Center.Y - 3.5f * nameScale)), nameScale, highlight ? Palette.Accent : Palette.Text);
    }

    private void SwitchTo(GameMode mode, Difficulty difficulty)
    {
        if (mode == _mode && difficulty == _difficulty) return;
        Game.PlaySound(Sfx.Click);
        _mode = mode;
        _difficulty = difficulty;
        RefreshTabs();
        Load();
    }

    private void RefreshTabs()
    {
        for (var i = 0; i < _modes.Length; i++) _modes[i].Style = _mode == Modes[i] ? ButtonStyle.Primary : ButtonStyle.Normal;
        for (var i = 0; i < 3; i++) _difficulties[i].Style = (int)_difficulty == i ? ButtonStyle.Primary : ButtonStyle.Normal;
    }

    private void Load()
    {
        _entries = null;
        _failed = false;
        try
        {
            _loading = _board.GetTopAsync(_mode, _difficulty, Rows);
        }
        catch (Exception)
        {
            _loading = null;
            _failed = true;
        }
    }

    private void PollLoading()
    {
        if (_loading is not { IsCompleted: true } task) return;
        _loading = null;
        if (task.IsCompletedSuccessfully)
        {
            _entries = task.Result;
            StartIntroIfDue();
        }
        else
        {
            _ = task.Exception; // observed: an offline ranking is not an error worth crashing for
            _failed = true;
        }
    }

    private void GoToMenu()
    {
        Game.PlaySound(Sfx.Click);
        Game.SetScene(new MenuScene(Game));
    }
}
