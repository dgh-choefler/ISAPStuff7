namespace ISAP.Frontend.Pages_Production;

/// <summary>
/// Application-wide logger.
/// Replace this stub with your actual logging implementation.
/// </summary>
public static class Logger
{
    public enum LogEntryCategories
    {
        Info,
        Warning,
        Error,
    }

    public static void AddLogEntry(
        LogEntryCategories category,
        string message,
        Exception? exception,
        string source)
    {
        var prefix = category switch
        {
            LogEntryCategories.Error => "ERR",
            LogEntryCategories.Warning => "WRN",
            _ => "INF",
        };

        System.Diagnostics.Debug.WriteLine($"[{prefix}] [{source}] {message}");

        if (exception is not null)
            System.Diagnostics.Debug.WriteLine($"  Exception: {exception}");
    }
}
