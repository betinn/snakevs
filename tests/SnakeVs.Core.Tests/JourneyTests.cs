using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class JourneyTests
{
    private const int W = Stage.Width;
    private const int H = Stage.Height;

    /// <summary>A run whose first stage is replaced by a hand-made one (index 0, so local and world cells are the same).</summary>
    private static JourneySession Custom(Action<StageBuilder>? setup = null, int appleGoal = 1)
    {
        var s = new JourneySession(Difficulty.Normal, seed: 5);
        var b = new StageBuilder(0, StageGenerator.FirstEntryX, StageGenerator.FirstEntryX) { AppleGoal = appleGoal };
        b.Elements.Add(StageElement.Blocks);
        setup?.Invoke(b);
        s.ReplaceStageForTest(b.Build());
        s.PlaceAppleForTest(new Int2(0, H - 1));
        return s;
    }

    private static void OpenPortal(JourneySession s)
    {
        var p = s.Player;
        p.Reset(new Int2(3, 20), Direction.Right, 4);
        s.PlaceAppleForTest(new Int2(4, 20));
        s.Step();
        Assert.True(s.Current.GateOpen);
    }

    // ---- The run ----

    [Fact]
    public void FirstStage_HasOneAppleLowOnTheMap_AndAClosedPortal()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var s = new JourneySession(Difficulty.Normal, seed);
            Assert.Equal(1, s.StageNumber);
            Assert.Equal(1, s.Current.AppleGoal);
            Assert.False(s.Current.GateOpen);
            Assert.NotNull(s.Apple);
            Assert.True(s.Apple!.Value.Y > s.Player.Head.Y + 5, "The first apple must be below the snake.");
            Assert.Equal(Direction.Up, s.Player.Heading);
        }
    }

    [Fact]
    public void EatingTheLastApple_OpensThePortal()
    {
        var s = Custom();
        s.Player.Reset(new Int2(3, 20), Direction.Right, 4);
        s.PlaceAppleForTest(new Int2(4, 20));

        s.Step();

        Assert.True(s.Current.GateOpen);
        Assert.Null(s.Apple);
        Assert.Contains(s.Events, e => e.Type == GameEventType.GateOpened);
        Assert.Equal(Rules.AppleValue(1), s.Player.Score);
    }

    [Fact]
    public void Portal_StaysClosed_UntilTheGoalIsReached()
    {
        var s = Custom(appleGoal: 2);
        s.Player.Reset(new Int2(3, 20), Direction.Right, 4);
        s.PlaceAppleForTest(new Int2(4, 20));

        s.Step();

        Assert.False(s.Current.GateOpen);
        Assert.NotNull(s.Apple);
        Assert.Equal(1, s.ApplesThisStage);
    }

    [Fact]
    public void ClosedPortal_IsAWall()
    {
        var s = Custom();
        s.Player.Reset(new Int2(s.Current.GateX + 1, 1), Direction.Up, 4);

        s.Step();

        Assert.False(s.Player.Alive);
        Assert.Equal(SessionState.GameOver, s.State);
    }

    [Fact]
    public void GoingThroughThePortal_EntersTheNextStage_WithoutStopping()
    {
        var s = Custom();
        OpenPortal(s);
        var first = s.Current;
        var scoreBefore = s.Player.Score;
        var stepsBefore = s.StepCount;
        s.Player.Reset(new Int2(first.GateX + 1, 2), Direction.Up, 4);
        s.ClearEvents();

        s.Step(); // row 1
        s.Step(); // inside the portal (the wall row)
        Assert.Equal(1, s.StageNumber);
        s.Step(); // first row of the next stage

        Assert.Equal(2, s.StageNumber);
        Assert.Equal(SessionState.Playing, s.State);
        Assert.True(s.Player.Alive);
        Assert.Equal(Direction.Up, s.Player.Heading);
        Assert.Same(first, s.Previous);
        Assert.Equal(first.GateX, s.Current.EntryX);
        Assert.Equal(s.Current.Bottom, s.Player.Head.Y);
        Assert.Equal(0, s.StageTick);
        Assert.Equal(stepsBefore + 3, s.StepCount);
        Assert.Equal(scoreBefore + Rules.JourneyStageBonus, s.Player.Score);
        Assert.Contains(s.Events, e => e.Type == GameEventType.StageEntered && e.Value == Rules.JourneyStageBonus);
        Assert.NotNull(s.Apple);
        Assert.False(s.Current.GateOpen);

        // The run simply goes on.
        s.Step();
        Assert.True(s.Player.Alive);
        Assert.Equal(1, s.StageTick);
    }

    [Fact]
    public void ThereIsNoWayBack_ThroughThePortalJustCrossed()
    {
        var s = Custom();
        OpenPortal(s);
        s.Player.Reset(new Int2(s.Current.GateX + 1, 1), Direction.Up, 2);
        s.Step();
        s.Step();
        Assert.Equal(2, s.StageNumber);

        s.Player.QueueTurn(Direction.Right);
        s.Step();
        s.Player.QueueTurn(Direction.Down);
        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void PortalPosts_GrowOnlyAwayFromTheSnake()
    {
        var s = Custom();
        var post = s.Current.Posts[0];
        s.Player.Reset(post.Cell + new Int2(0, 2), Direction.Right, 3);
        s.PlaceAppleForTest(s.Player.Head + new Int2(1, 0));
        s.Step();
        Assert.True(s.Current.GateOpen);
        Assert.False(post.Solid);

        s.Player.Reset(new Int2(2, 25), Direction.Right, 3);
        s.Step();

        Assert.All(s.Current.Posts, p => Assert.True(p.Solid));
        Assert.True(s.Current.IsWall(post.Cell));
    }

    [Fact]
    public void Speed_GoesUpWithTheStages_DownToAFloor()
    {
        foreach (var difficulty in Enum.GetValues<Difficulty>())
        {
            Assert.Equal(Rules.BaseStepMs(difficulty), Rules.JourneyStepMs(difficulty, 1));
            for (var stage = 2; stage < 200; stage++)
            {
                Assert.True(Rules.JourneyStepMs(difficulty, stage) <= Rules.JourneyStepMs(difficulty, stage - 1));
            }

            Assert.Equal(Rules.JourneyMinStepMs, Rules.JourneyStepMs(difficulty, 500));
        }
    }

    // ---- Hazards: the head dies, the tail is cut ----

    [Fact]
    public void Saw_RunsBackAndForthOnItsRail()
    {
        var saw = new Saw([new Int2(2, 5), new Int2(3, 5), new Int2(4, 5)], loop: false, period: 1, offset: 0);
        var xs = Enumerable.Range(0, 9).Select(t => saw.CellAt(t).X).ToArray();
        Assert.Equal([2, 3, 4, 3, 2, 3, 4, 3, 2], xs);

        var slow = new Saw([new Int2(2, 5), new Int2(3, 5)], loop: false, period: 2, offset: 0);
        Assert.Equal([2, 2, 3, 3, 2], Enumerable.Range(0, 5).Select(t => slow.CellAt(t).X).ToArray());

        var loop = new Saw([new Int2(0, 1), new Int2(1, 1), new Int2(1, 2), new Int2(0, 2)], loop: true, period: 1, offset: 0);
        Assert.Equal(loop.CellAt(0), loop.CellAt(4));
    }

    [Fact]
    public void Saw_KillsTheHead()
    {
        var s = Custom(b => b.Saws.Add(new Saw([new Int2(6, 10)], loop: false, period: 1, offset: 0)));
        s.Player.Reset(new Int2(5, 10), Direction.Right, 4);

        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void Saw_AndHeadTradingPlaces_Collide()
    {
        // Blade at x=6 moving to x=5 on the same step the head goes from 5 to 6.
        var s = Custom(b => b.Saws.Add(new Saw([new Int2(6, 10), new Int2(5, 10), new Int2(4, 10)], loop: false, period: 1, offset: 0)));
        s.Player.Reset(new Int2(5, 10), Direction.Right, 2);

        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void Saw_CutsTheTail()
    {
        // The blade sits on the column the body is crossing.
        var s = Custom(b => b.Saws.Add(new Saw([new Int2(5, 12)], loop: false, period: 1, offset: 0)));
        s.Player.Reset(new Int2(5, 9), Direction.Up, 6); // body on rows 9..14: the segment on row 12 is index 3
        s.Player.GhostMs = 0;

        s.Step(); // head to row 8, body now rows 8..13: row 12 is index 4

        Assert.True(s.Player.Alive);
        Assert.Equal(4, s.Player.Length);
        var cut = Assert.Single(s.Events, e => e.Type == GameEventType.SnakeCut);
        Assert.Equal(2, cut.Value);
        Assert.Equal(new Int2(5, 12), cut.Cell);
    }

    [Fact]
    public void Laser_WarnsBeforeFiring_AndStopsAtTheFirstWall()
    {
        var s = Custom(b =>
        {
            b.TryWall(0, 10);
            b.TryWall(8, 10);
            b.Lasers.Add(new Laser(new Int2(0, 10), Direction.Right, period: 10, onTicks: 3, warnTicks: 2, delay: 4));
        });
        var laser = s.Current.Lasers[0];

        Assert.Equal(7, laser.Beam.Count);
        Assert.Equal(new Int2(7, 10), laser.Beam[^1]);
        var states = Enumerable.Range(0, 15).Select(t => laser.StateAt(t)).ToArray();
        Assert.All(states.Take(9), st => Assert.Equal(LaserState.Off, st));        // delay 4 + off 5
        Assert.Equal([LaserState.Warning, LaserState.Warning], states[9..11]);
        Assert.Equal([LaserState.On, LaserState.On, LaserState.On], states[11..14]);
        Assert.Equal(LaserState.Off, states[14]);
    }

    [Fact]
    public void Laser_KillsOnlyWhileItIsOn()
    {
        JourneySession Make() => Custom(b =>
        {
            b.TryWall(0, 10);
            b.Lasers.Add(new Laser(new Int2(0, 10), Direction.Right, period: 10, onTicks: 3, warnTicks: 2, delay: 0));
        });

        var safe = Make();
        safe.Player.Reset(new Int2(5, 11), Direction.Up, 2);
        safe.SetStageTickForTest(0); // the step lands on tick 1: off
        safe.Step();
        Assert.True(safe.Player.Alive);

        var warned = Make();
        warned.Player.Reset(new Int2(5, 11), Direction.Up, 2);
        warned.SetStageTickForTest(5); // tick 6: warning
        warned.Step();
        Assert.True(warned.Player.Alive);

        var burnt = Make();
        burnt.Player.Reset(new Int2(5, 11), Direction.Up, 2);
        burnt.SetStageTickForTest(7); // tick 8: on
        burnt.Step();
        Assert.False(burnt.Player.Alive);
    }

    [Fact]
    public void PhaseBlocks_TakeTurnsBeingSolid()
    {
        var a = new PhaseGroup([new Int2(3, 3)], period: 10, solidTicks: 5, warnTicks: 2, offset: 0);
        var b = new PhaseGroup([new Int2(6, 3)], period: 10, solidTicks: 5, warnTicks: 2, offset: 5);
        for (var t = 0; t < 40; t++) Assert.NotEqual(a.SolidAt(t), b.SolidAt(t));
        Assert.True(a.SolidAt(0));
        Assert.False(a.SolidAt(5));
        Assert.True(a.WarningAt(8));
        Assert.False(a.WarningAt(6));
    }

    [Fact]
    public void Lava_RisesRowByRow_UpToItsLimit_AndBurnsTheTail()
    {
        var tide = new LavaTide(Delay: 5, TicksPerRow: 2, MaxRows: 3);
        Assert.Equal(0, tide.RowsAt(4));
        Assert.Equal(1, tide.RowsAt(5));
        Assert.Equal(2, tide.RowsAt(7));
        Assert.Equal(3, tide.RowsAt(9));
        Assert.Equal(3, tide.RowsAt(1000));

        // The snake runs along the second row from the bottom, then turns up: the lava reaches that row behind it.
        var s = Custom(b => b.Lava = new LavaTide(Delay: 1, TicksPerRow: 2, MaxRows: 3));
        s.Player.Reset(new Int2(6, H - 2), Direction.Right, 5);
        s.Step();                             // tick 1: one row of lava, under the snake
        s.Player.QueueTurn(Direction.Up);
        s.Step();                             // tick 2
        Assert.Equal(5, s.Player.Length);
        s.Step();                             // tick 3: two rows, the tail is still on the second one

        Assert.True(s.Player.Alive);
        Assert.Contains(s.Events, e => e.Type == GameEventType.SnakeCut);
        Assert.Equal(2, s.Player.Length);
        Assert.All(s.Player.Body, c => Assert.True(c.Y < H - 2));
    }

    [Fact]
    public void Meteors_AreAnnouncedBeforeTheyLand_AndAlwaysAimInsideTheStage()
    {
        var shower = new MeteorShower(Seed: 99, Start: 20, Interval: 4, WarnTicks: 6);
        Assert.Equal(-1, shower.StrikeAt(19));
        Assert.Equal(0, shower.StrikeAt(20));
        Assert.Equal(-1, shower.StrikeAt(21));
        Assert.Equal(1, shower.StrikeAt(24));
        Assert.Equal((0L, -1L), shower.AnnouncedAt(13));
        Assert.Equal((0L, 0L), shower.AnnouncedAt(14));
        Assert.Equal((0L, 1L), shower.AnnouncedAt(20));
        Assert.Equal((1L, 2L), shower.AnnouncedAt(22));

        var seen = new HashSet<Int2>();
        for (var k = 0; k < 2000; k++)
        {
            var target = shower.Target(k);
            Assert.True(Stage.InPlay(target));
            seen.Add(target);
        }

        Assert.True(seen.Count > 400, "Strikes should spread over the stage.");
    }

    [Fact]
    public void Ghost_PassesThroughHazards_AndWrapsAtTheClosedPortal()
    {
        var s = Custom(b => b.Saws.Add(new Saw([new Int2(5, 1)], loop: false, period: 1, offset: 0)));
        s.Player.Reset(new Int2(5, 2), Direction.Up, 3);
        s.Player.GhostMs = 5000;

        s.Step(); // through the blade
        Assert.True(s.Player.Alive);
        Assert.Equal(3, s.Player.Length);

        s.Step(); // the wall row is closed: out at the bottom of the same stage
        Assert.True(s.Player.Alive);
        Assert.Equal(new Int2(5, H - 1), s.Player.Head);
        Assert.Equal(1, s.StageNumber);
    }

    [Fact]
    public void Shield_AbsorbsOneHazard()
    {
        var s = Custom(b => b.Saws.Add(new Saw([new Int2(6, 10)], loop: false, period: 1, offset: 0)));
        s.Player.Reset(new Int2(5, 10), Direction.Right, 4);
        s.PlacePowerUpForTest(new Int2(5, 10), PowerUpKind.Shield);
        s.Player.Shields = 1;

        s.Step();

        Assert.True(s.Player.Alive);
        Assert.Equal(0, s.Player.Shields);
        Assert.True(s.Player.IsGhost);
        Assert.Contains(s.Events, e => e.Type == GameEventType.ShieldUsed);
    }

    // ---- Terrain ----

    [Fact]
    public void Ice_KeepsTheSnakeGoingStraight_UntilSolidGround()
    {
        var s = Custom(b =>
        {
            for (var x = 4; x <= 7; x++) b.Ice[Stage.IndexOf(new Int2(x, 20))] = true;
        });
        s.Player.Reset(new Int2(3, 20), Direction.Right, 3);

        s.Step(); // onto the ice (x=4)
        s.Player.QueueTurn(Direction.Up);
        s.Step(); // x=5, turn ignored
        s.Step(); // x=6
        s.Step(); // x=7
        Assert.Equal(new Int2(7, 20), s.Player.Head);
        s.Step(); // x=8: off the ice, still heading right (the head was on ice when the step began)
        Assert.Equal(new Int2(8, 20), s.Player.Head);
        s.Step(); // the queued turn finally applies
        Assert.Equal(new Int2(8, 19), s.Player.Head);
    }

    [Fact]
    public void Teleporter_MovesTheHeadToItsOtherEnd_KeepingTheHeading()
    {
        var s = Custom(b =>
        {
            b.Claim(new Int2(6, 20));
            b.Claim(new Int2(15, 8));
            b.Teleports.Add((new Int2(6, 20), new Int2(15, 8)));
        });
        s.Player.Reset(new Int2(5, 20), Direction.Right, 3);

        s.Step();
        Assert.Equal(new Int2(15, 8), s.Player.Head);
        Assert.Contains(s.Events, e => e.Type == GameEventType.Teleported);

        s.Step();
        Assert.Equal(new Int2(16, 8), s.Player.Head);
        Assert.True(s.Player.Alive);
    }

    // ---- Creatures ----

    [Fact]
    public void Bug_BitesTheHead()
    {
        var s = Custom(b => b.BugSpawns.Add(new Int2(6, 10)));
        s.Player.Reset(new Int2(5, 10), Direction.Right, 4);

        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void Bugs_NeverWalkIntoWallsOrTheSnakesBody()
    {
        var s = Custom(b =>
        {
            for (var i = 0; i < 6; i++) b.BugSpawns.Add(new Int2(2 + i * 3, 5));
            for (var x = 4; x < 16; x++) b.TryWall(x, 8);
        });
        s.Player.Reset(new Int2(18, 28), Direction.Left, 12);

        for (var i = 0; i < 300 && s.Player.Alive; i++)
        {
            // Walk the bottom rows in a loop, far from the bugs.
            var head = s.Player.Head;
            if (head.X == 1 && s.Player.Heading == Direction.Left) s.Player.QueueTurn(head.Y == 28 ? Direction.Up : Direction.Down);
            else if (head.X == 18 && s.Player.Heading == Direction.Right) s.Player.QueueTurn(head.Y == 28 ? Direction.Up : Direction.Down);
            else if (s.Player.Heading is Direction.Up or Direction.Down) s.Player.QueueTurn(head.X <= 1 ? Direction.Right : Direction.Left);
            s.Step();
            foreach (var bug in s.Bugs)
            {
                if (!bug.Alive) continue;
                Assert.False(s.Current.IsWall(bug.Cell), $"Bug inside a wall at {bug.Cell}");
                Assert.True(Stage.InPlay(bug.Cell));
                Assert.True(bug.Cell.ManhattanTo(bug.Previous) <= 1);
            }
        }

        Assert.Contains(s.Bugs, b => b.Cell != s.Current.ToWorld(s.Current.BugSpawns[0]));
    }

    [Fact]
    public void Specter_HuntsTheHead_AndIsStunnedByAnApple()
    {
        var s = Custom(b => b.SpecterSpawn = new Int2(2, 3), appleGoal: 3);
        s.Player.Reset(new Int2(1, 25), Direction.Right, 3);
        var spawn = s.Specter!.Cell;

        for (var i = 0; i < JourneySession.DormantTicks - 1; i++) s.Step();
        Assert.Equal(spawn, s.Specter.Cell); // still waking up
        for (var i = 0; i < 6; i++) s.Step();
        Assert.True(s.Player.Alive);
        Assert.True(s.Specter.Cell.ManhattanTo(s.Player.Head) < spawn.ManhattanTo(s.Player.Head), "The specter should be closing in.");

        s.PlaceAppleForTest(s.Player.Head + new Int2(1, 0));
        s.Step();
        Assert.True(s.Specter.Stunned);
        var frozen = s.Specter.Cell;
        s.Step();
        s.Step();
        Assert.Equal(frozen, s.Specter.Cell);
    }

    [Fact]
    public void Specter_KillsOnContact_UnlessStunned()
    {
        var s = Custom(b => b.SpecterSpawn = new Int2(6, 10), appleGoal: 3);
        s.Player.Reset(new Int2(5, 10), Direction.Right, 3);
        s.SetStageTickForTest(JourneySession.DormantTicks + 1); // awake; tick 12 is a rest step for it (every 3rd)... it may move, towards the head
        s.Step();
        Assert.False(s.Player.Alive);

        var stunned = Custom(b => b.SpecterSpawn = new Int2(7, 10), appleGoal: 3);
        stunned.Player.Reset(new Int2(5, 10), Direction.Right, 3);
        stunned.SetStageTickForTest(JourneySession.DormantTicks + 1);
        stunned.PlaceAppleForTest(new Int2(6, 10));
        stunned.Step(); // eats: the specter (wherever it stepped) is stunned
        if (!stunned.Player.Alive) return; // it reached the head on that very step: nothing left to check
        stunned.Step();
        stunned.Step();
        Assert.True(stunned.Player.Alive);
    }

    [Fact]
    public void Rival_StealsApples_ButNeverCountsForThePlayer_AndStopsGrowing()
    {
        var s = Custom(b => b.RivalSpawn = new SpawnPoint(new Int2(12, 6), Direction.Left), appleGoal: 6);
        s.PlaceAppleForTest(new Int2(15, 4));
        s.Player.Reset(new Int2(17, 28), Direction.Right, 3);
        var stolen = 0;
        var eaten = 0;
        Direction[] circle = [Direction.Up, Direction.Left, Direction.Down, Direction.Right];
        for (var i = 0; i < 1200 && s.Rival is { Alive: true } && s.Player.Alive; i++)
        {
            // The player turns in a tight circle in a corner; the rival has the apples for itself.
            s.Player.QueueTurn(circle[i % 4]);
            s.ClearEvents();
            s.Step();
            stolen += s.Events.Count(e => e.Type == GameEventType.AppleStolen);
            eaten += s.Events.Count(e => e.Type == GameEventType.AteApple);
            if (s.Rival is { } r) Assert.True(r.Length <= Rules.JourneyRivalMaxLength);
        }

        Assert.True(stolen >= 3, $"The rival only ate {stolen} apples.");
        Assert.Equal(eaten, s.ApplesThisStage);
        Assert.Equal(eaten, s.Player.ApplesEaten);
    }

    [Fact]
    public void Rival_CrashingIntoThePlayer_PaysABonus()
    {
        var bonuses = 0;
        for (var seed = 0; seed < 60; seed++)
        {
            var s = new JourneySession(Difficulty.Normal, seed, startStage: 13);
            Assert.Contains(StageElement.Rival, s.Current.Elements);
            s.Player.IsAiControlled = true;
            for (var i = 0; i < 600 && s.State == SessionState.Playing && s.StageNumber == 13; i++)
            {
                s.ClearEvents();
                var before = s.Player.Score;
                s.Step();
                foreach (var e in s.Events)
                {
                    if (e.Type != GameEventType.EnemyDied || e.SnakeId != JourneySession.RivalId) continue;
                    bonuses++;
                    Assert.Equal(Rules.JourneyRivalBonus, e.Value);
                    Assert.True(s.Player.Score >= before + Rules.JourneyRivalBonus);
                    Assert.False(s.Rival!.Alive);
                }
            }
        }

        Assert.True(bonuses > 0, "In 60 runs the rival should crash at least once.");
    }

    // ---- Stage generator ----

    [Fact]
    public void Generator_IsDeterministic()
    {
        for (var index = 0; index < 30; index++)
        {
            var a = StageGenerator.Create(index, 42, 8);
            var b = StageGenerator.Create(index, 42, 8);
            Assert.Equal(a.GateX, b.GateX);
            Assert.Equal(a.Elements, b.Elements);
            Assert.Equal(Fingerprint(a), Fingerprint(b));
        }
    }

    [Fact]
    public void Generator_IntroducesOneElementPerStage_ThenMixes()
    {
        var entry = StageGenerator.FirstEntryX;
        for (var index = 0; index < 60; index++)
        {
            var stage = StageGenerator.Create(index, 7, entry);
            entry = stage.GateX;
            if (index < StageGenerator.IntroStages)
            {
                Assert.Equal([StageGenerator.Intro[index]], stage.Elements);
            }
            else
            {
                Assert.Equal((index + 1) % 5 == 0 ? 3 : 2, stage.Elements.Count);
                Assert.DoesNotContain(StageElement.Start, stage.Elements);
                Assert.Equal(stage.Elements.Count, stage.Elements.Distinct().Count());
            }

            Assert.InRange(stage.AppleGoal, 1, Rules.JourneyMaxApplesPerStage);
        }
    }

    [Fact]
    public void Generator_StagesMatchTheirElements()
    {
        for (var seed = 0; seed < 12; seed++)
        {
            var entry = StageGenerator.FirstEntryX;
            for (var index = 0; index < 40; index++)
            {
                var stage = StageGenerator.Create(index, seed, entry);
                entry = stage.GateX;
                bool Has(StageElement e) => stage.Elements.Contains(e);
                Assert.Equal(Has(StageElement.Saws), stage.Saws.Count > 0);
                Assert.Equal(Has(StageElement.Lasers) || Has(StageElement.Turret), stage.Lasers.Count > 0);
                Assert.Equal(Has(StageElement.Bugs), stage.BugSpawns.Count > 0);
                Assert.Equal(Has(StageElement.Phase), stage.PhaseGroups.Count > 0);
                Assert.Equal(Has(StageElement.Lava), stage.Lava is not null);
                Assert.Equal(Has(StageElement.Teleports), stage.Teleports.Count > 0);
                Assert.Equal(Has(StageElement.Specter), stage.SpecterSpawn is not null);
                Assert.Equal(Has(StageElement.Blackout), stage.Dark);
                Assert.Equal(Has(StageElement.Meteors), stage.Meteors is not null);
                Assert.Equal(Has(StageElement.Rival), stage.RivalSpawn is not null);
                Assert.Equal(Has(StageElement.Ice), Enumerable.Range(0, Stage.CellCount).Any(i => stage.IsIce(Stage.FromIndex(i))));
            }
        }
    }

    [Fact]
    public void Generator_EveryStageIsFair()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var entry = StageGenerator.FirstEntryX;
            for (var index = 0; index < 45; index++)
            {
                var stage = StageGenerator.Create(index, seed, entry);
                var label = $"seed {seed} stage {stage.Number}";
                Assert.Equal(entry, stage.EntryX);
                Assert.InRange(stage.GateX, 1, W - Stage.GateWidth - 1);

                // The way in: nothing solid, no rails, no ice and no blocks that come and go right above the opening.
                if (index > 0)
                {
                    for (var y = H - 7; y < H; y++)
                    {
                        for (var x = entry; x < entry + Stage.GateWidth; x++)
                        {
                            var c = new Int2(x, y);
                            Assert.False(stage.IsStaticWall(c), $"{label}: wall in the way in at {c}");
                            Assert.False(stage.IsIce(c), $"{label}: ice in the way in at {c}");
                            Assert.DoesNotContain(stage.Saws, saw => saw.Path.Contains(c));
                            Assert.DoesNotContain(stage.PhaseGroups, g => g.Cells.Contains(c));
                        }
                    }
                }

                // The way out: the cells under the portal are open.
                for (var x = stage.GateX; x < stage.GateX + Stage.GateWidth; x++)
                {
                    for (var y = 1; y <= 3; y++) Assert.False(stage.IsStaticWall(new Int2(x, y)), $"{label}: wall under the portal");
                }

                // Every open cell can be reached, with the portal posts already grown.
                var open = OpenCells(stage);
                var start = index == 0 ? new Int2(W / 2, H / 2) : new Int2(entry + 1, H - 1);
                Assert.Equal(open.Count, Reachable(stage, open, start).Count);

                // Pickups only where the snake can get in and out again.
                var items = 0;
                foreach (var c in open)
                {
                    if (!stage.CanHoldItem(c)) continue;
                    items++;
                    var exits = Directions.All.Count(d => open.Contains(c + d.ToOffset()));
                    Assert.True(exits >= 2, $"{label}: pickup cell {c} is a dead end");
                    Assert.Null(stage.TeleportFrom(c));
                    Assert.DoesNotContain(stage.Saws, saw => saw.Path.Contains(c));
                    if (stage.Lava is { } lava) Assert.True(c.Y < H - lava.MaxRows, $"{label}: pickup cell {c} gets flooded");
                }

                Assert.True(items >= 40, $"{label}: only {items} cells for pickups");

                foreach (var laser in stage.Lasers)
                {
                    Assert.True(stage.IsStaticWall(laser.Emitter));
                    Assert.All(laser.Beam, c => Assert.False(stage.IsStaticWall(c)));
                    Assert.True(laser.Period - laser.OnTicks - laser.WarnTicks >= 5, $"{label}: no time to cross a laser");
                    Assert.True(laser.WarnTicks >= 2);
                    Assert.Equal(LaserState.Off, laser.StateAt(0));
                }

                foreach (var saw in stage.Saws)
                {
                    Assert.All(saw.Path, c => Assert.False(stage.IsStaticWall(c)));
                    for (var i = 1; i < saw.Path.Count; i++) Assert.True(saw.Path[i].IsAdjacentTo(saw.Path[i - 1]));
                }

                foreach (var (a, b) in stage.Teleports)
                {
                    Assert.False(stage.IsStaticWall(a));
                    Assert.False(stage.IsStaticWall(b));
                    Assert.Equal(b, stage.TeleportFrom(a));
                    Assert.Equal(a, stage.TeleportFrom(b));
                }

                foreach (var bug in stage.BugSpawns) Assert.False(stage.IsStaticWall(bug));
                entry = stage.GateX;
            }
        }
    }

    [Fact]
    public void Generator_NeighbouringSeeds_GiveDifferentJourneys()
    {
        var gates = Enumerable.Range(0, 40).Select(seed => StageGenerator.Create(1, seed, 8).GateX).Distinct().Count();
        Assert.True(gates >= 6, $"Only {gates} different portal positions in 40 seeds.");

        var along = Enumerable.Range(1, 30).Select(index => StageGenerator.Create(index, 3, 8).GateX).ToArray();
        var steps = along.Zip(along.Skip(1), (a, b) => Math.Abs(a - b)).Count(d => d == 1);
        Assert.True(steps < 15, "Portal positions should not just walk one column per stage.");
    }

    // ---- Autopilot and whole runs ----

    [Fact]
    public void Autopilot_GoesThroughThePortal()
    {
        var cleared = 0;
        for (var seed = 0; seed < 20; seed++)
        {
            var s = new JourneySession(Difficulty.Normal, seed);
            s.Player.IsAiControlled = true;
            for (var i = 0; i < 400 && s.State == SessionState.Playing && s.StageNumber < 3; i++) s.Step();
            if (s.StageNumber >= 3) cleared++;
        }

        Assert.True(cleared >= 18, $"The autopilot only cleared the first two stages in {cleared} of 20 runs.");
    }

    /// <summary>
    /// Plays many whole runs with the autopilot. Catches crashes, runs that get stuck and broken invariants across
    /// every element and their mixes, and checks that every final score is one the ranking accepts.
    /// </summary>
    [Fact]
    public void Simulation_ManyRuns_KeepInvariants()
    {
        var best = 0;
        var seenElements = new HashSet<StageElement>();
        foreach (var difficulty in Enum.GetValues<Difficulty>())
        {
            for (var seed = 0; seed < 16; seed++)
            {
                var s = new JourneySession(difficulty, 300 + seed);
                s.Player.IsAiControlled = true;
                var lastProgress = 0L;
                var progress = (Stage: 1, Apples: 0);
                for (var frame = 0; frame < 5000 && s.State == SessionState.Playing; frame++)
                {
                    s.ClearEvents();
                    s.Update(s.StepIntervalMs);
                    foreach (var e in s.Current.Elements) seenElements.Add(e);

                    var p = s.Player;
                    var stage = s.Current;
                    Assert.True(p.Length >= 1);
                    Assert.InRange(p.Head.X, 0, W - 1);
                    Assert.InRange(p.Head.Y, stage.Top, stage.Bottom);
                    Assert.InRange(s.ApplesThisStage, 0, stage.AppleGoal);
                    Assert.Equal(stage.GateOpen, s.Apple is null);
                    if (s.Apple is { } apple)
                    {
                        Assert.True(stage.CanHoldItem(stage.ToLocal(apple)), $"Apple on a bad cell {stage.ToLocal(apple)} ({difficulty}, seed {300 + seed}, stage {stage.Number})");
                    }

                    if (!p.IsGhost && p.Alive)
                    {
                        Assert.Equal(p.Length, p.Body.Distinct().Count());
                        for (var k = 1; k < p.Length; k++)
                        {
                            var local = stage.ToLocal(p.Body[k]);
                            if (Stage.InPlay(local)) Assert.Equal(HazardKind.None, s.HazardAt(local));
                        }
                    }

                    if ((s.StageNumber, s.Player.ApplesEaten) != progress)
                    {
                        progress = (s.StageNumber, s.Player.ApplesEaten);
                        lastProgress = s.StepCount;
                    }

                    Assert.True(s.StepCount - lastProgress < 2500, $"Stuck: {difficulty}, seed {300 + seed}, stage {s.StageNumber}");
                }

                best = Math.Max(best, s.StageNumber);
                if (s.Player.Score > 0)
                {
                    var run = new ScoreSubmission(GameMode.Journey, difficulty, "RUN", s.Player.Score, s.Player.ApplesEaten, s.StageNumber, s.StepCount);
                    Assert.True(Leaderboard.IsPlausible(run), $"Real run rejected: {run}");
                }
            }
        }

        Assert.True(best >= 15, $"No run got past the introduction (best stage: {best}).");
        Assert.Equal(StageGenerator.Intro.Length, seenElements.Count);
    }

    [Fact]
    public void StartingAtALaterStage_PutsTheSnakeAtItsEntrance()
    {
        for (var stage = 2; stage <= 20; stage++)
        {
            var s = new JourneySession(Difficulty.Normal, 77, startStage: stage);
            Assert.Equal(stage, s.StageNumber);
            Assert.Equal(StageGenerator.EntryColumn(stage - 1, 77), s.Current.EntryX);
            Assert.Equal(Direction.Up, s.Player.Heading);
            Assert.All(s.Player.Body, c => Assert.False(s.Current.IsWall(s.Current.ToLocal(c))));
            for (var i = 0; i < 4; i++) s.Step();
            Assert.True(s.Player.Alive, $"Died right at the entrance of stage {stage}.");
        }
    }

    // ---- Ranking ----

    [Fact]
    public void Ranking_AcceptsJourneyScores_WithinTheRules()
    {
        Assert.True(Leaderboard.IsRanked(GameMode.Journey));
        ScoreSubmission Run(int score, int apples, int stage, long steps = 5000) => new(GameMode.Journey, Difficulty.Normal, "T", score, apples, stage, steps);

        Assert.True(Leaderboard.IsPlausible(Run(10, 1, 1)));
        Assert.True(Leaderboard.IsPlausible(Run(60, 1, 2)));        // one apple + the first stage's bonus
        Assert.False(Leaderboard.IsPlausible(Run(60, 1, 3)));       // two stages cleared need two apples
        Assert.False(Leaderboard.IsPlausible(Run(100, 20, 2)));     // more apples than two stages hold
        Assert.False(Leaderboard.IsPlausible(Run(1_000_000, 10, 3)));

        // Absurd numbers must not overflow the check (the answer itself does not matter here).
        _ = Leaderboard.IsPlausible(Run(int.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue));
        Assert.False(Leaderboard.IsPlausible(Run(int.MaxValue, 10, int.MaxValue, long.MaxValue)));
    }

    private static HashSet<Int2> OpenCells(Stage stage)
    {
        var posts = stage.Posts.Select(p => p.Cell).ToHashSet();
        var open = new HashSet<Int2>();
        for (var y = 1; y < H; y++)
        {
            for (var x = 0; x < W; x++)
            {
                var c = new Int2(x, y);
                if (!stage.IsStaticWall(c) && !posts.Contains(c)) open.Add(c);
            }
        }

        return open;
    }

    private static HashSet<Int2> Reachable(Stage stage, HashSet<Int2> open, Int2 start)
    {
        var seen = new HashSet<Int2> { start };
        var queue = new Queue<Int2>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var d in Directions.All)
            {
                var n = c + d.ToOffset();
                if (open.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }

            if (stage.TeleportFrom(c) is { } other && open.Contains(other) && seen.Add(other)) queue.Enqueue(other);
        }

        return seen;
    }

    private static string Fingerprint(Stage stage)
    {
        var grid = new HazardKind[Stage.CellCount];
        var sb = new System.Text.StringBuilder();
        foreach (var tick in new[] { 0L, 7L, 23L, 60L })
        {
            stage.FillHazards(grid, tick);
            for (var i = 0; i < Stage.CellCount; i++)
            {
                var c = Stage.FromIndex(i);
                sb.Append(stage.IsStaticWall(c) ? '#' : stage.IsIce(c) ? '~' : (char)('0' + (int)grid[i]));
            }
        }

        return sb.ToString();
    }
}
