using Microsoft.Win32;

namespace ResolutionToggle;

/// <summary>
/// Reads and writes the per-monitor DPI scaling override stored in the registry.
/// Changes take effect after sign-out / sign-in (Windows limitation).
/// </summary>
internal static class ScalingHelper
{
    private const string RegistryPath =
        @"HKEY_CURRENT_USER\Control Panel\Desktop";

    private const string LogPixelsKey = "LogPixels";
    private const string Win8DpiScalingKey = "Win8DpiScaling";

    /// <summary>
    /// Sets the desktop DPI scaling percentage (100 = 96 DPI, 125 = 120 DPI, etc.).
    /// The change is persisted to the registry; Windows applies it after the next sign-in.
    /// Pass 0 or 100 to revert to the system default (recommended) scaling.
    /// </summary>
    public static void SetScalingPercent(int percent)
    {
        int logPixels = (int)Math.Round(percent / 100.0 * 96);

        if (percent <= 100)
        {
            Registry.SetValue(RegistryPath, Win8DpiScalingKey, 0, RegistryValueKind.DWord);
            Registry.SetValue(RegistryPath, LogPixelsKey, 96, RegistryValueKind.DWord);
        }
        else
        {
            Registry.SetValue(RegistryPath, Win8DpiScalingKey, 1, RegistryValueKind.DWord);
            Registry.SetValue(RegistryPath, LogPixelsKey, logPixels, RegistryValueKind.DWord);
        }
    }

    /// <summary>
    /// Reads the currently persisted DPI scaling from the registry.
    /// Returns 100 when the default / recommended scaling is active.
    /// </summary>
    public static int GetPersistedScalingPercent()
    {
        object? value = Registry.GetValue(RegistryPath, LogPixelsKey, 96);
        int logPixels = value is int lp ? lp : 96;

        object? win8Flag = Registry.GetValue(RegistryPath, Win8DpiScalingKey, 0);
        int flag = win8Flag is int f ? f : 0;

        if (flag == 0)
            return 100;

        return (int)Math.Round(logPixels / 96.0 * 100);
    }
}
