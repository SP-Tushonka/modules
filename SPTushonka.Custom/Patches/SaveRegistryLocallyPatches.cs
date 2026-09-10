using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using BepInEx.Logging;
using Il2CppInterop.Runtime;

namespace SPTushonka.Custom.Patches;

public sealed class SaveRegistryLocallyPatches
{
    private static readonly string _sptRegistryPath = Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptRegistry");
    private static readonly string _registryFilePath = Path.Combine(_sptRegistryPath, "registry.json");
    private static readonly ManualLogSource _log = Logger.CreateLogSource("SPTushonka Registry");
    private static Dictionary<string, object> _sptRegistry = [];
    private static readonly List<Delegate> _hooks = [];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetIntDelegate(IntPtr key, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetFloatDelegate(IntPtr key, float value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SetStringDelegate(IntPtr key, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetIntDelegate(IntPtr key, int fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float GetFloatDelegate(IntPtr key, float fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetStringDelegate(IntPtr key, IntPtr fallback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool HasKeyDelegate(IntPtr key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void KeyDelegate(IntPtr key);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidDelegate();

    public void Enable()
    {
        Init();

        Hook<SetIntDelegate>("UnityEngine.PlayerPrefs::TrySetInt", (key, value) => Set(key, value));
        Hook<SetFloatDelegate>("UnityEngine.PlayerPrefs::TrySetFloat", (key, value) => Set(key, value));
        Hook<SetStringDelegate>("UnityEngine.PlayerPrefs::TrySetSetString", (key, value) => Set(key, IL2CPP.Il2CppStringToManaged(value)));
        Hook<GetIntDelegate>("UnityEngine.PlayerPrefs::GetInt", (key, fallback) => Get(key, out var value) ? Convert.ToInt32(value) : fallback);
        Hook<GetFloatDelegate>("UnityEngine.PlayerPrefs::GetFloat", (key, fallback) => Get(key, out var value) ? Convert.ToSingle(value) : fallback);
        Hook<GetStringDelegate>(
            "UnityEngine.PlayerPrefs::GetString",
            (key, fallback) => Get(key, out var value) ? IL2CPP.ManagedStringToIl2Cpp(Convert.ToString(value)) : fallback
        );
        Hook<HasKeyDelegate>("UnityEngine.PlayerPrefs::HasKey", key => _sptRegistry.ContainsKey(IL2CPP.Il2CppStringToManaged(key)));
        Hook<KeyDelegate>("UnityEngine.PlayerPrefs::DeleteKey", key => _sptRegistry.Remove(IL2CPP.Il2CppStringToManaged(key)));
        Hook<VoidDelegate>("UnityEngine.PlayerPrefs::DeleteAll", () => _sptRegistry.Clear());
        Hook<VoidDelegate>("UnityEngine.PlayerPrefs::Save", Write);

        _log.LogInfo($"registry: {_hooks.Count} PlayerPrefs icalls redirected to {_registryFilePath}");
    }

    private static void Hook<T>(string icall, T replacement)
        where T : Delegate
    {
        if (IL2CPP.il2cpp_resolve_icall(icall) == IntPtr.Zero)
        {
            _log.LogWarning($"registry: {icall} is not an icall on this build, left to the engine");
            return;
        }

        _hooks.Add(replacement);
        IL2CPP.il2cpp_add_internal_call(Marshal.StringToHGlobalAnsi(icall), Marshal.GetFunctionPointerForDelegate(replacement));
    }

    private static bool Set(IntPtr key, object value)
    {
        _sptRegistry[IL2CPP.Il2CppStringToManaged(key)] = value;
        return true;
    }

    private static bool Get(IntPtr key, out object value)
    {
        return _sptRegistry.TryGetValue(IL2CPP.Il2CppStringToManaged(key), out value);
    }

    private static void Init()
    {
        Directory.CreateDirectory(_sptRegistryPath);
        if (File.Exists(_registryFilePath))
        {
            try
            {
                _sptRegistry = Read(File.ReadAllText(_registryFilePath));
            }
            catch (Exception e)
            {
                _log.LogWarning($"registry: registry.json was unreadable and is reset: {e.Message}");
                File.WriteAllText(_registryFilePath, "{}");
            }
        }

        // The engine flushes its own store on quit natively, so the file is written here as well.
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
        try
        {
            File.WriteAllText(_registryFilePath, JsonSerializer.Serialize(_sptRegistry));
        }
        catch (Exception e)
        {
            _log.LogWarning($"registry: could not write registry.json: {e.Message}");
        }
    }
}
