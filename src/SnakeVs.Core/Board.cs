namespace SnakeVs.Core;

/// <summary>The playing field: a fixed size grid with static obstacles. Borders are walls.</summary>
public sealed class Board
{
    private readonly bool[] _obstacles;

    public Board(int width, int height, IEnumerable<Int2>? obstacles = null)
    {
        if (width < 4 || height < 4) throw new ArgumentOutOfRangeException(nameof(width), "Board too small.");
        Width = width;
        Height = height;
        _obstacles = new bool[width * height];
        if (obstacles is null) return;
        foreach (var p in obstacles)
        {
            if (InBounds(p)) _obstacles[Index(p)] = true;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int CellCount => Width * Height;

    public bool InBounds(Int2 p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

    public bool IsObstacle(Int2 p) => InBounds(p) && _obstacles[Index(p)];

    public int Index(Int2 p) => p.Y * Width + p.X;

    public Int2 FromIndex(int index) => new(index % Width, index / Width);

    /// <summary>Maps an out of bounds cell to the opposite edge.</summary>
    public Int2 Wrap(Int2 p) => new(((p.X % Width) + Width) % Width, ((p.Y % Height) + Height) % Height);

    public IEnumerable<Int2> Obstacles
    {
        get
        {
            for (var i = 0; i < _obstacles.Length; i++)
            {
                if (_obstacles[i]) yield return FromIndex(i);
            }
        }
    }

    public int ObstacleCount => _obstacles.Count(o => o);
}
