namespace SnakeVs.Core;

/// <summary>A beetle that wanders around a stage. It bites the head (fatal) and turns away from the body.</summary>
public sealed class Bug
{
    internal Bug(Int2 cell, Direction heading, int phase)
    {
        Cell = cell;
        Previous = cell;
        Heading = heading;
        Phase = phase;
    }

    /// <summary>World cell.</summary>
    public Int2 Cell { get; internal set; }

    /// <summary>Where it was before the last step, to draw it sliding.</summary>
    public Int2 Previous { get; internal set; }

    public Direction Heading { get; internal set; }
    public bool Alive { get; internal set; } = true;
    internal int Phase { get; }
}

/// <summary>A ghost that floats straight at the head, through walls. Eating an apple stuns it for a while.</summary>
public sealed class Specter
{
    internal Specter(Int2 cell)
    {
        Cell = cell;
        Previous = cell;
    }

    /// <summary>World cell.</summary>
    public Int2 Cell { get; internal set; }

    public Int2 Previous { get; internal set; }
    public int StunTicks { get; internal set; }
    public bool Stunned => StunTicks > 0;
}

/// <summary>
/// The journey: one run through stages stacked upwards, with no stops. Eating a stage's apples opens the portal in
/// its top wall; going through it puts the snake in the next stage, which the screen scrolls to while the game goes on.
/// Coordinates are world cells (Y grows downwards, so every new stage has smaller Y). Everything moves in steps:
/// hazards are functions of <see cref="StageTick"/>, creatures take their turn right before the snake.
/// </summary>
public sealed class JourneySession
{
    public const int PlayerId = GameSession.PlayerId;
    public const int RivalId = GameSession.RivalId;

    /// <summary>Steps creatures wait after the snake enters their stage.</summary>
    public const int DormantTicks = 10;

    private const int RivalFadeTicks = 8;
    private const int PostClearance = 3;

    private readonly List<GameEvent> _events = [];
    private readonly List<Bug> _bugs = [];
    private readonly HazardKind[] _hazards = new HazardKind[Stage.CellCount];
    private readonly HashSet<Int2> _playerBody = [];
    private readonly Random _rng;
    private readonly int _seed;
    private double _accumulatorMs;
    private int _rivalFade;
    private long _lastAppleTick;

    public JourneySession(Difficulty difficulty, int seed, int startStage = 1)
    {
        Difficulty = difficulty;
        _seed = seed;
        _rng = new Random(unchecked(seed * 486187739 + 17));

        var index = Math.Max(0, startStage - 1);
        Current = StageGenerator.Create(index, seed, StageGenerator.EntryColumn(index, seed));
        var head = index == 0
            ? new Int2(Stage.Width / 2, Stage.Height / 2)
            : new Int2(Current.EntryX + 1, Stage.Height - Rules.StartLength);
        Player = new Snake(PlayerId, Current.ToWorld(head), Direction.Up, Rules.StartLength);
        EnterStage();
    }

    public Difficulty Difficulty { get; }
    public Snake Player { get; }

    /// <summary>The stage being played.</summary>
    public Stage Current { get; private set; }

    /// <summary>The stage just left, still on screen while the map scrolls. Its hazards are switched off.</summary>
    public Stage? Previous { get; private set; }

    public int StageNumber => Current.Number;

    /// <summary>Steps since the snake entered the current stage.</summary>
    public long StageTick { get; private set; }

    /// <summary>Stage step at which <see cref="Previous"/> was left (its hazards are drawn frozen at that moment).</summary>
    public long PreviousStageTick { get; private set; }

    public int ApplesThisStage { get; private set; }

    /// <summary>Steps since the last apple of this stage was eaten (or since the snake came in).</summary>
    public long TicksSinceApple => StageTick - _lastAppleTick;

    /// <summary>World cell of the apple, or null once the stage's apples are done.</summary>
    public Int2? Apple { get; private set; }

    public TimedItem? Golden { get; private set; }
    public TimedItem? PowerUp { get; private set; }
    public IReadOnlyList<Bug> Bugs => _bugs;
    public Specter? Specter { get; private set; }

