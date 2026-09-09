using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace SPTushonka.Reflection.Patching;

public abstract class ModulePatch
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    protected static ManualLogSource Logger { get; private set; }

    private static readonly List<string> Missing = new();
    private static int _requested;
    private static int _reportedRequests;
    private static int _reportedMissing;

    private readonly Harmony _harmony;

    protected ModulePatch()
    {
        Logger ??= BepInEx.Logging.Logger.CreateLogSource("SPTushonka");
        _harmony = new Harmony(GetType().Name);
    }

    protected abstract MethodBase GetTargetMethod();

    public void Enable()
    {
        var name = GetType().Name;
        _requested++;
        try
        {
            var target = GetTargetMethod();
            if (target == null)
            {
                Missing.Add(name);
                Logger.LogError($"{name}: target method not found - patch not applied");
                return;
            }

            _harmony.Patch(target, Hook<PatchPrefixAttribute>(), Hook<PatchPostfixAttribute>());
            Logger.LogDebug($"{name}: patched {target.DeclaringType?.Name}.{target.Name}");
        }
        catch (Exception ex)
        {
            Missing.Add(name);
            Logger.LogError($"{name}: {ex}");
        }
    }

    // A rename in a new client shows up as a handful of errors scattered through a very large
    // log; this is the one line that says how many patches did not take.
    public static void Summarise(string scope)
    {
        var requested = _requested - _reportedRequests;
        var missing = Missing.Skip(_reportedMissing).ToArray();
        _reportedRequests = _requested;
        _reportedMissing = Missing.Count;

        if (missing.Length == 0)
        {
            Logger.LogMessage($"{scope}: {requested} patch(es) applied");
            return;
        }

        Logger.LogWarning($"{scope}: {requested - missing.Length}/{requested} patch(es) applied - "
                          + $"not applied: {string.Join(", ", missing)}");
    }

    private HarmonyMethod Hook<T>() where T : Attribute
    {
        var method = GetType().GetMethods(Flags).FirstOrDefault(m => m.GetCustomAttribute<T>() != null);
        return method == null ? null : new HarmonyMethod(method);
    }
}
