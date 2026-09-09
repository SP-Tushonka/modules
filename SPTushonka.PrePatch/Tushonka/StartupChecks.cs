using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Logging;
using Mono.Cecil;

namespace SPTushonka.PrePatch.Tushonka;

// Refuses to start when the install cannot work: no launcher arguments, a missing SPTushonka
// plugin, or a mod built against another SPTushonka major.minor. Each shows a message box and exits,
// which beats the exception it would otherwise turn into inside the chainloader.
internal static class StartupChecks
{
    private const string PluginFolder = "sptushonka";
    private const int MaxListedMods = 10;

    private static readonly string[] RequiredPlugins =
    [
        "SPTushonka.Common.dll",
        "SPTushonka.Reflection.dll",
        "SPTushonka.Core.dll",
        "SPTushonka.Custom.dll",
        "SPTushonka.SinglePlayer.dll",
    ];

    public static void Run(ManualLogSource log)
    {
        if (Environment.GetCommandLineArgs().Length <= 1)
        {
            Exit(log, "Startup Error", "Please start SPTushonka through the launcher. Exiting.");
        }

        var ourPlugins = Path.Combine(BepInEx.Paths.PluginPath, PluginFolder);
        var missing = RequiredPlugins.Where(name => !File.Exists(Path.Combine(ourPlugins, name))).ToList();
        if (missing.Count > 0)
        {
            Exit(log, "Missing Core Files", $"Missing from '{ourPlugins}':\n\n    {string.Join("\n    ", missing)}\n\nPlease reinstall SPTushonka. Exiting.");
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var mismatched = MismatchedMods(log, ourPlugins, version);
        if (mismatched.Count > 0)
        {
            foreach (var mod in mismatched)
            {
                log.LogError($"mods: {mod.Key} was built for SPTushonka {mod.Value.ToString(3)}, this is {version.ToString(3)}");
            }

            Exit(log, "Outdated Mods", MismatchMessage(mismatched, version));
        }
    }

    // Every plugin outside our own folder that references an SPTushonka assembly of another major.minor.
    private static Dictionary<string, Version> MismatchedMods(ManualLogSource log, string ourPlugins, Version version)
    {
        var mismatched = new Dictionary<string, Version>();
        var root = Path.GetFullPath(BepInEx.Paths.PluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var ours = Path.GetFullPath(ourPlugins).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var file in Directory.GetFiles(BepInEx.Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (full.StartsWith(ours, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var referenced = ReferencedVersion(log, full);
            if (referenced != null && (referenced.Major != version.Major || referenced.Minor != version.Minor))
            {
                mismatched[full.Substring(root.Length)] = referenced;
            }
        }

        return mismatched;
    }

    // Read from memory because Cecil keeps the file open and the chainloader loads it next.
    private static Version ReferencedVersion(ManualLogSource log, string file)
    {
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(file));
            using var assembly = AssemblyDefinition.ReadAssembly(stream);
            return assembly.MainModule.AssemblyReferences
                .Where(reference => reference.Name.StartsWith("SPTushonka.", StringComparison.OrdinalIgnoreCase))
                .Select(reference => reference.Version)
                .OrderByDescending(v => v)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            // Native libraries sit beside plugins and are not assemblies.
            log.LogDebug($"mods: {Path.GetFileName(file)} not read as an assembly: {ex.Message}");
            return null;
        }
    }

    private static string MismatchMessage(Dictionary<string, Version> mismatched, Version version)
    {
        var lines = mismatched.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListedMods)
            .Select(m => $"    {m.Key} (built for {m.Value.ToString(3)})")
            .ToList();
        if (mismatched.Count > MaxListedMods)
        {
            lines.Add($"    ...and {mismatched.Count - MaxListedMods} more, see the log");
        }

        return $"These mods were built for a different SPTushonka version than {version.ToString(3)}:\n\n{string.Join("\n", lines)}\n\nUpdate or remove them before starting the game. Exiting.";
    }

    private static void Exit(ManualLogSource log, string title, string message)
    {
        log.LogFatal($"{title}: {message.Replace('\n', ' ')}");
        try
        {
            MessageBox(IntPtr.Zero, message, title, 0x10);
        }
        catch
        {
            // No user32 means no box, the log line has to do.
        }

        Environment.Exit(1);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