    /// <summary>The rival snake of the stage. Stays here for a moment after dying so its body can be shown fading.</summary>
    public Snake? Rival { get; private set; }

    /// <summary>False on the steps the rival rested (it is slower than the player): nothing to animate then.</summary>
    public bool RivalMoved { get; private set; }

    public SessionState State { get; private set; }
    public double SlowMs { get; private set; }
    public long StepCount { get; private set; }
    public IReadOnlyList<GameEvent> Events => _events;

    public double StepIntervalMs => Rules.JourneyStepMs(Difficulty, StageNumber) * (SlowMs > 0 ? Rules.SlowFactor : 1.0);

    /// <summary>Progress between the last step and the next one, in [0, 1]. Used to animate movement.</summary>
    public float StepAlpha => (float)Math.Clamp(_accumulatorMs / StepIntervalMs, 0, 1);

    /// <summary>True during the first steps in a stage, while its creatures are still waking up.</summary>
    public bool CreaturesDormant => StageTick < DormantTicks;

    public void ClearEvents() => _events.Clear();

    /// <summary>Hazard on a cell of the current stage right now.</summary>
    public HazardKind HazardAt(Int2 local) => Stage.InPlay(local) ? _hazards[Stage.IndexOf(local)] : HazardKind.None;

    /// <summary>Advances the simulation by real time. Does nothing once the run is over.</summary>
    public void Update(double elapsedMs)
    {
        if (State != SessionState.Playing) return;
        elapsedMs = Math.Clamp(elapsedMs, 0, Rules.MaxFrameMs);
        TickTimers(elapsedMs);

        _accumulatorMs += elapsedMs;
        while (State == SessionState.Playing && _accumulatorMs >= StepIntervalMs)
        {
            _accumulatorMs -= StepIntervalMs;
            Step();
        }
    }

    /// <summary>One step: hazards and creatures move, then the snake, then cuts, pickups and the portal.</summary>
    public void Step()
    {
        if (State != SessionState.Playing) return;
        StepCount++;
        StageTick++;

        var stage = Current;
        var tick = StageTick;
        var p = Player;
        var head = p.Head;

        // On ice there is no turning: the snake keeps sliding and queued turns wait for solid ground.
        var onIce = stage.ContainsRow(head.Y) && stage.IsIce(stage.ToLocal(head));
        if (p.IsAiControlled)
        {
            var choice = JourneyAi.ChooseDirection(this);
            if (!onIce) p.ForceHeading(choice);
        }
        else if (!onIce)
        {
            p.ConsumeNextHeading();
        }

        var next = head + p.Heading.ToOffset();
        var hitsBorder = false;
        var leaves = false;
        if (next.X < 0 || next.X >= Stage.Width || next.Y > stage.Bottom) hitsBorder = true;
        else if (next.Y == stage.Top) hitsBorder = !(stage.GateOpen && stage.IsGateColumn(next.X));
        else if (next.Y < stage.Top) leaves = true;

        var ghost = p.IsGhost;
        var wrapped = false;
        var fatal = false;
        if (hitsBorder)
        {
            if (ghost)
            {
                next = WrapAround(stage, next);
                wrapped = true;
            }
            else
            {
                fatal = true;
            }
        }

        var teleported = false;
        if (!fatal && !leaves && stage.TeleportFrom(stage.ToLocal(next)) is { } exit)
        {
            next = stage.ToWorld(exit);
            wrapped = true;
            teleported = true;
        }

        stage.FillHazards(_hazards, tick);
        AnnounceHazards(stage, tick);

        _playerBody.Clear();
        for (var k = 0; k < p.Length; k++)
        {
            if (k == p.Length - 1 && p.TailWillMove) continue;
            _playerBody.Add(p.Body[k]);
        }

        var headOn = MoveCreatures(next);

        if (!fatal && !ghost) fatal = headOn || IsFatal(stage, head, next, leaves, tick);

        if (fatal && p.Shields > 0)
        {
            p.Shields--;
            p.GhostMs = Math.Max(p.GhostMs, Rules.ShieldGhostMs);
            if (hitsBorder)
            {
                next = WrapAround(stage, next);
                wrapped = true;
            }

            fatal = false;
            _events.Add(new GameEvent(GameEventType.ShieldUsed, next, p.Id));
        }

        if (fatal)
        {
            p.Alive = false;
            _events.Add(new GameEvent(GameEventType.SnakeDied, head, p.Id));
            Finish();
            return;
        }

        p.MoveTo(next, wrapped);
        if (teleported) _events.Add(new GameEvent(GameEventType.Teleported, next, p.Id));

        if (leaves)
        {
            EnterNextStage();
            return;
        }

        if (!p.IsGhost) CutTail(stage);
        CollectPickups();
        GrowPosts(stage);
    }

