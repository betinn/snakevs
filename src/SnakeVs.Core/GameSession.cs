namespace SnakeVs.Core;

/// <summary>
/// The complete, platform independent game simulation. Feed it time with <see cref="Update"/>,
/// read state for rendering, and drain <see cref="Events"/> for feedback.
/// </summary>
public sealed class GameSession
{
    public const int PlayerId = 0;
    public const int RivalId = 1;

    private readonly List<Snake> _snakes = [];
    private readonly List<GameEvent> _events = [];
    private readonly Random _rng;
    private readonly int _seed;
    private double _accumulatorMs;

    public GameSession(GameMode mode, Difficulty difficulty, int seed, int startLevel = 1, int? versusLayout = null)
    {
        Mode = mode;
        Difficulty = difficulty;
        _seed = seed;
        _rng = new Random(seed);
        Level = Math.Max(1, startLevel);
        Layout = mode == GameMode.Versus ? versusLayout ?? LevelGenerator.VersusLayouts[_rng.Next(LevelGenerator.VersusLayouts.Length)] : Level;

        var spawns = LevelGenerator.Spawns(mode, Rules.BoardWidth, Rules.BoardHeight);
        Player = new Snake(PlayerId, spawns[0].Head, spawns[0].Heading, Rules.StartLength);
        _snakes.Add(Player);
        if (mode == GameMode.Versus)
        {
            Rival = new Snake(RivalId, spawns[1].Head, spawns[1].Heading, Rules.StartLength)
            {
                IsAiControlled = true,
                AiMistakeChance = Rules.AiMistakeChance(difficulty),
                AiChasesBonuses = difficulty != Difficulty.Easy,
            };
            _snakes.Add(Rival);
        }

        Board = new Board(Rules.BoardWidth, Rules.BoardHeight);
        StartLevel();
    }

    public GameMode Mode { get; }
    public Difficulty Difficulty { get; }
    public Board Board { get; private set; }
    public int Level { get; private set; }

    /// <summary>Obstacle layout in use. Equals <see cref="Level"/> in classic mode.</summary>
    public int Layout { get; private set; }

    public Snake Player { get; }
    public Snake? Rival { get; }
    public IReadOnlyList<Snake> Snakes => _snakes;

    public Int2 Apple { get; private set; }
    public TimedItem? Golden { get; private set; }
    public TimedItem? PowerUp { get; private set; }

    public int ApplesThisLevel { get; private set; }
    public SessionState State { get; private set; }
    public MatchOutcome Outcome { get; private set; }
    public double SlowMs { get; private set; }
    public long StepCount { get; private set; }

    public IReadOnlyList<GameEvent> Events => _events;

    public double StepIntervalMs => Rules.StepMs(Mode, Difficulty, Level) * (SlowMs > 0 ? Rules.SlowFactor : 1.0);

    /// <summary>Progress between the last step and the next one, in [0, 1]. Used to animate movement.</summary>
    public float StepAlpha => (float)Math.Clamp(_accumulatorMs / StepIntervalMs, 0, 1);

    public int TargetApples => Mode == GameMode.Versus ? Rules.VersusTargetApples : Rules.ApplesPerLevel;

    public void ClearEvents() => _events.Clear();

    /// <summary>Advances the simulation by real time. Does nothing unless the state is <see cref="SessionState.Playing"/>.</summary>
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

    /// <summary>Moves every snake one cell and resolves collisions and pickups.</summary>
    public void Step()
    {
        if (State != SessionState.Playing) return;
        StepCount++;

        var alive = _snakes.Where(s => s.Alive).ToList();
        foreach (var snake in alive)
        {
            if (snake.IsAiControlled)
            {
                snake.ForceHeading(SnakeAi.ChooseDirection(this, snake, _rng));
            }
            else
            {
                snake.ConsumeNextHeading();
            }
        }

        // Where every head wants to go.
        var targets = new Int2[alive.Count];
        var wrapped = new bool[alive.Count];
        var hitWall = new bool[alive.Count];
        for (var i = 0; i < alive.Count; i++)
        {
            var s = alive[i];
            var next = s.Head + s.Heading.ToOffset();
            if (!Board.InBounds(next))
            {
                if (s.IsGhost)
                {
                    next = Board.Wrap(next);
                    wrapped[i] = true;
                }
                else
                {
                    hitWall[i] = true;
                }
            }

            targets[i] = next;
        }

        // Cells that will be occupied by bodies after this move (tails that move away are free).
        var occupied = new HashSet<Int2>();
        foreach (var s in alive)
        {
            for (var k = 0; k < s.Length; k++)
            {
                if (k == s.Length - 1 && s.TailWillMove) continue;
                occupied.Add(s.Body[k]);
            }
        }

        var headCounts = new Dictionary<Int2, int>();
        for (var i = 0; i < alive.Count; i++)
        {
            if (hitWall[i]) continue;
            headCounts[targets[i]] = headCounts.TryGetValue(targets[i], out var n) ? n + 1 : 1;
        }

        var dies = new bool[alive.Count];
        for (var i = 0; i < alive.Count; i++)
        {
            var s = alive[i];
            var fatal = hitWall[i];
            if (!fatal && !s.IsGhost)
            {
                var t = targets[i];
                fatal = Board.IsObstacle(t) || occupied.Contains(t) || headCounts[t] > 1;
            }

            if (!fatal) continue;

            if (s.Shields > 0)
            {
                s.Shields--;
                s.GhostMs = Math.Max(s.GhostMs, Rules.ShieldGhostMs);
                if (hitWall[i])
                {
                    targets[i] = Board.Wrap(targets[i]);
                    wrapped[i] = true;
                }

                _events.Add(new GameEvent(GameEventType.ShieldUsed, targets[i], s.Id));
                continue;
            }

            dies[i] = true;
        }

        for (var i = 0; i < alive.Count; i++)
        {
            var s = alive[i];
            if (dies[i])
            {
                s.Alive = false;
                _events.Add(new GameEvent(GameEventType.SnakeDied, s.Head, s.Id));
            }
            else
            {
                s.MoveTo(targets[i], wrapped[i]);
            }
        }

        foreach (var s in alive)
        {
            if (s.Alive) CollectPickups(s);
        }

        ResolveEndConditions();
    }

