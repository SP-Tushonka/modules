using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core.Patches.Metrics;

internal sealed class SendLoadMetricsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ClientBackendSession), nameof(ClientBackendSession.SendLoadMetrics));
    }

    [PatchPrefix]
    private static bool PatchPrefix()
    {
        return false; // Skip original
    }
}
