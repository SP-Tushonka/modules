using System.Reflection;
using System.Text.Json;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPTushonka.Common.Http;
using SPTushonka.Custom.Models;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

/// <summary>
///     Seed the offline raid screen with the server's raid defaults, reset its raid mode and unlock its settings
/// </summary>
public class SetPreRaidSettingsScreenDefaultsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(MatchmakerOfflineRaidScreen),
            nameof(MatchmakerOfflineRaidScreen.Show),
            [typeof(MatchmakerOfflineRaidScreen.OfflineRaidScreenController)]
        );
    }

    /// <summary>
    ///     Untick the offline toggle, reset the raid mode and apply the server's raid defaults before the screen shows
    /// </summary>
    /// <param name="__instance">Offline raid screen</param>
    /// <param name="__0">Screen controller holding the raid settings</param>
    [PatchPrefix]
    public static void PatchPrefix(MatchmakerOfflineRaidScreen __instance, MatchmakerOfflineRaidScreen.OfflineRaidScreenController __0)
    {
        // An unticked offline toggle is PvE
        __instance._offlineModeToggle.isOn = false;

        // ForceRaidModeToLocalPatches leaves the last raid's Local mode on both settings. The toggle only sets the mode
        // when its value changes, and a Local mode skips the insurance screen.
        __0.RaidSettings.RaidMode = ERaidMode.Online;
        __0.OfflineRaidSettings.RaidMode = ERaidMode.Online;

        var json = RequestHandler.GetJson("/singleplayer/settings/raid/menu");
        var defaults = JsonSerializer.Deserialize<DefaultRaidSettings>(json, DefaultRaidSettings.SerializerOptions);
        if (defaults == null)
        {
            return;
        }

        // These are struct fields exposed as properties, so each one is copied out, changed and written back
        var settings = __0.OfflineRaidSettings;

        var bots = settings.BotSettings;
        bots.BotAmount = defaults.AiAmount;
        bots.IsScavWars = false;
        settings.BotSettings = bots;

        var waves = settings.WavesSettings;
        waves.BotAmount = defaults.AiAmount;
        waves.BotDifficulty = defaults.AiDifficulty;
        waves.IsBosses = defaults.BossEnabled;
        waves.IsTaggedAndCursed = defaults.TaggedAndCursed;
        settings.WavesSettings = waves;

        var weather = settings.TimeAndWeatherSettings;
        weather.IsRandomWeather = defaults.RandomWeather;
        weather.IsRandomTime = defaults.RandomTime;
        settings.TimeAndWeatherSettings = weather;
    }

    /// <summary>
    ///     Hide the online blocker and the warning panel, and enable the settings button
    /// </summary>
    /// <param name="__instance">Offline raid screen</param>
    [PatchPostfix]
    public static void PatchPostfix(MatchmakerOfflineRaidScreen __instance)
    {
        __instance._onlineBlocker.gameObject.SetActive(false);
        __instance._changeSettingsButton.Interactable = true;

        var warning = __instance.transform.Find("Content/WarningPanelHorLayout");
        if (warning != null)
        {
            warning.gameObject.SetActive(false);
        }
    }
}
