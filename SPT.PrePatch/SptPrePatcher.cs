using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json;

namespace SPT.PrePatch;

public static class SptPrePatcher
{
    public static IEnumerable<string> TargetDLLs { get; } = ["Assembly-CSharp.dll"];
    private const string PluginFolder = "plugins";
    private const string SptPluginFolder = "plugins/spt";

    private const string EnumEntriesRoute = "/singleplayer/customEnumEntries";
    private const string OutdatedModsExitMessage = "Update or remove them before starting the game. Exiting.";

    private static readonly ManualLogSource _logger = Logger.CreateLogSource(nameof(SptPrePatcher));

    public static void Patch(ref AssemblyDefinition assembly)
    {
        PerformPreValidation();
        ChangeAppDataPath(assembly);

        EnumPatcher.PatchEnums(_logger, ref assembly, GetCustomEnumEntries());
    }

    private static List<EnumEntryDefinition> GetCustomEnumEntries()
    {
        var backendUrl = GetBackendUrl();
        var requestUri = new Uri(new Uri(backendUrl.TrimEnd('/') + "/"), EnumEntriesRoute.TrimStart('/'));

        var response = WinHttpClient.GetString(requestUri);
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new InvalidOperationException($"The server returned an empty response from {EnumEntriesRoute}.");
        }

        var resp = JsonConvert.DeserializeObject<List<EnumEntryDefinition>>(response);
        if (resp is null)
        {
            throw new InvalidOperationException($"The response from {EnumEntriesRoute} is null.");
        }

