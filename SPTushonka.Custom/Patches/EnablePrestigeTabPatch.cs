using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Show the Prestige tab in PvE. The client shows it, and makes it clickable, only for the first game mode.
/// </summary>
public class EnablePrestigeTabPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(InventoryScreen), nameof(InventoryScreen.ConfigurePrestigeTab));
    }

    [PatchPostfix]
    public static void PatchPostfix(InventoryScreen __instance)
    {
        if (__instance._tabDictionary.TryGetValue(EInventoryTab.Prestige, out var prestigeTab))
        {
            prestigeTab.gameObject.SetActive(true);
            prestigeTab.SetInteractable(true);
        }
    }
}
