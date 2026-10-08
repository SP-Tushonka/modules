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

/// <summary>
///     Start the cutscene a scene's <see cref="HandlerStartCutscene"/> asks for when its trigger fires. The handler
///     subscribes to its trigger, but its OnTrigger is empty in the client build, so nothing follows offline.
/// </summary>
public class TriggerCutscenePatches : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(HandlerStartCutscene), "Start");
    }

    /// <summary>
    ///     Subscribe the handler's trigger to a start of its cutscene
    /// </summary>
    /// <param name="__instance">Handler being started</param>
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

    /// <summary>
    ///     Start a cutscene on the server controller for every alive human player. Fika lets only the host start one.
    /// </summary>
    /// <param name="cutsceneId">Cutscene to start</param>
    private static void Start(string cutsceneId)
    {
        var world = Singleton<GameWorld>.Instance;
        var server = world == null ? null : world.CutscenesServerController;
        if (server == null || world.MainPlayer == null)
        {
            return;
        }

        var ids = new Il2CppSystem.Collections.Generic.List<int>();
        var players = world.AllAlivePlayersList;
        for (var i = 0; i < players.Count; i++)
        {
            if (!players[i].IsAI)
            {
                ids.Add(players[i].PlayerId);
            }
        }

        var process = server.StartCutscene(cutsceneId, ids, false, false, 0f);
        Logger.LogInfo($"trigger cutscene: '{cutsceneId}' {(process == null ? "refused" : "started")}");
    }
}

/// <summary>
///     Log what each quest gate in a trigger chain asks for, so a cutscene that never starts can be traced to the quest
///     it wants. Debug only and never enabled automatically.
/// </summary>
[IgnoreAutoPatch]
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

/// <summary>
///     Emit a rally zone's trigger once every alive human player is inside. The zone raises its update event before its
///     all-players check. Offline that event runs synchronously and marks the zone triggered first, so the check never emits.
/// </summary>
/// <remarks>
///     OnTriggerEnter has the enter logic inlined, so a patch on OnEnterAuthority would never run. The zone still tracks
///     who is inside natively, and this watches that set.
/// </remarks>
public static class RallyZonePatches
{
    /// <summary>
    ///     Rally zones in the current raid
    /// </summary>
    private static readonly List<TriggerRallyZone> _zones = new();

    /// <summary>
    ///     Zones whose trigger has already been emitted
    /// </summary>
    private static readonly HashSet<TriggerRallyZone> _emitted = new();

    private static int _frames;

    /// <summary>
    ///     Collect the raid's rally zones when the game starts
    /// </summary>
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

    /// <summary>
    ///     Every 15 frames, emit the trigger of each zone that has every alive human player inside
    /// </summary>
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
                    if (zone == null || _emitted.Contains(zone) || string.IsNullOrEmpty(zone._triggerId))
                    {
                        continue;
                    }

                    var alive = zone.GetAliveHumanPlayerCount();
                    if (alive == 0 || zone._playersInside.Count < alive)
                    {
                        continue;
                    }

                    _emitted.Add(zone);

                    // Keeps the zone's own check from emitting a second time
                    zone._wasTriggered = true;
                    // Handlers resolve the origin player through GetAlivePlayerByRaidID
                    world.TriggersEmitter.Emit(zone._triggerId, player.RaidId);
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
