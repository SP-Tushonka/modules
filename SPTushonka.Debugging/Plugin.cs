using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using SPTushonka.Debugging.Commands;
using SPTushonka.Debugging.Patches;
using SPTushonka.Debugging.Scripts;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Debugging;

[BepInPlugin("sptushonka.debugging", "SPTushonka Debugging", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        ClassInjector.RegisterTypeInIl2Cpp<BotMonitor>();

        new ConsoleCommands().Enable();
        new BsgLogPatch().Enable();
        GodModeCommand.Patch();
        NoclipCommand.Patch();

        ModulePatch.Summarise("Debugging");
    }
}
