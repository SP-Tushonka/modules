using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer.Patches.MainMenu;

/// <summary>
/// This patch ensures that the gamemode is always <see cref="ERaidMode.Local"/> and that IsPveOffline is always true when starting a game<br/>
/// This prevents a bug where the gameworld is instantiated as an online world
/// One outcome of not having this patch is grenades do not explode after being thrown
/// </summary>
public static class ForceRaidModeToLocalPatches
{
    private static void ForceLocal(TarkovApplication application)
    {
        var raidSettings = application._raidSettings;
        raidSettings.RaidMode = ERaidMode.Local;
        raidSettings.IsPveOffline = true;
    }

    public class OnMatchingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LocalGameMatching));
        }

        [PatchPrefix]
        public static void Prefix(TarkovApplication __instance)
        {
            ForceLocal(__instance);
        }
    }

    public class OnGamePreparePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.GamePrepare));
        }

        [PatchPrefix]
        public static void Prefix(TarkovApplication __instance)
        {
            ForceLocal(__instance);
        }
    }
}
