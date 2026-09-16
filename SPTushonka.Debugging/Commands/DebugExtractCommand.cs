using System;
using Comfort.Common;
using EFT;
using EFT.UI;

namespace SPTushonka.Debugging.Commands;

// "debug_extract <status>" ends the raid from the console with the given ExitStatus
// (Survived, Killed, Left, Runner, MissingInAction, Transit) through the first extraction point.
public static class DebugExtractCommand
{
    public static void Run(string text)
    {
        if (!Enum.TryParse(text, true, out ExitStatus status))
        {
            ConsoleScreen.LogError($"Unknown exit status '{text}', use one of {string.Join(", ", Enum.GetNames(typeof(ExitStatus)))}");
            return;
        }

        var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
        var world = Singleton<GameWorld>.Instance;
        if (game == null || world == null || world.MainPlayer == null)
        {
            ConsoleScreen.LogError("No local raid running");
            return;
        }

        var exits = world.ExfiltrationController?.ExfiltrationPoints;
        var exitName = exits == null || exits.Length == 0 ? "" : exits[0].Settings.Name;
        ConsoleScreen.Log($"Ending raid as {status} through '{exitName}'");
        game.Stop(world.MainPlayer.ProfileId, status, exitName, 0f);
    }
}