    /// <summary>Kills the player immediately. Used by the automated tour.</summary>
    public void KillPlayer()
    {
        if (State != SessionState.Playing || !Player.Alive) return;
        Player.Alive = false;
        _events.Add(new GameEvent(GameEventType.SnakeDied, Player.Head, Player.Id));
        Finish();
    }

    /// <summary>True if the world cell holds a wall of the current stage or a snake segment.</summary>
    public bool IsBlocked(Int2 world)
    {
        if (Player.Occupies(world)) return true;
        if (Rival is { Alive: true } rival && rival.Occupies(world)) return true;
        return Current.ContainsRow(world.Y) && Current.IsWall(Current.ToLocal(world));
    }

    internal void ReplaceStageForTest(Stage stage)
    {
        Current = stage;
        Previous = null;
        EnterStage();
    }

    internal void PlaceAppleForTest(Int2 world) => Apple = world;

    internal void PlacePowerUpForTest(Int2 world, PowerUpKind kind) => PowerUp = new TimedItem(world, Rules.PowerUpLifetimeMs, kind);

    internal void SetStageTickForTest(long tick) => StageTick = tick;

    private static Int2 WrapAround(Stage stage, Int2 cell)
    {
        var x = ((cell.X % Stage.Width) + Stage.Width) % Stage.Width;
        var y = cell.Y;
        if (y > stage.Bottom) y = stage.Top + 1;
        else if (y <= stage.Top) y = stage.Bottom;
        return new Int2(x, y);
    }

    private bool IsFatal(Stage stage, Int2 head, Int2 next, bool leaves, long tick)
    {
        var p = Player;
        if (_playerBody.Contains(next)) return true;
        if (leaves) return false;

        var local = stage.ToLocal(next);
        if (local.Y >= 1 && (stage.IsWall(local) || _hazards[Stage.IndexOf(local)] != HazardKind.None)) return true;

        // A blade and the head trading places also meet.
        foreach (var saw in stage.Saws)
        {
            var before = stage.ToWorld(saw.CellAt(tick - 1));
            var now = stage.ToWorld(saw.CellAt(tick));
            if (before != now && before == next && now == head) return true;
        }

        foreach (var bug in _bugs)
        {
            if (!bug.Alive) continue;
            if (bug.Cell == next || (bug.Previous == next && bug.Cell == head && bug.Previous != bug.Cell)) return true;
        }

        if (Specter is { Stunned: false } specter && !CreaturesDormant)
        {
            if (specter.Cell == next || (specter.Previous == next && specter.Cell == head && specter.Previous != specter.Cell)) return true;
        }

        return Rival is { Alive: true } rival && rival.Occupies(next);
    }

    /// <summary>Segments caught by a hazard are lost, together with everything behind them.</summary>
    private void CutTail(Stage stage)
    {
        var p = Player;
        for (var i = 1; i < p.Length; i++)
        {
            var segment = p.Body[i];
            if (!stage.ContainsRow(segment.Y)) continue;
            var local = stage.ToLocal(segment);
            if (local.Y < 1) continue;
            var kind = _hazards[Stage.IndexOf(local)];
            if (kind == HazardKind.None) continue;
            var lost = p.CutFrom(i);
            _events.Add(new GameEvent(GameEventType.SnakeCut, segment, p.Id, lost));
            return;
        }
    }

