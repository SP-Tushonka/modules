using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using System.Collections.Generic;
using System.Text.Json;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Redirect registry reads/writes to a folder in the SPT directory, instead of sharing
/// registry entries with live.
///
/// Note this is a multi-patch to keep these patches grouped together, as we need to patch
/// many methods to properly implement this
/// </summary>
public class SaveRegistryLocallyPatches
{
    private static readonly string _sptRegistryPath = Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptRegistry");
    private static readonly string _registryFilePath = Path.Combine(_sptRegistryPath, "registry.json");
    private static Dictionary<string, object> _sptRegistry = new Dictionary<string, object>();

    public void Enable()
    {
        Init();

        new PatchPlayerPrefsSetInt().Enable();
        new PatchPlayerPrefsSetFloat().Enable();
        new PatchPlayerPrefsSetString().Enable();
        new PatchPlayerPrefsGetInt().Enable();
        new PatchPlayerPrefsGetFloat().Enable();
        new PatchPlayerPrefsGetString().Enable();
        new PatchPlayerPrefsHasKey().Enable();
        new PatchPlayerPrefsDeleteKey().Enable();
        new PatchPlayerPrefsDeleteAll().Enable();
        new PatchPlayerPrefsSave().Enable();
    }

    public void Init()
    {
        // Make sure the registry directory exists
        if (!Directory.Exists(_sptRegistryPath))
        {
            Directory.CreateDirectory(_sptRegistryPath);
        }

        if (!File.Exists(_registryFilePath))
        {
            return;
        }

        try
        {
            // Load existing registry
            _sptRegistry = Read(File.ReadAllText(_registryFilePath));
        }
        catch (Exception e)
        {
            Console.WriteLine($"SPT registry file was corrupt and unable to be read, reset file to defaults. {e.Message}");
            File.WriteAllText(_registryFilePath, "{}");
        }

        // Make sure we save the registry on exit, for some reason this isn't triggering by Unity itself
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write();
    }

    private static Dictionary<string, object> Read(string json)
    {
        var loaded = new Dictionary<string, object>();

        foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json))
        {
            loaded[entry.Key] = entry.Value.ValueKind switch
            {
                JsonValueKind.String => entry.Value.GetString(),
                JsonValueKind.Number => entry.Value.TryGetInt32(out var number) ? number : entry.Value.GetSingle(),
                _ => entry.Value.ToString(),
            };
        }

        return loaded;
    }

    private static void Write()
    {
        File.WriteAllText(_registryFilePath, JsonSerializer.Serialize(_sptRegistry));
    }

    public class PatchPlayerPrefsSetInt : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.SetInt));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(string __0, int __1)
        {
            _sptRegistry[__0] = __1;
            return false;
        }
    }

    public class PatchPlayerPrefsSetFloat : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.SetFloat));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(string __0, float __1)
        {
            _sptRegistry[__0] = __1;
            return false;
        }
    }

    public class PatchPlayerPrefsSetString : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.SetString));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(string __0, string __1)
        {
            _sptRegistry[__0] = __1;
            return false;
        }
    }

    public class PatchPlayerPrefsGetInt : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.GetInt), [typeof(string), typeof(int)]);
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref int __result, string __0, int __1)
        {
            if (_sptRegistry.TryGetValue(__0, out var value))
            {
                __result = Convert.ToInt32(value);
            }
            else
            {
                __result = __1;
            }
            return false;
        }
    }

    public class PatchPlayerPrefsGetFloat : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.GetFloat), [typeof(string), typeof(float)]);
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref float __result, string __0, float __1)
        {
            if (_sptRegistry.TryGetValue(__0, out var value))
            {
                __result = Convert.ToSingle(value);
            }
            else
            {
                __result = __1;
            }
            return false;
        }
    }

    public class PatchPlayerPrefsGetString : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.GetString), [typeof(string), typeof(string)]);
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref string __result, string __0, string __1)
        {
            if (_sptRegistry.TryGetValue(__0, out var value))
            {
                __result = Convert.ToString(value);
            }
            else
            {
                __result = __1;
            }
            return false;
        }
    }

    public class PatchPlayerPrefsHasKey : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.HasKey));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref bool __result, string __0)
        {
            __result = _sptRegistry.ContainsKey(__0);
            return false;
        }
    }

    public class PatchPlayerPrefsDeleteKey : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.DeleteKey));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix(string __0)
        {
            _sptRegistry.Remove(__0);
            return false;
        }
    }

    public class PatchPlayerPrefsDeleteAll : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.DeleteAll));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            _sptRegistry.Clear();
            return false;
        }
    }

    public class PatchPlayerPrefsSave : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            MethodInfo method = AccessTools.Method(typeof(PlayerPrefs), nameof(PlayerPrefs.Save));
            return method;
        }

        [PatchPrefix]
        private static bool PatchPrefix()
        {
            Write();
            return false;
        }
    }
}
