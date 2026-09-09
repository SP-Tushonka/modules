using System.Reflection;
using System.Text.Json;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Common.Http;
using SPTushonka.Custom.Models;
using SPTushonka.Reflection.Patching;
using EftVersion = EFT.Version;

namespace SPTushonka.Custom.Patches;

// The label is a format string fed from the text field beside it, so the key is reduced to a bare
// placeholder and the version put in the field, same shape as the 4.1x patch. 1.1.5 creates the
// version before the preloader exists, so the label is also applied when the preloader wakes.
public class VersionLabelPatch : ModulePatch
{
    private static string _versionLabel;

    public static void ApplyLabel(PreloaderUI preloader)
    {
        if (preloader == null || preloader._alphaVersionLabel == null || string.IsNullOrEmpty(_versionLabel))
        {
            return;
        }

        preloader._alphaVersionLabel.LocalizationKey = "{0}";
        preloader._alphaVersionText = _versionLabel;
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EftVersion), nameof(EftVersion.Create));
    }

    [PatchPostfix]
    public static void PatchPostfix(EftVersion __result)
    {
        if (string.IsNullOrEmpty(_versionLabel))
        {
            var json = RequestHandler.GetJson("/singleplayer/settings/version");
            var server = JsonSerializer.Deserialize<VersionResponse>(json).Version;
            _versionLabel = $"{server} | {__result.Major}";
            Logger.LogInfo($"Version label: {_versionLabel}");
        }

        ApplyLabel(PreloaderUI.Instance);
        __result.Major = _versionLabel;
    }
}

public class PreloaderVersionLabelPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.Awake));
    }

    [PatchPostfix]
    public static void PatchPostfix(PreloaderUI __instance)
    {
        VersionLabelPatch.ApplyLabel(__instance);
    }
}
