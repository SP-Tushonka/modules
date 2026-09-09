using BepInEx;
using BepInEx.Unity.IL2CPP;
using SPTushonka.SinglePlayer.Patches.Cutscenes;
using SPTushonka.SinglePlayer.Patches.MainMenu;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer;

[BepInPlugin("sptushonka.singleplayer", "SPTushonka SinglePlayer", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        new DisableUseBSGServersCheckbox().Enable();
        ForceRaidModeToLocalPatches.Patch();
        new DisableMatchmakerPlayerPreviewButtonsPatch().Enable();
        ReadyButtonPatches.Patch();
        new TimelineBankPatches().Enable();
        new TriggerCutscenePatches().Enable();
        FinalMissionDirectorPatches.Patch();
        ServerActionPatches.Patch();
        CutsceneUpdateGuardPatches.Patch();
        BlackScreenShowPatch.Patch();

        ModulePatch.Summarise("SinglePlayer");
    }
}
