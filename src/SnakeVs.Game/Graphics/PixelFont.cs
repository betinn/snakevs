using Microsoft.Xna.Framework;

namespace SnakeVs.Game.Graphics;

/// <summary>
/// Built-in 5x7 pixel font (upper case, digits, punctuation, Portuguese accents).
/// Text is drawn as merged pixel runs, so it stays crisp at any scale.
/// </summary>
public sealed class PixelFont
{
    public const int GlyphHeight = 7;

    /// <summary>Line advance in font pixels (cap height + room for accents and a gap).</summary>
    public const int LineAdvance = 11;

    private static readonly Dictionary<char, string[]> Glyphs = BuildGlyphs();

    private static readonly Dictionary<char, (char Base, string[]? Above, string[]? Below)> Accented = new()
    {
        ['Á'] = ('A', Acute, null), ['À'] = ('A', Grave, null), ['Â'] = ('A', Circumflex, null), ['Ã'] = ('A', Tilde, null),
        ['É'] = ('E', Acute, null), ['Ê'] = ('E', Circumflex, null),
        ['Í'] = ('I', Acute, null),
        ['Ó'] = ('O', Acute, null), ['Ô'] = ('O', Circumflex, null), ['Õ'] = ('O', Tilde, null),
        ['Ú'] = ('U', Acute, null), ['Ü'] = ('U', Umlaut, null),
        ['Ç'] = ('C', null, Cedilla),
        ['Ñ'] = ('N', Tilde, null),
    };

    private static string[] Acute => ["   # ", "  #  "];
    private static string[] Grave => [" #   ", "  #  "];
    private static string[] Circumflex => ["  #  ", " # # "];
    private static string[] Tilde => [" ## #", "#  # "];
    private static string[] Umlaut => [" # # ", "     "];
    private static string[] Cedilla => ["  #  ", " ##  "];

    public static float LineHeight(float scale) => LineAdvance * scale;

    public Vector2 Measure(string text, float scale)
    {
        var maxWidth = 0;
        var width = 0;
        var lines = 1;
        foreach (var raw in text)
        {
            if (raw == '\n')
            {
                maxWidth = Math.Max(maxWidth, width);
                width = 0;
                lines++;
                continue;
            }

            if (width > 0) width += 1;
            width += Resolve(raw).Rows[0].Length;
        }

        maxWidth = Math.Max(maxWidth, width);
        return new Vector2(maxWidth * scale, ((lines - 1) * LineAdvance + GlyphHeight) * scale);
    }

    /// <summary>Draws text with its top-left cap corner at <paramref name="position"/>.</summary>
    public void Draw(Canvas canvas, string text, Vector2 position, float scale, Color color)
    {
        var x = position.X;
        var y = position.Y;
        foreach (var raw in text)
        {
            if (raw == '\n')
            {
                x = position.X;
                y += LineAdvance * scale;
                continue;
            }

            var glyph = Resolve(raw);
            var width = glyph.Rows[0].Length;
            canvas.Bitmap(glyph.Rows, new Vector2(x, y), scale, color);
            if (glyph.Above is not null)
            {
                var offset = (width - 5) / 2f * scale;
                canvas.Bitmap(glyph.Above, new Vector2(x + offset, y - 2.5f * scale), scale, color);
            }

            if (glyph.Below is not null)
            {
                var offset = (width - 5) / 2f * scale;
                canvas.Bitmap(glyph.Below, new Vector2(x + offset, y + GlyphHeight * scale), scale, color);
            }

            x += (width + 1) * scale;
        }
    }

    public void DrawShadowed(Canvas canvas, string text, Vector2 position, float scale, Color color, float alpha = 1f)
    {
        var offset = MathF.Max(1f, scale * 0.6f);
        Draw(canvas, text, position + new Vector2(offset, offset), scale, Palette.TextShadow * (0.8f * alpha));
        Draw(canvas, text, position, scale, color * alpha);
    }

    /// <summary>Draws each line centered horizontally on <paramref name="center"/>.X; the block is centered vertically.</summary>
    public void DrawCentered(Canvas canvas, string text, Vector2 center, float scale, Color color, bool shadow = true, float alpha = 1f)
    {
        var lines = text.Split('\n');
        var total = Measure(text, scale);
        var y = center.Y - total.Y / 2f;
        foreach (var line in lines)
        {
            var size = Measure(line, scale);
            var pos = new Vector2(MathF.Round(center.X - size.X / 2f), MathF.Round(y));
            if (shadow) DrawShadowed(canvas, line, pos, scale, color, alpha);
            else Draw(canvas, line, pos, scale, color * alpha);
            y += LineAdvance * scale;
        }
    }

