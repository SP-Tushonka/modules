using BepInEx;
using BepInEx.Unity.IL2CPP;
using SPTushonka.Core.Models;
using SPTushonka.Core.Patches;
using SPTushonka.Reflection.Il2Cpp;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core;

[BepInPlugin("sptushonka.core", "SPTushonka Core", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        MainThread.Install(this);
        FilesCheckerStubs.Apply(Log);

        new PatchManager(this, true).EnablePatches();

        ModulePatch.Summarise("Core");
    }
}
