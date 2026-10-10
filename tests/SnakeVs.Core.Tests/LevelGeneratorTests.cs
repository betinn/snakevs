using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class LevelGeneratorTests
{
    public static TheoryData<GameMode, int> AllLayouts()
    {
        var data = new TheoryData<GameMode, int>();
        foreach (var mode in Enum.GetValues<GameMode>())
        {
            for (var layout = 1; layout <= 25; layout++) data.Add(mode, layout);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllLayouts))]
    public void EveryLayout_IsConnected_AndKeepsSpawnLanesClear(GameMode mode, int layout)
    {
        var board = LevelGenerator.Create(mode, layout, Rules.BoardWidth, Rules.BoardHeight, seed: 1234);
        var spawns = LevelGenerator.Spawns(mode, board.Width, board.Height);

        Assert.True(LevelGenerator.IsFullyConnected(board, spawns[0].Head));
        foreach (var cell in LevelGenerator.ReservedCells(spawns, board.Width, board.Height))
        {
            Assert.False(board.IsObstacle(cell), $"Spawn lane blocked at {cell} on layout {layout} ({mode}).");
        }
    }

    [Fact]
    public void FirstLevel_HasNoObstacles()
    {
        var board = LevelGenerator.Create(GameMode.Levels, 1, Rules.BoardWidth, Rules.BoardHeight, seed: 1);
        Assert.Equal(0, board.ObstacleCount);
    }

    [Fact]
    public void LaterLevels_HaveObstacles()
    {
        for (var layout = 2; layout <= 12; layout++)
        {
            var board = LevelGenerator.Create(GameMode.Levels, layout, Rules.BoardWidth, Rules.BoardHeight, seed: 99);
            Assert.True(board.ObstacleCount > 0, $"Layout {layout} has no obstacles.");
        }
    }

    [Fact]
    public void GeneratedLevels_AreDeterministicForASeed()
    {
        var a = LevelGenerator.Create(GameMode.Levels, 9, Rules.BoardWidth, Rules.BoardHeight, seed: 42);
        var b = LevelGenerator.Create(GameMode.Levels, 9, Rules.BoardWidth, Rules.BoardHeight, seed: 42);
        Assert.Equal(a.Obstacles, b.Obstacles);
    }

    [Fact]
    public void VersusLayouts_KeepTheirDesignedShape()
    {
        foreach (var layout in LevelGenerator.VersusLayouts)
        {
            var versus = LevelGenerator.Create(GameMode.Versus, layout, Rules.BoardWidth, Rules.BoardHeight, seed: 1);
            var classic = LevelGenerator.Create(GameMode.Levels, layout, Rules.BoardWidth, Rules.BoardHeight, seed: 1);
            Assert.Equal(classic.ObstacleCount, versus.ObstacleCount);
        }
    }
}