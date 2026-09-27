using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using WhoCarried.Core;
using WhoCarried.Game;

namespace WhoCarried.UI;

/// <summary>
/// Puts a picture on the system clipboard, for pasting into Discord and the like. Godot can read an image from the
/// clipboard but can't write one, so each system gets its own path: the Windows clipboard, which Proton passes on to
/// the Linux desktop, or osascript on a Mac. Reports back exactly once; never throws.
/// </summary>
internal static class ImageClipboard
{
    /// <param name="onDone">
    /// Called on the main thread with null once the picture is on the clipboard, or the reason it isn't; and the PNG's
    /// size in bytes (0 if it was never made).
    /// </param>
    public static void Copy(Image image, Action<string?, int> onDone)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            onDone("copying images isn't supported on this system", 0);
            return;
        }
        byte[] png;
        try
        {
            // No alpha channel: the picture is opaque, and the file comes out smaller.
            using var rgb = (Image)image.Duplicate();
            if (rgb.GetFormat() != Image.Format.Rgb8) rgb.Convert(Image.Format.Rgb8);
            png = rgb.SavePngToBuffer();
        }
        catch (Exception e)
        {
            Tracker.LogError("copy: encoding the picture", e);
            onDone(e.Message, 0);
            return;
        }
        if (OperatingSystem.IsWindows()) onDone(Win32.Copy(image, png), png.Length);
        else Mac.Copy(png, onDone);
    }

    /// <summary>The clipboard's formats right now ("PNG, DIB, 17, 2"), for the dev preview's check.</summary>
    public static string Describe() => OperatingSystem.IsWindows() ? Win32.Describe() : "(only listed on Windows)";

    /// <summary>
    /// The Windows clipboard: the PNG, which Chromium apps (Discord, Slack, Chrome) read first, and a DIB for everything
    /// else. Under Proton, Wine hands both to the Linux desktop (image/png, image/bmp).
    /// </summary>
    private static class Win32
    {
        private const uint CfDib = 8, GmemMoveable = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr owner);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
        private static extern uint RegisterClipboardFormat(string name);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint EnumClipboardFormats(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClipboardFormatNameW")]
        private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr memory);

        public static string? Copy(Image image, byte[] png)
        {
            byte[] dib;
            try
            {
                using var rgba = (Image)image.Duplicate();
                if (rgba.GetFormat() != Image.Format.Rgba8) rgba.Convert(Image.Format.Rgba8);
                dib = Dib.FromRgba(rgba.GetWidth(), rgba.GetHeight(), rgba.GetData());
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: building the bitmap", e);
                return e.Message;
            }
            try
            {
                // The clipboard belongs to a window: SetClipboardData can fail without one.
                var owner = new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
                if (owner == IntPtr.Zero) return "no game window to own the clipboard";
                if (!Open(owner)) return "clipboard busy";
                try
                {
                    if (!EmptyClipboard()) return $"EmptyClipboard failed ({Marshal.GetLastWin32Error()})";
                    uint pngFormat = RegisterClipboardFormat("PNG");
                    if (pngFormat == 0) return $"RegisterClipboardFormat failed ({Marshal.GetLastWin32Error()})";
                    return Put(pngFormat, png) ?? Put(CfDib, dib);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: the Windows clipboard", e);
                return e.Message;
            }
        }

        public static string Describe()
        {
            try
            {
                if (!Open(IntPtr.Zero)) return "clipboard busy";
                try
                {
                    var formats = new List<string>();
                    for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
                    {
                        var name = new StringBuilder(128);
                        formats.Add(GetClipboardFormatName(format, name, name.Capacity) > 0 ? name.ToString()
                            : format == CfDib ? "DIB" : format.ToString(CultureInfo.InvariantCulture));
                    }
                    return formats.Count == 0 ? "empty" : string.Join(", ", formats);
                }
                finally
                {
                    CloseClipboard();
                }
            }
            catch (Exception e)
            {
                return e.Message;
            }
        }

        /// <summary>Another program may hold the clipboard for a moment: try ten times, 10 ms apart.</summary>
        private static bool Open(IntPtr owner)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (OpenClipboard(owner)) return true;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        /// <summary>One format's bytes onto the clipboard. Windows owns the memory once it takes it; until then it's ours to free.</summary>
        private static string? Put(uint format, byte[] bytes)
        {
            IntPtr memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (memory == IntPtr.Zero) return $"GlobalAlloc failed ({Marshal.GetLastWin32Error()})";
            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero)
            {
                int lockError = Marshal.GetLastWin32Error();
                GlobalFree(memory);
                return $"GlobalLock failed ({lockError})";
            }
            Marshal.Copy(bytes, 0, target, bytes.Length);
            GlobalUnlock(memory);
            if (SetClipboardData(format, memory) != IntPtr.Zero) return null;
            int error = Marshal.GetLastWin32Error();
            GlobalFree(memory);
            return $"SetClipboardData failed ({error})";
        }
    }

    /// <summary>
    /// A Mac: osascript reads the PNG from a temporary file onto the pasteboard, off the main thread. It never calls the
    /// system's libraries by hand, so the worst it can do is fail with an exit code or a timeout.
    /// </summary>
    private static class Mac
    {
        private const int TimeoutMs = 5000;

        public static void Copy(byte[] png, Action<string?, int> onDone)
        {
            string path = Path.Combine(Path.GetTempPath(), $"who-carried-{Guid.NewGuid():N}.png");
            try
            {
                File.WriteAllBytes(path, png);
            }
            catch (Exception e)
            {
                Tracker.LogError("copy: writing the temporary picture", e);
                onDone(e.Message, png.Length);
                return;
            }
            // The answer goes back to the main thread: the caller touches the recap.
            Task.Run(() => Run(path)).ContinueWith(done =>
            {
                string? result = done.IsFaulted ? done.Exception?.GetBaseException().Message ?? "osascript failed" : done.Result;
                Callable.From(() => onDone(result, png.Length)).CallDeferred();
            });
        }

        /// <summary>Runs osascript, then deletes the file. Null on success, or what went wrong. Never throws.</summary>
        private static string? Run(string path)
        {
            try
            {
                // An AppleScript string: escape backslashes and quotes (a temporary path has neither, but be sure).
                string file = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var start = new ProcessStartInfo("/usr/bin/osascript")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add($"set the clipboard to (read (POSIX file \"{file}\") as «class PNGf»)");
                using Process? process = Process.Start(start);
                if (process == null) return "osascript didn't start";
                Task<string> errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(TimeoutMs))
                {
                    process.Kill(entireProcessTree: true);
                    return "osascript timed out";
                }
                return process.ExitCode == 0 ? null : $"osascript exit {process.ExitCode}: {errors.Result.Trim()}";
            }
            catch (Exception e)
            {
                return e.Message;
            }
            finally
            {
                try { File.Delete(path); }
                catch (Exception) { /* a leftover temporary file is harmless */ }
            }
        }
    }
}
