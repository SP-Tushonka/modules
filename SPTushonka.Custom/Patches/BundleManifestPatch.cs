using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SPTushonka.Custom.Utils;
using SPTushonka.Reflection.Patching;
using UnityEngine.Build.Pipeline;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Puts the server's bundles into the manifest before the game reads it, so EasyAssets builds and
/// loads them alongside its own. Dependencies resolve through the game's normal lookups because
/// the entries live in the manifest itself.
/// </summary>
public class BundleManifestPatch : ModulePatch
{
    private static bool _injected;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(CompatibilityAssetBundleManifest),
            nameof(CompatibilityAssetBundleManifest.GetAllAssetBundles));
    }

    [PatchPrefix]
    private static void PatchPrefix(CompatibilityAssetBundleManifest __instance)
    {
        if (_injected || BundleManager.Bundles.IsEmpty)
        {
            return;
        }

        _injected = true;

        foreach (var bundle in BundleManager.Bundles.Values)
        {
            var dependencies = bundle.Dependencies ?? Array.Empty<string>();

            var declared = __instance.GetDirectDependencies(bundle.FileName);
            if (declared != null && declared.Length > 0)
            {
                dependencies = declared.ToArray().Union(dependencies).ToArray();
            }

            BundleDetailsFactory.AddToDictionary(
                __instance.m_Details,
                bundle.FileName,
                bundle.FileName,
                bundle.Crc,
                new Il2CppStringArray(dependencies));
        }

        Logger.LogInfo($"{BundleManager.Bundles.Count} mod bundle(s) added to the manifest");
    }
}
