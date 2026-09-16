using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using SPTushonka.Common.Utils;
using SPTushonka.Custom.Utils;
using SPTushonka.Reflection.Patching;
using UnityEngine.Build.Pipeline;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Puts the server's bundles into the manifest before the game reads it, so EasyAssets builds and
/// loads them alongside its own. Dependencies resolve through the game's normal lookups because
/// the entries live in the manifest itself.
/// </summary>
public sealed class BundleManifestPatch : ModulePatch
{
    private const int MissingListLimit = 10;

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

        var missing = new List<string>();

        foreach (var bundle in BundleManager.Bundles.Values)
        {
            var dependencies = bundle.Dependencies ?? Array.Empty<string>();

            var declared = __instance.GetDirectDependencies(bundle.FileName);
            if (declared != null && declared.Length > 0)
            {
                dependencies = declared.ToArray().Union(dependencies).ToArray();
            }

            __instance.m_Details[bundle.FileName] = new BundleDetails
            {
                FileName = bundle.FileName,
                Crc = bundle.Crc,
                Dependencies = new Il2CppStringArray(dependencies),
            };

            if (!VFS.Exists(BundleManager.GetBundleFilePath(bundle)))
            {
                missing.Add(bundle.FileName);
            }
        }

        Logger.LogInfo($"{BundleManager.Bundles.Count} mod bundle(s) added to the manifest");

        if (missing.Count > 0)
        {
            ReportMissing(missing);
        }
    }

    private static void ReportMissing(List<string> missing)
    {
        Logger.LogError(
            $"{missing.Count} of {BundleManager.Bundles.Count} mod bundle(s) were found in neither the bundle cache nor a mod folder");

        foreach (var name in missing.Take(MissingListLimit))
        {
            Logger.LogError($"Missing bundle: {name}");
        }

        if (missing.Count > MissingListLimit)
        {
            Logger.LogError($"...and {missing.Count - MissingListLimit} more");
        }

        throw new InvalidOperationException(
            $"{missing.Count} mod bundle(s) could not be found. Start the game through the SPT launcher so it can fetch them.");
    }
}
