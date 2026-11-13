namespace SnakeVs.Core;

/// <summary>
/// Collects the pieces of a stage while keeping two zones clear: where the snake comes in (bottom, under the
/// previous portal) and the approach to this stage's portal (top).
/// </summary>
internal sealed class StageBuilder
{
    private const int W = Stage.Width;
    private const int H = Stage.Height;
    private readonly bool[] _reserved = new bool[Stage.CellCount];
    private readonly bool[] _claimed = new bool[Stage.CellCount];

    public StageBuilder(int index, int entryX, int gateX)
    {
        Index = index;
        EntryX = entryX;
        GateX = gateX;
        AppleGoal = Rules.JourneyAppleGoal(index + 1);

        // Approach to the portal, and the posts that grow on both sides of it once it opens.
        Reserve(gateX - 2, 1, gateX + Stage.GateWidth + 1, 4);
        foreach (var x in new[] { gateX - 1, gateX + Stage.GateWidth })
        {
            for (var y = 1; y <= 2; y++)
            {
                var post = new Int2(x, y);
                if (!Stage.InPlay(post)) continue;
                PostCells.Add(post);
                NoItems[Stage.IndexOf(post)] = true;
            }
        }

        if (index == 0)
        {
            // The run starts in the middle of the first stage, heading up.
            var spawn = Start;
            Reserve(spawn.X - 1, spawn.Y - 5, spawn.X + 1, spawn.Y + Rules.StartLength);
        }
        else
        {
            Reserve(entryX - 1, H - 8, entryX + Stage.GateWidth, H - 1);
        }
    }

    public int Index { get; }
    public int EntryX { get; }
    public int GateX { get; }
    public int AppleGoal { get; set; }
    public List<StageElement> Elements { get; } = [];
    public bool[] Walls { get; } = new bool[Stage.CellCount];
    public bool[] Ice { get; } = new bool[Stage.CellCount];
    public bool[] NoItems { get; } = new bool[Stage.CellCount];
    public List<Saw> Saws { get; } = [];
    public List<Laser> Lasers { get; } = [];
    public List<PhaseGroup> PhaseGroups { get; } = [];
    public LavaTide? Lava { get; set; }
    public MeteorShower? Meteors { get; set; }
    public List<(Int2 A, Int2 B)> Teleports { get; } = [];
    public bool Dark { get; set; }
    public float LightRadius { get; set; } = 5f;
    public List<Int2> BugSpawns { get; } = [];
    public int BugEvery { get; set; } = 2;
    public Int2? SpecterSpawn { get; set; }
    public int SpecterEvery { get; set; } = 3;
    public SpawnPoint? RivalSpawn { get; set; }
    public Int2? FixedApple { get; set; }
    public int ItemMaxRow { get; set; } = H - 1;
    public List<Int2> PostCells { get; } = [];

    /// <summary>Local cell where the snake's head is when it enters the stage (or starts the run, in the first one).</summary>
    public Int2 Start => Index == 0 ? new Int2(W / 2, H / 2) : new Int2(EntryX + 1, H - 1);

    /// <summary>A playfield cell nothing has been put on yet and that is outside the reserved zones.</summary>
    public bool Free(Int2 c)
    {
        if (!Stage.InPlay(c)) return false;
        var i = Stage.IndexOf(c);
        return !Walls[i] && !_reserved[i] && !_claimed[i];
    }

    public bool IsReserved(Int2 c) => Stage.InPlay(c) && _reserved[Stage.IndexOf(c)];

    public bool TryWall(int x, int y)
    {
        var c = new Int2(x, y);
        if (!Free(c)) return false;
        Walls[Stage.IndexOf(c)] = true;
        return true;
    }

    /// <summary>Marks a cell as taken by a rail, a phase block or a teleporter: no walls and no pickups there.</summary>
    public void Claim(Int2 c)
    {
        var i = Stage.IndexOf(c);
        _claimed[i] = true;
        NoItems[i] = true;
    }

    /// <summary>True when every open cell can be reached from where the snake comes in (teleporters count as links).</summary>
    public bool IsConnected()
    {
        var blocked = (bool[])Walls.Clone();
        foreach (var post in PostCells) blocked[Stage.IndexOf(post)] = true;
        var links = new Dictionary<Int2, Int2>();
        foreach (var (a, b) in Teleports)
        {
            links[a] = b;
            links[b] = a;
        }

        var open = 0;
        for (var y = 1; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                if (!blocked[y * W + x]) open++;
            }
        }

