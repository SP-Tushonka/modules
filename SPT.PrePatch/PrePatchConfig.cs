using System.IO;
using System.Reflection;
using BepInEx.Configuration;

namespace SPT.PrePatch;

/// <summary>
///     Patcher-local config. SPT.Core is not loaded yet, so this cannot use the normal plugin ConfigFile.
/// </summary>
internal static class PrePatchConfig
{
    private const string ConfigFileName = "spt-prepatch.cfg";
    private const string PluginValidationSection = "Plugin Validation";

    internal static bool StrictPluginVersionCheck { get; }

    static PrePatchConfig()
    {
        // Patcher lives in BepInEx/patchers; keep the cfg next to other BepInEx settings
        var assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var configPath = Path.GetFullPath(Path.Combine(assemblyFolder, "..", "config", ConfigFileName));
        var config = new ConfigFile(configPath, true);
        StrictPluginVersionCheck = config
            .Bind(
                PluginValidationSection,
                nameof(StrictPluginVersionCheck),
                true,
                "When enabled (default), plugins that reference a different SPT major/minor version prevent the game from starting. Disable to allow those plugins to attempt to load. This may cause runtime errors."
            )
            .Value;
    }
}