    private void AnnounceHazards(Stage stage, long tick)
    {
        foreach (var laser in stage.Lasers)
        {
            if (laser.StateAt(tick) != LaserState.On || laser.StateAt(tick - 1) == LaserState.On) continue;
            _events.Add(new GameEvent(GameEventType.LaserFired, stage.ToWorld(laser.Emitter)));
        }

        if (stage.Meteors is { } meteors && meteors.StrikeAt(tick) is >= 0 and var strike)
        {
            _events.Add(new GameEvent(GameEventType.MeteorImpact, stage.ToWorld(meteors.Target(strike))));
        }
    }

    /// <summary>Bugs, the specter and the rival take their step. Returns true if the rival ran head first into the player's next cell.</summary>
    private bool MoveCreatures(Int2 playerNext)
    {
        var stage = Current;
        var tick = StageTick;
        var awake = !CreaturesDormant;

        foreach (var bug in _bugs)
        {
            if (!bug.Alive) continue;
            bug.Previous = bug.Cell;
            if (_hazards[Stage.IndexOf(stage.ToLocal(bug.Cell))] != HazardKind.None)
            {
                bug.Alive = false;
                _events.Add(new GameEvent(GameEventType.EnemyDied, bug.Cell));
                continue;
            }

            if (!awake || (tick + bug.Phase) % stage.BugEvery != 0) continue;
            MoveBug(stage, bug);
        }

        if (Specter is { } specter)
        {
            specter.Previous = specter.Cell;
            if (specter.StunTicks > 0) specter.StunTicks--;
            else if (awake && tick % stage.SpecterEvery == 0) specter.Cell = StepTowards(stage, specter.Cell, playerNext, tick);
        }

        RivalMoved = false;
        if (Rival is not { } rival) return false;
        if (!rival.Alive)
        {
            if (--_rivalFade <= 0) Rival = null;
            return false;
        }

        // Slower than the player: it rests one step out of two, three or four, depending on the difficulty.
        var rest = Difficulty switch
        {
            Difficulty.Easy => 2,
            Difficulty.Normal => 3,
            _ => 4,
        };
        if (!awake || tick % rest == 0) return false;

        var seesPlayer = _rng.NextDouble() >= Rules.JourneyRivalBlindChance(Difficulty);
        rival.ForceHeading(JourneyAi.ChooseRivalDirection(this, rival, playerNext, Player.Heading, seesPlayer, _rng));
        var target = rival.Head + rival.Heading.ToOffset();
        var local = stage.ToLocal(target);
        var headOn = target == playerNext;
        var crashed = headOn
            || !Stage.InPlay(local)
            || stage.IsWall(local)
            || _hazards[Stage.IndexOf(local)] != HazardKind.None
            || _playerBody.Contains(target)
            || _bugs.Any(b => b.Alive && b.Cell == target)
            || RivalBodyBlocks(rival, target);

        if (!crashed)
        {
            rival.MoveTo(target, wrapped: false);
            RivalMoved = true;
            return false;
        }

        KillRival(rival);
        return headOn;
    }

    private static bool RivalBodyBlocks(Snake rival, Int2 target)
    {
        for (var k = 0; k < rival.Length; k++)
        {
            if (k == rival.Length - 1 && rival.TailWillMove) continue;
            if (rival.Body[k] == target) return true;
        }

        return false;
    }

    private void KillRival(Snake rival)
    {
        rival.Alive = false;
        _rivalFade = RivalFadeTicks;
        var points = Rules.JourneyRivalBonus * (Player.HasDouble ? 2 : 1);
        Player.Score += points;
        _events.Add(new GameEvent(GameEventType.EnemyDied, rival.Head, RivalId, points));

        // What is left of it: a golden apple where its head was.
        var local = Current.ToLocal(rival.Head);
        if (Golden is null && Current.CanHoldItem(local) && rival.Head != Apple && !_playerBody.Contains(rival.Head))
        {
            Golden = new TimedItem(rival.Head, Rules.GoldenLifetimeMs);
            _events.Add(new GameEvent(GameEventType.GoldenSpawned, rival.Head));
        }
    }

