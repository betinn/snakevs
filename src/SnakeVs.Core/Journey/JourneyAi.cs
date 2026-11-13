namespace SnakeVs.Core;

/// <summary>
/// CPU players of the journey. The autopilot (demo runs, automated captures, tests) searches a path to the apple or
/// the open portal that stays clear of hazards at the step it would reach each cell, since saws, lasers, lava and
/// the rest follow the clock. The rival snake of a stage uses a simpler search: it only looks at the present.
/// </summary>
public static class JourneyAi
{
    private const int W = Stage.Width;
    private const int H = Stage.Height;

    /// <summary>How many steps ahead hazards are predicted. Further than that the search assumes the cell is clear.</summary>
    private const int Horizon = 48;

    /// <summary>Steps without an apple after which the autopilot starts taking odd turns, to get out of a loop.</summary>
    private const int RestlessAfter = 160;

    [ThreadStatic] private static HazardKind[][]? _ahead;
    [ThreadStatic] private static bool[]? _aheadReady;
    [ThreadStatic] private static int[]? _freeAt;
    [ThreadStatic] private static int[]? _dangerUntil;
    /// <summary>
    /// Arrival times the search keeps for one cell and heading. More than one, because the first way to get somewhere
    /// may come too soon: a beam still on, a block still solid, the snake's own tail still in the way.
    /// </summary>
    private const int Arrivals = 6;

    [ThreadStatic] private static byte[]? _arrivals;
    [ThreadStatic] private static int[]? _arrivedAt;
    [ThreadStatic] private static bool[]? _flood;