        var start = Start;
        if (blocked[Stage.IndexOf(start)]) return false;
        var seen = new bool[Stage.CellCount];
        var queue = new Queue<Int2>();
        seen[Stage.IndexOf(start)] = true;
        queue.Enqueue(start);
        var reached = 0;

        void Visit(Int2 n)
        {
            if (!Stage.InPlay(n)) return;
            var i = Stage.IndexOf(n);
            if (seen[i] || blocked[i]) return;
            seen[i] = true;
            queue.Enqueue(n);
        }

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            reached++;
            foreach (var d in Directions.All) Visit(c + d.ToOffset());
            if (links.TryGetValue(c, out var other)) Visit(other);
        }

        return reached == open;
    }

    public Stage Build()
    {
        for (var i = 0; i < Stage.CellCount; i++)
        {
            if (Walls[i]) Ice[i] = false;
        }

        // A teleporter is never iced over: the snake must be able to steer the moment it comes out of one.
        foreach (var (a, z) in Teleports)
        {
            Ice[Stage.IndexOf(a)] = false;
            Ice[Stage.IndexOf(z)] = false;
        }

        foreach (var laser in Lasers)
        {
            var beam = new List<Int2>();
            var step = laser.Direction.ToOffset();
            for (var c = laser.Emitter + step; Stage.InPlay(c) && !Walls[Stage.IndexOf(c)]; c += step)
            {
                beam.Add(c);

                // An apple on a beam would have to be fetched by riding along it: pickups stay off the line of fire.
                NoItems[Stage.IndexOf(c)] = true;
            }

            laser.Beam = beam;
        }

        MarkTrappedIce();
        MarkDeadEnds();
        return new Stage(this);
    }

    /// <summary>
    /// A snake cannot back out of a pocket, so pickups only go on cells that can be entered one way and left another:
    /// cells with a single open neighbour are peeled off, over and over, and what is peeled gets no pickups.
    /// </summary>
    private void MarkDeadEnds()
    {
        var open = new bool[Stage.CellCount];
        for (var i = 0; i < Stage.CellCount; i++) open[i] = Stage.InPlay(Stage.FromIndex(i)) && !Walls[i];
        foreach (var post in PostCells) open[Stage.IndexOf(post)] = false;
        var linked = new HashSet<Int2>();
        foreach (var (a, b) in Teleports)
        {
            linked.Add(a);
            linked.Add(b);
        }

        int Exits(Int2 c)
        {
            var n = linked.Contains(c) ? 1 : 0;
            foreach (var d in Directions.All)
            {
                var o = c + d.ToOffset();
                if (Stage.InPlay(o) && open[Stage.IndexOf(o)]) n++;
            }

            return n;
        }

        var pending = new Queue<Int2>();
        for (var i = 0; i < Stage.CellCount; i++)
        {
            if (open[i] && Exits(Stage.FromIndex(i)) < 2) pending.Enqueue(Stage.FromIndex(i));
        }

        while (pending.Count > 0)
        {
            var c = pending.Dequeue();
            var index = Stage.IndexOf(c);
            if (!open[index]) continue;
            open[index] = false;
            NoItems[index] = true;
            foreach (var d in Directions.All)
            {
                var o = c + d.ToOffset();
                if (Stage.InPlay(o) && open[Stage.IndexOf(o)] && Exits(o) < 2) pending.Enqueue(o);
            }
        }
    }

    /// <summary>
    /// The snake cannot turn on ice, so a pickup on ice is only fair when a straight line through it comes from open
    /// ground and leaves to open ground. Ice cells without such a line get no pickups. A rail, a block that comes
    /// and goes or a teleporter is not open ground: a slide that starts or ends on one depends on the clock, or
    /// carries on somewhere else.
    /// </summary>
    private void MarkTrappedIce()
    {
        var timed = new bool[Stage.CellCount];
        foreach (var (a, z) in Teleports)
        {
            timed[Stage.IndexOf(a)] = true;
            timed[Stage.IndexOf(z)] = true;
        }

        foreach (var group in PhaseGroups)
        {
            foreach (var c in group.Cells) timed[Stage.IndexOf(c)] = true;
        }

        foreach (var saw in Saws)
        {
            foreach (var c in saw.Path) timed[Stage.IndexOf(c)] = true;
        }

        bool Leaves(Int2 from, Direction d)
        {
            var step = d.ToOffset();
            for (var c = from + step; ; c += step)
            {
                if (!Stage.InPlay(c) || Walls[Stage.IndexOf(c)]) return false;
                if (!Ice[Stage.IndexOf(c)]) return !timed[Stage.IndexOf(c)];
            }
        }

        for (var i = 0; i < Stage.CellCount; i++)
        {
            if (!Ice[i]) continue;
            var c = Stage.FromIndex(i);
            var row = Leaves(c, Direction.Left) && Leaves(c, Direction.Right);
            var column = Leaves(c, Direction.Up) && Leaves(c, Direction.Down);
            if (!row && !column) NoItems[i] = true;
        }
    }

    private void Reserve(int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var c = new Int2(x, y);
                if (Stage.InPlay(c)) _reserved[Stage.IndexOf(c)] = true;
            }
        }
    }
}

