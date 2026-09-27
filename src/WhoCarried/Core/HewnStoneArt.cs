namespace WhoCarried.Core;

/// <summary>
/// The recap's own button stone, drawn rather than loaded from the game: a slab with its corners chamfered away, a
/// bronze rim lit along the top and shadowed along the bottom, and a bevel just inside it. Whole pixels only, so the
/// edges step like the game's hand-drawn art (the same reason <see cref="HardPixels"/> exists).
///
/// The tile is square and nine-sliceable: the <see cref="Result.Margin"/> corners hold their shape while the middle
/// stretches, so one texture dresses a button at any width. That is the defect this replaces — the old stone scaled
/// whole, so the same art came out a different shape on a wide button than on a narrow one.
/// </summary>
public static class HewnStoneArt
{
    public readonly record struct Rgba(byte R, byte G, byte B, byte A)
    {
        public static readonly Rgba Clear = new(0, 0, 0, 0);
    }

    public readonly record struct Palette(Rgba Rim, Rgba RimLit, Rgba RimShade, Rgba Face, Rgba FaceLit, Rgba FaceShade)
    {
        /// <summary>Bronze drawn from the recap's gold, over the table's own dark.</summary>
        public static readonly Palette Default = new(
            Rim: new Rgba(0xB9, 0x8F, 0x3F, 0xFF),
            RimLit: new Rgba(0xE8, 0xC2, 0x68, 0xFF),
            RimShade: new Rgba(0x6E, 0x53, 0x22, 0xFF),
            Face: new Rgba(0x16, 0x23, 0x2F, 0xFF),
            FaceLit: new Rgba(0x24, 0x36, 0x47, 0xFF),
            FaceShade: new Rgba(0x0C, 0x14, 0x1D, 0xFF));

        /// <summary>Iron: the same face with a grey rim, for the stone beside the main action (Export as image).</summary>
        public static readonly Palette Plain = new(
            Rim: new Rgba(0x6F, 0x7C, 0x8C, 0xFF),
            RimLit: new Rgba(0xA9, 0xB4, 0xC2, 0xFF),
            RimShade: new Rgba(0x3A, 0x43, 0x4F, 0xFF),
            Face: Default.Face,
            FaceLit: Default.FaceLit,
            FaceShade: Default.FaceShade);
    }

    public readonly record struct Result(byte[] Pixels, int Width, int Height, int Margin);

    /// <summary>How far the corner is cut back, in pixels, measured along each edge.</summary>
    public const int Chamfer = 6;

    /// <summary>The bronze edge's thickness.</summary>
    public const int Rim = 2;

    /// <summary>The nine-slice margin: the corner block that must keep its shape.</summary>
    public const int Margin = Chamfer + Rim + 2;

    /// <summary>The tile's side: two corners, plus two pixels in the middle for the stretch to work from.</summary>
    public const int Side = 2 * Margin + 2;

    public static Result? Draw(Palette palette) => Draw(palette, Side);

    /// <summary>The tile, or null if <paramref name="side"/> is too small to hold two corners. Never throws.</summary>
    public static Result? Draw(Palette palette, int side)
    {
        if (side < 2 * Margin + 1) return null;
        var pixels = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                Rgba colour = ColourAt(x, y, side, palette);
                int i = (y * side + x) * 4;
                pixels[i] = colour.R;
                pixels[i + 1] = colour.G;
                pixels[i + 2] = colour.B;
                pixels[i + 3] = colour.A;
            }
        return new Result(pixels, side, side, Margin);
    }

    /// <summary>
    /// How deep a pixel sits decides what it is: outside the chamfer it's nothing, the outermost ring is the lit or
    /// shadowed rim, the rest of the rim is bronze, the ring under it is the bevel, and everything deeper is the face.
    /// </summary>
    private static Rgba ColourAt(int x, int y, int side, Palette p)
    {
        int left = x, right = side - 1 - x, top = y, bottom = side - 1 - y;
        int edge = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        int corner = Math.Min(Math.Min(left + top, right + top), Math.Min(left + bottom, right + bottom)) - Chamfer;
        int depth = Math.Min(edge, corner);
        bool lit = top < bottom;

        if (depth < 0) return Rgba.Clear;
        if (depth == 0) return lit ? p.RimLit : p.RimShade;
        if (depth < Rim) return p.Rim;
        if (depth == Rim) return lit ? p.FaceLit : p.FaceShade;
        return p.Face;
    }
}
