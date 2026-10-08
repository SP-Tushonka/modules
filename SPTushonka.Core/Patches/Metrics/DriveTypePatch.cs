using System;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core.Patches.Metrics;

internal sealed class DriveTypePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SystemInfoExtension), nameof(SystemInfoExtension.GetDriveType), Type.EmptyTypes);
    }

    [PatchPrefix]
    private static bool PatchPrefix(ref EDriveType __result)
    {
        __result = EDriveType.Unknown;
        return false; // Skip original
    }
}
