namespace SnakeVs.Core;

/// <summary>
/// CPU opponent. Finds the shortest path to the most attractive pickup with a breadth first search,
/// then checks with a flood fill that the move does not trap the snake. Falls back to the move that keeps
/// the most room when no safe path exists.
/// </summary>
public static class SnakeAi
{
    private const int GoldenDistanceBonus = 6;
    private const int PowerUpDistanceBonus = 3;

    public static Direction ChooseDirection(GameSession session, Snake me, Random rng)
    {
        var board = session.Board;
        var blocked = BuildBlockedMap(session);

        var candidates = new List<Candidate>(3);
        foreach (var dir in Directions.All)
        {
            if (dir.IsOpposite(me.Heading)) continue;
            var next = me.Head + dir.ToOffset();
            if (!board.InBounds(next))
            {
                if (!me.IsGhost) continue;
                next = board.Wrap(next);
            }

            if (blocked[board.Index(next)] && !me.IsGhost) continue;

            var risky = session.Snakes.Any(o => o != me && o.Alive && o.Head.ManhattanTo(next) == 1);
            candidates.Add(new Candidate(dir, next, risky));
        }

        if (candidates.Count == 0) return me.Heading;

        if (me.AiMistakeChance > 0 && rng.NextDouble() < me.AiMistakeChance)
        {
            return candidates[rng.Next(candidates.Count)].Direction;
        }

        foreach (var c in candidates)
        {
            c.Room = FloodArea(board, blocked, c.Cell, me.Length * 2 + 8);
        }

        var needed = Math.Min(me.Length + 2, candidates.Max(c => c.Room));
        var firstMove = PathFirstMove(session, me, blocked, candidates);
        if (firstMove is not null)
        {
            var best = candidates.First(c => c.Direction == firstMove);
            if (best.Room >= needed && !best.Risky) return best.Direction;
        }

        // No safe path to food: keep the most room, avoid risky cells, prefer getting closer to the apple.
        return candidates
            .OrderByDescending(c => Math.Min(c.Room, needed))
            .ThenBy(c => c.Risky)
            .ThenBy(c => c.Cell.ManhattanTo(session.Apple))
            .First()
            .Direction;
    }

    internal static bool[] BuildBlockedMap(GameSession session)
    {
        var board = session.Board;
        var blocked = new bool[board.CellCount];
        foreach (var o in board.Obstacles) blocked[board.Index(o)] = true;
        foreach (var s in session.Snakes)
        {
            if (!s.Alive) continue;
            for (var k = 0; k < s.Length; k++)
            {
                if (k == s.Length - 1 && s.TailWillMove) continue;
                blocked[board.Index(s.Body[k])] = true;
            }
        }

        return blocked;
    }

    /// <summary>Counts reachable free cells from <paramref name="start"/>, stopping early at <paramref name="limit"/>.</summary>
    internal static int FloodArea(Board board, bool[] blocked, Int2 start, int limit)
    {
        var seen = new bool[board.CellCount];
        var queue = new Queue<Int2>();
        queue.Enqueue(start);
        seen[board.Index(start)] = true;
        var count = 0;
        while (queue.Count > 0 && count < limit)
        {
            var c = queue.Dequeue();
            count++;
            foreach (var d in Directions.All)
            {
                var n = c + d.ToOffset();
                if (!board.InBounds(n)) continue;
                var i = board.Index(n);
                if (seen[i] || blocked[i]) continue;
                seen[i] = true;
                queue.Enqueue(n);
            }
        }

        return count;
    }

    private static Direction? PathFirstMove(GameSession session, Snake me, bool[] blocked, List<Candidate> candidates)
    {
        var board = session.Board;
        var distance = new int[board.CellCount];
        var first = new Direction[board.CellCount];
        Array.Fill(distance, -1);

        var queue = new Queue<Int2>();
        foreach (var c in candidates)
        {
            var i = board.Index(c.Cell);
            if (distance[i] >= 0) continue;
            distance[i] = 1;
            first[i] = c.Direction;
            queue.Enqueue(c.Cell);
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            var ci = board.Index(c);
            foreach (var d in Directions.All)
            {
                var n = c + d.ToOffset();
                if (!board.InBounds(n)) continue;
                var ni = board.Index(n);
                if (distance[ni] >= 0 || blocked[ni]) continue;
                distance[ni] = distance[ci] + 1;
                first[ni] = first[ci];
                queue.Enqueue(n);
            }
        }

        Direction? bestMove = null;
        var bestCost = int.MaxValue;

        void Consider(Int2 target, int bonus)
        {
            var i = board.Index(target);
            if (distance[i] < 0) return;
            var cost = distance[i] - bonus;
            if (cost >= bestCost) return;
            bestCost = cost;
            bestMove = first[i];
        }

        Consider(session.Apple, 0);
        if (me.AiChasesBonuses)
        {
            if (session.Golden is { } golden)
            {
                // Only chase the golden apple if it can be reached before it vanishes.
                var i = board.Index(golden.Cell);
                if (distance[i] >= 0 && distance[i] * session.StepIntervalMs < golden.RemainingMs)
                {
                    Consider(golden.Cell, GoldenDistanceBonus);
                }
            }

            if (session.PowerUp is { } power) Consider(power.Cell, PowerUpDistanceBonus);
        }

        return bestMove;
    }

    private sealed class Candidate(Direction direction, Int2 cell, bool risky)
    {
        public Direction Direction { get; } = direction;
        public Int2 Cell { get; } = cell;
        public bool Risky { get; } = risky;
        public int Room { get; set; }
    }
}
