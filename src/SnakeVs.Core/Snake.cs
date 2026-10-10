namespace SnakeVs.Core;

public sealed class Snake
{
    private const int MaxQueuedTurns = 2;

    private readonly List<Int2> _body = [];
    private readonly List<Direction> _pendingTurns = [];

    public Snake(int id, Int2 head, Direction heading, int length)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        Id = id;
        Reset(head, heading, length);
    }

    public int Id { get; }

    /// <summary>When true the CPU decides the moves for this snake.</summary>
    public bool IsAiControlled { get; set; }

    /// <summary>Chance per move that the CPU picks a random move instead of the best one.</summary>
    public double AiMistakeChance { get; set; }

    /// <summary>When false the CPU only goes for apples.</summary>
    public bool AiChasesBonuses { get; set; } = true;

    public Direction Heading { get; private set; }
    public bool Alive { get; internal set; } = true;
    public int Score { get; internal set; }
    public int ApplesEaten { get; internal set; }
    public int GrowPending { get; internal set; }

    public double GhostMs { get; internal set; }
    public double DoubleMs { get; internal set; }
    public int Shields { get; internal set; }

    public bool IsGhost => GhostMs > 0;
    public bool HasDouble => DoubleMs > 0;

    public IReadOnlyList<Int2> Body => _body;
    public Int2 Head => _body[0];
    public Int2 Tail => _body[^1];
    public int Length => _body.Count;

    /// <summary>Where the tail was before the last move. Used to animate the tail smoothly.</summary>
    public Int2 PreviousTail { get; private set; }

    /// <summary>Direction the snake will take on the next move.</summary>
    public Direction NextHeading => _pendingTurns.Count > 0 ? _pendingTurns[0] : Heading;

    /// <summary>True when the last move went through a border (ghost wrap).</summary>
    public bool WrappedLastMove { get; private set; }

    public void Reset(Int2 head, Direction heading, int length)
    {
        _body.Clear();
        _pendingTurns.Clear();
        var back = heading.Opposite().ToOffset();
        for (var i = 0; i < length; i++)
        {
            _body.Add(head + back * i);
        }

        Heading = heading;

        // Pretend the snake just slid one cell forward, so the interpolated drawing shows the full length before the first move.
        PreviousTail = _body[^1] + back;
        GrowPending = 0;
        GhostMs = 0;
        DoubleMs = 0;
        WrappedLastMove = false;
        Alive = true;
    }

    /// <summary>
    /// Queues a turn for the next moves. Turns that reverse or repeat the last queued direction are ignored,
    /// so quick double swipes (e.g. up then left) are both honoured.
    /// </summary>
    public bool QueueTurn(Direction direction)
    {
        var last = _pendingTurns.Count > 0 ? _pendingTurns[^1] : Heading;
        if (direction == last || direction.IsOpposite(last)) return false;
        if (_pendingTurns.Count >= MaxQueuedTurns) return false;
        _pendingTurns.Add(direction);
        return true;
    }

    public bool CanTurnTo(Direction direction) => !direction.IsOpposite(Heading);

    internal Direction ConsumeNextHeading()
    {
        if (_pendingTurns.Count > 0)
        {
            Heading = _pendingTurns[0];
            _pendingTurns.RemoveAt(0);
        }

        return Heading;
    }

    internal void ForceHeading(Direction direction)
    {
        _pendingTurns.Clear();
        if (!direction.IsOpposite(Heading)) Heading = direction;
    }

    internal void MoveTo(Int2 newHead, bool wrapped)
    {
        PreviousTail = _body[^1];
        WrappedLastMove = wrapped;
        _body.Insert(0, newHead);
        if (GrowPending > 0)
        {
            GrowPending--;
        }
        else
        {
            _body.RemoveAt(_body.Count - 1);
        }
    }

    /// <summary>Removes up to <paramref name="amount"/> tail segments, never going below <see cref="Rules.MinLength"/>.</summary>
    internal int Shrink(int amount)
    {
        var removable = Math.Max(0, Math.Min(amount, _body.Count - Rules.MinLength));
        if (removable > 0) _body.RemoveRange(_body.Count - removable, removable);
        GrowPending = 0;
        PreviousTail = _body[^1];
        return removable;
    }

    /// <summary>Whether the tail cell will be free after the next move.</summary>
    public bool TailWillMove => GrowPending == 0;

    public bool Occupies(Int2 cell) => _body.Contains(cell);
}
