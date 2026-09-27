using System.Buffers.Binary;

namespace WhoCarried.Core;

/// <summary>
/// A device-independent bitmap, Windows' <c>CF_DIB</c> clipboard format, from RGBA pixels: a 40-byte
/// BITMAPINFOHEADER, then 32-bit pixels in blue, green, red, alpha order, bottom row first. Alpha is always 255: the
/// picture is opaque, and apps disagree about what a DIB's fourth byte means.
/// </summary>
public static class Dib
{
    /// <summary>The BITMAPINFOHEADER's size, which is also where the first pixel starts.</summary>
    public const int HeaderSize = 40;

    /// <param name="rgba">Four bytes a pixel, top row first, as Godot's <c>Image.GetData()</c> gives an RGBA8 image.</param>
    /// <exception cref="ArgumentException">A side isn't positive, or the buffer isn't width × height × 4 bytes.</exception>
    public static byte[] FromRgba(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException($"no bitmap is {width}×{height}");
        long size = (long)width * height * 4;
        if (rgba.Length != size) throw new ArgumentException($"{width}×{height} needs {size} bytes, not {rgba.Length}");

        var dib = new byte[HeaderSize + size];
        Span<byte> header = dib.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header, HeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], height); // positive: stored bottom-up
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1); // planes
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32); // bits per pixel
        // 16: compression 0, BI_RGB. 24 to 39: resolution and palette, all 0.
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], (int)size);

        int stride = width * 4;
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<byte> from = rgba.Slice(y * stride, stride);
            Span<byte> to = dib.AsSpan(HeaderSize + (height - 1 - y) * stride, stride);
            for (int x = 0; x < stride; x += 4)
            {
                to[x] = from[x + 2];
                to[x + 1] = from[x + 1];
                to[x + 2] = from[x];
                to[x + 3] = 255;
            }
        }
        return dib;
    }
}
