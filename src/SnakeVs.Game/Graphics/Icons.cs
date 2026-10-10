using SnakeVs.Core;

namespace SnakeVs.Game.Graphics;

/// <summary>7x7 pixel icons drawn with <see cref="Canvas.Bitmap"/>.</summary>
public static class Icons
{
    public static readonly string[] Hourglass =
    [
        "#######",
        " #   # ",
        "  # #  ",
        "   #   ",
        "  # #  ",
        " #   # ",
        "#######",
    ];

    public static readonly string[] Ghost =
    [
        "  ###  ",
        " ##### ",
        "## # ##",
        "#######",
        "#######",
        "#######",
        "# # # #",
    ];

    public static readonly string[] Double =
    [
        "       ",
        "    ###",
        "      #",
        "# # ###",
        " #  #  ",
        "# # ###",
        "       ",
    ];

    public static readonly string[] Shrink =
    [
        "#     #",
        "##   ##",
        "### ###",
        "### ###",
        "##   ##",
        "#     #",
        "       ",
    ];

    public static readonly string[] Shield =
    [
        "#######",
        "#######",
        "#######",
        "#######",
        " ##### ",
        "  ###  ",
        "   #   ",
    ];

    public static readonly string[] Pause =
    [
        "## ##",
        "## ##",
        "## ##",
        "## ##",
        "## ##",
    ];

    public static readonly string[] ArrowUp =
    [
        "   #   ",
        "  ###  ",
        " ##### ",
        "#######",
        "  ###  ",
        "  ###  ",
        "  ###  ",
    ];

    public static readonly string[] ArrowRight = Rotate(ArrowUp);
    public static readonly string[] ArrowDown = Rotate(ArrowRight);
    public static readonly string[] ArrowLeft = Rotate(ArrowDown);

    public static readonly string[] Star =
    [
        "   #   ",
        "   #   ",
        "#######",
        " ##### ",
        "  ###  ",
        " ## ## ",
        "##   ##",
    ];

    public static string[] PowerUp(PowerUpKind kind) => kind switch
    {
        PowerUpKind.Slow => Hourglass,
        PowerUpKind.Ghost => Ghost,
        PowerUpKind.Double => Double,
        PowerUpKind.Shrink => Shrink,
        PowerUpKind.Shield => Shield,
        _ => Star,
    };

    public static string[] Arrow(Direction direction) => direction switch
    {
        Direction.Up => ArrowUp,
        Direction.Right => ArrowRight,
        Direction.Down => ArrowDown,
        _ => ArrowLeft,
    };

    /// <summary>Rotates a square bitmap 90 degrees clockwise.</summary>
    public static string[] Rotate(string[] rows)
    {
        var n = rows.Length;
        var result = new string[n];
        for (var y = 0; y < n; y++)
        {
            var chars = new char[n];
            for (var x = 0; x < n; x++) chars[x] = rows[n - 1 - x][y];
            result[y] = new string(chars);
        }

        return result;
    }
}
