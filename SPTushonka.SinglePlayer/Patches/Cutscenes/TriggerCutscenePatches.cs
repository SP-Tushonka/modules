using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// A scene starts a cutscene from a trigger through HandlerStartCutscene. The handler subscribes
// its trigger on the local emitter but its OnTrigger is a server-only stub, so offline the
// trigger fires and nothing follows. This subscribes the same trigger and starts the cutscene
// on the server controller for every human player.
public class TriggerCutscenePatches : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(HandlerStartCutscene), "Start");
    }

    [PatchPostfix]
    public static void Postfix(HandlerStartCutscene __instance)
    {
        var triggerId = __instance._triggerId;
        var cutsceneId = __instance._cutsceneId;
        var emitter = TriggersEmitter.Instance;
        if (emitter == null || string.IsNullOrEmpty(triggerId) || string.IsNullOrEmpty(cutsceneId))
        {
            return;
        }

        Il2CppSystem.Action<TriggerEvent> handler = new Action<TriggerEvent>(_ => Start(cutsceneId));
        emitter.Subscribe(triggerId, handler);
        Logger.LogInfo($"trigger cutscene: '{triggerId}' starts '{cutsceneId}'");
    }

    private static void Start(string cutsceneId)
    {
        var world = Singleton<GameWorld>.Instance;
        var server = world == null ? null : world.CutscenesServerController;
        if (server == null || world.MainPlayer == null)
        {
            return;
        }

        var ids = new Il2CppSystem.Collections.Generic.List<int>();
        ids.Add(world.MainPlayer.PlayerId);
        var process = server.StartCutscene(cutsceneId, ids, false, false, 0f);
        Logger.LogInfo($"trigger cutscene: '{cutsceneId}' {(process == null ? "refused" : "started")}");
    }
}

// Logs what a quest gate in the trigger chain asks for, so a cutscene that never starts can be
// traced to the quest it wants.
public class QuestGatePatches : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(HandlerEnsureAllPlayersHasQuest), "Start");
    }

    [PatchPostfix]
    public static void Postfix(HandlerEnsureAllPlayersHasQuest __instance)
    {
        Logger.LogInfo($"quest gate: '{__instance._triggerId}' needs quest '{__instance._questTemplateId}', then '{__instance._outputAllPlayersHasQuestTriggerId}' else '{__instance._outputAnyPlayerHasNoQuestTriggerId}'");
    }
}

// A rally zone emits its trigger from the all-players check that runs right after its own zone
// update. Offline that update is dispatched synchronously and marks the zone triggered first, so the
// check never emits. The zone's methods are called directly by other game code and cannot be hooked
// on this runtime, but the zone still tracks the local player natively. This watches that flag and
// emits once, which is all the check would do for a single human player.
public static class RallyZonePatches
{
    private static readonly List<TriggerRallyZone> _zones = new();
    private static readonly HashSet<TriggerRallyZone> _emitted = new();
    private static int _frames;

    public static void Patch()
    {
        new StartPatch().Enable();
        new TickPatch().Enable();
    }

    public class StartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        public static void Postfix()
        {
            _zones.Clear();
            _emitted.Clear();
            _frames = 0;
            foreach (var zone in UnityEngine.Object.FindObjectsOfType<TriggerRallyZone>(true))
            {
                _zones.Add(zone);
            }

            if (_zones.Count > 0)
            {
                Logger.LogInfo($"rally zones: {_zones.Count} watched");
            }
        }
    }

    public class TickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), "Update");
        }

        [PatchPostfix]
        public static void Postfix()
        {
            if (_zones.Count == 0 || ++_frames % 15 != 0)
            {
                return;
            }

            var world = Singleton<GameWorld>.Instance;
            var player = world == null ? null : world.MainPlayer;
            if (player == null || world.TriggersEmitter == null)
            {
                return;
            }

            try
            {
                foreach (var zone in _zones)
                {
                    if (zone == null || _emitted.Contains(zone) || !zone._localPlayerInZone || string.IsNullOrEmpty(zone._triggerId))
                    {
                        continue;
                    }

                    _emitted.Add(zone);
                    zone._wasTriggered = true;
                    world.TriggersEmitter.Emit(zone._triggerId, player.PlayerId);
                    Logger.LogInfo($"rally zone '{zone._triggerId}' emitted");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"rally zone watch failed: {ex.Message}");
                _zones.Clear();
            }
        }
    }
}
