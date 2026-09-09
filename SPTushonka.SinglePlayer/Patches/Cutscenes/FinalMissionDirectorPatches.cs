using System;
using System.Collections.Generic;
using System.Reflection;
using CommonAssets.Scripts.Cutscenes;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

// The dedicated server owns the final mission on Terminal. Offline this fills in its decisions:
// start the intro, arm the exit zone when the pier gate opens and evacuate when its clock runs
// out. Every other step is wired inside the scene.
// Both cutscene controllers initialise themselves from their constructors, so they must not be
// initialised again here. A second Init subscribes every handler twice.
public static class FinalMissionDirectorPatches
{
    // Live holds players in a waiting room first. Solo there is nobody to wait for.
    private const int IntroDelayFrames = 120;

    // The live server ends a fixed-time cutscene this long after its duration. Offline it reads as zero.
    private const float LagCompensation = 2f;

    // The key to the pier gate is the last thing the map hands out before the boat.
    private const string PierDoorKey = "6866adbe09b973bf45094339";

    private static GameWorld _world;
    private static MapTimelinesBank _bank;
    private static FinallExitZone _zone;
    private static string _timerCutsceneId;
    private static List<WorldInteractiveObject> _gates;
    private static int _frames;
    private static bool _introStarted;
    private static bool _timerCutsceneSeen;
    private static bool _zoneInitialised;
    private static bool _stateSent;
    private static bool _evacuated;

    public static void Patch()
    {
        new StartPatch().Enable();
        new TickPatch().Enable();
    }

    private static void Reset()
    {
        _world = null;
        _bank = null;
        _zone = null;
        _timerCutsceneId = null;
        _gates = null;
        _frames = 0;
        _introStarted = false;
        _timerCutsceneSeen = false;
        _zoneInitialised = false;
        _stateSent = false;
        _evacuated = false;
    }

    public class StartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        public static void Postfix(GameWorld __instance)
        {
            Reset();
            try
            {
                var bank = UnityEngine.Object.FindObjectOfType<MapTimelinesBank>();
                if (bank == null || __instance.CutscenesServerController == null || __instance.CutscenesClientController == null)
                {
                    return;
                }

                _world = __instance;
                _bank = bank;
                _zone = UnityEngine.Object.FindObjectOfType<FinallExitZone>();
                var timer = UnityEngine.Object.FindObjectOfType<StartCutsceneByTimer>();
                _timerCutsceneId = timer == null ? null : timer.cutsceneId;
                Logger.LogInfo($"final mission: {bank.timelines.Count} timelines, exit zone {(_zone == null ? "missing" : "found")}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"final mission: start failed: {ex}");
                Reset();
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
            if (_world == null)
            {
                return;
            }

            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Logger.LogError($"final mission: tick failed: {ex}");
            }
        }

        private static void Tick()
        {
            var server = _world.CutscenesServerController;
            var client = _world.CutscenesClientController;
            if (server == null || client == null || _world.MainPlayer == null)
            {
                Reset();
                return;
            }

            _frames++;
            if (!_introStarted)
            {
                // The controllers' own Init runs async after the game starts. Both must have found
                // the bank before a cutscene can be handled on either side.
                if (_frames < IntroDelayFrames || server._timelinesMapBank == null || client._timelinesMapBank == null)
                {
                    return;
                }

                _introStarted = true;
                server._lagTimeCompensation = LagCompensation;
                if (UnityEngine.Object.FindObjectOfType<StartCutsceneByStartRaid>() == null)
                {
                    Logger.LogInfo("final mission: no start-raid marker, intro skipped");
                    return;
                }

                var intro = _bank.timelines[0].cutsceneId;
                var process = server.StartCutscene(intro, HumanPlayerIds(), false, false, 0f);
                Logger.LogInfo($"final mission: intro '{intro}' {(process == null ? "refused" : "started")}");
                return;
            }

            if (_zoneInitialised)
            {
                Evacuation();
                return;
            }

            if (_zone == null)
            {
                return;
            }

            // The exit only matters once the attack cutscene has run.
            var inCutscene = server.CheckPlayerInCutscene(_world.MainPlayer.Cast<IPlayer>(), out var current);
            if (!_timerCutsceneSeen)
            {
                if (inCutscene && current != null && current.currentCutsceneId == _timerCutsceneId)
                {
                    _timerCutsceneSeen = true;
                }

                return;
            }

            if (!inCutscene)
            {
                ArmWhenGateOpens();
            }
        }

