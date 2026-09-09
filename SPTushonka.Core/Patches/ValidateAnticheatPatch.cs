using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using Task = Il2CppSystem.Threading.Tasks.Task;

namespace SPTushonka.Core.Patches;

public class ValidateAnticheatPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.RunValidateAnticheat));
    }

    [PatchPrefix]
    private static bool PatchPrefix(ref Task __result)
    {
        __result = Task.CompletedTask;

        return false; // Skip original
    }
}
