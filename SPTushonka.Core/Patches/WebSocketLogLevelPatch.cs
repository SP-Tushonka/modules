using System.Reflection;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using WebSocketSharp;
using WsLogger = WebSocketSharp.Logger;

namespace SPTushonka.Core.Patches;

// The notifier's connect routine sets its websocket-sharp logger to Trace unconditionally, and the
// output handler writes straight to stdout, so every ping and pong lands in the console once a
// second. The level is held at Warn instead.
public class WebSocketLogLevelPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertySetter(typeof(WsLogger), nameof(WsLogger.Level));
    }

    [PatchPrefix]
    public static void PatchPrefix(ref LogLevel __0)
    {
        if (__0 < LogLevel.Warn)
        {
            __0 = LogLevel.Warn;
        }
    }
}