/// <summary>
/// Builds the stages of the journey from a seed. The first <see cref="IntroStages"/> stages bring in one element
/// each; after that every stage mixes two of them (three in every fifth stage), a little harder each time.
/// </summary>
public static class StageGenerator
{
    private const int W = Stage.Width;
    private const int H = Stage.Height;
    private const int MaxAttempts = 24;
    private const int MaxLevel = 5;

    // The room is 16 x 24. Rows 1 to 4 are the approach to the portal, rows FirstRow to LastRow are where layouts and
    // hazards go, and the rows below are where the snake comes in.
    private const int FirstRow = 5;
    private const int LastRow = H - 6;

    /// <summary>Order in which the elements are introduced, one per stage.</summary>
    public static readonly StageElement[] Intro =
    [
        StageElement.Start, StageElement.Blocks, StageElement.Saws, StageElement.Lasers, StageElement.Bugs,
        StageElement.Phase, StageElement.Lava, StageElement.Teleports, StageElement.Specter, StageElement.Blackout,
        StageElement.Ice, StageElement.Meteors, StageElement.Rival, StageElement.Turret,
    ];

    private static readonly StageElement[] MixPool =
    [
        StageElement.Saws, StageElement.Lasers, StageElement.Bugs, StageElement.Phase, StageElement.Lava,
        StageElement.Teleports, StageElement.Specter, StageElement.Blackout, StageElement.Ice, StageElement.Meteors,
        StageElement.Rival, StageElement.Turret,
    ];

    public static int IntroStages => Intro.Length;

    /// <summary>Column where the first stage's snake would have come in (it starts in the middle instead).</summary>
    public const int FirstEntryX = (W - Stage.GateWidth) / 2;

    /// <param name="index">0 for the first stage.</param>
    /// <param name="seed">Seed of the run: the same seed always gives the same journey.</param>
    /// <param name="entryX">First column of the previous stage's portal, where the snake comes in.</param>
    public static Stage Create(int index, int seed, int entryX)
    {
        index = Math.Max(0, index);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var rng = new Random(Mix(seed, index, attempt));
            var gateX = index == 0 ? FirstEntryX : rng.Next(2, W - Stage.GateWidth - 1);
            var b = new StageBuilder(index, entryX, gateX);
            if (index < Intro.Length) BuildIntro(b, Intro[index], rng);
            else BuildMix(b, index, rng);
            if (b.IsConnected() && Materialized(b)) return b.Build();
        }

