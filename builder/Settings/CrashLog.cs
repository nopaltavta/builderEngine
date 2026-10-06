using System;
using System.IO;

namespace builder.Settings;

/// <summary>
/// Append-only diagnostics (%AppData%/BuilderStudio/startup.log + crash.log).
/// Everything is best-effort and exception-proof: logging must never crash the app.
/// Used to diagnose silent startup deaths (e.g. native driver crashes) where no
/// window or dialog ever appears.
/// </summary>
internal static class CrashLog
{
    private static readonly object Gate = new();
    private static string _dir = "";
    private static bool _sessionStarted;

    private static string Dir
    {
        get
        {
            if (string.IsNullOrEmpty(_dir))
            {
                _dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BuilderStudio");
                try { Directory.CreateDirectory(_dir); } catch { /* ignore */ }
            }
            return _dir;
        }
    }

    private static void Append(string file, string line)
    {
        try
        {
            lock (Gate)
            {
                if (!_sessionStarted && file == "startup.log")
                {
                    _sessionStarted = true;
                    File.AppendAllText(Path.Combine(Dir, file),
                        $"\n===== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} =====\n");
                }
                File.AppendAllText(Path.Combine(Dir, file),
                    $"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
            }
        }
        catch { /* logging never throws */ }
    }

    public static void Startup(string milestone) => Append("startup.log", milestone);

    public static void Crash(string context, object? error) =>
        Append("crash.log", context + ": " + (error?.ToString() ?? "<null>"));
}
