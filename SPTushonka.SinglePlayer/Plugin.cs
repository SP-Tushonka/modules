using BepInEx;
using BepInEx.Unity.IL2CPP;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer;

[BepInPlugin("sptushonka.singleplayer", "SPTushonka SinglePlayer", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        new PatchManager(this, true).EnablePatches();

        ModulePatch.Summarise("SinglePlayer");
    }
}