        return resp;
    }

    private static string GetBackendUrl()
    {
        const string configPrefix = "-config=";
        var configArgument = Environment
            .GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith(configPrefix, StringComparison.OrdinalIgnoreCase));

        if (configArgument is null)
        {
            throw new InvalidOperationException("Could not find SPT's -config launch argument containing the backend URL.");
        }

        var launcherConfig = JsonConvert.DeserializeObject<LauncherConfig>(configArgument[configPrefix.Length..]);
        if (string.IsNullOrWhiteSpace(launcherConfig?.BackendUrl))
        {
            throw new InvalidOperationException("SPT's -config launch argument did not contain a backend URL.");
        }

        return launcherConfig.BackendUrl;
    }

    private static void ChangeAppDataPath(AssemblyDefinition assembly)
    {
        // Change icon cache folder path to be local to SPT
        // find the type that contains a method called ClearIconCache, there is currently only one
        var typeToEdit = assembly.MainModule.GetTypes().FirstOrDefault(x => x.Methods.Any(m => m.Name == "ClearIconCache"));

        // find the .cctor and change the instructions to use our path instead
        var methodToEdit = typeToEdit.Methods.FirstOrDefault(x => x.Name == ".cctor");
        var ilProc = methodToEdit.Body.GetILProcessor();
        var instructions = GetCacheInstructions(assembly);

        // all this constructor does is set this static field up
        methodToEdit.Body.Instructions.Clear();

        foreach (var ins in instructions)
        {
            ilProc.Append(ins);
        }
    }

    private static List<Instruction> GetCacheInstructions(AssemblyDefinition assembly)
    {
        return new List<Instruction>
        {
            Instruction.Create(OpCodes.Call, assembly.MainModule.ImportReference(typeof(Environment).GetMethod("get_CurrentDirectory"))),
            Instruction.Create(OpCodes.Ldstr, "SPT_Runtime"),
            Instruction.Create(OpCodes.Ldstr, "user"),
            Instruction.Create(OpCodes.Ldstr, "sptappdata"),
            Instruction.Create(
                OpCodes.Call,
                assembly.MainModule.ImportReference(
                    typeof(Path).GetMethod("Combine", new[] { typeof(string), typeof(string), typeof(string), typeof(string) })
                )
            ),
            Instruction.Create(
                OpCodes.Stsfld,
                assembly
                    .MainModule.GetTypes()
                    .FirstOrDefault(x => x.Methods.Any(m => m.Name == "ClearIconCache"))
                    .Fields.FirstOrDefault(f => f.Name == "Path")
            ),
            Instruction.Create(OpCodes.Ret),
        };
    }

    private static void PerformPreValidation()
    {
        // Check if the launcher was used
        var launcherUsed = ValidateLauncherUse(out var launcherError);

        // Check that all the expected plugins are in the BepInEx/Plugins/spt/ folder
        var executingAssembly = System.Reflection.Assembly.GetExecutingAssembly();
        var assemblyFolder = Path.GetDirectoryName(executingAssembly.Location);
        var pluginPath = Path.GetFullPath(Path.Combine(assemblyFolder, "..", PluginFolder));
        var sptPluginPath = Path.GetFullPath(Path.Combine(assemblyFolder, "..", SptPluginFolder));
        var pluginsValidated = ValidateSptPlugins(sptPluginPath, out string pluginErrorMessage);

        if (!launcherUsed)
        {
            ExitWithError("Startup Error", launcherError);
        }

        if (!pluginsValidated)
        {
            ExitWithError("Missing Core Files", pluginErrorMessage);
        }

        // Check no mods were built against a different version of SPT
        var sptVersion = executingAssembly.GetName().Version;
        var strictPluginVersionCheck = PrePatchConfig.StrictPluginVersionCheck;

        if (!PluginValidator.ValidatePlugins(_logger, pluginPath, sptPluginPath, sptVersion, out string compatibilityReport))
        {
            EnforcePluginVersionCheck(strictPluginVersionCheck, compatibilityReport);
        }
    }

    private static void EnforcePluginVersionCheck(bool strictPluginVersionCheck, string compatibilityReport)
    {
        if (strictPluginVersionCheck)
        {
            ExitWithError("Outdated Mods", compatibilityReport + OutdatedModsExitMessage);
            return;
        }

        _logger.LogWarning(
            "Plugin version validation failed, but StrictPluginVersionCheck is disabled. "
                + "Startup will continue and BepInEx will attempt to load these plugins. This may cause runtime errors."
                + Environment.NewLine
                + compatibilityReport
        );
    }

    private static void ExitWithError(string title, string message)
    {
        MessageBoxHelper.Show(message, title, MessageBoxHelper.MessageBoxType.OK);
        Environment.Exit(0);
    }

    private static bool ValidateLauncherUse(out string message)
    {
        // Validate that parameters were passed to EscapeFromTarkov.exe, to verify the
        // player used the SPT Launcher to start the process
        string[] args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            message = "";
            return true;
        }

        message = "Please start SPT using SPT.Launcher.exe. Exiting.";
        return false;
    }

    private static bool ValidateSptPlugins(string sptPluginPath, out string message)
    {
        string exitMessage = "\n\nPlease re-install SPT. Exiting.";

        // Validate that the SPT plugin path exists
        if (!Directory.Exists(sptPluginPath))
        {
            message = $"'{sptPluginPath}' directory not found{exitMessage}";
            _logger.LogError(message);
            return false;
        }

        // Validate that the folder exists, and contains our plugins
        string[] sptPlugins = new string[]
        {
            "spt-common.dll",
            "spt-reflection.dll",
            "spt-core.dll",
            "spt-custom.dll",
            "spt-singleplayer.dll",
        };
        string[] foundPlugins = Directory.GetFiles(sptPluginPath).Select(x => Path.GetFileName(x)).ToArray();

        foreach (string pluginNameAndSuffix in sptPlugins)
        {
            if (!foundPlugins.Contains(pluginNameAndSuffix))
            {
                message = $"Required SPT plugin: {pluginNameAndSuffix} missing from '{sptPluginPath}' {exitMessage}";
                _logger.LogError(message);
                return false;
            }
        }

        message = "";
        return true;
    }

    private sealed class LauncherConfig
    {
        public string BackendUrl { get; set; }
    }
}
