using SnakeVs.Core;

namespace SnakeVs.Core.Tests;

public class SnakeTests
{
    [Fact]
    public void NewSnake_IsLaidOutBehindTheHead()
    {
        var snake = new Snake(0, new Int2(5, 5), Direction.Up, 4);

        Assert.Equal(4, snake.Length);
        Assert.Equal(new Int2(5, 5), snake.Head);
        Assert.Equal(new Int2(5, 8), snake.Tail);
    }

    [Fact]
    public void QueueTurn_RejectsReverseAndRepeat()
    {
        var snake = new Snake(0, new Int2(5, 5), Direction.Up, 4);

        Assert.False(snake.QueueTurn(Direction.Down));
        Assert.False(snake.QueueTurn(Direction.Up));
        Assert.True(snake.QueueTurn(Direction.Left));
        Assert.False(snake.QueueTurn(Direction.Right));
    }

    [Fact]
    public void QueueTurn_BuffersTwoQuickTurns()
    {
        var snake = new Snake(0, new Int2(5, 5), Direction.Up, 4);

        Assert.True(snake.QueueTurn(Direction.Left));
        Assert.True(snake.QueueTurn(Direction.Down));
        Assert.False(snake.QueueTurn(Direction.Right));

        Assert.Equal(Direction.Left, snake.ConsumeNextHeading());
        Assert.Equal(Direction.Down, snake.ConsumeNextHeading());
        Assert.Equal(Direction.Down, snake.ConsumeNextHeading());
    }

    [Fact]
    public void MoveTo_GrowsWhenGrowPending()
    {
        var snake = new Snake(0, new Int2(5, 5), Direction.Up, 3);
        snake.GrowPending = 1;

        snake.MoveTo(new Int2(5, 4), wrapped: false);
        Assert.Equal(4, snake.Length);

        snake.MoveTo(new Int2(5, 3), wrapped: false);
        Assert.Equal(4, snake.Length);
        Assert.Equal(new Int2(5, 6), snake.Tail);
        Assert.Equal(new Int2(5, 7), snake.PreviousTail);
    }

    [Fact]
    public void Shrink_NeverGoesBelowMinimum()
    {
        var snake = new Snake(0, new Int2(5, 5), Direction.Up, 5);

        var removed = snake.Shrink(10);

        Assert.Equal(5 - Rules.MinLength, removed);
        Assert.Equal(Rules.MinLength, snake.Length);
    }

    [Fact]
    public void Directions_OppositeAndBetween()
    {
        foreach (var d in Directions.All)
        {
            Assert.Equal(d, d.Opposite().Opposite());
            Assert.Equal(d, Directions.Between(new Int2(3, 3), new Int2(3, 3) + d.ToOffset()));
        }

        Assert.Null(Directions.Between(new Int2(0, 0), new Int2(2, 0)));
    }
}