        private static Il2CppSystem.Collections.Generic.List<int> HumanPlayerIds()
        {
            var ids = new Il2CppSystem.Collections.Generic.List<int>();
            ids.Add(_world.MainPlayer.PlayerId);

            return ids;
        }

        // Live starts the evacuation clock once the pier gate is opened. The gate is found by the
        // key it takes. Without one the zone arms on entry instead.
        private static void ArmWhenGateOpens()
        {
            if (_frames % 60 != 0)
            {
                return;
            }

            if (_gates == null)
            {
                _gates = new List<WorldInteractiveObject>();
                foreach (var door in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(true))
                {
                    if (door.KeyId == PierDoorKey)
                    {
                        _gates.Add(door);
                        Logger.LogInfo($"final mission: pier gate '{door.Id}' object '{door.name}' state={door.DoorState}");
                    }
                }

                if (_gates.Count == 0)
                {
                    Logger.LogError("final mission: no door takes the pier key, arming on zone entry instead");
                }
            }

            var open = false;
            foreach (var gate in _gates)
            {
                open |= gate.DoorState == EDoorState.Open;
            }

            if (!open && !(_gates.Count == 0 && InZone(_world.MainPlayer.Position)))
            {
                return;
            }

            _zoneInitialised = true;
            _zone.InitZoneServer();
            Logger.LogInfo($"final mission: exit zone armed, seats={_zone._playersToEvacuate} timer={_zone.evacuateTimer}");
        }

        private static bool InZone(Vector3 position)
        {
            foreach (var collider in _zone.GetComponentsInChildren<Collider>(true))
            {
                if (collider.bounds.Contains(position))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasItem(Player player, string templateId)
        {
            var items = player.Profile.Inventory.GetAllItemByTemplate(templateId);
            var enumerator = items == null ? null : items.GetEnumerator().TryCast<Il2CppSystem.Collections.IEnumerator>();
            return enumerator != null && enumerator.MoveNext();
        }

        private static bool HasRequiredItems(Player player)
        {
            var requirements = _zone.questItemsRequirements;
            for (var i = 0; requirements != null && i < requirements.Count; i++)
            {
                if (!HasItem(player, requirements[i].itemId))
                {
                    return false;
                }
            }

            return true;
        }

        private static void SendExitState(Player player, EFinalExitStatus status)
        {
            new ChangeFinalExitStateEvent { playerId = player.PlayerId, exitStatus = status }.Invoke();
        }

        // The zone counts its clock itself but only the dedicated server acts when it runs out. It
        // starts the ending cutscene for everyone in the zone and ends the raid for everyone else.
        // The watching player's raid end comes from the cutscene's own exit action. The server's
        // finalizer would stop the raid twenty seconds in, so it is not used.
        private static void Evacuation()
        {
            if (_frames % 60 != 0)
            {
                return;
            }

            var player = _world.MainPlayer;
            var inZone = InZone(player.Position);
            var hasItems = HasRequiredItems(player);

            // The zone's own enter callback is server only. The exit panel state is sent on entry.
            if (inZone != _stateSent)
            {
                _stateSent = inZone;
                if (inZone)
                {
                    SendExitState(player, hasItems ? EFinalExitStatus.available : EFinalExitStatus.noItem);
                }
            }

            if (_evacuated || !_zone._evacuateInitialized)
            {
                return;
            }

            _evacuated = true;
            if (!inZone || !hasItems)
            {
                Logger.LogInfo($"final mission: {(inZone ? "no quest item" : "player outside the zone")}, bad end");
                _zone.ImmediateBadEndForPlayer(player);
                return;
            }

            var cutscene = _zone.GetVariantCutsceneId(player);
            Logger.LogInfo($"final mission: evacuating with '{cutscene}'");
            _zone.ActivateCutsceneForPlayer(player, cutscene);
        }
    }
}
