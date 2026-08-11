using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Logging;
using Mono.Cecil;

namespace SPT.PrePatch;

internal static class PluginValidator
{
    private const int MaxListedPlugins = 10;

    private static readonly HashSet<string> _sptAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "spt-common",
        "spt-core",
        "spt-custom",
        "spt-debugging",
        "spt-reflection",
        "spt-singleplayer",
    };

    public static bool ValidatePlugins(
        ManualLogSource logger,
        string pluginPath,
        string sptPluginPath,
        Version sptVersion,
        out string message
    )
    {
        var mismatched = GetMismatchedPlugins(logger, pluginPath, sptPluginPath, sptVersion);

        foreach (var plugin in mismatched)
        {
            logger.LogError(
                $"Plugin `{plugin.Key}` was built for SPT {plugin.Value.ToString(3)}, you are running {sptVersion.ToString(3)}"
            );
        }

        if (mismatched.Count == 0)
        {
            message = "";
            return true;
        }

        message = BuildMessage(mismatched, sptVersion);
        return false;
    }

    private static Dictionary<string, Version> GetMismatchedPlugins(
        ManualLogSource logger,
        string pluginPath,
        string sptPluginPath,
        Version sptVersion
    )
    {
        var mismatched = new Dictionary<string, Version>();

        if (!Directory.Exists(pluginPath))
        {
            return mismatched;
        }

        var sptPluginPrefix = sptPluginPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pluginPrefix = Path.GetFullPath(pluginPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var pluginFile in Directory.GetFiles(pluginPath, "*.dll", SearchOption.AllDirectories))
        {
            // SPT's own plugins ship with the running build, so they can never mismatch
            if (Path.GetFullPath(pluginFile).StartsWith(sptPluginPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var referencedVersion = GetSptReferenceVersion(logger, pluginFile);

            if (referencedVersion is null || SatisfiesSptVersion(referencedVersion, sptVersion))
            {
                continue;
            }

            // Relative so a stale spt-*.dll shipped inside a mod's own folder is identifiable
            mismatched[Path.GetFullPath(pluginFile).Substring(pluginPrefix.Length)] = referencedVersion;
        }

        return mismatched;
    }

    private static bool SatisfiesSptVersion(Version referencedVersion, Version sptVersion)
    {
        return referencedVersion.Major == sptVersion.Major && referencedVersion.Minor == sptVersion.Minor;
    }

    /// <summary>
    ///     Highest SPT assembly version the plugin was compiled against, or null if it references none
    /// </summary>
    private static Version GetSptReferenceVersion(ManualLogSource logger, string pluginFile)
    {
        try
        {
            // Read from memory, Cecil holds the file open and the chainloader loads these next
            using (var pluginStream = new MemoryStream(File.ReadAllBytes(pluginFile)))
            using (var plugin = AssemblyDefinition.ReadAssembly(pluginStream))
            {
                return plugin
                    .MainModule.AssemblyReferences.Where(reference => _sptAssemblyNames.Contains(reference.Name))
                    .Select(reference => reference.Version)
                    .OrderByDescending(version => version)
                    .FirstOrDefault();
            }
        }
        catch (Exception exception)
        {
            // Native libraries and other non-managed files live alongside plugins, they are not our concern
            logger.LogDebug($"Unable to read assembly references from `{Path.GetFileName(pluginFile)}`: {exception.Message}");

            return null;
        }
    }

    private static string BuildMessage(Dictionary<string, Version> mismatched, Version sptVersion)
    {
        var message = new StringBuilder()
            .AppendLine($"These mods were built for a different version of SPT than the one you are running ({sptVersion.ToString(3)}):")
            .AppendLine();

        foreach (var plugin in mismatched.OrderBy(plugin => plugin.Key, StringComparer.OrdinalIgnoreCase).Take(MaxListedPlugins))
        {
            message.AppendLine($"    {plugin.Key} (built for SPT {plugin.Value.ToString(3)})");
        }

        if (mismatched.Count > MaxListedPlugins)
        {
            message.AppendLine($"    ...and {mismatched.Count - MaxListedPlugins} more, see the log for the full list");
        }

        return message.AppendLine().Append("Update or remove them before starting the game. Exiting.").ToString();
    }
}
