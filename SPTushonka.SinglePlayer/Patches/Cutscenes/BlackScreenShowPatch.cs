using System.Reflection;
using CommonAssets.Scripts.Cutscenes;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// The preloader's fade to black runs until the image alpha passes one, which the image never
// reports, so the callback that ends a skipped cutscene never fires and the screen stays black
// until the timeline runs out on its own. While the client handles a skip, a fade to full black
// is applied at once instead. Every other caller keeps the original fade.
public static class BlackScreenShowPatch
{
    private static bool _skipping;

    public static void Patch()
    {
        new SkipPatch().Enable();
        new ShowPatch().Enable();
    }

    public class SkipPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesClientController), nameof(CutscenesClientController.OnSkipEvent));
        }

        [PatchPrefix]
        public static void Prefix()
        {
            _skipping = true;
        }

        [PatchPostfix]
        public static void Postfix()
        {
            _skipping = false;
        }
    }

    public class ShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(PreloaderUI), nameof(PreloaderUI.StartBlackScreenShow));
        }

        [PatchPrefix]
        public static bool Prefix(PreloaderUI __instance, float to, Il2CppSystem.Action callback)
        {
            if (!_skipping || to < 1f)
            {
                return true;
            }

            __instance.SetBlackImageAlpha(1f);
            callback?.Invoke();
            return false;
        }
    }
}
