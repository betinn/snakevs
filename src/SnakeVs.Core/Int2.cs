namespace SnakeVs.Core;

/// <summary>Integer grid coordinate. X grows to the right, Y grows downwards.</summary>
public readonly record struct Int2(int X, int Y)
{
    public static readonly Int2 Zero = new(0, 0);

    public static Int2 operator +(Int2 a, Int2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Int2 operator -(Int2 a, Int2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Int2 operator *(Int2 a, int k) => new(a.X * k, a.Y * k);

    public int ManhattanTo(Int2 other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public bool IsAdjacentTo(Int2 other) => ManhattanTo(other) == 1;

    public override string ToString() => $"({X},{Y})";
}
