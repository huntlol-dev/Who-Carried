using System.Buffers.Binary;
using WhoCarried.Core;

namespace WhoCarried.Tests;

/// <summary>The bitmap Windows gets on the clipboard: its header, its colour order and its row order.</summary>
public static class DibTests
{
    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));

    private static int Short(byte[] bytes, int at) => BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(at));

    [Test]
    public static void TheHeaderDescribesA32BitBottomUpBitmap()
    {
        byte[] dib = Dib.FromRgba(3, 2, new byte[3 * 2 * 4]);
        Check.Equal(40, Int(dib, 0), "header size");
        Check.Equal(3, Int(dib, 4), "width");
        Check.Equal(2, Int(dib, 8), "height, positive: the bottom row comes first");
        Check.Equal(1, Short(dib, 12), "planes");
        Check.Equal(32, Short(dib, 14), "bits per pixel");
        Check.Equal(0, Int(dib, 16), "BI_RGB");
        Check.Equal(24, Int(dib, 20), "image size");
        for (int at = 24; at < 40; at += 4) Check.Equal(0, Int(dib, at), $"the field at {at}");
        Check.Equal(40 + 24, dib.Length, "the header, then the pixels");
    }

    [Test]
    public static void PixelsTurnBlueGreenRedAndOpaque()
    {
        byte[] dib = Dib.FromRgba(1, 1, new byte[] { 10, 20, 30, 40 });
        Check.Equal("30,20,10,255", string.Join(",", dib.Skip(40)), "blue, green, red, alpha 255");
    }

    [Test]
    public static void RowsAreStoredBottomUp()
    {
        // 3×2: the top row red, the bottom row blue.
        byte[] rgba = new byte[3 * 2 * 4];
        for (int x = 0; x < 3; x++)
        {
            rgba[x * 4] = 255;
            rgba[12 + x * 4 + 2] = 255;
        }
        byte[] dib = Dib.FromRgba(3, 2, rgba);
        Check.Equal("255,0,0,255", string.Join(",", dib.Skip(40).Take(4)), "the first stored row is the bottom one: blue");
        Check.Equal("0,0,255,255", string.Join(",", dib.Skip(40 + 12).Take(4)), "the last stored row is the top one: red");
    }

    [Test]
    public static void AWrongSizeIsRejected()
    {
        foreach ((int w, int h, int bytes) in new[] { (3, 2, 23), (3, 2, 25), (0, 2, 0), (3, -1, 0) })
        {
            bool threw = false;
            try { Dib.FromRgba(w, h, new byte[bytes]); }
            catch (ArgumentException) { threw = true; }
            Check.True(threw, $"{w}×{h} with {bytes} bytes");
        }
    }
}
