using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SPTushonka.Core.Models;
using SPTushonka.Core.Patches;

using SPTushonka.Reflection.Patching;

namespace SPTushonka.Core;

[BepInPlugin("sptushonka.core", "SPTushonka Core", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<FakeCertificateHandler>();
        FilesCheckerStubs.Apply(Log);

        new PatchManager(this, true).EnablePatches();

        ModulePatch.Summarise("Core");
    }
}
