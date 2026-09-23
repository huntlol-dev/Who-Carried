namespace WhoCarried.Core;

/// <summary>
/// Fitting a picture into a box, from CSS's object-fit: cover (zoom 1: fills the box, cropping) to contain (zoom 0:
/// the whole picture, leaving strips). What's left of the box around <see cref="Placement.Dest"/> is the strips.
/// </summary>
public static class PictureFit
{
    public readonly record struct Rect(float X, float Y, float W, float H);

    /// <param name="Dest">Where the picture is drawn, in the box's own coordinates.</param>
    /// <param name="Source">The part of the picture drawn there, in the picture's pixels.</param>
    public readonly record struct Placement(Rect Dest, Rect Source);

    /// <param name="zoom">1 = cover, 0 = contain, in between scales between the two. Clamped to 0–1.</param>
    /// <param name="focusY">Which band is kept when the picture is cropped top and bottom: 0 the top, 1 the bottom.</param>
    /// <returns>Null when the box or the picture has no size.</returns>
    public static Placement? Place(float boxW, float boxH, float picW, float picH, float zoom = 1, float focusY = 0.5f)
    {
        if (boxW <= 0 || boxH <= 0 || picW <= 0 || picH <= 0) return null;
        zoom = Math.Clamp(zoom, 0, 1);
        float cover = Math.Max(boxW / picW, boxH / picH), contain = Math.Min(boxW / picW, boxH / picH);
        float scale = contain + (cover - contain) * zoom;
        float w = Math.Min(boxW, picW * scale), h = Math.Min(boxH, picH * scale);
        var dest = new Rect((boxW - w) / 2, (boxH - h) / 2, w, h);
        float srcW = w / scale, srcH = h / scale;
        var source = new Rect((picW - srcW) / 2, (picH - srcH) * focusY, srcW, srcH);
        return new Placement(dest, source);
    }
}
