using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using Task = Il2CppSystem.Threading.Tasks.Task;

namespace SPTushonka.Core.Patches;

public class BattlEyePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(AnticheatValidationOperation), nameof(AnticheatValidationOperation.RunValidation));
    }

    [PatchPrefix]
    private static bool PatchPrefix(AnticheatValidationOperation __instance, ref Task __result)
    {
        __instance.Succeed = true;
        __result = Task.CompletedTask;
        return false; // Skip original
    }
}