    private void MoveBug(Stage stage, Bug bug)
    {
        var forward = bug.Heading;
        var left = (Direction)(((int)forward + 3) % 4);
        var right = (Direction)(((int)forward + 1) % 4);
        if (_rng.Next(2) == 0) (left, right) = (right, left);

        // Mostly straight ahead, with the odd turn; always away from whatever is in the way.
        Span<Direction> order = _rng.Next(8) == 0
            ? [left, forward, right, forward.Opposite()]
            : [forward, left, right, forward.Opposite()];

        foreach (var d in order)
        {
            var target = bug.Cell + d.ToOffset();
            if (!CreatureCanEnter(stage, target, bug)) continue;
            bug.Heading = d;
            bug.Cell = target;
            return;
        }
    }

    private bool CreatureCanEnter(Stage stage, Int2 world, Bug self)
    {
        var local = stage.ToLocal(world);
        if (!Stage.InPlay(local) || stage.IsWall(local)) return false;
        if (_hazards[Stage.IndexOf(local)] != HazardKind.None || stage.TeleportFrom(local) is not null) return false;
        if (_playerBody.Contains(world)) return false;
        if (Rival is { Alive: true } rival && rival.Occupies(world)) return false;
        foreach (var other in _bugs)
        {
            if (other != self && other.Alive && other.Cell == world) return false;
        }

        return true;
    }

    private static Int2 StepTowards(Stage stage, Int2 from, Int2 to, long tick)
    {
        var dx = to.X - from.X;
        var dy = Math.Clamp(to.Y, stage.Top + 1, stage.Bottom) - from.Y;
        if (dx == 0 && dy == 0) return from;
        var vertical = Math.Abs(dy) > Math.Abs(dx) || (Math.Abs(dy) == Math.Abs(dx) && tick % 2 == 0);
        return vertical ? new Int2(from.X, from.Y + Math.Sign(dy)) : new Int2(from.X + Math.Sign(dx), from.Y);
    }

    private void CollectPickups()
    {
        var p = Player;
        var head = p.Head;
        var multiplier = p.HasDouble ? 2 : 1;

        if (Rival is { Alive: true } rival && rival.Head == Apple)
        {
            if (rival.Length < Rules.JourneyRivalMaxLength) rival.GrowPending++;
            rival.ApplesEaten++;
            _events.Add(new GameEvent(GameEventType.AppleStolen, rival.Head, RivalId));
            Apple = null;
            SpawnApple();
        }

        if (head == Apple)
        {
            var points = Rules.AppleValue(StageNumber) * multiplier;
            p.GrowPending++;
            p.ApplesEaten++;
            p.Score += points;
            ApplesThisStage++;
            _lastAppleTick = StageTick;
            Apple = null;
            _events.Add(new GameEvent(GameEventType.AteApple, head, p.Id, points));

            if (Specter is { } specter && !CreaturesDormant)
            {
                specter.StunTicks = Rules.SpecterStunTicks;
                _events.Add(new GameEvent(GameEventType.SpecterStunned, specter.Cell));
            }

            if (ApplesThisStage >= Current.AppleGoal) OpenGate();
            else SpawnApple();
            MaybeSpawnBonus();
        }

        if (Golden is not null && head == Golden.Cell)
        {
            var points = Rules.GoldenScore * multiplier;
            p.Score += points;
            _events.Add(new GameEvent(GameEventType.AteGolden, head, p.Id, points));
            Golden = null;
        }

        if (PowerUp is not null && head == PowerUp.Cell)
        {
            var kind = PowerUp.Kind;
            PowerUp = null;
            p.Score += Rules.PowerUpScore * multiplier;
            switch (kind)
            {
                case PowerUpKind.Slow: SlowMs = Rules.SlowDurationMs; break;
                case PowerUpKind.Ghost: p.GhostMs = Rules.GhostDurationMs; break;
                case PowerUpKind.Double: p.DoubleMs = Rules.DoubleDurationMs; break;
                case PowerUpKind.Shrink: p.Shrink(Rules.ShrinkAmount); break;
                case PowerUpKind.Shield: p.Shields = Math.Min(Rules.MaxShields, p.Shields + 1); break;
            }

            _events.Add(new GameEvent(GameEventType.PowerUpCollected, head, p.Id, (int)kind));
        }
    }

