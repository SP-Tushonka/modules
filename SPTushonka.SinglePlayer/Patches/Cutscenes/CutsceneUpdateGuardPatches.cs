using System.Reflection;
using CommonAssets.Scripts.Cutscenes;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// Both cutscene controllers end up subscribed to the world's late update twice offline, so the
// server's fixed clock ran at double speed. Only the first update in a frame is let through.
public static class CutsceneUpdateGuardPatches
{
    public static void Patch()
    {
        new ServerPatch().Enable();
        new ClientPatch().Enable();
    }

    public class ServerPatch : ModulePatch
    {
        private static int _lastFrame = -1;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesServerController), nameof(CutscenesServerController.OnUpdate));
        }

        [PatchPrefix]
        public static bool Prefix()
        {
            if (_lastFrame == Time.frameCount)
            {
                return false;
            }

            _lastFrame = Time.frameCount;
            return true;
        }
    }

    public class ClientPatch : ModulePatch
    {
        private static int _lastFrame = -1;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesClientController), nameof(CutscenesClientController.OnUpdate));
        }

        [PatchPrefix]
        public static bool Prefix()
        {
            if (_lastFrame == Time.frameCount)
            {
                return false;
            }

            _lastFrame = Time.frameCount;
            return true;
        }
    }
}
