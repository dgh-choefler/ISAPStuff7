using System;
using System.Diagnostics;

namespace ISAP.Frontend.Pages_Production
{
    /// <summary>
    /// Application-wide logger stub.
    /// Replace with your actual logging implementation.
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
            Exception exception,
            string source)
        {
            string prefix;
            switch (category)
            {
                case LogEntryCategories.Error:
                    prefix = "ERR";
                    break;
                case LogEntryCategories.Warning:
                    prefix = "WRN";
                    break;
                default:
                    prefix = "INF";
                    break;
            }

            Debug.WriteLine("[" + prefix + "] [" + source + "] " + message);

            if (exception != null)
                Debug.WriteLine("  Exception: " + exception);
        }
    }
}
