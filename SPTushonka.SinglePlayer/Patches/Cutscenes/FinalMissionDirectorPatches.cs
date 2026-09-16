using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using CommonAssets.Scripts.Cutscenes;
using Diz.LanguageExtensions;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT;
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

    // Used only when the scene's own list cannot be read.
    private static readonly string[] FallbackKeptSlots =
    [
        "Dogtag",
        "SecuredContainer",
        "FaceCover",
        "Eyewear",
        "TacticalVest",
        "ArmBand",
        "ArmorVest",
    ];

    private const float PendingTimeoutSeconds = 10f;

    private static readonly List<Item> _pending = [];
    private static PrivateLootableContainer _pendingContainer;
    private static float _pendingUntil;
    private static string _lastRefusal;
    private static GameWorld _world;
    private static MapTimelinesBank _bank;
    private static FinallExitZone _zone;
    private static string _timerCutsceneId;
    private static List<WorldInteractiveObject> _gates;
    private static int _frames;
    private static bool _inventoryTaken;
    private static bool _introStarted;
    private static bool _timerCutsceneSeen;
    private static bool _zoneInitialised;
    private static bool _stateSent;
    private static bool _evacuated;

    private static void Reset()
    {
        _world = null;
        _bank = null;
        _zone = null;
        _timerCutsceneId = null;
        _gates = null;
        _frames = 0;
        _inventoryTaken = false;
        _introStarted = false;
        _timerCutsceneSeen = false;
        _zoneInitialised = false;
        _stateSent = false;
        _evacuated = false;
        _pending.Clear();
        _pendingContainer = null;
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
            if (_pending.Count > 0 && _frames % 30 == 0)
            {
                RetryPending();
            }

            if (!_introStarted)
            {
                // The controllers' own Init runs async after the game starts. Both must have found
                // the bank before a cutscene can be handled on either side.
                if (_frames < IntroDelayFrames || server._timelinesMapBank == null || client._timelinesMapBank == null)
                {
                    return;
                }

                if (!_inventoryTaken)
                {
                    _inventoryTaken = true;
                    TakeAwayInventory(_world.MainPlayer);
                }

                // The intro takes the hands too, so the weapons have to be gone before it starts
                if (_pending.Count > 0)
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
            var inCutscene = server.CheckPlayerInCutscene(_world.MainPlayer, out var current);
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

        // Live's take away picks a free private container, stamps it with the player's id and moves the
        // gear in. The client's TryTakeAwayInventory is a stub, so the same steps run here.
        private static void TakeAwayInventory(Player player)
        {
            var taker = UnityEngine.Object.FindObjectOfType<TakeInventoryFromConnectedPlayer>();
            if (taker == null)
            {
                return;
            }

            var kept = KeptSlots(taker);
            var controller = player.InventoryController;
            var container = FreeContainer(taker);
            if (container != null)
            {
                container.Init(player.PlayerId);
                Uncover(container, controller);
            }

            var taken = new List<Item>();
            var weapons = new List<Item>();
            var equipment = player.Profile.Inventory.Equipment;
            foreach (var name in InventoryEquipment.AllSlotNames)
            {
                if (kept.Contains(name.ToString()))
                {
                    continue;
                }

                var slot = equipment.GetSlot(name);
                var item = slot == null ? null : slot.ContainedItem;
                if (item == null)
                {
                    continue;
                }

                // The game reads the special slots off the pockets item every frame, so pockets are emptied instead
                if (name == EquipmentSlot.Pockets)
                {
                    taken.AddRange(Contents(item));
                    continue;
                }

                if (name == EquipmentSlot.FirstPrimaryWeapon || name == EquipmentSlot.SecondPrimaryWeapon || name == EquipmentSlot.Holster || name == EquipmentSlot.Scabbard)
                {
                    weapons.Add(item);
                    continue;
                }

                taken.Add(item);
            }

            _pendingContainer = container;
            var moved = 0;
            var removed = 0;
            foreach (var item in taken)
            {
                if (container != null && MoveInto(item, container, controller))
                {
                    moved++;
                }
                else if (Remove(item, controller))
                {
                    removed++;
                }
            }

            _pending.AddRange(weapons);
            
            if (_pending.Count > 0)
            {
                _pendingUntil = Time.time + PendingTimeoutSeconds;
                player.SetEmptyHands(null);
            }

            var target = container == null ? "no container" : container.Id;
            Logger.LogInfo(
                $"final mission: moved {moved} and removed {removed} item(s), into {target}, {_pending.Count} weapon(s) waiting for empty hands, kept {string.Join(", ", kept)}"
            );
        }

        private static void RetryPending()
        {
            var player = _world.MainPlayer;
            var controller = player.InventoryController;
            var equipment = player.Profile.Inventory.Equipment;
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (!OnPlayer(_pending[i], equipment))
                {
                    Logger.LogInfo($"final mission: took weapon '{_pending[i].TemplateId}'");
                    _pending.RemoveAt(i);
                }
            }

            if (_pending.Count == 0)
            {
                return;
            }

            if (Time.time > _pendingUntil)
            {
                foreach (var item in _pending)
                {
                    Logger.LogError($"final mission: could not take '{item.TemplateId}', left on the player, {_lastRefusal}");
                }

                _pending.Clear();
                return;
            }

            if (player.ScheduledProcess != null)
            {
                _lastRefusal = "the hands were still changing";
                return;
            }

            if (player.HandsController == null || player.HandsController.TryCast<Player.EmptyHandsController>() == null)
            {
                _lastRefusal = "the hands never emptied";
                player.SetEmptyHands(null);
                return;
            }

            var next = _pending[_pending.Count - 1];
            if (_pendingContainer == null || !MoveInto(next, _pendingContainer, controller))
            {
                Remove(next, controller);
            }
        }

        private static bool OnPlayer(Item item, InventoryEquipment equipment)
        {
            foreach (var name in InventoryEquipment.AllSlotNames)
            {
                var slot = equipment.GetSlot(name);
                if (slot != null && slot.ContainedItem != null && slot.ContainedItem.Id == item.Id)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Uncover(PrivateLootableContainer container, InventoryController controller)
        {
            var root = container.ItemOwner.RootItem.TryCast<LootContainer>();
            var search = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)controller.SearchController).TryCast<PlayerSearchController>();
            if (root == null || search == null)
            {
                Logger.LogWarning($"final mission: could not uncover '{container.Id}' for the player");
                return;
            }

            search.SetItemAsKnown(root, false);
            search.SetItemAsSearched(root);
        }

        private static PrivateLootableContainer FreeContainer(TakeInventoryFromConnectedPlayer taker)
        {
            var chosen = taker.GetRandomFreeContainer();
            if (chosen != null && chosen.ItemOwner != null)
            {
                return chosen;
            }

            var total = 0;
            var free = 0;
            var owned = 0;
            PrivateLootableContainer fallback = null;
            foreach (var container in taker.containers)
            {
                total++;
                if (container == null || container.playerId != 0)
                {
                    continue;
                }

                free++;
                if (container.ItemOwner == null)
                {
                    continue;
                }

                owned++;
                if (fallback == null)
                {
                    fallback = container;
                }
            }

            Logger.LogWarning($"final mission: {total} private container(s), {free} free, {owned} with an item owner");
            return fallback;
        }

        private static List<Item> Contents(Item container)
        {
            var contents = new List<Item>();
            var compound = container.TryCast<CompoundItem>();
            if (compound == null)
            {
                return contents;
            }

            if (compound.Grids != null)
            {
                foreach (var grid in compound.Grids)
                {
                    foreach (var item in grid.Items)
                    {
                        contents.Add(item);
                    }
                }
            }

            if (compound.Slots != null)
            {
                foreach (var slot in compound.Slots)
                {
                    if (slot.ContainedItem != null)
                    {
                        contents.Add(slot.ContainedItem);
                    }
                }
            }

            return contents;
        }

        private static bool MoveInto(Item item, PrivateLootableContainer container, InventoryController controller)
        {
            var root = container.ItemOwner.RootItem.TryCast<CompoundItem>();
            if (root == null || root.Grids == null)
            {
                _lastRefusal = "the container has no grid";
                return false;
            }

            _lastRefusal = "no free space";
            foreach (var grid in root.Grids)
            {
                var address = grid.FindLocationForItem(item);
                if (address == null)
                {
                    continue;
                }

                var result = ItemManipulator.Move(item, address, controller, true);
                if (result.Failed)
                {
                    _lastRefusal = result.Error == null ? "move refused" : result.Error.ToString();
                    continue;
                }

                OperationResult operation = result;
                if (!operation.Value.CanExecute(controller))
                {
                    _lastRefusal = "the controller cannot execute the move";
                    continue;
                }

                controller.TryRunNetworkTransaction(operation, null);
                return true;
            }

            return false;
        }

        private static bool Remove(Item item, InventoryController controller)
        {
            var result = ItemManipulator.Remove(item, controller, true);
            if (result.Failed)
            {
                _lastRefusal = result.Error == null ? "removal refused" : result.Error.ToString();
                return false;
            }

            OperationResult operation = result;
            if (!operation.Value.CanExecute(controller))
            {
                _lastRefusal = "the controller cannot execute the removal";
                return false;
            }

            controller.TryRunNetworkTransaction(operation, null);
            return true;
        }

        private static HashSet<string> KeptSlots(TakeInventoryFromConnectedPlayer taker)
        {
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var listed = taker._slotsToIgnoreFirstPart;
                if (listed != null)
                {
                    foreach (var slot in listed)
                    {
                        kept.Add(slot);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"final mission: could not read the scene's kept slots: {ex.Message}");
            }

            if (kept.Count > 0)
            {
                return kept;
            }

            Logger.LogWarning("final mission: the scene listed no slots to keep, using the known set");
            foreach (var slot in FallbackKeptSlots)
            {
                kept.Add(slot);
            }

            return kept;
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
            return items != null && items.Any();
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
