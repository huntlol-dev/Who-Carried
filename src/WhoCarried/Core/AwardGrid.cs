namespace WhoCarried.Core;

/// <summary>How the Awards tab spreads its cards over its 1130 × 640 area.</summary>
public static class AwardGrid
{
    public const float AreaW = 1130, AreaH = 640, MaxStep = 226, GapX = 20, GapY = 22;

    /// <param name="Width">A card's width; its height is Width × <see cref="HandLayout.Aspect"/>.</param>
    /// <param name="Step">From one card's left edge to the next one's.</param>
    /// <param name="RowStep">From one row's top to the next one's.</param>
    public readonly record struct Layout(float Width, float Step, int Columns, float RowStep);

    /// <summary>One row up to five awards, two up to ten, three beyond: as big as fits across and down, 206 wide at most.</summary>
    public static Layout For(int count)
    {
        int rows = count <= 5 ? 1 : count <= 10 ? 2 : 3;
        int columns = Math.Max(1, (count + rows - 1) / rows);
        float tallest = (AreaH - GapY * (rows - 1)) / rows / HandLayout.Aspect + GapX;
        float step = Math.Min(Math.Min(MaxStep, AreaW / columns), tallest);
        float width = step - GapX;
        return new Layout(width, step, columns, width * HandLayout.Aspect + GapY);
    }
}
