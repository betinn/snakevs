using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class GameSessionTests
{
    private static GameSession NewClassic(int level = 1) => new(GameMode.Classic, Difficulty.Normal, seed: 7, startLevel: level);

    [Fact]
    public void ClassicStart_PlayerAtSpawn_ApplePlacedOnFreeCell()
    {
        var s = NewClassic();

        Assert.Equal(SessionState.Playing, s.State);
        Assert.Equal(new Int2(10, 24), s.Player.Head);
        Assert.Equal(Direction.Up, s.Player.Heading);
        Assert.False(s.IsBlocked(s.Apple));
        Assert.Null(s.Rival);
    }

    [Fact]
    public void Update_StepsAtTheConfiguredInterval()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        var interval = s.StepIntervalMs;

        s.Update(interval - 1);
        Assert.Equal(0, s.StepCount);

        s.Update(1);
        Assert.Equal(1, s.StepCount);
        Assert.Equal(new Int2(10, 23), s.Player.Head);
    }

    [Fact]
    public void EatingApple_GrowsScoresAndRespawnsApple()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(10, 23));

        s.Step();

        Assert.Equal(1, s.Player.ApplesEaten);
        Assert.Equal(Rules.AppleValue(1), s.Player.Score);
        Assert.Equal(1, s.Player.GrowPending);
        Assert.NotEqual(new Int2(10, 23), s.Apple);
        Assert.Contains(s.Events, e => e.Type == GameEventType.AteApple);

        s.Step();
        Assert.Equal(Rules.StartLength + 1, s.Player.Length);
    }

    [Fact]
    public void HittingTheBorder_EndsClassicGame()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));

        for (var i = 0; i < 40 && s.State == SessionState.Playing; i++) s.Step();

        Assert.Equal(SessionState.GameOver, s.State);
        Assert.False(s.Player.Alive);
        Assert.Contains(s.Events, e => e.Type == GameEventType.GameOver);
    }

    [Fact]
    public void HittingAnObstacle_Kills()
    {
        var s = NewClassic();
        s.ReplaceBoardForTest(new Board(Rules.BoardWidth, Rules.BoardHeight, [new Int2(10, 22)]));
        s.PlaceAppleForTest(new Int2(0, 0));

        s.Step();
        Assert.True(s.Player.Alive);
        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void BitingOwnBody_Kills()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.Player.GrowPending = 4;
        for (var i = 0; i < 4; i++) s.Step();

        s.Player.QueueTurn(Direction.Left);
        s.Step();
        s.Player.QueueTurn(Direction.Down);
        s.Step();
        s.Player.QueueTurn(Direction.Right);
        s.Step();

        Assert.False(s.Player.Alive);
    }

    [Fact]
    public void FollowingOwnTail_IsAllowed()
    {
        // A 4 long snake turning in a 2x2 square always moves into the cell its tail just left.
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        var turns = new[] { Direction.Left, Direction.Down, Direction.Right, Direction.Up };
        for (var i = 0; i < 12; i++)
        {
            s.Player.QueueTurn(turns[i % 4]);
            s.Step();
            Assert.True(s.Player.Alive, $"Died at step {i}");
        }
    }

    [Fact]
    public void Shield_SavesFromWallAndWraps()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.PlacePowerUpForTest(new Int2(10, 23), PowerUpKind.Shield);

        s.Step();
        Assert.Equal(1, s.Player.Shields);

        for (var i = 0; i < 24; i++) s.Step();

        Assert.True(s.Player.Alive);
        Assert.Equal(0, s.Player.Shields);
        Assert.Equal(new Int2(10, Rules.BoardHeight - 1), s.Player.Head);
        Assert.Contains(s.Events, e => e.Type == GameEventType.ShieldUsed);
    }

    [Fact]
    public void Ghost_PassesThroughObstacles()
    {
        var s = NewClassic();
        s.ReplaceBoardForTest(new Board(Rules.BoardWidth, Rules.BoardHeight, [new Int2(10, 21)]));
        s.PlaceAppleForTest(new Int2(0, 0));
        s.PlacePowerUpForTest(new Int2(10, 23), PowerUpKind.Ghost);

        s.Step();
        s.Step();
        s.Step();

        Assert.True(s.Player.Alive);
        Assert.Equal(new Int2(10, 21), s.Player.Head);
    }

    [Fact]
    public void Ghost_ExpiresWithTime()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.PlacePowerUpForTest(new Int2(10, 23), PowerUpKind.Ghost);
        s.Step();
        Assert.True(s.Player.IsGhost);

        RunInCircles(s, Rules.GhostDurationMs + 100);

        Assert.True(s.Player.Alive);
        Assert.False(s.Player.IsGhost);
    }
    [Fact]
    public void Shrink_CutsTail()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.Player.GrowPending = 6;
        for (var i = 0; i < 6; i++) s.Step();
        var before = s.Player.Length;
        s.PlacePowerUpForTest(s.Player.Head + Direction.Up.ToOffset(), PowerUpKind.Shrink);

        s.Step();

        Assert.Equal(before - Rules.ShrinkAmount, s.Player.Length);
    }

    [Fact]
    public void Slow_IncreasesStepInterval()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        var normal = s.StepIntervalMs;
        s.PlacePowerUpForTest(new Int2(10, 23), PowerUpKind.Slow);

        s.Step();

        Assert.True(s.StepIntervalMs > normal);
        s.Update(Rules.MaxFrameMs);
        Assert.True(s.SlowMs < Rules.SlowDurationMs);
    }

    [Fact]
    public void Double_DoublesApplePoints()
    {
        var s = NewClassic();
        s.PlacePowerUpForTest(new Int2(10, 23), PowerUpKind.Double);
        s.PlaceAppleForTest(new Int2(10, 22));

        s.Step();
        var afterPower = s.Player.Score;
        s.Step();

        Assert.Equal(Rules.AppleValue(1) * 2, s.Player.Score - afterPower);
    }

    [Fact]
    public void GoldenApple_GivesBonus()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.PlaceGoldenForTest(new Int2(10, 23));

        s.Step();

        Assert.Equal(Rules.GoldenScore, s.Player.Score);
        Assert.Null(s.Golden);
    }

    [Fact]
    public void GoldenApple_ExpiresAfterLifetime()
    {
        var s = NewClassic();
        s.PlaceAppleForTest(new Int2(0, 0));
        s.PlaceGoldenForTest(new Int2(0, 5));

        RunInCircles(s, Rules.GoldenLifetimeMs + 100);

        Assert.Equal(SessionState.Playing, s.State);
        Assert.Null(s.Golden);
        Assert.Contains(s.Events, e => e.Type == GameEventType.GoldenExpired);
    }
    [Fact]
    public void EatingEnoughApples_ClearsLevel_AndAdvanceLoadsNextLayout()
    {
        var s = NewClassic();
        for (var i = 0; i < Rules.ApplesPerLevel; i++)
        {
            Assert.Equal(SessionState.Playing, s.State);
            s.PlaceAppleForTest(s.Player.Head + s.Player.Heading.ToOffset());
            s.Step();
        }

        Assert.Equal(SessionState.LevelCleared, s.State);
        Assert.Contains(s.Events, e => e.Type == GameEventType.LevelCleared);
        var scoreBefore = s.Player.Score;

        s.AdvanceLevel();

        Assert.Equal(2, s.Level);
        Assert.Equal(SessionState.Playing, s.State);
        Assert.Equal(Rules.StartLength, s.Player.Length);
        Assert.Equal(scoreBefore, s.Player.Score);
        Assert.True(s.Board.ObstacleCount > 0);
        Assert.False(s.IsBlocked(s.Apple));
    }

    [Fact]
    public void HigherLevels_AreFaster()
    {
        Assert.True(Rules.StepMs(GameMode.Classic, Difficulty.Normal, 5) < Rules.StepMs(GameMode.Classic, Difficulty.Normal, 1));
        Assert.Equal(Rules.MinStepMs, Rules.StepMs(GameMode.Classic, Difficulty.Hard, 100));
    }

    [Fact]
    public void Versus_HeadOnCollision_IsADraw()
    {
        var s = new GameSession(GameMode.Versus, Difficulty.Hard, seed: 3, versusLayout: 1);
        var rival = s.Rival!;
        rival.IsAiControlled = false;
        s.PlaceAppleForTest(new Int2(0, 0));

        // Put both heads on the same column, facing each other with one empty cell between them.
        rival.Reset(new Int2(5, 20), Direction.Down, 3);
        s.Player.Reset(new Int2(5, 22), Direction.Up, 3);

        s.Step();

        Assert.Equal(SessionState.MatchOver, s.State);
        Assert.Equal(MatchOutcome.Draw, s.Outcome);
    }

    [Fact]
    public void Versus_PlayerCrashing_GivesRivalTheWin()
    {
        var s = new GameSession(GameMode.Versus, Difficulty.Normal, seed: 3, versusLayout: 1);
        s.PlaceAppleForTest(new Int2(19, 29));

        s.KillPlayer();

        Assert.Equal(SessionState.MatchOver, s.State);
        Assert.Equal(MatchOutcome.RivalWon, s.Outcome);
    }

    [Fact]
    public void Versus_ReachingTargetApples_Wins()
    {
        var s = new GameSession(GameMode.Versus, Difficulty.Easy, seed: 5, versusLayout: 1);
        s.Rival!.IsAiControlled = false;
        s.Rival.Reset(new Int2(15, 5), Direction.Down, 3);
        s.Player.ApplesEaten = Rules.VersusTargetApples - 1;
        s.PlaceAppleForTest(s.Player.Head + Direction.Up.ToOffset());

        s.Step();

        Assert.Equal(MatchOutcome.PlayerWon, s.Outcome);
    }

    /// <summary>Drives the player around a 2x2 loop (always into its own moving tail) for the given time.</summary>
    private static void RunInCircles(GameSession s, double ms)
    {
        for (var t = 0.0; t < ms && s.State == SessionState.Playing; t += 10)
        {
            var next = s.Player.Heading switch
            {
                Direction.Up => Direction.Left,
                Direction.Left => Direction.Down,
                Direction.Down => Direction.Right,
                _ => Direction.Up,
            };
            s.Player.QueueTurn(next);
            s.Update(10);
        }
    }
}
