using System.Runtime.InteropServices;

namespace ResolutionToggle;

/// <summary>
/// Manages display resolution enumeration and switching via Win32 APIs.
/// </summary>
internal sealed class DisplayManager
{
    public record DisplayMode(int Width, int Height, int Frequency);

    /// <summary>
    /// Returns the current display resolution of the primary monitor.
    /// </summary>
    public DisplayMode GetCurrentMode()
    {
        var dm = CreateDevMode();
        NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);
        return new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
    }

    /// <summary>
    /// Returns all supported display modes sorted by descending resolution then frequency.
    /// </summary>
    public List<DisplayMode> GetSupportedModes()
    {
        var modes = new HashSet<DisplayMode>();
        var dm = CreateDevMode();

        int index = 0;
        while (NativeMethods.EnumDisplaySettings(null, index, ref dm) != 0)
        {
            modes.Add(new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
            index++;
        }

        return modes
            .OrderByDescending(m => m.Width * m.Height)
            .ThenByDescending(m => m.Frequency)
            .ToList();
    }

    /// <summary>
    /// Returns the recommended (highest supported) display mode for the primary monitor.
    /// </summary>
    public DisplayMode GetRecommendedMode()
    {
        var modes = GetSupportedModes();
        return modes.First();
    }

    /// <summary>
    /// Sets the primary display to the specified resolution.
    /// Returns a human-readable status string.
    /// </summary>
    public string SetResolution(int width, int height)
    {
        var dm = CreateDevMode();
        NativeMethods.EnumDisplaySettings(null, NativeMethods.ENUM_CURRENT_SETTINGS, ref dm);

        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT;

        int result = NativeMethods.ChangeDisplaySettings(ref dm, NativeMethods.CDS_UPDATEREGISTRY);
        return result switch
        {
            NativeMethods.DISP_CHANGE_SUCCESSFUL => $"Resolution changed to {width}x{height}.",
            NativeMethods.DISP_CHANGE_RESTART => $"Resolution changed to {width}x{height}. A restart is required.",
            NativeMethods.DISP_CHANGE_BADMODE => $"The mode {width}x{height} is not supported by this display.",
            NativeMethods.DISP_CHANGE_FAILED => "The display driver failed to apply the requested mode.",
            NativeMethods.DISP_CHANGE_BADPARAM => "Bad parameter passed to ChangeDisplaySettings.",
            _ => $"ChangeDisplaySettings returned unexpected code: {result}"
        };
    }

    /// <summary>
    /// Returns the current effective DPI scaling percentage for the primary monitor.
    /// </summary>
    public int GetCurrentScalingPercent()
    {
        var point = new NativeMethods.POINT { X = 0, Y = 0 };
        IntPtr monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTOPRIMARY);

        int hr = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _);
        if (hr != 0)
            return 100;

        return (int)Math.Round(dpiX / 96.0 * 100);
    }

    private static NativeMethods.DEVMODE CreateDevMode()
    {
        var dm = new NativeMethods.DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(NativeMethods.DEVMODE));
        return dm;
    }
}
