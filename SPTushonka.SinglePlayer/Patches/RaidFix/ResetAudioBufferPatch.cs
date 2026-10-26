using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.SinglePlayer.Patches.RaidFix;

/// <summary>
/// Prevents <see cref="AudioUtils.ResetAudioBuffer"/> from calling <c>AudioSettings.Reset</c>
/// (<c>AudioSettings.SetConfiguration_Injected</c>), which tears down and reinitializes Unity's FMOD
/// system while MetaXRAudio spatializer DSP instances are active during local raid startup, causing
/// an access violation (<c>0xC0000005</c>) inside <c>UnityPlayer.dll</c>'s TLSF heap allocator.
/// </summary>
public class ResetAudioBufferPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(AudioUtils), nameof(AudioUtils.ResetAudioBuffer));
    }

    [PatchPrefix]
    public static bool PatchPrefix()
    {
        return false;
    }
}
