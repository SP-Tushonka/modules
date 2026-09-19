using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.MemoryImprovements;

// Bots ask every frame for their weapon's underbarrel launcher, and the game flattens all of the weapon's
// slots through LINQ to answer. This walks the same slots in the same order without allocating.
public class UnderbarrelWeaponPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Weapon), nameof(Weapon.GetUnderbarrelWeapon));
    }

    [PatchPrefix]
    public static bool PatchPrefix(Weapon __instance, ref Launcher __result)
    {
        __result = FindLauncher(__instance);
        return false;
    }

    // Flatten yields every slot of an item before descending into any of them
    private static Launcher FindLauncher(CompoundItem item)
    {
        var slots = item.Slots;
        if (slots == null)
        {
            return null;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var launcher = slots[i].ContainedItem?.TryCast<Launcher>();
            if (launcher != null)
            {
                return launcher;
            }
        }

        for (var i = 0; i < slots.Length; i++)
        {
            var compound = slots[i].ContainedItem?.TryCast<CompoundItem>();
            if (compound == null)
            {
                continue;
            }

            var launcher = FindLauncher(compound);
            if (launcher != null)
            {
                return launcher;
            }
        }

        return null;
    }
}
