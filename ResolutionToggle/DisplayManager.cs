using System.Runtime.InteropServices;

namespace ResolutionToggle;

/// <summary>
/// Manages display resolution enumeration and switching via Win32 APIs.
/// Thread-safe: all public methods are safe to call from any thread.
/// </summary>
internal sealed class DisplayManager
{
    public record DisplayMode(int Width, int Height, int Frequency);

    public record SetResolutionResult(bool Success, string Message, int NativeCode);

    private readonly object _lock = new();

    /// <summary>
    /// Returns the current display resolution of the primary monitor.
    /// Returns null when the API call fails instead of returning garbage data.
    /// </summary>
    public DisplayMode? GetCurrentMode()
    {
        var dm = CreateDevMode();
        int ok = NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
        if (ok == 0)
            return null;

        return new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    /// <summary>
    /// Returns all supported display modes sorted by descending resolution then frequency.
    /// Returns an empty list if the driver reports no modes.
    /// </summary>
    public List<DisplayMode> GetSupportedModes()
    {
        var modes = new HashSet<DisplayMode>();
        var dm = CreateDevMode();

        int index = 0;
        while (NativeMethods.EnumDisplaySettings(null, index, ref dm) != 0)
        {
            if (dm.dmPelsWidth > 0 && dm.dmPelsHeight > 0)
                modes.Add(new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
            index++;
        }

        return modes
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.Frequency)
            .ToList();
    }

    /// <summary>
    /// Returns the recommended (highest supported) display mode, or null if none are available.
    /// </summary>
    public DisplayMode? GetRecommendedMode()
    {
        var modes = GetSupportedModes();
        return modes.Count > 0 ? modes[0] : null;
    }

    /// <summary>
    /// Sets the primary display to the specified resolution.
    /// Validates parameters before calling the native API.
    /// Serialised via a lock to prevent concurrent resolution changes.
    /// </summary>
    public SetResolutionResult SetResolution(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return new SetResolutionResult(false, $"Invalid dimensions: {width}x{height}.", 0);

        lock (_lock)
        {
            var dm = CreateDevMode();
            if (NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm) == 0)
                return new SetResolutionResult(false, "Unable to read current display settings.", 0);

            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;

            int result = NativeMethods.ChangeDisplaySettings(ref dm, NativeMethods.CDS_UPDATEREGISTRY);
            string message = result switch
            {
                NativeMethods.DISP_CHANGE_SUCCESSFUL => $"Resolution changed to {width}x{height}.",
                NativeMethods.DISP_CHANGE_RESTART    => $"Resolution changed to {width}x{height}. A restart is required.",
                NativeMethods.DISP_CHANGE_BADMODE    => $"The mode {width}x{height} is not supported by this display.",
                NativeMethods.DISP_CHANGE_FAILED     => "The display driver failed to apply the requested mode.",
                NativeMethods.DISP_CHANGE_BADPARAM   => "Bad parameter passed to ChangeDisplaySettings.",
                _ => $"ChangeDisplaySettings returned unexpected code: {result}"
            };

            bool success = result == NativeMethods.DISP_CHANGE_SUCCESSFUL
                        || result == NativeMethods.DISP_CHANGE_RESTART;

            return new SetResolutionResult(success, message, result);
        }
    }

    /// <summary>
    /// Returns the current effective DPI scaling percentage for the primary monitor.
    /// Falls back to 100 % when the API is unavailable.
    /// </summary>
    public int GetCurrentScalingPercent()
    {
        try
        {
            var point = new NativeMethods.POINT { X = 0, Y = 0 };
            IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTOPRIMARY);

            if (monitor == IntPtr.Zero)
                return 100;

            int hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
            if (hr != 0)
                return 100;

            return (int)Math.Round(dpiX / 96.0 * 100);
        }
        catch (DllNotFoundException)
        {
            return 100;
        }
        catch (EntryPointNotFoundException)
        {
            return 100;
        }
    }

    private static NativeMethods.DEVMODE CreateDevMode()
    {
        var dm = new NativeMethods.DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
        return dm;
    }
}