    /// <summary>Direction the autopilot wants for the player's next step.</summary>
    public static Direction ChooseDirection(JourneySession s)
    {
        var stage = s.Current;
        var p = s.Player;
        var head = stage.ToLocal(p.Head);

        // Inside the portal there is only one way: through.
        if (head.Y <= 0) return Direction.Up;

        var ahead = _ahead ??= NewAhead();
        var ready = _aheadReady ??= new bool[Horizon + 1];
        Array.Clear(ready);
        var freeAt = _freeAt ??= new int[Stage.CellCount];
        var dangerUntil = _dangerUntil ??= new int[Stage.CellCount];
        Array.Clear(freeAt);
        Array.Clear(dangerUntil);

        // Own body: segment i is still there until the tail has moved past it.
        MarkBody(stage, p, freeAt);
        if (s.Rival is { Alive: true } rival)
        {
            MarkBody(stage, rival, freeAt);
            MarkAround(stage, rival.Head, 2, 3, dangerUntil);
        }

        foreach (var bug in s.Bugs)
        {
            if (bug.Alive) MarkAround(stage, bug.Cell, 1, 2, dangerUntil);
        }

        if (s.Specter is { Stunned: false } specter) MarkAround(stage, specter.Cell, 1, 2, dangerUntil);

        var baseTick = s.StageTick;
        HazardKind HazardAhead(int depth, int index)
        {
            if (depth > Horizon) return HazardKind.None;
            if (!ready[depth])
            {
                stage.FillHazards(ahead[depth], baseTick + depth - 1);
                ready[depth] = true;
            }

            return ahead[depth][index];
        }

        // Next cell when moving from c in direction d as the depth-th step from now, or null when that kills.
        Int2? Step(Int2 c, Direction d, int depth)
        {
            var n = c + d.ToOffset();
            if (n.X < 0 || n.X >= W || n.Y >= H || n.Y < 0) return null;
            if (n.Y == 0) return stage.GateOpen && stage.IsGateColumn(n.X) ? n : null;
            if (stage.IsWall(n)) return null;
            if (stage.TeleportFrom(n) is { } exit) n = exit;
            var i = Stage.IndexOf(n);
            if (HazardAhead(depth, i) != HazardKind.None) return null;
            if (depth < freeAt[i] || depth <= dangerUntil[i]) return null;
            return n;
        }

        // Same, but stepping onto ice commits to the whole slide: it has to end on open ground.
        Int2? Move(Int2 c, Direction d, int depth)
        {
            if (Step(c, d, depth) is not { } n) return null;
            if (stage.IsIce(c)) return n;
            var slide = n;
            for (var k = depth + 1; slide.Y > 0 && stage.IsIce(slide); k++)
            {
                if (Step(slide, d, k) is not { } further) return null;
                slide = further;
            }

            return n;
        }

        var onIce = stage.IsIce(head);
        var candidates = new List<(Direction Direction, Int2 Cell)>(3);
        foreach (var d in Directions.All)
        {
            if (d.IsOpposite(p.Heading) || (onIce && d != p.Heading)) continue;
            if (Move(head, d, 1) is { } cell) candidates.Add((d, cell));
        }

        if (candidates.Count == 0) return p.Heading;

        var apple = s.Apple is { } a ? stage.ToLocal(a) : (Int2?)null;
        var toGate = apple is null && stage.GateOpen;

        // A specter sitting on the apple cannot be raced to it, and circling the two of them never ends. Walk away
        // instead: it follows, and being slower than the snake it is left behind on the way back.
        if (apple is { } guarded && s.Specter is { Stunned: false } guardian && !s.CreaturesDormant)
        {
            var guard = stage.ToLocal(guardian.Cell).ManhattanTo(guarded);
            var arrival = (head.ManhattanTo(guarded) + stage.SpecterEvery - 1) / stage.SpecterEvery;
            if (guard <= 2 && guard <= arrival) apple = FarFrom(stage, guarded);
        }

        bool IsGoal(Int2 c) => apple is not null ? c == apple : toGate && c.Y == 0;

        // Breadth first over (cell, heading): the heading matters because of the no-reverse rule and of ice. A state
        // is taken again when it is reached later, a few times over: that is how the search waits for something.
        var arrivals = _arrivals ??= new byte[Stage.CellCount * 4];
        var arrivedAt = _arrivedAt ??= new int[Stage.CellCount * 4];
        Array.Clear(arrivals);
        Array.Clear(arrivedAt);
        bool Visit(int key, int depth)
        {
            if (arrivedAt[key] == depth || arrivals[key] >= Arrivals) return false;
            arrivedAt[key] = depth;
            arrivals[key]++;
            return true;
        }

        var queue = new Queue<(Int2 Cell, Direction Heading, Direction First, int Depth)>();
        Direction? path = null;
        foreach (var (d, cell) in candidates)
        {
            if (IsGoal(cell))
            {
                path = d;
                break;
            }

            if (Visit(Stage.IndexOf(cell) * 4 + (int)d, 1)) queue.Enqueue((cell, d, d, 1));
        }

        while (path is null && queue.Count > 0)
        {
            var (cell, heading, first, depth) = queue.Dequeue();
            if (cell.Y == 0) continue;
            var sliding = stage.IsIce(cell);
            foreach (var d in Directions.All)
            {
                if (d.IsOpposite(heading) || (sliding && d != heading)) continue;
                if (Move(cell, d, depth + 1) is not { } n) continue;
                if (IsGoal(n))
                {
                    path = first;
                    break;
                }

                if (Visit(Stage.IndexOf(n) * 4 + (int)d, depth + 1)) queue.Enqueue((n, d, first, depth + 1));
            }
        }

        // The portal is the way out of the stage: no need to check for room behind it.
        if (path is { } go && toGate) return go;

        var limit = p.Length * 2 + 8;
        var rooms = new int[candidates.Count];
        var best = 0;
        for (var i = 0; i < candidates.Count; i++)
        {
            rooms[i] = Room(stage, candidates[i].Cell, limit, c => freeAt[c] > 2 || HazardAhead(1, c) != HazardKind.None);
            best = Math.Max(best, rooms[i]);
        }

        var needed = Math.Min(p.Length + 2, best);
        if (path is { } wanted)
        {
            var i = candidates.FindIndex(c => c.Direction == wanted);
            if (rooms[i] >= needed) return wanted;
        }

        var target = apple ?? new Int2(stage.GateX + 1, 0);
        var pick = 0;
        for (var i = 1; i < candidates.Count; i++)
        {
            var (a1, b1) = (Math.Min(rooms[i], needed), Math.Min(rooms[pick], needed));
            if (a1 > b1 || (a1 == b1 && candidates[i].Cell.ManhattanTo(target) < candidates[pick].Cell.ManhattanTo(target))) pick = i;
        }

        // No way to the apple for a long while: the same choice every time can be a loop (a snake following its own
        // tail around what it wants). One step in three, take any other move that leaves as much room.
        if (s.TicksSinceApple > RestlessAfter)
        {
            var roll = Scramble(s.StepCount);
            if (roll % 3 == 0)
            {
                var other = (int)(roll / 3 % (uint)candidates.Count);
                if (Math.Min(rooms[other], needed) >= Math.Min(rooms[pick], needed)) pick = other;
            }
        }

        return candidates[pick].Direction;
    }

