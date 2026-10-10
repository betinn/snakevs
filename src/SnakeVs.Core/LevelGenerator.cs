namespace SnakeVs.Core;

/// <summary>Builds obstacle layouts. Levels 1-5 are hand made, later levels are generated from a seed.</summary>
public static class LevelGenerator
{
    public const int DesignedLevels = 5;

    /// <summary>Layouts whose shapes stay intact with both versus spawn lanes reserved.</summary>
    public static readonly int[] VersusLayouts = [3, 4];
    private const int SafeAhead = 3;
    private const int MaxAttempts = 200;

    public static IReadOnlyList<SpawnPoint> Spawns(GameMode mode, int width, int height) => mode switch
    {
        GameMode.Versus =>
        [
            new SpawnPoint(new Int2(5, height - 6), Direction.Up),
            new SpawnPoint(new Int2(width - 6, 5), Direction.Down),
        ],
        _ => [new SpawnPoint(new Int2(width / 2, height - 6), Direction.Up)],
    };

    public static string LayoutName(int layout) => layout switch
    {
        1 => "open",
        2 => "corners",
        3 => "bars",
        4 => "pillars",
        5 => "box",
        _ => "random",
    };

    /// <summary>Creates the board for a layout number. The result always keeps spawn lanes clear and all free cells connected.</summary>
    public static Board Create(GameMode mode, int layout, int width, int height, int seed)
    {
        var spawns = Spawns(mode, width, height);
        var reserved = ReservedCells(spawns, width, height);

        if (layout <= DesignedLevels)
        {
            var designed = Designed(layout, width, height).Where(c => !reserved.Contains(c));
            var board = new Board(width, height, designed);
            if (IsFullyConnected(board, spawns[0].Head)) return board;
        }

        var rng = new Random(unchecked(seed * 7919 + layout * 104729));
        var pieces = Math.Min(6 + Math.Max(0, layout - DesignedLevels) * 2, 30);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var cells = RandomPieces(rng, pieces, width, height, reserved);
            var board = new Board(width, height, cells);
            if (IsFullyConnected(board, spawns[0].Head)) return board;
        }

        return new Board(width, height);
    }

    internal static HashSet<Int2> ReservedCells(IEnumerable<SpawnPoint> spawns, int width, int height)
    {
        var reserved = new HashSet<Int2>();
        foreach (var spawn in spawns)
        {
            var forward = spawn.Heading.ToOffset();
            var side = new Int2(forward.Y, forward.X);
            for (var i = -(Rules.StartLength - 1); i <= SafeAhead; i++)
            {
                var c = spawn.Head + forward * i;
                for (var s = -1; s <= 1; s++)
                {
                    var cell = c + side * s;
                    if (cell.X >= 0 && cell.Y >= 0 && cell.X < width && cell.Y < height) reserved.Add(cell);
                }
            }
        }

        return reserved;
    }

    public static bool IsFullyConnected(Board board, Int2 start)
    {
        if (board.IsObstacle(start)) return false;
        var free = board.CellCount - board.ObstacleCount;
        var seen = new bool[board.CellCount];
        var queue = new Queue<Int2>();
        queue.Enqueue(start);
        seen[board.Index(start)] = true;
        var count = 0;
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            count++;
            foreach (var d in Directions.All)
            {
                var n = c + d.ToOffset();
                if (!board.InBounds(n) || board.IsObstacle(n) || seen[board.Index(n)]) continue;
                seen[board.Index(n)] = true;
                queue.Enqueue(n);
            }
        }

        return count == free;
    }

    private static IEnumerable<Int2> Designed(int layout, int w, int h)
    {
        switch (layout)
        {
            case 2:
                // Four L shaped corners.
                foreach (var c in Corner(3, 3, 1, 1)) yield return c;
                foreach (var c in Corner(w - 4, 3, -1, 1)) yield return c;
                foreach (var c in Corner(3, h - 4, 1, -1)) yield return c;
                foreach (var c in Corner(w - 4, h - 4, -1, -1)) yield return c;
                break;

            case 3:
                // Two long bars.
                for (var x = 3; x <= w - 4; x++)
                {
                    yield return new Int2(x, h / 3);
                    yield return new Int2(x, 2 * h / 3);
                }

                break;

            case 4:
                // Grid of 2x2 pillars.
                foreach (var px in new[] { 2, w / 2 - 1, w - 4 })
                {
                    foreach (var py in new[] { 4, h / 2 - 4, h / 2 + 3 })
                    {
                        yield return new Int2(px, py);
                        yield return new Int2(px + 1, py);
                        yield return new Int2(px, py + 1);
                        yield return new Int2(px + 1, py + 1);
                    }
                }

                break;

            case 5:
                // A box with four doors.
                int left = 3, right = w - 4, top = 7, bottom = h - 8;
                int midX = w / 2, midY = h / 2;
                for (var x = left; x <= right; x++)
                {
                    if (Math.Abs(x - midX + 0.5) <= 1.5) continue;
                    yield return new Int2(x, top);
                    yield return new Int2(x, bottom);
                }

                for (var y = top; y <= bottom; y++)
                {
                    if (Math.Abs(y - midY + 0.5) <= 1.5) continue;
                    yield return new Int2(left, y);
                    yield return new Int2(right, y);
                }

                break;
        }
    }

    private static IEnumerable<Int2> Corner(int x, int y, int dx, int dy)
    {
        for (var i = 0; i < 5; i++) yield return new Int2(x + i * dx, y);
        for (var i = 1; i < 5; i++) yield return new Int2(x, y + i * dy);
    }

    private static HashSet<Int2> RandomPieces(Random rng, int pieces, int w, int h, HashSet<Int2> reserved)
    {
        var cells = new HashSet<Int2>();
        for (var p = 0; p < pieces; p++)
        {
            var (pw, ph) = rng.Next(4) switch
            {
                0 => (2, 2),
                1 => (3, 1),
                2 => (1, 3),
                _ => (5, 1),
            };

            var x = rng.Next(1, w - 1 - pw + 1);
            var y = rng.Next(1, h - 1 - ph + 1);
            var piece = new List<Int2>();
            for (var i = 0; i < pw; i++)
            {
                for (var j = 0; j < ph; j++) piece.Add(new Int2(x + i, y + j));
            }

            if (piece.Any(reserved.Contains)) continue;
            foreach (var c in piece) cells.Add(c);
        }

        return cells;
    }
}
