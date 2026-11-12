namespace SnakeVs.Core;

/// <summary>What a journey stage is made of. The first stages introduce one element each, later ones mix them.</summary>
public enum StageElement
{
    Start,
    Blocks,
    Saws,
    Lasers,
    Bugs,
    Phase,
    Lava,
    Teleports,
    Specter,
    Blackout,
    Ice,
    Meteors,
    Rival,
    Turret,
}

/// <summary>Hazards that follow the clock. They kill when they catch the head and cut the tail when they catch the body.</summary>
public enum HazardKind : byte
{
    None,
    Saw,
    Laser,
    Phase,
    Lava,
    Meteor,
}

public enum LaserState
{
    Off,
    Warning,
    On,
}

/// <summary>A blade that runs along a rail, back and forth or around a loop. Cells are stage-local.</summary>
public sealed class Saw(Int2[] path, bool loop, int period, int offset)
{
    public IReadOnlyList<Int2> Path { get; } = path;
    public bool Loop { get; } = loop;

    /// <summary>Steps the blade takes to move one cell.</summary>
    public int Period { get; } = Math.Max(1, period);

    public Int2 CellAt(long tick)
    {
        var n = Path.Count;
        if (n == 1) return Path[0];
        var k = Math.Max(0, tick) / Period + offset;
        if (Loop) return Path[(int)(k % n)];
        var cycle = 2 * (n - 1);
        var r = (int)(k % cycle);
        return Path[r < n ? r : cycle - r];
    }
}

/// <summary>A beam from an emitter block to the first wall: off, a short warning, then on. Cells are stage-local.</summary>
public sealed class Laser(Int2 emitter, Direction direction, int period, int onTicks, int warnTicks, int delay)
{
    public Int2 Emitter { get; } = emitter;
    public Direction Direction { get; } = direction;
    public int Period { get; } = period;
    public int OnTicks { get; } = onTicks;
    public int WarnTicks { get; } = warnTicks;

    /// <summary>Stage step at which the first cycle starts (always with the off part).</summary>
    public int Delay { get; } = delay;

    public IReadOnlyList<Int2> Beam { get; internal set; } = [];

    public LaserState StateAt(long tick)
    {
        var c = tick - Delay;
        if (c < 0) return LaserState.Off;
        c %= Period;
        if (c >= Period - OnTicks) return LaserState.On;
        return c >= Period - OnTicks - WarnTicks ? LaserState.Warning : LaserState.Off;
    }
}

/// <summary>Blocks that are solid for part of a cycle and gone for the rest. Cells are stage-local.</summary>
public sealed class PhaseGroup(Int2[] cells, int period, int solidTicks, int warnTicks, int offset)
{
    public IReadOnlyList<Int2> Cells { get; } = cells;
    public int Period { get; } = period;
    public int SolidTicks { get; } = solidTicks;
    public int WarnTicks { get; } = warnTicks;

    public bool SolidAt(long tick) => Mod(tick) < SolidTicks;

    /// <summary>True during the last steps before the blocks turn solid.</summary>
    public bool WarningAt(long tick) => Mod(tick) >= Period - WarnTicks;

    private long Mod(long tick) => (Math.Max(0, tick) + offset) % Period;
}

/// <summary>Lava that rises from the bottom of the stage, one row at a time, up to a limit.</summary>
public sealed record LavaTide(int Delay, int TicksPerRow, int MaxRows)
{
    public int RowsAt(long tick) => tick < Delay ? 0 : (int)Math.Min(MaxRows, 1 + (tick - Delay) / TicksPerRow);
}

/// <summary>Meteors that hit a cell and its four neighbours, each announced a few steps ahead.</summary>
public sealed record MeteorShower(int Seed, int Start, int Interval, int WarnTicks)
{
    public long ImpactTick(long strike) => Start + strike * Interval;