    private static uint Scramble(long n)
    {
        var h = unchecked((uint)n * 2654435761u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        return h ^ (h >> 13);
    }

    /// <summary>Direction for the rival snake of a stage: shortest way to the apple that does not box it in.</summary>
    /// <param name="playerNext">Cell the player's head is moving to in this same step.</param>
    /// <param name="playerHeading">The player's heading: the rival does not cut in right in front of it.</param>
    /// <param name="seesPlayer">False on the steps it is distracted: the player's body is not in its map then.</param>
    public static Direction ChooseRivalDirection(JourneySession s, Snake rival, Int2 playerNext, Direction playerHeading, bool seesPlayer, Random rng)
    {
        var stage = s.Current;
        var blocked = new bool[Stage.CellCount];
        for (var i = 0; i < Stage.CellCount; i++)
        {
            var c = Stage.FromIndex(i);
            blocked[i] = !Stage.InPlay(c) || stage.IsWall(c) || s.HazardAt(c) != HazardKind.None;
        }

        void Block(Int2 world)
        {
            var local = stage.ToLocal(world);
            if (Stage.InPlay(local)) blocked[Stage.IndexOf(local)] = true;
        }

        var p = s.Player;
        for (var k = 0; seesPlayer && k < p.Length; k++)
        {
            if (k == p.Length - 1 && p.TailWillMove) continue;
            Block(p.Body[k]);
        }

        for (var k = 0; k < rival.Length; k++)
        {
            if (k == rival.Length - 1 && rival.TailWillMove) continue;
            Block(rival.Body[k]);
        }

        foreach (var bug in s.Bugs)
        {
            if (bug.Alive) Block(bug.Cell);
        }

        Block(playerNext);
        Block(playerNext + playerHeading.ToOffset());

        var head = stage.ToLocal(rival.Head);
        var candidates = new List<(Direction Direction, Int2 Cell)>(3);
        foreach (var d in Directions.All)
        {
            if (d.IsOpposite(rival.Heading)) continue;
            var n = head + d.ToOffset();
            if (Stage.InPlay(n) && !blocked[Stage.IndexOf(n)]) candidates.Add((d, n));
        }

        if (candidates.Count == 0) return rival.Heading;
        if (rival.AiMistakeChance > 0 && rng.NextDouble() < rival.AiMistakeChance) return candidates[rng.Next(candidates.Count)].Direction;

        Direction? path = null;
        if (s.Apple is { } appleWorld)
        {
            var apple = stage.ToLocal(appleWorld);
            var first = new Direction[Stage.CellCount];
            var seen = new bool[Stage.CellCount];
            var queue = new Queue<Int2>();
            foreach (var (d, cell) in candidates)
            {
                var i = Stage.IndexOf(cell);
                if (seen[i]) continue;
                seen[i] = true;
                first[i] = d;
                queue.Enqueue(cell);
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var ci = Stage.IndexOf(c);
                if (c == apple)
                {
                    path = first[ci];
                    break;
                }

                foreach (var d in Directions.All)
                {
                    var n = c + d.ToOffset();
                    if (!Stage.InPlay(n)) continue;
                    var ni = Stage.IndexOf(n);
                    if (seen[ni] || blocked[ni]) continue;
                    seen[ni] = true;
                    first[ni] = first[ci];
                    queue.Enqueue(n);
                }
            }
        }

        var limit = rival.Length * 2 + 8;
        var rooms = candidates.Select(c => Room(stage, c.Cell, limit, i => blocked[i])).ToList();
        var needed = Math.Min(rival.Length + 2, rooms.Max());
        if (path is { } wanted && rooms[candidates.FindIndex(c => c.Direction == wanted)] >= needed) return wanted;

        var pick = 0;
        for (var i = 1; i < candidates.Count; i++)
        {
            if (Math.Min(rooms[i], needed) > Math.Min(rooms[pick], needed)) pick = i;
        }

        return candidates[pick].Direction;
    }

    /// <summary>The open cell, out of one near each corner of the dry part of the stage, that is farthest from <paramref name="from"/>.</summary>
    private static Int2 FarFrom(Stage stage, Int2 from)
    {
        var low = Math.Max(3, Math.Min(H - 3, stage.ItemMaxRow));
        Span<Int2> corners = [new(2, 3), new(W - 3, 3), new(2, low), new(W - 3, low)];
        var best = corners[0];
        var bestDistance = -1;
        foreach (var corner in corners)
        {
            var distance = corner.ManhattanTo(from);
            if (stage.IsWall(corner) || distance <= bestDistance) continue;
            best = corner;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>Counts playfield cells reachable from <paramref name="start"/>, stopping early at <paramref name="limit"/>.</summary>
    private static int Room(Stage stage, Int2 start, int limit, Func<int, bool> blocked)
    {
        if (start.Y == 0) return limit;
        var seen = _flood ??= new bool[Stage.CellCount];
        Array.Clear(seen);
        var queue = new Queue<Int2>();
        seen[Stage.IndexOf(start)] = true;
        queue.Enqueue(start);
        var count = 0;
        while (queue.Count > 0 && count < limit)
        {
            var c = queue.Dequeue();
            count++;
            foreach (var d in Directions.All)
            {
                var n = c + d.ToOffset();
                if (!Stage.InPlay(n)) continue;
                var i = Stage.IndexOf(n);
                if (seen[i] || stage.IsWall(n) || blocked(i)) continue;
                seen[i] = true;
                queue.Enqueue(n);
            }
        }

        return count;
    }

    private static void MarkBody(Stage stage, Snake snake, int[] freeAt)
    {
        for (var i = 0; i < snake.Length; i++)
        {
            var local = stage.ToLocal(snake.Body[i]);
            if (!Stage.InPlay(local)) continue;
            var index = Stage.IndexOf(local);
            freeAt[index] = Math.Max(freeAt[index], snake.Length - i + snake.GrowPending);
        }
    }

    private static void MarkAround(Stage stage, Int2 world, int radius, int until, int[] dangerUntil)
    {
        var center = stage.ToLocal(world);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > radius) continue;
                var c = new Int2(center.X + dx, center.Y + dy);
                if (!Stage.InPlay(c)) continue;
                var index = Stage.IndexOf(c);
                dangerUntil[index] = Math.Max(dangerUntil[index], until);
            }
        }
    }

    private static HazardKind[][] NewAhead()
    {
        var grids = new HazardKind[Horizon + 1][];
        for (var i = 0; i < grids.Length; i++) grids[i] = new HazardKind[Stage.CellCount];
        return grids;
    }
}
