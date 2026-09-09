using System.Reflection;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

/// <summary>
/// The session only asks the server for prestige settings in the Regular game mode. The globals
/// loader queues all of its requests before it first yields, so the mode is flipped for that stretch.
/// </summary>
public class LoadPrestigeSettingsPatch : ModulePatch
{
    private static EGameMode _mode;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TarkovApplication.GlobalsDataLoader), nameof(TarkovApplication.GlobalsDataLoader.Load));
    }

    [PatchPrefix]
    public static void PatchPrefix(IEftSession __0)
    {
        var descriptor = Descriptor(__0);
        if (descriptor == null)
        {
            return;
        }

        _mode = descriptor.GameMode;
        descriptor._GameMode_k__BackingField = EGameMode.Regular;
    }

    [PatchPostfix]
    public static void PatchPostfix(IEftSession __0)
    {
        var descriptor = Descriptor(__0);
        if (descriptor != null)
        {
            descriptor._GameMode_k__BackingField = _mode;
        }
    }

    private static GameModeDescriptor Descriptor(IEftSession session)
    {
        return session?.TryCast<ClientBackendSession>()?._GameModeDescriptor_k__BackingField;
    }
}
