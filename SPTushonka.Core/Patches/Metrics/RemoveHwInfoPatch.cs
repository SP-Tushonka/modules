using System.Reflection;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core.Patches.Metrics;

internal sealed class RemoveHwInfoPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(HWEcho), nameof(HWEcho.HWEcho_Json));
    }

    [PatchPrefix]
    private static bool PatchPrefix()
    {
        return false; // Skip original
    }
}
