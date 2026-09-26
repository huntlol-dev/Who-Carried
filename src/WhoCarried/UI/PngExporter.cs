using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>Renders a control offscreen in a SubViewport and hands back the image, or saves it as a PNG. Never throws.</summary>
internal static class PngExporter
{
    /// <summary>Where images go when Steam isn't running: beside the mod's other files in the game's save folder.</summary>
    public static string FallbackFolder => Path.Combine(Tracker.DataDir, "images");

    /// <param name="onDone">Called with null on success, or an error message.</param>
    public static void Save(Control content, int width, string path, Action<string?> onDone)
    {
        Render(content, width, (image, error) =>
        {
            if (image == null)
            {
                onDone(error);
                return;
            }
            onDone(SavePng(image, path));
        });
    }

    /// <summary>Saves the image, making its folder if needed. Returns null on success, or an error message.</summary>
    public static string? SavePng(Image image, string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            Error result = image.SavePng(path);
            return result == Error.Ok ? null : result.ToString();
        }
        catch (Exception e)
        {
            Tracker.LogError("export", e);
            return e.Message;
        }
    }

    /// <param name="onDone">Called with the image, or null and an error message.</param>
    public static void Render(Control content, int width, Action<Image?, string?> onDone)
    {
        var viewport = new SubViewport
        {
            Size = new Vector2I(width, ExportLayout.TileHeight),
            TransparentBg = true,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewport.AddChild(content);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);

        // Let layout settle, then render bounded slices. The CPU image keeps the full page even when it
        // exceeds the GPU viewport size; otherwise a long creation list silently cuts off the page's end.
        Later.Run(0.15, () =>
        {
            Image? combined = null;
            ExportLayout.Slice[] slices;
            try
            {
                int height = Math.Max(1, Mathf.CeilToInt(Math.Max(content.Size.Y, content.GetCombinedMinimumSize().Y)));
                slices = ExportLayout.Slices(height).ToArray();
                if (slices.Length > 1)
                {
                    combined = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
                    if (combined.GetWidth() != width || combined.GetHeight() != height)
                        throw new InvalidOperationException("Could not allocate the complete export image");
                }
                Capture(0);
            }
            catch (Exception e) { Fail(e); }

            void Fail(Exception e)
            {
                combined?.Dispose();
                combined = null;
                Tracker.LogError("export", e);
                viewport.QueueFree();
                onDone(null, e.Message);
            }

            void Capture(int index)
            {
                ExportLayout.Slice slice = slices[index];
                viewport.Size = new Vector2I(width, slice.Height);
                content.Position = new Vector2(0, -slice.Offset);
                Later.Run(0.15, () =>
                {
                    Image? tile = null;
                    Image? result = null;
                    try
                    {
                        tile = viewport.GetTexture().GetImage();
                        if (tile == null || tile.GetWidth() != width || tile.GetHeight() != slice.Height)
                            throw new InvalidOperationException("Incomplete export slice");
                        if (combined == null)
                        {
                            result = tile;
                            tile = null;
                        }
                        else
                        {
                            if (tile.GetFormat() != Image.Format.Rgba8) tile.Convert(Image.Format.Rgba8);
                            combined.BlitRect(tile, new Rect2I(0, 0, width, slice.Height), new Vector2I(0, slice.Offset));
                            tile.Dispose();
                            tile = null;
                            if (index + 1 < slices.Length) Capture(index + 1);
                            else
                            {
                                result = combined;
                                combined = null;
                            }
                        }
                    }
                    catch (Exception e) { tile?.Dispose(); Fail(e); return; }
                    // Ownership passes to the caller. Its callback cannot turn a successful render into a second callback.
                    if (result != null)
                    {
                        viewport.QueueFree();
                        onDone(result, null);
                    }
                });
            }
        });
    }
}
