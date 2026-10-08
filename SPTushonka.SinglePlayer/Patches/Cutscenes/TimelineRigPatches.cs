using System.Collections.Generic;
using System.Reflection;
using CommonAssets.Scripts.Cutscenes;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

/// <summary>
///     Keep a cutscene's rig hidden until its timeline is about to play. The client controller turns the rig on before
///     it waits out the look reset and fade frames, and a rig the timeline has not evaluated yet stands in bind pose.
/// </summary>
public static class TimelineRigPatches
{
    /// <summary>
    ///     Rig objects switched off by <see cref="HoldPatch"/>, waiting for <see cref="RevealPatch"/>
    /// </summary>
    private static readonly List<GameObject> Held = [];

    /// <summary>
    ///     Switch the rig off after the last start action. The start actions run first because they may still need it active
    /// </summary>
    public class HoldPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesClientController), "InitAction");
        }

        [PatchPostfix]
        public static void Postfix(CutscenesClientController __instance, string actionId)
        {
            var data = __instance._currentTimelineData;
            var startActions = data.startActionsId;
            if (startActions == null || startActions.Length == 0 || startActions[startActions.Length - 1] != actionId)
            {
                return;
            }

            Held.Clear();
            var objects = data.timelineObjects;
            for (var i = 0; objects != null && i < objects.Count; i++)
            {
                var rig = objects[i];
                if (rig != null && rig.activeSelf)
                {
                    rig.SetActive(false);
                    Held.Add(rig);
                }
            }
        }
    }

    /// <summary>
    ///     Switch the held rig back on once the signal tracks are set up, which is the last step before the director plays
    /// </summary>
    public class RevealPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesClientController), "SetupSignalTracks");
        }

        [PatchPostfix]
        public static void Postfix()
        {
            foreach (var rig in Held)
            {
                if (rig != null)
                {
                    rig.SetActive(true);
                }
            }

            Held.Clear();
        }
    }
}
