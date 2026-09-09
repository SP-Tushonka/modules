using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Logging;

namespace SPTushonka.PrePatch.Loader;

// BepInEx only writes LogOutput.log once the chainloader is up, so a crash during patching loses
// everything before it. Until that file exists every line is appended to the loader's tushonka.log.
internal sealed class LoaderLogListener : ILogListener
{
    public LogLevel LogLevelFilter => LogLevel.All & ~LogLevel.Debug;

    public void LogEvent(object sender, LogEventArgs e)
    {
        // The chainloader replays the preloader lines into its disk log, no need to hold both
        if (Logger.Listeners.Any(listener => listener is DiskLogListener))
        {
            Logger.Listeners.Remove(this);
            return;
        }

        var text = e.Data?.ToString() ?? string.Empty;
        try
        {
            using var writer = new StreamWriter(new FileStream(Path.Combine(global::BepInEx.Paths.GameRootPath, "tushonka.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
            writer.WriteLine($"bepinex: [{e.Level}:{e.Source?.SourceName}] {text}");
        }
        catch
        {
            // The loader log is a convenience, never a reason to fail
        }
    }

    public void Dispose()
    {
    }
}
