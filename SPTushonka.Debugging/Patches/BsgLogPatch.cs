using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Extensions.Logging;
using SPTushonka.Common.Http;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Debugging.Patches;

// Copies the game's own log lines into the BepInEx log from the verbosity the server's core config
// asks for, and to the server console when it asks for that too. 1.1 logs through
// Microsoft.Extensions.Logging, so the level numbers match the config directly.
public class BsgLogPatch : ModulePatch
{
    private static LoggingLevelResponse _settings;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(AbstractLogger),
            nameof(AbstractLogger.Log),
            [typeof(string), typeof(string), typeof(LogLevel), typeof(Il2CppReferenceArray<Il2CppSystem.Object>)]
        );
    }

    [PatchPostfix]
    public static void Postfix(string __0, LogLevel __2, Il2CppReferenceArray<Il2CppSystem.Object> __3)
    {
        _settings ??= Load();
        if ((int)__2 < _settings.Verbosity || string.IsNullOrEmpty(__0))
        {
            return;
        }

        var line = Format(__0, __3);
        Logger.LogDebug($"bsg {__2}: {line}");
        if (_settings.SendToServer)
        {
            ServerLog.Log("EFT", $"{__2}: {line}", (int)__2);
        }
    }

    private static LoggingLevelResponse Load()
    {
        try
        {
            return JsonSerializer.Deserialize<LoggingLevelResponse>(RequestHandler.GetJson("/singleplayer/enableBSGlogging"));
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"bsg logging settings unavailable, staying off: {ex.Message}");
            return new LoggingLevelResponse { Verbosity = (int)LogLevel.None };
        }
    }

    // The format holds {0} style holes plus literal braces from json payloads, so only the
    // numbered holes are left for string.Format.
    private static string Format(string format, Il2CppReferenceArray<Il2CppSystem.Object> args)
    {
        var escaped = Regex.Replace(format.Replace("{", "{{").Replace("}", "}}"), @"\{\{(\d+)\}\}", "{$1}");
        var values = new object[args == null ? 0 : args.Length];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = args[i]?.ToString() ?? "null";
        }

        try
        {
            return string.Format(escaped, values);
        }
        catch (FormatException)
        {
            return format;
        }
    }

    public class LoggingLevelResponse
    {
        [JsonPropertyName("verbosity")]
        public int Verbosity { get; set; }

        [JsonPropertyName("sendToServer")]
        public bool SendToServer { get; set; }
    }
}
