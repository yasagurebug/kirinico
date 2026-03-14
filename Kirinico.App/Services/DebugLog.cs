using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Kirinico.App.Services;

public static class DebugLog
{
#if DEBUG
    private static readonly object SyncRoot = new();
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "kirinico-debug.log");
#endif

    [Conditional("DEBUG")]
    public static void InitializeSession()
    {
#if DEBUG
        WriteLine("===== session start =====");
#endif
    }

    [Conditional("DEBUG")]
    public static void WriteLine(string message)
    {
#if DEBUG
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");

        lock (SyncRoot)
        {
            File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
#endif
    }
}
