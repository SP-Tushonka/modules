using System;
using System.IO;
using System.Reflection;
using EFT.Settings;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Redirect the settings data to save into the SPT folder, not app data
/// </summary>
public class SaveSettingsLocallyPatch : ModulePatch
{
    private static readonly string _sptPath = Path.Combine(Environment.CurrentDirectory, "SPT_Runtime", "user", "sptSettings");

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SettingsManager), nameof(SettingsManager.InstantiateSingleton));
    }

    [PatchPrefix]
    public static void PatchPrefix()
    {
        if (!Directory.Exists(_sptPath))
        {
            Directory.CreateDirectory(_sptPath);
        }

        SettingsManager.OldSettingsFolderPath = _sptPath;
        SettingsManager.SettingsFolderPath = _sptPath;
    }
}
