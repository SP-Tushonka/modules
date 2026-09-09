using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using CommonAssets.Scripts.Cutscenes;
using CommonAssets.Scripts.Game.Syncable;
using EFT;
using EFT.GameTriggers;
using EFT.Interactive;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// Ten cutscene action types have an empty Init in the client build. The dedicated server handles
// those by type when it hands the action out. This does the same for the ones Terminal uses.
// Offline both controllers share one set of action objects and the client runs every start and
// end action itself, so the server side only stamps the action and never calls Init. Otherwise
// every client-visible action would run twice.
public static class ServerActionPatches
{
    private static readonly List<(float at, HandlerStateTimer timer, bool state)> PendingStates = new();

    public static void Patch()
    {
        new InitActionPatch().Enable();
        new ClientInitActionPatch().Enable();
        new TickPatch().Enable();
    }

    // The ending cutscenes play client only, so their end actions never reach the server side.
    // The exit action is handled here as well. Only the first stop counts either way.
    public class ClientInitActionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesClientController), nameof(CutscenesClientController.InitAction));
        }

        [PatchPostfix]
        public static void Postfix(CutscenesClientController __instance, string actionId)
        {
            try
            {
                if (__instance._actions == null || !__instance._actions.TryGetValue(actionId, out var action) || action == null)
                {
                    return;
                }

                var exit = action.TryCast<CutsceneActionExitAfterCutscene>();
                if (exit != null)
                {
                    StopRaid(exit.ExitStatus);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"client action '{actionId}' failed: {ex}");
            }
        }
    }

    private static ExitStatus? _pendingStop;

    // The exit action runs inside the cutscene's own stop sequence. Stopping the raid from there
    // tears the world down under that sequence, so the stop waits for the next frame.
    private static void StopRaid(ExitStatus status)
    {
        _pendingStop ??= status;
    }

    private static void RunPendingStop()
    {
        if (_pendingStop == null)
        {
            return;
        }

        var status = _pendingStop.Value;
        _pendingStop = null;
        var game = Singleton<AbstractGame>.Instance?.TryCast<LocalGame>();
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (game != null && player != null && game.Status == GameStatus.Started)
        {
            game.Stop(player.ProfileId, status, FinallExitZone.FINAL_EXIT_NAME, 0f);
        }
    }

    public class InitActionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CutscenesServerController), nameof(CutscenesServerController.InitAction));
        }

        [PatchPrefix]
        public static bool Prefix(CutscenesServerController __instance, string actionId, Il2CppSystem.Collections.Generic.List<int> playersId, string cutsceneId)
        {
            try
            {
                if (__instance._actions == null || !__instance._actions.TryGetValue(actionId, out var action) || action == null)
                {
                    return false;
                }

                action.playersId = playersId;
                action.cutsceneId = cutsceneId;
                Emulate(action, playersId);
            }
            catch (Exception ex)
            {
                Logger.LogError($"server action '{actionId}' failed: {ex}");
            }

            return false;
        }

        private static void Emulate(CutsceneBaseAction action, Il2CppSystem.Collections.Generic.List<int> playersId)
        {
            switch (action.GetIl2CppType().Name)
            {
                case nameof(CutsceneActionChangeBotActiveState):
                {
                    var state = action.TryCast<CutsceneActionChangeBotActiveState>().BotActiveState;
                    new CutSceneChangeBotState { IsActive = state }.Invoke();
                    Logger.LogInfo($"server action: bots {(state ? "activated" : "deactivated")}");
                    break;
                }
                case nameof(CutsceneActionInitTimerToCutscene):
                {
                    var timer = action.TryCast<CutsceneActionInitTimerToCutscene>().startCutsceneByTimer;
                    if (timer != null)
                    {
                        timer.StartTimer();
                        Logger.LogInfo($"server action: timer to '{timer.cutsceneId}' started, {timer.time}s");
                    }

                    break;
                }
                case nameof(CutsceneActionActivateStateTimer):
                {
                    var timer = action.TryCast<CutsceneActionActivateStateTimer>().stateTimer;
                    if (timer != null)
                    {
                        var now = Time.time;
                        PendingStates.Add((now + timer._startDelaySeconds, timer, timer._targetState));
                        if (timer._workDurationSeconds > 0f)
                        {
                            PendingStates.Add((now + timer._startDelaySeconds + timer._workDurationSeconds, timer, !timer._targetState));
                        }

                        Logger.LogInfo($"server action: state timer '{timer._id}' in {timer._startDelaySeconds}s for {timer._workDurationSeconds}s");
                    }

                    break;
                }
                case nameof(CutsceneActionsActivateLamp):
                {
                    var ids = action.TryCast<CutsceneActionsActivateLamp>().triggersId;
                    var emitter = TriggersEmitter.Instance;
                    for (var i = 0; ids != null && emitter != null && i < ids.Count; i++)
                    {
                        emitter.SetTriggerState(ids[i], true);
                    }

                    Logger.LogInfo($"server action: lamps via {(ids == null ? 0 : ids.Count)} trigger(s)");
                    break;
                }
                case nameof(CutsceneActionOpenDoors):
                {
                    // The client side of this action only shows a notification. The door state
                    // itself comes from the dedicated server's door sync.
                    var ids = action.TryCast<CutsceneActionOpenDoors>().doorsId;
                    var opened = 0;
                    foreach (var door in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(true))
                    {
                        if (ids == null || !ids.Contains(door.Id))
                        {
                            continue;
                        }

                        if (door.DoorState == EDoorState.Locked)
                        {
                            door.DoorState = EDoorState.Shut;
                        }

                        door.Interact(EInteractionType.Open);
                        opened++;
                        Logger.LogInfo($"server action: door '{door.Id}' ({door.name}) opened, now {door.DoorState}");
                    }

                    Logger.LogInfo($"server action: {opened} of {(ids == null ? 0 : ids.Count)} door(s) opened");
                    break;
                }
                case nameof(CutsceneActionExitAfterCutscene):
                {
                    var status = action.TryCast<CutsceneActionExitAfterCutscene>().ExitStatus;
                    StopRaid(status);
                    break;
                }
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
            RunPendingStop();
            if (PendingStates.Count == 0)
            {
                return;
            }

            var now = Time.time;
            for (var i = PendingStates.Count - 1; i >= 0; i--)
            {
                var (at, timer, state) = PendingStates[i];
                if (now < at)
                {
                    continue;
                }

                PendingStates.RemoveAt(i);
                try
                {
                    if (timer != null)
                    {
                        timer.SetState(state);
                        Logger.LogInfo($"server action: state timer '{timer._id}' set {state}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"state timer failed: {ex}");
                }
            }
        }
    }
}