    private void OpenGate()
    {
        var stage = Current;
        stage.GateOpen = true;
        stage.GateOpenTick = StageTick;
        _events.Add(new GameEvent(GameEventType.GateOpened, stage.ToWorld(new Int2(stage.GateX + Stage.GateWidth / 2, 0)), Player.Id));
    }

    /// <summary>The posts next to an open portal turn solid one by one, never on top of or right in front of the snake.</summary>
    private void GrowPosts(Stage stage)
    {
        if (!stage.GateOpen) return;
        foreach (var post in stage.Posts)
        {
            if (post.Solid) continue;
            var world = stage.ToWorld(post.Cell);
            if (Player.Occupies(world) || Player.Head.ManhattanTo(world) <= PostClearance) continue;
            if (_bugs.Any(b => b.Alive && b.Cell == world)) continue;
            if (Rival is { Alive: true } rival && (rival.Occupies(world) || rival.Head.ManhattanTo(world) <= PostClearance)) continue;
            if (Golden?.Cell == world || PowerUp?.Cell == world) continue;
            post.Solid = true;
        }
    }

    private void EnterNextStage()
    {
        var left = Current;
        var bonus = Rules.JourneyStageBonus * left.Number;
        Player.Score += bonus;
        Previous = left;
        PreviousStageTick = StageTick;
        Current = StageGenerator.Create(left.Index + 1, _seed, left.GateX);
        EnterStage();
        _events.Add(new GameEvent(GameEventType.StageEntered, Player.Head, Player.Id, bonus));
    }

    /// <summary>Resets what belongs to a stage (step counter, pickups, creatures) and wakes the new one up.</summary>
    private void EnterStage()
    {
        var stage = Current;
        StageTick = 0;
        _lastAppleTick = 0;
        ApplesThisStage = 0;
        Apple = null;
        Golden = null;
        PowerUp = null;
        _bugs.Clear();
        Specter = null;
        Rival = null;
        State = SessionState.Playing;

        var phase = 0;
        foreach (var spawn in stage.BugSpawns)
        {
            _bugs.Add(new Bug(stage.ToWorld(spawn), Directions.All[_rng.Next(4)], phase++));
        }

        if (stage.SpecterSpawn is { } specterCell) Specter = new Specter(stage.ToWorld(specterCell));
        if (stage.RivalSpawn is { } rivalSpawn)
        {
            Rival = new Snake(RivalId, stage.ToWorld(rivalSpawn.Head), rivalSpawn.Heading, Rules.StartLength)
            {
                AiMistakeChance = Rules.AiMistakeChance(Difficulty),
            };
        }

        stage.FillHazards(_hazards, 0);
        _playerBody.Clear();
        foreach (var segment in Player.Body) _playerBody.Add(segment);
        SpawnApple();
    }

    private void SpawnApple()
    {
        var stage = Current;
        if (ApplesThisStage == 0 && stage.FixedApple is { } fixedCell && IsFreeForItem(stage, fixedCell))
        {
            Apple = stage.ToWorld(fixedCell);
            return;
        }

        // Apples take the snake across the stage: up first, and the last one down low, far from the portal.
        var last = ApplesThisStage == stage.AppleGoal - 1;
        var maxRow = stage.ItemMaxRow;
        var (from, to) = last
            ? (Math.Min(maxRow, maxRow * 3 / 5), maxRow)
            : ApplesThisStage % 2 == 0 ? (1, Math.Max(2, maxRow / 2)) : (1, maxRow);
        var cell = RandomFreeCell(from, to) ?? RandomFreeCell(1, maxRow);
        Apple = cell is null ? null : stage.ToWorld(cell.Value);

        // Nowhere to put it (a very long snake in a flooded stage): the portal opens instead of leaving the run stuck.
        if (Apple is null && !stage.GateOpen) OpenGate();
    }

