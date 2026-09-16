using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using EFT.Settings;
using Il2CppInterop.Runtime;
using SPTushonka.Custom.Patches;
using SPTushonka.Custom.Utils;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom;

[BepInPlugin("sptushonka.custom", "SPTushonka Custom", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    // Redirect the game's icon cache to the SPT_Runtime directory
    private static void RedirectIconCache()
    {
        var path = System.IO.Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptappdata");
        System.IO.Directory.CreateDirectory(path);
        ItemIconCache.Path = path;
    }

    // Redirect the game's settings folder to the SPT_Runtime directory
    private void RedirectSettingsFolder()
    {
        var path = System.IO.Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptSettings");
        System.IO.Directory.CreateDirectory(path);
        IL2CPP.il2cpp_runtime_class_init(Il2CppClassPointerStore<SettingsManager>.NativeClassPtr);
        SettingsManager.OldSettingsFolderPath = path;
        SettingsManager.SettingsFolderPath = path;

        Log.LogInfo($"settings: folder set to {SettingsManager.SettingsFolderPath}");
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
        RedirectSettingsFolder();
        RedirectIconCache();
        SaveRegistryLocallyPatches.Enable();
        new RedirectClientImageRequestsPatch().Enable();
        new EnablePrestigeTabPatch().Enable();
        new LoadPrestigeSettingsPatch().Enable();
        new SetPreRaidSettingsScreenDefaultsPatch().Enable();
        new BundleManifestPatch().Enable();
        BundlePathPatches.Patch();
        new ExpansionsOpenPatch().Enable();

        ModulePatch.Summarise("Custom");
    }
}