        // Never seen in practice: an open room keeps the run going.
        var open = new StageBuilder(index, entryX, FirstEntryX);
        open.Elements.Add(StageElement.Blocks);
        return open.Build();
    }

    /// <summary>Column of the portal the run would use to enter the given stage, following the journey from its start.</summary>
    public static int EntryColumn(int index, int seed)
    {
        var entry = FirstEntryX;
        for (var i = 0; i < index; i++) entry = Create(i, seed, entry).GateX;
        return entry;
    }

    /// <summary>True when every element the stage is named after actually found room in it.</summary>
    private static bool Materialized(StageBuilder b)
    {
        foreach (var element in b.Elements)
        {
            var there = element switch
            {
                StageElement.Saws => b.Saws.Count > 0,
                StageElement.Lasers or StageElement.Turret => b.Lasers.Count > 0,
                StageElement.Bugs => b.BugSpawns.Count > 0,
                StageElement.Phase => b.PhaseGroups.Count > 0,
                StageElement.Teleports => b.Teleports.Count > 0,
                StageElement.Ice => b.Ice.Contains(true),
                StageElement.Rival => b.RivalSpawn is not null,
                _ => true,
            };
            if (!there) return false;
        }

        return true;
    }

    private static void BuildIntro(StageBuilder b, StageElement element, Random rng)
    {
        b.Elements.Add(element);
        switch (element)
        {
            case StageElement.Start:
                // One apple, low on the map: the run opens by going down, then up through the portal.
                b.FixedApple = new Int2(Math.Clamp(W / 2 + rng.Next(-5, 6), 1, W - 2), H - 4);
                break;

            case StageElement.Blocks:
                switch (rng.Next(4))
                {
                    case 0: LayoutBars(b, rng); break;
                    case 1: LayoutPillars(b); break;
                    case 2: LayoutZigzag(b, rng); break;
                    default: LayoutCorners(b); break;
                }

                break;

            case StageElement.Saws:
                AddSaws(b, rng, 1);
                LayoutScatter(b, rng, 3);
                break;

            case StageElement.Lasers:
                AddLasers(b, rng, 1);
                LayoutScatter(b, rng, 2);
                break;

            case StageElement.Bugs:
                LayoutPillars(b);
                AddBugs(b, rng, 1);
                break;

            case StageElement.Phase:
                AddPhase(b, rng, 1);
                break;

            case StageElement.Lava:
                AddLava(b, 1);
                LayoutScatter(b, rng, 3, maxRow: b.ItemMaxRow);
                break;

            case StageElement.Teleports:
                AddTeleportSplit(b, rng);
                break;

            case StageElement.Specter:
                LayoutBars(b, rng);
                AddSpecter(b, 1);
                break;

            case StageElement.Blackout:
                if (rng.Next(2) == 0) LayoutZigzag(b, rng);
                else LayoutPillars(b);
                AddBlackout(b, 1);
                break;

            case StageElement.Ice:
                AddIce(b, rng, 1);
                break;

            case StageElement.Meteors:
                LayoutScatter(b, rng, 3);
                AddMeteors(b, rng, 1);
                break;

            case StageElement.Rival:
                LayoutCorners(b);
                AddRival(b);
                break;

            case StageElement.Turret:
                AddTurret(b, rng, 1);
                LayoutCorners(b);
                break;
        }
    }

    private static void BuildMix(StageBuilder b, int index, Random rng)
    {
        var level = Math.Min(MaxLevel, 1 + (index - Intro.Length) / 4);
        var count = (index + 1) % 5 == 0 ? 3 : 2;

        var pool = MixPool.ToList();
        var picked = new List<StageElement>();
        while (picked.Count < count && pool.Count > 0)
        {
            var e = pool[rng.Next(pool.Count)];
            pool.Remove(e);

            // Two sets of beams, or strikes nobody can see coming, are more unfair than fun.
            if (e == StageElement.Turret && picked.Contains(StageElement.Lasers)) continue;
            if (e == StageElement.Lasers && picked.Contains(StageElement.Turret)) continue;
            if (e == StageElement.Meteors && picked.Contains(StageElement.Blackout)) continue;
            if (e == StageElement.Blackout && picked.Contains(StageElement.Meteors)) continue;
            picked.Add(e);
        }

        picked.Sort();
        b.Elements.AddRange(picked);
        bool Has(StageElement e) => picked.Contains(e);

        // Fixed order: things that need long clear lines first, loose blocks after, creatures last.
        if (Has(StageElement.Lava)) AddLava(b, level);
        if (Has(StageElement.Turret)) AddTurret(b, rng, level);
        if (Has(StageElement.Saws)) AddSaws(b, rng, level);
        if (Has(StageElement.Lasers)) AddLasers(b, rng, level);
        if (Has(StageElement.Phase)) AddPhase(b, rng, level);
        if (Has(StageElement.Ice)) AddIce(b, rng, level);
        LayoutScatter(b, rng, 1 + rng.Next(3), maxRow: b.ItemMaxRow);
        if (Has(StageElement.Teleports)) AddTeleportShortcuts(b, rng);
        if (Has(StageElement.Bugs)) AddBugs(b, rng, level);
        if (Has(StageElement.Specter)) AddSpecter(b, level);
        if (Has(StageElement.Rival)) AddRival(b);
        if (Has(StageElement.Meteors)) AddMeteors(b, rng, level);
        if (Has(StageElement.Blackout)) AddBlackout(b, level);
    }

    // ---- Wall layouts ----

    private static void LayoutBars(StageBuilder b, Random rng)
    {
        foreach (var y in new[] { 6, 11, 16 })
        {
            var gap = rng.Next(3, W - 6);
            for (var x = 2; x <= W - 3; x++)
            {
                if (x >= gap && x < gap + 3) continue;
                b.TryWall(x, y);
            }
        }
    }

    private static void LayoutPillars(StageBuilder b)
    {
        foreach (var px in new[] { 2, 7, 12 })
        {
            foreach (var py in new[] { 5, 9, 13, 17 })
            {
                b.TryWall(px, py);
                b.TryWall(px + 1, py);
                b.TryWall(px, py + 1);
                b.TryWall(px + 1, py + 1);
            }
        }
    }

    private static void LayoutZigzag(StageBuilder b, Random rng)
    {
        var flip = rng.Next(2) == 0;
        var row = 0;
        foreach (var y in new[] { 6, 11, 16 })
        {
            var fromLeft = (row++ % 2 == 0) != flip;
            for (var i = 0; i < W - 5; i++) b.TryWall(fromLeft ? i : W - 1 - i, y);
        }
    }

    private static void LayoutCorners(StageBuilder b)
    {
        void Corner(int x, int y, int dx, int dy)
        {
            for (var i = 0; i < 4; i++) b.TryWall(x + i * dx, y);
            for (var i = 1; i < 4; i++) b.TryWall(x, y + i * dy);
        }

        Corner(2, FirstRow, 1, 1);
        Corner(W - 3, FirstRow, -1, 1);
        Corner(2, LastRow, 1, -1);
        Corner(W - 3, LastRow, -1, -1);
    }

    private static void LayoutScatter(StageBuilder b, Random rng, int pieces, int maxRow = H - 1)
    {
        var bottom = Math.Min(LastRow, maxRow);
        for (var p = 0; p < pieces; p++)
        {
            var (pw, ph) = rng.Next(5) switch
            {
                0 => (2, 2),
                1 => (3, 1),
                2 => (1, 3),
                3 => (4, 1),
                _ => (1, 4),
            };

            for (var attempt = 0; attempt < 8; attempt++)
            {
                var x = rng.Next(1, W - pw);
                var y = rng.Next(FirstRow, Math.Max(FirstRow + 1, bottom - ph + 2));
                var fits = true;
                for (var i = 0; i < pw && fits; i++)
                {
                    for (var j = 0; j < ph && fits; j++) fits = b.Free(new Int2(x + i, y + j));
                }

                if (!fits) continue;
                for (var i = 0; i < pw; i++)
                {
                    for (var j = 0; j < ph; j++) b.TryWall(x + i, y + j);
                }

                break;
            }
        }
    }

    // ---- Elements ----
    //
    // Everything follows the clock of the snake: it moves one cell per step, so a number of steps is also a distance.
    // "5 steps of warning" means the player sees the danger coming from 5 cells away.

    /// <summary>
    /// Blades on rails. Two at first, at half the speed of the snake; the fast ones only show up at level 3.
    /// </summary>
    private static void AddSaws(StageBuilder b, Random rng, int level)
    {
        var rows = new List<int> { 5, 8, 11, 14, 17, 20 };
        rows.RemoveAll(y => y > b.ItemMaxRow);
        Shuffle(rows, rng);
        var wanted = Math.Min(4, 1 + level);
        var placed = 0;
        foreach (var y in rows)
        {
            if (placed >= wanted) break;
            var run = LongestFreeRun(b, y);
            if (run.Length < 5) continue;
            var (x0, length) = run;
            if (length > 10 && rng.Next(2) == 0)
            {
                var shorter = rng.Next(6, 11);
                x0 += rng.Next(length - shorter + 1);
                length = shorter;
            }

            var path = new Int2[length];
            for (var i = 0; i < length; i++)
            {
                path[i] = new Int2(x0 + i, y);
                b.Claim(path[i]);
            }

            var period = level >= 4 ? 1 : level == 3 && placed % 2 == 0 ? 1 : 2;
            var offset = rng.Next(2 * (length - 1));
            b.Saws.Add(new Saw(path, loop: false, period, offset));
            if (level >= 5 && length >= 10) b.Saws.Add(new Saw(path, loop: false, period, offset + length - 1));
            placed++;
        }

        if (level < 2) return;

        // One blade circling a block of free cells.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var w = rng.Next(5, 8);
            var h = rng.Next(4, 7);
            var x0 = rng.Next(1, W - w);
            var y0 = rng.Next(FirstRow, Math.Max(FirstRow + 1, Math.Min(LastRow, b.ItemMaxRow) - h + 1));
            var ring = new List<Int2>();
            for (var x = x0; x < x0 + w; x++) ring.Add(new Int2(x, y0));
            for (var y = y0 + 1; y < y0 + h; y++) ring.Add(new Int2(x0 + w - 1, y));
            for (var x = x0 + w - 2; x >= x0; x--) ring.Add(new Int2(x, y0 + h - 1));
            for (var y = y0 + h - 2; y > y0; y--) ring.Add(new Int2(x0, y));
            if (!ring.All(b.Free)) continue;
            foreach (var c in ring) b.Claim(c);
            b.Saws.Add(new Saw([.. ring], loop: true, level >= 4 ? 1 : 2, rng.Next(ring.Count)));
            break;
        }
    }

    /// <summary>
    /// Beams across part of a row, from an emitter on the side wall to a block that stops them.
    /// <list type="bullet">
    /// <item>A beam never spans the row: past the block there is a gap of 5 cells (2 at the hardest) to go around,
    /// on alternating sides. Waiting for the beam is the short way, never the only one.</item>
    /// <item>The warning lasts 6 steps (5 later on): a snake heading for the beam sees it charge from that far away
    /// and still has room to turn.</item>
    /// <item>Dark plus warning, the time it is safe to go in, is 15 steps or more; the beam is on for 5. A snake
    /// of up to 15 segments gets across a dark beam whole.</item>
    /// <item>The beams fire on one beat in the introduction and on two (half a cycle apart) after that: one rhythm
    /// to learn, not one per beam.</item>
    /// <item>Two beams in the introduction, three after it, on rows at least 4 apart: there is always room to turn
    /// between two of them. Later levels get harder through longer beams and a shorter cycle, not through more beams.</item>
    /// </list>
    /// </summary>
    private static void AddLasers(StageBuilder b, Random rng, int level)
    {
        // Rows with nothing on them yet (no rail, no reserved zone): a beam takes its row whole.
        var rows = new List<int>();
        for (var y = FirstRow; y <= Math.Min(LastRow - 1, b.ItemMaxRow); y++)
        {
            if (Enumerable.Range(0, W).All(x => b.Free(new Int2(x, y)))) rows.Add(y);
        }

        Shuffle(rows, rng);
        var wanted = level == 1 ? 2 : 3;
        var chosen = new List<int>();
        foreach (var y in rows)
        {
            if (chosen.Count >= wanted) break;
            if (chosen.All(other => Math.Abs(other - y) >= LaserRowSpacing)) chosen.Add(y);
        }

        chosen.Sort();

        var period = LaserPeriod(level);
        var warn = level <= 2 ? 6 : 5;
        var reach = Math.Min(W - 4, W - 8 + level);
        var fromLeft = rng.Next(2) == 0;
        var placed = 0;
        foreach (var y in chosen)
        {
            if (!TryBeam(b, y, fromLeft, reach)) continue;
            var emitter = new Int2(fromLeft ? 0 : W - 1, y);
            var beat = level >= 2 && placed % 2 == 1 ? period / 2 : 0;
            b.Lasers.Add(new Laser(emitter, fromLeft ? Direction.Right : Direction.Left, period, LaserOnTicks, warn, delay: 8 + beat));
            fromLeft = !fromLeft;
            placed++;
        }
    }

    public const int LaserOnTicks = 5;
    public const int LaserRowSpacing = 4;

    /// <summary>Steps of a full laser cycle: 24 at first, 20 at the hardest.</summary>
    public static int LaserPeriod(int level) => 24 - 2 * ((Math.Clamp(level, 1, MaxLevel) - 1) / 2);

    /// <summary>
    /// Puts an emitter on the side wall of a row and the block that stops its beam. The whole row is then taken: the
    /// line of fire stays plain floor (no ice, no teleporter, nothing that comes and goes on it) and the cells past
    /// the block stay open. False when the row has no room for it.
    /// </summary>
    private static bool TryBeam(StageBuilder b, int y, bool fromLeft, int reach)
    {
        var emitter = new Int2(fromLeft ? 0 : W - 1, y);
        var step = fromLeft ? 1 : -1;
        for (var x = 0; x < W; x++)
        {
            if (!b.Free(new Int2(x, y))) return false;
        }

        var stop = new Int2(emitter.X + step * (reach + 1), y);
        b.TryWall(emitter.X, emitter.Y);
        b.TryWall(stop.X, stop.Y);
        for (var x = 0; x < W; x++)
        {
            var c = new Int2(x, y);
            if (c != emitter && c != stop) b.Claim(c);
        }

        return true;
    }

    /// <summary>
    /// A block in the middle whose four beams fire one after the other, like a turning lighthouse. Each beam warns
    /// for 5 steps while the one before it is still on, and two steps of darkness separate one beam from the next.
    /// </summary>
    private static void AddTurret(StageBuilder b, Random rng, int level)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var cx = W / 2 - 1 + rng.Next(-2, 3);
            var cy = rng.Next(8, Math.Max(9, Math.Min(13, b.ItemMaxRow - 3)));
            Int2[] block = [new(cx, cy), new(cx + 1, cy), new(cx + 1, cy + 1), new(cx, cy + 1)];
            if (!block.All(b.Free)) continue;
            foreach (var c in block) b.TryWall(c.X, c.Y);

            var period = Math.Max(20, 30 - level * 2);
            var quarter = period / 4;
            Direction[] directions = [Direction.Left, Direction.Up, Direction.Right, Direction.Down];
            for (var i = 0; i < 4; i++)
            {
                b.Lasers.Add(new Laser(block[i], directions[i], period, onTicks: quarter - 2, warnTicks: 5, delay: 8 + i * quarter));

                // The line of fire stays plain floor, like the beams of the side emitters.
                var step = directions[i].ToOffset();
                for (var c = block[i] + step; Stage.InPlay(c); c += step) b.Claim(c);
            }

            return;
        }
    }

    private static void AddBugs(StageBuilder b, Random rng, int level)
    {
        var wanted = Math.Min(5, 1 + level);
        var maxRow = Math.Min(H - 10, b.ItemMaxRow);
        for (var attempt = 0; attempt < 200 && b.BugSpawns.Count < wanted; attempt++)
        {
            var c = new Int2(rng.Next(W), rng.Next(3, maxRow + 1));
            if (!b.Free(c) || b.BugSpawns.Any(o => o.ManhattanTo(c) < 4)) continue;
            b.BugSpawns.Add(c);
        }

        b.BugEvery = 2;
    }

    /// <summary>Blocks that come and go: 12 steps solid, 12 gone at first, with 5 steps of warning before they return.</summary>
    private static void AddPhase(StageBuilder b, Random rng, int level)
    {
        var period = Math.Max(16, 26 - level * 2);
        var groups = new[] { new List<Int2>(), new List<Int2>() };

        if (rng.Next(2) == 0)
        {
            // Checkerboard of 2x2 blocks: half of them solid at any time.
            var shift = rng.Next(2);
            var xs = new[] { 1 + shift, 5 + shift, 9 + shift, 13 + shift };
            var ys = new[] { 5, 9, 13, 17 };
            for (var i = 0; i < xs.Length; i++)
            {
                for (var j = 0; j < ys.Length; j++)
                {
                    Int2[] block = [new(xs[i], ys[j]), new(xs[i] + 1, ys[j]), new(xs[i], ys[j] + 1), new(xs[i] + 1, ys[j] + 1)];
                    if (!block.All(b.Free)) continue;
                    foreach (var c in block) b.Claim(c);
                    groups[(i + j) % 2].AddRange(block);
                }
            }
        }
        else
        {
            // Barriers across the whole stage that take turns closing.
            var row = 0;
            foreach (var y in new[] { 7, 11, 15 })
            {
                if (y > b.ItemMaxRow) continue;
                var group = groups[row++ % 2];
                for (var x = 0; x < W; x++)
                {
                    var c = new Int2(x, y);
                    if (!b.Free(c)) continue;
                    b.Claim(c);
                    group.Add(c);
                }
            }
        }

        for (var g = 0; g < 2; g++)
        {
            if (groups[g].Count == 0) continue;
            b.PhaseGroups.Add(new PhaseGroup([.. groups[g]], period, period / 2, warnTicks: 5, offset: g == 0 ? 0 : period / 2));
        }
    }

    /// <summary>Lava floods the lower 9 rows at first (13 at most) and leaves the rest of the room dry.</summary>
    private static void AddLava(StageBuilder b, int level)
    {
        var maxRows = Math.Min(13, 8 + level);
        b.Lava = new LavaTide(Delay: 20, TicksPerRow: Math.Max(3, 7 - level), maxRows);
        b.ItemMaxRow = H - maxRows - 2;
    }

    /// <summary>A wall across the middle: the only way to the other half is through the teleporters.</summary>
    private static void AddTeleportSplit(StageBuilder b, Random rng)
    {
        for (var x = 0; x < W; x++)
        {
            b.TryWall(x, 11);
            b.TryWall(x, 12);
        }

        var lowY = 15 + rng.Next(-1, 2);
        var highY = 7 + rng.Next(-1, 2);
        var left = 2 + rng.Next(0, 2);
        var right = W - 3 - rng.Next(0, 2);
        AddTeleport(b, new Int2(left, lowY), new Int2(right, highY));
        AddTeleport(b, new Int2(right, lowY), new Int2(left, highY));

        foreach (var (x, y) in new[] { (W / 2 - 1, 5), (W / 2, 5), (W / 2 - 1, 18), (W / 2, 18) }) b.TryWall(x, y);
    }

    private static void AddTeleportShortcuts(StageBuilder b, Random rng)
    {
        var maxRow = Math.Min(LastRow, b.ItemMaxRow - 1);
        for (var pair = 0; pair < 2; pair++)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var a = new Int2(rng.Next(1, W - 1), rng.Next(FirstRow, maxRow + 1));
                var z = new Int2(rng.Next(1, W - 1), rng.Next(FirstRow, maxRow + 1));
                if (a.ManhattanTo(z) < 10 || !Roomy(b, a) || !Roomy(b, z)) continue;
                AddTeleport(b, a, z);
                break;
            }
        }
    }

    /// <summary>A free cell of plain floor (a teleporter on ice would throw the snake out sliding) with free cells around it.</summary>
    private static bool Roomy(StageBuilder b, Int2 c) =>
        b.Free(c) && !b.Ice[Stage.IndexOf(c)] && Directions.All.All(d => b.Free(c + d.ToOffset()));

    private static void AddTeleport(StageBuilder b, Int2 a, Int2 z)
    {
        if (!b.Free(a) || !b.Free(z) || a == z) return;
        b.Claim(a);
        b.Claim(z);
        b.Teleports.Add((a, z));
    }

    private static void AddSpecter(StageBuilder b, int level)
    {
        b.SpecterSpawn = new Int2(b.GateX < W / 2 ? W - 3 : 2, 5);
        b.SpecterEvery = level >= 3 ? 2 : 3;
    }

    private static void AddBlackout(StageBuilder b, int level)
    {
        b.Dark = true;
        b.LightRadius = MathF.Max(3.5f, 5.4f - 0.4f * level);
    }

    private static void AddIce(StageBuilder b, Random rng, int level)
    {
        var bottom = Math.Min(LastRow - 1, b.ItemMaxRow);
        if (level <= 1)
        {
            // One big rink with a few rocks to slide into.
            FillIce(b, 2, FirstRow, W - 3, bottom);
            for (var rock = 0; rock < 3; rock++) b.TryWall(rng.Next(3, W - 3), rng.Next(FirstRow + 2, Math.Max(FirstRow + 3, bottom - 1)));
            return;
        }

        for (var patch = 0; patch < 2; patch++)
        {
            var w = rng.Next(5, 9);
            var h = rng.Next(4, 7);
            var x0 = rng.Next(1, W - w);
            var y0 = rng.Next(FirstRow, Math.Max(FirstRow + 1, bottom - h + 2));
            FillIce(b, x0, y0, x0 + w - 1, Math.Min(bottom, y0 + h - 1));
        }
    }

    private static void FillIce(StageBuilder b, int x0, int y0, int x1, int y1)
    {
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var c = new Int2(x, y);
                if (b.Free(c)) b.Ice[Stage.IndexOf(c)] = true;
            }
        }
    }

    /// <summary>One strike every 5 steps at first, every 3 at most, each announced 8 steps ahead.</summary>
    private static void AddMeteors(StageBuilder b, Random rng, int level) =>
        b.Meteors = new MeteorShower(rng.Next(), Start: 16, Interval: Math.Max(3, 6 - level), WarnTicks: 8);

    private static void AddRival(StageBuilder b)
    {
        var fromRight = b.GateX < W / 2;
        var heading = fromRight ? Direction.Left : Direction.Right;
        var back = heading.Opposite().ToOffset();
        for (var y = FirstRow; y <= 11; y++)
        {
            var head = new Int2(fromRight ? W - 6 : 5, y);
            var clear = true;
            for (var i = -2; i < Rules.StartLength && clear; i++) clear = b.Free(head + back * i);
            if (!clear) continue;
            b.RivalSpawn = new SpawnPoint(head, heading);
            return;
        }
    }

    /// <summary>Scrambles the run seed with the stage number: neighbouring seeds must not give look-alike stages.</summary>
    private static int Mix(int seed, int index, int attempt)
    {
        var h = unchecked((uint)seed * 2654435761u + (uint)index * 2246822519u + (uint)attempt * 3266489917u);
        h ^= h >> 16;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        h = unchecked(h * 3266489917u);
        h ^= h >> 16;
        return (int)h;
    }

    private static (int X, int Length) LongestFreeRun(StageBuilder b, int y)
    {
        var best = (X: 0, Length: 0);
        var start = -1;
        for (var x = 0; x <= W; x++)
        {
            var free = x < W && b.Free(new Int2(x, y));
            if (free && start < 0) start = x;
            if (free || start < 0) continue;
            if (x - start > best.Length) best = (start, x - start);
            start = -1;
        }

        return best;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