    /// <summary>Starts the next level after <see cref="SessionState.LevelCleared"/>.</summary>
    public void AdvanceLevel()
    {
        if (State != SessionState.LevelCleared) return;
        Level++;
        Layout = Level;
        StartLevel();
    }

    /// <summary>Kills the player immediately. Used for debugging and the in-game "give up" flow.</summary>
    public void KillPlayer()
    {
        if (State != SessionState.Playing || !Player.Alive) return;
        Player.Alive = false;
        _events.Add(new GameEvent(GameEventType.SnakeDied, Player.Head, Player.Id));
        ResolveEndConditions();
    }

    internal void PlaceAppleForTest(Int2 cell) => Apple = cell;

    internal void PlacePowerUpForTest(Int2 cell, PowerUpKind kind) => PowerUp = new TimedItem(cell, Rules.PowerUpLifetimeMs, kind);

    internal void PlaceGoldenForTest(Int2 cell) => Golden = new TimedItem(cell, Rules.GoldenLifetimeMs);

    internal void ReplaceBoardForTest(Board board) => Board = board;

    /// <summary>True if the cell holds an obstacle or any snake segment.</summary>
    public bool IsBlocked(Int2 cell) => Board.IsObstacle(cell) || _snakes.Any(s => s.Alive && s.Occupies(cell));

    private void StartLevel()
    {
        Board = LevelGenerator.Create(Mode, Layout, Rules.BoardWidth, Rules.BoardHeight, _seed);
        var spawns = LevelGenerator.Spawns(Mode, Board.Width, Board.Height);
        for (var i = 0; i < _snakes.Count; i++)
        {
            _snakes[i].Reset(spawns[i].Head, spawns[i].Heading, Rules.StartLength);
        }

        ApplesThisLevel = 0;
        Golden = null;
        PowerUp = null;
        SlowMs = 0;
        _accumulatorMs = 0;
        State = SessionState.Playing;
        Outcome = MatchOutcome.None;
        Apple = RandomFreeCell() ?? new Int2(0, 0);
    }

