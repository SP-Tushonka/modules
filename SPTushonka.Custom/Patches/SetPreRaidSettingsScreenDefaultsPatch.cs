using System.Reflection;
using System.Text.Json;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPTushonka.Common.Http;
using SPTushonka.Custom.Models;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// Seed the offline raid screen with the server's raid defaults and unlock its settings button
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

    [PatchPrefix]
    public static void PatchPrefix(MatchmakerOfflineRaidScreen __instance, MatchmakerOfflineRaidScreen.OfflineRaidScreenController __0)
    {
        // Default checkbox to be unchecked so we're in PvE
        __instance._offlineModeToggle.isOn = false;

        var json = RequestHandler.GetJson("/singleplayer/settings/raid/menu");
        var defaults = JsonSerializer.Deserialize<DefaultRaidSettings>(json, DefaultRaidSettings.SerializerOptions);
        if (defaults == null)
        {
            return;
        }

        // The settings are value types behind interop properties, so each one is copied out, changed and written back
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
