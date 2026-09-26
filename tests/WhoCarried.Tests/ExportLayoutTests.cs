using WhoCarried.Core;

namespace WhoCarried.Tests;

public static class ExportLayoutTests
{
    [Test]
    public static void TallExportsKeepEveryRowBeyondTheOld8192Limit()
    {
        foreach (int height in new[] { 1, 4095, 4096, 4097, 8192, 8193, 20001 })
        {
            var slices = ExportLayout.Slices(height).ToArray();
            Check.Equal(height, slices.Sum(s => s.Height), "full image height " + height);
            int next = 0;
            foreach (var slice in slices)
            {
                Check.Equal(next, slice.Offset, "no missing or duplicate rows");
                Check.True(slice.Height is > 0 and <= 4096, "bounded viewport");
                next += slice.Height;
            }
            Check.Equal(height, next, "footer included");
        }
    }

    [Test]
    public static void EmptyLayoutStillHasAValidImageSize()
    {
        Check.Equal(1, ExportLayout.Slices(0).Single().Height, "minimum pixel");
        Check.Equal(1, ExportLayout.Slices(-1).Single().Height, "unlaid control");
    }
}