    private void TickTimers(double ms)
    {
        SlowMs = Math.Max(0, SlowMs - ms);
        foreach (var s in _snakes)
        {
            s.GhostMs = Math.Max(0, s.GhostMs - ms);
            s.DoubleMs = Math.Max(0, s.DoubleMs - ms);
        }

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

    private void CollectPickups(Snake s)
    {
        var head = s.Head;
        var multiplier = s.HasDouble ? 2 : 1;

        if (head == Apple)
        {
            s.GrowPending++;
            s.ApplesEaten++;
            var points = Rules.AppleValue(Level) * multiplier;
            s.Score += points;
            if (s.Id == PlayerId) ApplesThisLevel++;
            _events.Add(new GameEvent(GameEventType.AteApple, head, s.Id, points));

            var levelDone = Mode == GameMode.Classic && ApplesThisLevel >= Rules.ApplesPerLevel;
            if (!levelDone)
            {
                var next = RandomFreeCell();
                if (next is null)
                {
                    // Board is full: treat it as a cleared level / finished match.
                    ApplesThisLevel = Math.Max(ApplesThisLevel, Rules.ApplesPerLevel);
                }
                else
                {
                    Apple = next.Value;
                }

                MaybeSpawnBonus();
            }
        }

        if (Golden is not null && head == Golden.Cell)
        {
            var points = Rules.GoldenScore * multiplier;
            s.Score += points;
            _events.Add(new GameEvent(GameEventType.AteGolden, head, s.Id, points));
            Golden = null;
        }

        if (PowerUp is not null && head == PowerUp.Cell)
        {
            var kind = PowerUp.Kind;
            PowerUp = null;
            s.Score += Rules.PowerUpScore * multiplier;
            ApplyPowerUp(s, kind);
            _events.Add(new GameEvent(GameEventType.PowerUpCollected, head, s.Id, (int)kind));
        }
    }

    private void ApplyPowerUp(Snake s, PowerUpKind kind)
    {
        switch (kind)
        {
            case PowerUpKind.Slow:
                SlowMs = Rules.SlowDurationMs;
                break;
            case PowerUpKind.Ghost:
                s.GhostMs = Rules.GhostDurationMs;
                break;
            case PowerUpKind.Double:
                s.DoubleMs = Rules.DoubleDurationMs;
                break;
            case PowerUpKind.Shrink:
                s.Shrink(Rules.ShrinkAmount);
                break;
            case PowerUpKind.Shield:
                s.Shields = Math.Min(Rules.MaxShields, s.Shields + 1);
                break;
        }
    }

    private void MaybeSpawnBonus()
    {
        if (Golden is null && _rng.NextDouble() < Rules.GoldenChance)
        {
            var cell = RandomFreeCell();
            if (cell is not null)
            {
                Golden = new TimedItem(cell.Value, Rules.GoldenLifetimeMs);
                _events.Add(new GameEvent(GameEventType.GoldenSpawned, cell.Value));
            }
        }

        if (PowerUp is null && _rng.NextDouble() < Rules.PowerUpChance)
        {
            var cell = RandomFreeCell();
            if (cell is not null)
            {
                var kinds = Enum.GetValues<PowerUpKind>();
                var kind = kinds[_rng.Next(kinds.Length)];
                PowerUp = new TimedItem(cell.Value, Rules.PowerUpLifetimeMs, kind);
                _events.Add(new GameEvent(GameEventType.PowerUpSpawned, cell.Value, Value: (int)kind));
            }
        }
    }

    /// <summary>A random empty cell, avoiding cells right in front of a head. Null when the board is full.</summary>
    private Int2? RandomFreeCell()
    {
        var free = new List<Int2>(Board.CellCount);
        var fallback = new List<Int2>();
        var bodies = new HashSet<Int2>();
        foreach (var s in _snakes)
        {
            if (!s.Alive) continue;
            foreach (var segment in s.Body) bodies.Add(segment);
        }

        for (var y = 0; y < Board.Height; y++)
        {
            for (var x = 0; x < Board.Width; x++)
            {
                var c = new Int2(x, y);
                if (Board.IsObstacle(c) || bodies.Contains(c)) continue;
                if (c == Apple) continue;
                if (Golden is not null && Golden.Cell == c) continue;
                if (PowerUp is not null && PowerUp.Cell == c) continue;
                if (_snakes.Any(s => s.Alive && s.Head.ManhattanTo(c) <= 1))
                {
                    fallback.Add(c);
                    continue;
                }

                free.Add(c);
            }
        }

        if (free.Count > 0) return free[_rng.Next(free.Count)];
        if (fallback.Count > 0) return fallback[_rng.Next(fallback.Count)];
        return null;
    }

    private void ResolveEndConditions()
    {
        if (Mode == GameMode.Classic)
        {
            if (!Player.Alive)
            {
                State = SessionState.GameOver;
                Outcome = MatchOutcome.RivalWon;
                _events.Add(new GameEvent(GameEventType.GameOver, Player.Head, Player.Id, Player.Score));
            }
            else if (ApplesThisLevel >= Rules.ApplesPerLevel)
            {
                var bonus = Rules.LevelBonus * Level;
                Player.Score += bonus;
                State = SessionState.LevelCleared;
                _events.Add(new GameEvent(GameEventType.LevelCleared, Player.Head, Player.Id, bonus));
            }

            return;
        }

        var rival = Rival!;
        var outcome = MatchOutcome.None;
        if (!Player.Alive && !rival.Alive) outcome = MatchOutcome.Draw;
        else if (!Player.Alive) outcome = MatchOutcome.RivalWon;
        else if (!rival.Alive) outcome = MatchOutcome.PlayerWon;
        else if (Player.ApplesEaten >= Rules.VersusTargetApples && rival.ApplesEaten >= Rules.VersusTargetApples) outcome = MatchOutcome.Draw;
        else if (Player.ApplesEaten >= Rules.VersusTargetApples) outcome = MatchOutcome.PlayerWon;
        else if (rival.ApplesEaten >= Rules.VersusTargetApples) outcome = MatchOutcome.RivalWon;

        if (outcome == MatchOutcome.None) return;
        Outcome = outcome;
        State = SessionState.MatchOver;
        _events.Add(new GameEvent(GameEventType.MatchOver, Player.Head, Value: (int)outcome));
    }
}

