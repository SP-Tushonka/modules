using BepInEx;
using BepInEx.Unity.IL2CPP;
using SPTushonka.Debugging.Commands;
using SPTushonka.Debugging.Scripts;
using SPTushonka.Reflection.Commands;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Debugging;

[BepInPlugin("com.sptushonka.debugging", "SPTushonka Debugging", PluginInfo.Version)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        ConsoleCommandRegistry.RegisterCommandGroup(typeof(GodModeCommand));
        ConsoleCommandRegistry.RegisterCommandGroup(typeof(NoclipCommand));
        ConsoleCommandRegistry.RegisterCommandGroup(typeof(BotMonitor));
        ConsoleCommandRegistry.RegisterCommandGroup(typeof(TeleportCommands));
        ConsoleCommandRegistry.RegisterCommandGroup(typeof(DebugExtractCommand));

        new PatchManager(this, true).EnablePatches();

        ModulePatch.Summarise("Debugging");
    }
}