    /// <summary>Stage-local cell the given strike is aimed at.</summary>
    public Int2 Target(long strike)
    {
        var h = unchecked((uint)Seed * 2654435761u ^ (uint)strike * 2246822519u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        var x = (int)(h % Stage.Width);
        var y = 1 + (int)(h / Stage.Width % (Stage.Height - 1));
        return new Int2(x, y);
    }

    /// <summary>Strike landing exactly on this step, or -1.</summary>
    public long StrikeAt(long tick) => tick >= Start && (tick - Start) % Interval == 0 ? (tick - Start) / Interval : -1;

    /// <summary>Strikes already announced at this step and not landed yet (the one landing now included).</summary>
    public (long First, long Last) AnnouncedAt(long tick)
    {
        var first = tick <= Start ? 0 : (tick - Start + Interval - 1) / Interval;
        var last = tick + WarnTicks < Start ? -1 : (tick + WarnTicks - Start) / Interval;
        return (first, last);
    }

    public static readonly Int2[] Shape = [new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
}

/// <summary>One of the wall cells that grow next to the portal when it opens.</summary>
public sealed class PortalPost(Int2 cell)
{
    public Int2 Cell { get; } = cell;
    public bool Solid { get; internal set; }
}

/// <summary>
/// One room of the journey: <see cref="Width"/> x <see cref="Height"/> cells. Local row 0 is the wall at the top where
/// the portal to the next stage opens; rows 1 to Height - 1 are the playfield. Stages are stacked upwards in world
/// space: stage <see cref="Index"/> has its top row at world Y = -Index * Height. Built by <see cref="StageBuilder"/>.
/// </summary>
public sealed class Stage
{
    public const int Width = Rules.BoardWidth;
    public const int Height = Rules.JourneyStageHeight;
    public const int GateWidth = Rules.JourneyGateWidth;
    public const int CellCount = Width * Height;

    private readonly bool[] _walls;
    private readonly bool[] _ice;
    private readonly bool[] _noItems;
    private readonly Dictionary<Int2, Int2> _teleports = [];

    internal Stage(StageBuilder b)
    {
        Index = b.Index;
        EntryX = b.EntryX;
        GateX = b.GateX;
        AppleGoal = b.AppleGoal;
        Elements = [.. b.Elements];
        _walls = b.Walls;
        _ice = b.Ice;
        _noItems = b.NoItems;
        Saws = [.. b.Saws];
        Lasers = [.. b.Lasers];
        PhaseGroups = [.. b.PhaseGroups];
        Lava = b.Lava;
        Meteors = b.Meteors;
        Teleports = [.. b.Teleports];
        foreach (var (a, z) in Teleports)
        {
            _teleports[a] = z;
            _teleports[z] = a;
        }

        Dark = b.Dark;
        LightRadius = b.LightRadius;
        BugSpawns = [.. b.BugSpawns];
        BugEvery = b.BugEvery;
        SpecterSpawn = b.SpecterSpawn;
        SpecterEvery = b.SpecterEvery;
        RivalSpawn = b.RivalSpawn;
        FixedApple = b.FixedApple;
        ItemMaxRow = b.ItemMaxRow;
        Posts = [.. b.PostCells.Select(c => new PortalPost(c))];
    }

    /// <summary>0 for the first stage.</summary>
    public int Index { get; }

    public int Number => Index + 1;

    /// <summary>World row of the wall at the top of the stage.</summary>
    public int Top => -Index * Height;

    /// <summary>World row of the last playfield row.</summary>
    public int Bottom => Top + Height - 1;

    /// <summary>First column of the opening the snake came in through (the previous stage's portal).</summary>
    public int EntryX { get; }

    /// <summary>First column of the portal in the top wall.</summary>
    public int GateX { get; }

    public int AppleGoal { get; }
    public IReadOnlyList<StageElement> Elements { get; }
    public IReadOnlyList<Saw> Saws { get; }
    public IReadOnlyList<Laser> Lasers { get; }
    public IReadOnlyList<PhaseGroup> PhaseGroups { get; }
    public LavaTide? Lava { get; }
    public MeteorShower? Meteors { get; }
    public IReadOnlyList<(Int2 A, Int2 B)> Teleports { get; }

    /// <summary>Lights out: only the surroundings of the head, the apple and the portal can be seen.</summary>
    public bool Dark { get; }

    public float LightRadius { get; }
    public IReadOnlyList<Int2> BugSpawns { get; }

    /// <summary>Steps a bug takes to move one cell.</summary>
    public int BugEvery { get; }

    public Int2? SpecterSpawn { get; }
    public int SpecterEvery { get; }
    public SpawnPoint? RivalSpawn { get; }

    /// <summary>Where the first apple goes, when the stage says so (the opening stage puts it at the bottom).</summary>
    public Int2? FixedApple { get; }

    /// <summary>Last local row where pickups may appear (the lava never gets above it).</summary>
    public int ItemMaxRow { get; }

    public IReadOnlyList<PortalPost> Posts { get; }
    public bool GateOpen { get; internal set; }

    /// <summary>Stage step at which the portal opened.</summary>
    public long GateOpenTick { get; internal set; }

    public static int IndexOf(Int2 local) => local.Y * Width + local.X;

    public static Int2 FromIndex(int index) => new(index % Width, index / Width);

    /// <summary>True for playfield cells (the wall row at the top is not one).</summary>
    public static bool InPlay(Int2 local) => local.X >= 0 && local.X < Width && local.Y >= 1 && local.Y < Height;

    public Int2 ToLocal(Int2 world) => new(world.X, world.Y - Top);

    public Int2 ToWorld(Int2 local) => new(local.X, local.Y + Top);

    public bool ContainsRow(int worldY) => worldY >= Top && worldY <= Bottom;

    public bool IsGateColumn(int x) => x >= GateX && x < GateX + GateWidth;

    /// <summary>Walls drawn at start-up (blocks, emitters, the top wall outside the portal).</summary>
    public bool IsStaticWall(Int2 local)
    {
        if (local.X < 0 || local.X >= Width || local.Y < 0 || local.Y >= Height) return true;
        if (local.Y == 0) return true;
        return _walls[IndexOf(local)];
    }

    /// <summary>Anything solid right now: static walls, the closed portal and the posts that have grown next to it.</summary>
    public bool IsWall(Int2 local)
    {
        if (local.X < 0 || local.X >= Width || local.Y < 0 || local.Y >= Height) return true;
        if (local.Y == 0) return !(GateOpen && IsGateColumn(local.X));
        if (_walls[IndexOf(local)]) return true;
        foreach (var post in Posts)
        {
            if (post.Solid && post.Cell == local) return true;
        }

        return false;
    }

    public bool IsIce(Int2 local) => InPlay(local) && _ice[IndexOf(local)];

    /// <summary>The other end of the teleporter on this cell, if there is one.</summary>
    public Int2? TeleportFrom(Int2 local) => _teleports.TryGetValue(local, out var to) ? to : null;

    /// <summary>Whether an apple or a bonus may be placed here (ignoring what moves around).</summary>
    public bool CanHoldItem(Int2 local) =>
        InPlay(local) && local.Y <= ItemMaxRow && !_walls[IndexOf(local)] && !_noItems[IndexOf(local)];

    /// <summary>Writes the hazard covering every cell at the given stage step into <paramref name="grid"/> (CellCount entries).</summary>
    public void FillHazards(HazardKind[] grid, long tick)
    {
        Array.Clear(grid);

        foreach (var group in PhaseGroups)
        {
            if (!group.SolidAt(tick)) continue;
            foreach (var c in group.Cells) grid[IndexOf(c)] = HazardKind.Phase;
        }

        if (Lava is not null)
        {
            var rows = Lava.RowsAt(tick);
            for (var y = Height - rows; y < Height; y++)
            {
                for (var x = 0; x < Width; x++) grid[y * Width + x] = HazardKind.Lava;
            }
        }

        if (Meteors is not null)
        {
            var strike = Meteors.StrikeAt(tick);
            if (strike >= 0)
            {
                var center = Meteors.Target(strike);
                foreach (var d in MeteorShower.Shape)
                {
                    var c = center + d;
                    if (InPlay(c) && !_walls[IndexOf(c)]) grid[IndexOf(c)] = HazardKind.Meteor;
                }
            }
        }

        foreach (var laser in Lasers)
        {
            if (laser.StateAt(tick) != LaserState.On) continue;
            foreach (var c in laser.Beam) grid[IndexOf(c)] = HazardKind.Laser;
        }

        foreach (var saw in Saws) grid[IndexOf(saw.CellAt(tick))] = HazardKind.Saw;
    }
}
