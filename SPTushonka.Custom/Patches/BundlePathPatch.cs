using System;
using System.Reflection;
using HarmonyLib;
using SPTushonka.Common.Utils;
using SPTushonka.Custom.Models;
using SPTushonka.Custom.Utils;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.Custom.Patches;

// The game inlines both the EasyBundle constructor and LoadingCoroutine, so neither can be
// detoured and _path cannot be rewritten the way it is on the managed runtime. The load call is
// the first point the path is observable.
public static class BundlePathPatches
{
    private static readonly BepInEx.Logging.ManualLogSource Log =
        BepInEx.Logging.Logger.CreateLogSource("SPTushonka");

    public static void Patch()
    {
        new LoadFromFilePatch().Enable();
        new LoadFromFileAsyncPatch().Enable();
        new LoadFromFileAsyncInternalPatch().Enable();
    }

    private static void TryRedirect(ref string path)
    {
        if (string.IsNullOrEmpty(path) || BundleManager.Bundles.Count == 0)
        {
            return;
        }

        var normalised = path.Replace('\\', '/');

        foreach (BundleItem bundle in BundleManager.Bundles.Values)
        {
            if (!normalised.EndsWith(bundle.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // set path to either cache (HTTP) or mod (local)
            var filepath = BundleManager.GetBundleFilePath(bundle);

            if (VFS.Exists(filepath))
            {
                Log.LogInfo($"bundle: loading '{bundle.FileName}' from '{filepath}'");
                path = filepath;
            }

            return;
        }
    }

    public class LoadFromFilePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AssetBundle), nameof(AssetBundle.LoadFromFile), new[] { typeof(string) });
        }

        [PatchPrefix]
        private static void PatchPrefix(ref string __0)
        {
            TryRedirect(ref __0);
        }
    }

    public class LoadFromFileAsyncPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AssetBundle), nameof(AssetBundle.LoadFromFileAsync), new[] { typeof(string) });
        }

        [PatchPrefix]
        private static void PatchPrefix(ref string __0)
        {
            TryRedirect(ref __0);
        }
    }

    public class LoadFromFileAsyncInternalPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(AssetBundle), "LoadFromFileAsync_Internal",
                new[] { typeof(string), typeof(uint), typeof(ulong) });
        }

        [PatchPrefix]
        private static void PatchPrefix(ref string __0)
        {
            TryRedirect(ref __0);
        }
    }
}
