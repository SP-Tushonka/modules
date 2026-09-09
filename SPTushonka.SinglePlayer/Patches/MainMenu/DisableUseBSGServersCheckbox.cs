using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer.Patches.MainMenu;

/// <summary>
/// Removes BSG's checkmark to use BSG servers instead of local hosted
/// Also Sets checkmark to false.if checkmark is somehow enabled it will default to false (local raid only)
/// </summary>
public class DisableUseBSGServersCheckbox : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LocationInfoPanel), nameof(LocationInfoPanel.HandlePveServerModeBlock));
    }

    [PatchPostfix]
    public static void PatchPostfix(LocationInfoPanel __instance)
    {
        var onlineModeToggle = __instance._onlineModeToggle;
        onlineModeToggle.isOn = false;
        onlineModeToggle.transform.parent.gameObject.SetActive(false);
    }
}
