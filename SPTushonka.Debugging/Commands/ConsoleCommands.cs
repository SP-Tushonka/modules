using System;
using System.Reflection;
using EFT.UI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using SPTushonka.Debugging.Scripts;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Debugging.Commands;

// Registers every debugging command once the game console exists.
public class ConsoleCommands : ModulePatch
{
    private static bool _registered;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ConsoleScreen), nameof(ConsoleScreen.InitConsole));
    }

    [PatchPostfix]
    public static void Postfix()
    {
        if (_registered || ConsoleScreen.Processor == null)
        {
            return;
        }

        _registered = true;
        Register("god", "Toggle invulnerability for the main player", GodModeCommand.Toggle);
        Register("noclip", "Toggle free flight through geometry for the main player", NoclipCommand.Toggle);
        Register("botmon", "Toggle the bot monitor overlay, alive bots per zone with role, difficulty and distance", BotMonitor.Toggle);
        TeleportCommands.Register();
        TeleportCommands.Register("debug_extract", "End the raid with an exit status: Survived, Killed, Left, Runner, MissingInAction, Transit", DebugExtractCommand.Run);
        Logger.LogInfo("console: god, noclip, botmon, debug_extract and teleport commands registered");
    }

    private static void Register(string name, string description, Action handler)
    {
        ConsoleScreen.Processor.RegisterCommand(name, DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(handler), description);
    }
}
