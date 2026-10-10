namespace SnakeVs.Core;

public enum Direction
{
    Up,
    Right,
    Down,
    Left,
}

public static class Directions
{
    public static readonly Direction[] All = [Direction.Up, Direction.Right, Direction.Down, Direction.Left];

    public static Int2 ToOffset(this Direction direction) => direction switch
    {
        Direction.Up => new Int2(0, -1),
        Direction.Right => new Int2(1, 0),
        Direction.Down => new Int2(0, 1),
        Direction.Left => new Int2(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    public static Direction Opposite(this Direction direction) => (Direction)(((int)direction + 2) % 4);

    public static bool IsOpposite(this Direction a, Direction b) => a.Opposite() == b;

    /// <summary>Direction that leads from <paramref name="from"/> to an adjacent cell <paramref name="to"/>.</summary>
    public static Direction? Between(Int2 from, Int2 to)
    {
        var d = to - from;
        if (d == new Int2(0, -1)) return Direction.Up;
        if (d == new Int2(1, 0)) return Direction.Right;
        if (d == new Int2(0, 1)) return Direction.Down;
        if (d == new Int2(-1, 0)) return Direction.Left;
        return null;
    }
}