    private void MaybeSpawnBonus()
    {
        if (Golden is null && _rng.NextDouble() < Rules.GoldenChance && RandomFreeCell(1, Current.ItemMaxRow) is { } goldenCell)
        {
            Golden = new TimedItem(Current.ToWorld(goldenCell), Rules.GoldenLifetimeMs);
            _events.Add(new GameEvent(GameEventType.GoldenSpawned, Golden.Cell));
        }

        if (PowerUp is null && _rng.NextDouble() < Rules.PowerUpChance && RandomFreeCell(1, Current.ItemMaxRow) is { } powerCell)
        {
            var kinds = Enum.GetValues<PowerUpKind>();
            var kind = kinds[_rng.Next(kinds.Length)];
            PowerUp = new TimedItem(Current.ToWorld(powerCell), Rules.PowerUpLifetimeMs, kind);
            _events.Add(new GameEvent(GameEventType.PowerUpSpawned, PowerUp.Cell, Value: (int)kind));
        }
    }

    private bool IsFreeForItem(Stage stage, Int2 local)
    {
        if (!stage.CanHoldItem(local) || _hazards[Stage.IndexOf(local)] != HazardKind.None) return false;
        var world = stage.ToWorld(local);
        if (world == Apple || Golden?.Cell == world || PowerUp?.Cell == world) return false;
        if (Player.Occupies(world)) return false;
        if (Rival is { Alive: true } rival && rival.Occupies(world)) return false;
        if (Specter?.Cell == world) return false;
        foreach (var bug in _bugs)
        {
            if (bug.Alive && bug.Cell == world) return false;
        }

        return true;
    }

    /// <summary>A random empty cell between two local rows, avoiding cells right in front of the head. Null when there is none.</summary>
    private Int2? RandomFreeCell(int fromRow, int toRow)
    {
        var stage = Current;
        var free = new List<Int2>();
        var fallback = new List<Int2>();
        for (var y = Math.Max(1, fromRow); y <= Math.Min(Stage.Height - 1, toRow); y++)
        {
            for (var x = 0; x < Stage.Width; x++)
            {
                var c = new Int2(x, y);
                if (!IsFreeForItem(stage, c)) continue;
                if (Player.Head.ManhattanTo(stage.ToWorld(c)) <= 1) fallback.Add(c);
                else free.Add(c);
            }
        }

        if (free.Count > 0) return free[_rng.Next(free.Count)];
        return fallback.Count > 0 ? fallback[_rng.Next(fallback.Count)] : null;
    }

    private void TickTimers(double ms)
    {
        SlowMs = Math.Max(0, SlowMs - ms);
        Player.GhostMs = Math.Max(0, Player.GhostMs - ms);
        Player.DoubleMs = Math.Max(0, Player.DoubleMs - ms);

        if (Golden is not null)
        {
            Golden.RemainingMs -= ms;
            if (Golden.RemainingMs <= 0)
            {
                _events.Add(new GameEvent(GameEventType.GoldenExpired, Golden.Cell));
                Golden = null;
            }
        }

        if (PowerUp is not null)
        {
            PowerUp.RemainingMs -= ms;
            if (PowerUp.RemainingMs <= 0)
            {
                _events.Add(new GameEvent(GameEventType.PowerUpExpired, PowerUp.Cell, Value: (int)PowerUp.Kind));
                PowerUp = null;
            }
        }
    }

    private void Finish()
    {
        State = SessionState.GameOver;
        _events.Add(new GameEvent(GameEventType.GameOver, Player.Head, Player.Id, Player.Score));
    }
}
