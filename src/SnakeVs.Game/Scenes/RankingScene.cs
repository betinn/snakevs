using Microsoft.Xna.Framework;
using SnakeVs.Core;
using SnakeVs.Game.Audio;
using SnakeVs.Game.Graphics;
using SnakeVs.Game.Online;
using SnakeVs.Game.UI;

namespace SnakeVs.Game.Scenes;

/// <summary>
/// Online top scores per mode and difficulty. Opened from the menu, or right after saving a score, in which case the
/// new entry is highlighted (and shown below the list when it did not make the top).
/// </summary>
public sealed class RankingScene : Scene
{
    private const int Rows = 10;

    private readonly ILeaderboard _board;
    private readonly ScoreEntry? _mine;
    private readonly (GameMode Mode, Difficulty Difficulty) _mineBoard;
    private readonly GameMode? _replayMode;
    private readonly Button _endless;
    private readonly Button _levels;
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

    /// <param name="mine">Entry just saved by the player, highlighted in the list.</param>
    /// <param name="replayMode">When set (coming from a finished run), offers "play again" in that mode.</param>
    public RankingScene(SnakeGame game, ILeaderboard board, GameMode mode, Difficulty difficulty, ScoreEntry? mine = null, GameMode? replayMode = null)
        : base(game)
    {
        _board = board;
        _mode = Leaderboard.IsRanked(mode) ? mode : GameMode.Endless;
        _difficulty = difficulty;
        _mine = mine;
        _mineBoard = (_mode, difficulty);
        _replayMode = replayMode;

        var t = game.Text;
        _endless = new Button(t.Classic);
        _levels = new Button(t.Levels);
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
        var half = (width - gap) / 2f;
        _endless.Bounds = new RectF(left, tabsY, half, 72);
        _levels.Bounds = new RectF(left + half + gap, tabsY, half, 72);
        var third = (width - gap * 2) / 3f;
        for (var i = 0; i < 3; i++) _difficulties[i].Bounds = new RectF(left + i * (third + gap), tabsY + 72 + gap, third, 62);

        const float buttonH = 88f;
        const float buttonGap = 18f;
        var count = _footer.Buttons.Count;
        var footerTop = MathF.Min(Safe.Bottom - 30f, top + 1240f) - (count * buttonH + (count - 1) * buttonGap);
        _footer.LayoutColumn(Safe.Center.X, footerTop, width, buttonH, buttonGap);

        var listTop = tabsY + 72 + gap + 62 + 26;
        _list = new RectF(left, listTop, width, footerTop - 26 - listTop);
        _rowHeight = MathF.Min(66f, (_list.H - 24f) / (Rows + 1.5f));
    }

    public override void Update(float dt)
    {
        var input = Game.Input;
        PollLoading();

        if (input.Back)
        {
            GoToMenu();
            return;
        }

        if (_endless.WasClicked(input) || (input.KeyDirection == Direction.Left && _mode != GameMode.Endless)) SwitchTo(GameMode.Endless, _difficulty);
        else if (_levels.WasClicked(input) || (input.KeyDirection == Direction.Right && _mode != GameMode.Levels)) SwitchTo(GameMode.Levels, _difficulty);

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
        if (clicked == _again && _replayMode is { } replay) Game.SetScene(new PlayScene(Game, replay));
        else GoToMenu();
    }

    public override void Draw(Canvas c)
    {
        var t = Game.Text;
        Ui.Backdrop(c, Screen, Time);
        Ui.Title(c, Font, t.Ranking, new Vector2(Safe.Center.X, _titleY), Font.FitScale(t.Ranking, Safe.W - 60, 9f), Palette.Gold);

        var input = Game.Input;
        _endless.Draw(c, Font, input, Time);
        _levels.Draw(c, Font, input, Time);
        foreach (var b in _difficulties) b.Draw(c, Font, input, Time);

        Ui.Panel(c, _list);
        DrawList(c);
        _footer.Draw(c, Font, input, Time);
    }

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

        var mineShown = false;
        var y = _list.Y + 12f;
        if (_entries.Length == 0)
        {
            Font.DrawCentered(c, t.RankingEmpty, center, Font.FitScale(t.RankingEmpty, _list.W - 60, 3.5f), Palette.TextDim);
        }

        foreach (var e in _entries)
        {
            var isMine = _mine is not null && e.Id == _mine.Id && _mineBoard == (_mode, _difficulty);
            mineShown |= isMine;
            DrawRow(c, e, y, isMine);
            y += _rowHeight;
        }

        // The run just saved did not make the top: show it under the list with its real position.
        if (_mine is not null && !mineShown && _mineBoard == (_mode, _difficulty))
        {
            Font.DrawCentered(c, "...", new Vector2(center.X, y + _rowHeight * 0.3f), 3f, Palette.TextDim, shadow: false);
            DrawRow(c, _mine, y + _rowHeight * 0.6f, highlight: true);
        }
    }

    private void DrawRow(Canvas c, ScoreEntry e, float y, bool highlight)
    {
        var row = new RectF(_list.X + 12, y, _list.W - 24, _rowHeight - 6);
        if (highlight) c.FillRoundRect(row, 12, Palette.Accent * (0.18f + 0.08f * MathF.Sin(Time * 5f)));

        var scale = MathF.Min(4f, MathF.Floor(row.H / 11f * 2f) / 2f);
        var textY = MathF.Round(row.Center.Y - 3.5f * scale);
        var rankColor = e.Rank switch
        {
            1 => Palette.Gold,
            2 => Palette.Text,
            3 => Palette.Shrink,
            _ => Palette.TextDim,
        };

        var rank = e.Rank.ToString();
        Font.Draw(c, rank, new Vector2(row.X + 14, textY), scale, rankColor);

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
        _endless.Style = _mode == GameMode.Endless ? ButtonStyle.Primary : ButtonStyle.Normal;
        _levels.Style = _mode == GameMode.Levels ? ButtonStyle.Primary : ButtonStyle.Normal;
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
        if (task.IsCompletedSuccessfully) _entries = task.Result;
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
