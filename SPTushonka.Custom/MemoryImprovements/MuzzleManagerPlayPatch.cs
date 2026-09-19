using System.Reflection;
using Comfort.Common;
using Il2CppSystems.Effects;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.Custom.MemoryImprovements;

// Every muzzle flash looks up its particle system with a closure and Enumerable.First. Found by ifp for Fika.
public class MuzzleManagerPlayPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(MuzzleManager).GetMethod("IMuzzleParticlePivot.Play", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    }

    [PatchPrefix]
    public static bool PatchPrefix(EMuzzleParticlePivot pivot, Transform pTransform)
    {
        var effects = Singleton<Effects>.Instance;
        var commonSystems = effects.MuzzleEffect.CommonSystems;
        effects.TryAddToMBOITParticleManager(commonSystems);
        for (var i = 0; i < commonSystems.Length; i++)
        {
            var container = commonSystems[i];
            if (container.Pivot != pivot)
            {
                continue;
            }

            var particles = container.RootParticleSystem;
            particles.transform.SetPositionAndRotation(pTransform.position, pTransform.rotation);
            particles.Stop(true);
            particles.Play(true);
            break;
        }

        return false;
    }
}
