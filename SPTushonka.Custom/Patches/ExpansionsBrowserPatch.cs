using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

public class ExpansionsOpenPatch : ModulePatch
{
    private static bool _applied;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(BrowserUtils), nameof(BrowserUtils.OpenExpansionsPageAsync));
    }

    [PatchPrefix]
    public static void PatchPrefix()
    {
        if (_applied)
        {
            return;
        }

        _applied = true;
        Vuplex.WebView.Web.SetIgnoreCertificateErrors(true);
    }
}
