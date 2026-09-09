using System;
using System.Reflection;
using Comfort.Common;
using CommonAssets.Scripts.Cutscenes;
using EFT;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// The bank's rig search waits for the game status Runned before it hides every timeline's rig
// by name. The offline game passes through Runned within a single frame, so a coroutine that
// polls once per frame never sees it and nothing is hidden. This stands in for the coroutine's
// MoveNext with the same body and accepts every running state instead.
public class TimelineBankPatches : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(MapTimelinesBank._SearchObjectsCoroutine_d__4), nameof(MapTimelinesBank._SearchObjectsCoroutine_d__4.MoveNext));
    }

    [PatchPrefix]
    public static bool Prefix(MapTimelinesBank._SearchObjectsCoroutine_d__4 __instance, ref bool __result)
    {
        __instance.__2__current = null;
        var game = Singleton<AbstractGame>.Instance;
        if (game == null || !IsRunning(game.Status))
        {
            __result = true;
            return false;
        }

        try
        {
            Search(__instance.__4__this);
        }
        catch (Exception ex)
        {
            Logger.LogError($"timeline bank search failed: {ex}");
        }

        __result = false;
        return false;
    }

    private static bool IsRunning(GameStatus status)
    {
        return status == GameStatus.Runned || status == GameStatus.Starting || status == GameStatus.Started;
    }

    // Each timeline's rig is looked up inside that timeline's own scene, since the names are
    // shared between the cutscene scenes and the ending scenes nest a copy under their own root.
    private static void Search(MapTimelinesBank bank)
    {
        var timelines = bank == null ? null : bank.timelines;
        for (var i = 0; timelines != null && i < timelines.Count; i++)
        {
            var data = timelines[i];
            var names = data.timelineObjectsName;
            var objects = data.timelineObjects;
            if (objects == null)
            {
                objects = new Il2CppSystem.Collections.Generic.List<GameObject>();
                data.timelineObjects = objects;
                timelines[i] = data;
            }

            var hidden = 0;
            for (var n = 0; names != null && n < names.Count; n++)
            {
                var found = FindInScene(data.sceneName, names[n]) ?? GameObject.Find(names[n]);
                if (found == null)
                {
                    continue;
                }

                if (!objects.Contains(found))
                {
                    objects.Add(found);
                }

                found.SetActive(false);
                hidden++;
            }

            Logger.LogInfo($"timeline bank: rig for '{data.cutsceneId}' hidden ({hidden})");
        }
    }

    private static GameObject FindInScene(string sceneName, string name)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return null;
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child.gameObject;
                }
            }
        }

        return null;
    }
}
