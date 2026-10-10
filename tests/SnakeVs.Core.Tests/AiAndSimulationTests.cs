using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class AiAndSimulationTests
{
    [Fact]
    public void Ai_NeverTurnsBackwards()
    {
        var s = new GameSession(GameMode.Levels, Difficulty.Normal, seed: 11);
        var rng = new Random(1);
        foreach (var heading in Directions.All)
        {
            s.Player.Reset(new Int2(10, 15), heading, 4);
            var dir = SnakeAi.ChooseDirection(s, s.Player, rng);
            Assert.NotEqual(heading.Opposite(), dir);
        }
    }

    [Fact]
    public void Ai_AvoidsWallWhenFacingIt()
    {
        var s = new GameSession(GameMode.Levels, Difficulty.Hard, seed: 11);
        s.PlaceAppleForTest(new Int2(19, 29));
        s.Player.Reset(new Int2(10, 0), Direction.Up, 4);

        var dir = SnakeAi.ChooseDirection(s, s.Player, new Random(1));

        Assert.NotEqual(Direction.Up, dir);
    }

    [Fact]
    public void Ai_HeadsTowardsTheApple()
    {
        var s = new GameSession(GameMode.Levels, Difficulty.Hard, seed: 11);
        s.Player.Reset(new Int2(10, 15), Direction.Up, 4);
        s.PlaceAppleForTest(new Int2(2, 15));

        var dir = SnakeAi.ChooseDirection(s, s.Player, new Random(1));

        Assert.Equal(Direction.Left, dir);
    }

    [Fact]
    public void FloodArea_CountsEnclosedRegion()
    {
        var walls = new List<Int2>();
        for (var x = 0; x < 5; x++) walls.Add(new Int2(x, 3));
        for (var y = 0; y < 3; y++) walls.Add(new Int2(4, y));
        var board = new Board(10, 10, walls);
        var blocked = new bool[board.CellCount];
        foreach (var w in walls) blocked[board.Index(w)] = true;

        Assert.Equal(12, SnakeAi.FloodArea(board, blocked, new Int2(0, 0), 1000));
    }

    /// <summary>
    /// Plays many full games with the CPU driving the player too. Catches crashes, invalid states and
    /// broken invariants across levels, modes, power-ups and difficulties.
    /// </summary>
    [Theory]
    [InlineData(GameMode.Levels, Difficulty.Easy)]
    [InlineData(GameMode.Levels, Difficulty.Normal)]
    [InlineData(GameMode.Levels, Difficulty.Hard)]
    [InlineData(GameMode.Endless, Difficulty.Easy)]
    [InlineData(GameMode.Endless, Difficulty.Normal)]
    [InlineData(GameMode.Endless, Difficulty.Hard)]
    [InlineData(GameMode.Versus, Difficulty.Easy)]
    [InlineData(GameMode.Versus, Difficulty.Normal)]
    [InlineData(GameMode.Versus, Difficulty.Hard)]
    public void Soak_AutoplayedGames_KeepInvariants(GameMode mode, Difficulty difficulty)
    {
        var maxLevel = 0;
        var maxApples = 0;
        var outcomes = new Dictionary<MatchOutcome, int>();
        for (var game = 0; game < 25; game++)
        {
            var s = new GameSession(mode, difficulty, seed: 1000 + game);
            s.Player.IsAiControlled = true;
            s.Player.AiMistakeChance = 0.01;

            for (var frame = 0; frame < 15000; frame++)
            {
                s.ClearEvents();
                s.Update(16.7);
                CheckInvariants(s);

                if (s.State == SessionState.LevelCleared) s.AdvanceLevel();
                if (s.State is SessionState.GameOver or SessionState.MatchOver) break;
            }

            maxLevel = Math.Max(maxLevel, s.Level);
            maxApples = Math.Max(maxApples, s.Player.ApplesEaten);
            outcomes[s.Outcome] = outcomes.GetValueOrDefault(s.Outcome) + 1;
        }

        if (mode == GameMode.Levels)
        {
            Assert.True(maxLevel >= 3, $"Autoplay never got past level {maxLevel}; AI or level flow is broken.");
        }
        else if (mode == GameMode.Endless)
        {
            Assert.Equal(1, maxLevel);
            Assert.True(maxApples >= 25, $"Autoplay only ate {maxApples} apples in endless mode.");
        }
        else
        {
            Assert.True(outcomes.Count > 0);
        }
    }

    private static void CheckInvariants(GameSession s)
    {
        foreach (var snake in s.Snakes)
        {
            Assert.True(snake.Length >= Rules.MinLength);
            if (!snake.Alive) continue;

            for (var i = 0; i < snake.Length; i++)
            {
                Assert.True(s.Board.InBounds(snake.Body[i]), $"Segment out of bounds: {snake.Body[i]}");
            }

            for (var i = 1; i < snake.Length; i++)
            {
                var a = snake.Body[i - 1];
                var b = snake.Body[i];
                var adjacent = a.IsAdjacentTo(b) || s.Board.Wrap(a + new Int2(1, 0)) == b || s.Board.Wrap(a - new Int2(1, 0)) == b
                               || s.Board.Wrap(a + new Int2(0, 1)) == b || s.Board.Wrap(a - new Int2(0, 1)) == b;
                Assert.True(adjacent, $"Body not continuous: {a} -> {b}");
            }
        }

        if (s.State == SessionState.Playing)
        {
            Assert.False(s.Board.IsObstacle(s.Apple), "Apple inside obstacle.");
            Assert.True(s.Board.InBounds(s.Apple));
        }
    }
}

