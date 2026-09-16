using System.Reflection;
using Diz.Resources;
using HarmonyLib;
using SPTushonka.Common.Utils;
using SPTushonka.Custom.Models;
using SPTushonka.Custom.Utils;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

public sealed class EasyBundlePathPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EasyBundle), nameof(EasyBundle.Load));
    }

    [PatchPrefix]
    private static void PatchPrefix(EasyBundle __instance)
    {
        if (BundleManager.Bundles.IsEmpty)
        {
            return;
        }

        var key = __instance.Key;

        if (string.IsNullOrEmpty(key) || !BundleManager.Bundles.TryGetValue(key, out BundleItem bundle))
        {
            return;
        }

        // set path to either cache (HTTP) or mod (local)
        var filepath = BundleManager.GetBundleFilePath(bundle);

        if (__instance._path == filepath)
        {
            return;
        }

        if (!VFS.Exists(filepath))
        {
            Logger.LogError($"bundle: '{bundle.FileName}' is in neither the cache nor its mod folder, loading the game's own copy");
            return;
        }

        __instance._path = filepath;
        Logger.LogInfo($"bundle: '{bundle.FileName}' redirected to '{filepath}'");
    }
}
