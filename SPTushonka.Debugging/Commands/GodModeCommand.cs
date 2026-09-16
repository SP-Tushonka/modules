using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Debugging.Commands;

public static class GodModeCommand
{
    private static bool Enabled;

    public static void Patch()
    {
        new DamagePatch().Enable();
        new KillPatch().Enable();
    }

    private static bool IsMainPlayer(ActiveHealthController controller)
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        return player != null && player.ActiveHealthController == controller;
    }

    public static void Toggle()
    {
        Enabled = !Enabled;
        ConsoleScreen.Log($"God mode {(Enabled ? "on" : "off")}");
    }

    public class DamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));
        }

        [PatchPrefix]
        public static bool Prefix(ActiveHealthController __instance, ref float __result)
        {
            if (!Enabled || !IsMainPlayer(__instance))
            {
                return true;
            }

            __result = 0f;
            return false;
        }
    }

    public class KillPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill));
        }

        [PatchPrefix]
        public static bool Prefix(ActiveHealthController __instance)
        {
            return !Enabled || !IsMainPlayer(__instance);
        }
    }
}