    /// <summary>Largest scale (in 0.5 steps) at which the text fits in the given width.</summary>
    public float FitScale(string text, float maxWidth, float preferred)
    {
        var scale = preferred;
        while (scale > 1f && Measure(text, scale).X > maxWidth) scale -= 0.5f;
        return scale;
    }

    private static (string[] Rows, string[]? Above, string[]? Below) Resolve(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c == '×') c = 'X';
        if (Glyphs.TryGetValue(c, out var rows)) return (rows, null, null);
        if (Accented.TryGetValue(c, out var acc)) return (Glyphs[acc.Base], acc.Above, acc.Below);
        return (Glyphs['?'], null, null);
    }

    private static Dictionary<char, string[]> BuildGlyphs() => new()
    {
        [' '] = ["   ", "   ", "   ", "   ", "   ", "   ", "   "],
        ['A'] = [" ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['B'] = ["#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### "],
        ['C'] = [" ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### "],
        ['D'] = ["#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### "],
        ['E'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####"],
        ['F'] = ["#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    "],
        ['G'] = [" ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####"],
        ['H'] = ["#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #"],
        ['I'] = ["###", " # ", " # ", " # ", " # ", " # ", "###"],
        ['J'] = ["  ###", "   # ", "   # ", "   # ", "#  # ", "#  # ", " ##  "],
        ['K'] = ["#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #"],
        ['L'] = ["#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####"],
        ['M'] = ["#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #"],
        ['N'] = ["#   #", "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #"],
        ['O'] = [" ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['P'] = ["#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    "],
        ['Q'] = [" ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #"],
        ['R'] = ["#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #"],
        ['S'] = [" ####", "#    ", "#    ", " ### ", "    #", "    #", "#### "],
        ['T'] = ["#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['U'] = ["#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### "],
        ['V'] = ["#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  "],
        ['W'] = ["#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # "],
        ['X'] = ["#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #"],
        ['Y'] = ["#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  "],
        ['Z'] = ["#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####"],
        ['0'] = [" ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### "],
        ['1'] = [" # ", "## ", " # ", " # ", " # ", " # ", "###"],
        ['2'] = [" ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####"],
        ['3'] = ["#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### "],
        ['4'] = ["   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # "],
        ['5'] = ["#####", "#    ", "#### ", "    #", "    #", "#   #", " ### "],
        ['6'] = ["  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### "],
        ['7'] = ["#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   "],
        ['8'] = [" ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### "],
        ['9'] = [" ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  "],
        ['.'] = [" ", " ", " ", " ", " ", " ", "#"],
        [','] = ["  ", "  ", "  ", "  ", "  ", " #", "# "],
        [':'] = [" ", "#", " ", " ", " ", "#", " "],
        [';'] = ["  ", " #", "  ", "  ", "  ", " #", "# "],
        ['!'] = ["#", "#", "#", "#", "#", " ", "#"],
        ['?'] = [" ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  "],
        ['-'] = ["    ", "    ", "    ", "####", "    ", "    ", "    "],
        ['+'] = ["     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     "],
        ['='] = ["    ", "    ", "####", "    ", "####", "    ", "    "],
        ['/'] = ["    #", "   # ", "   # ", "  #  ", " #   ", " #   ", "#    "],
        ['\''] = ["#", "#", " ", " ", " ", " ", " "],
        ['"'] = ["# #", "# #", "   ", "   ", "   ", "   ", "   "],
        ['('] = [" #", "# ", "# ", "# ", "# ", "# ", " #"],
        [')'] = ["# ", " #", " #", " #", " #", " #", "# "],
        ['%'] = ["##  #", "## # ", "   # ", "  #  ", " #   ", " # ##", "#  ##"],
        ['<'] = ["   #", "  # ", " #  ", "#   ", " #  ", "  # ", "   #"],
        ['>'] = ["#   ", " #  ", "  # ", "   #", "  # ", " #  ", "#   "],
        ['*'] = ["     ", "# # #", " ### ", "#####", " ### ", "# # #", "     "],
        ['#'] = [" # # ", " # # ", "#####", " # # ", "#####", " # # ", " # # "],
        ['_'] = ["    ", "    ", "    ", "    ", "    ", "    ", "####"],
    };
}
