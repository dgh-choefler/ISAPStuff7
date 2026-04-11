using System.Diagnostics;

namespace ResolutionToggle;

internal static class Program
{
    private const string MutexName = @"Global\ResolutionToggle_SingleInstance_B7E3F1A0";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            BringExistingInstanceToFront();
            return;
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            ShowFatalError("An unexpected UI error occurred.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ShowFatalError("A fatal error occurred.", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static void BringExistingInstanceToFront()
    {
        var current = Process.GetCurrentProcess();
        foreach (var proc in Process.GetProcessesByName(current.ProcessName))
        {
            if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                break;
            }
        }
    }

    private static void ShowFatalError(string context, Exception? ex)
    {
        string message = ex is not null
            ? $"{context}\n\n{ex.GetType().Name}: {ex.Message}"
            : context;

        MessageBox.Show(message, "Resolution Toggle — Error",
            MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
