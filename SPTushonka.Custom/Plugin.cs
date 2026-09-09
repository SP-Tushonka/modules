using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using SPTushonka.Custom.Patches;
using SPTushonka.Custom.Utils;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom;

[BepInPlugin("sptushonka.custom", "SPTushonka Custom", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    // The item icon cache is a static path set once in the type initialiser, so it only needs
    // overwriting before the first icon is rendered. Same folder as the redirected images.
    private static void RedirectIconCache()
    {
        var path = System.IO.Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptappdata");
        System.IO.Directory.CreateDirectory(path);
        ItemIconCache.Path = path;
    }

    public override void Load()
    {
        // EasyAssets reads the manifest during startup, so the server list has to be in hand
        // before any of the patches below can add to it
        try
        {
            BundleManager.DownloadManifest();
        }
        catch (Exception ex)
        {
            Log.LogError($"could not read the server bundle manifest: {ex.Message}");
        }

        new VersionLabelPatch().Enable();
        new PreloaderVersionLabelPatch().Enable();
        new SaveSettingsLocallyPatch().Enable();
        RedirectIconCache();
        new SaveRegistryLocallyPatches().Enable();
        new RedirectClientImageRequestsPatch().Enable();
        new EnablePrestigeTabPatch().Enable();
        new LoadPrestigeSettingsPatch().Enable();
        new SetPreRaidSettingsScreenDefaultsPatch().Enable();
        new BundleManifestPatch().Enable();
        BundlePathPatches.Patch();
        new ExpansionsOpenPatch().Enable();

        if (AutoDeployPatches.Enabled)
        {
            AutoDeployPatches.Patch();
        }

        ModulePatch.Summarise("Custom");
    }
}
