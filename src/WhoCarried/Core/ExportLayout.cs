namespace WhoCarried.Core;

/// <summary>Viewport-sized slices covering the entire exported page, without a height cap or overlap.</summary>
public static class ExportLayout
{
    public const int TileHeight = 4096;
    public readonly record struct Slice(int Offset, int Height);

    public static IEnumerable<Slice> Slices(int height)
    {
        height = Math.Max(1, height);
        for (int offset = 0; offset < height;)
        {
            int take = Math.Min(TileHeight, height - offset);
            yield return new Slice(offset, take);
            offset += take;
        }
    }
}
