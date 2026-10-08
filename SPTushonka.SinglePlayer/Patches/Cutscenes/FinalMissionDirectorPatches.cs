using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using CommonAssets.Scripts.Cutscenes;
using Diz.LanguageExtensions;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.Quests;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.SinglePlayer.Patches.Cutscenes;

/// <summary>
///     Run the Terminal final mission steps the dedicated server owns: take the gear away, start the intro, arm the exit
///     zone when the pier gate opens and evacuate when its clock runs out. The scene wires every other step.
/// </summary>
/// <remarks>
///     Both cutscene controllers initialise themselves from their constructors. A second Init would subscribe every
///     handler twice. Fika disables <see cref="StartPatch"/> and <see cref="TickPatch"/> by name and runs its own director.
/// </remarks>
public static class FinalMissionDirectorPatches
{
    /// <summary>
    ///     Frames to wait before the intro. Solo there is no waiting room to hold the player in
    /// </summary>
    private const int IntroDelayFrames = 120;

    /// <summary>
    ///     Seconds the server adds to a fixed-time cutscene before ending it. The offline controller has it at zero
    /// </summary>
    private const float LagCompensation = 2f;

    /// <summary>
    ///     Key to the pier gate, the last door before the boat
    /// </summary>
    private const string PierDoorKey = "6866adbe09b973bf45094339";

    private const float PendingTimeoutSeconds = 10f;

    /// <summary>
    ///     Slots kept on the player when the scene's own list cannot be read
    /// </summary>
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

    private static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("SPTushonka");

    /// <summary>
    ///     Weapons waiting for empty hands before they can be moved
    /// </summary>
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
            if (!string.Equals(__instance.LocationId, "Terminal", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                Begin(__instance);
            }
            catch (Exception ex)
            {
                Log.LogError($"final mission: start failed: {ex}");
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
                Log.LogError($"final mission: tick failed: {ex}");
            }
        }
    }

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

    private static void Begin(GameWorld world)
    {
        var bank = UnityEngine.Object.FindObjectOfType<MapTimelinesBank>();
        if (bank == null || world.CutscenesServerController == null || world.CutscenesClientController == null)
        {
            return;
        }

        _world = world;
        _bank = bank;
        _zone = UnityEngine.Object.FindObjectOfType<FinallExitZone>();
        var timer = UnityEngine.Object.FindObjectOfType<StartCutsceneByTimer>();
        _timerCutsceneId = timer == null ? null : timer.cutsceneId;
        Log.LogInfo($"final mission: {bank.timelines.Count} timelines, exit zone {(_zone == null ? "missing" : "found")}");
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
            TryStartIntro(server, client);
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

        // The exit only matters once the attack cutscene has run
        var inCutscene = server.CheckPlayerInCutscene(_world.MainPlayer, out var current);
        if (!_timerCutsceneSeen)
        {
            _timerCutsceneSeen = inCutscene && current != null && current.currentCutsceneId == _timerCutsceneId;
            return;
        }

        if (!inCutscene)
        {
            ArmWhenGateOpens();
        }
    }

    /// <summary>
    ///     Take the gear away, then start the intro once the weapons are gone. The intro takes the hands too
    /// </summary>
    /// <param name="server">Server cutscene controller</param>
    /// <param name="client">Client cutscene controller</param>
    private static void TryStartIntro(CutscenesServerController server, CutscenesClientController client)
    {
        // The controllers' own Init runs async after the game starts. Both must have found the bank first.
        if (_frames < IntroDelayFrames || server._timelinesMapBank == null || client._timelinesMapBank == null)
        {
            return;
        }

        if (!_inventoryTaken)
        {
            _inventoryTaken = true;
            TakeAwayInventory(_world.MainPlayer);
        }

        if (_pending.Count > 0)
        {
            return;
        }

        _introStarted = true;
        server._lagTimeCompensation = LagCompensation;
        if (UnityEngine.Object.FindObjectOfType<StartCutsceneByStartRaid>() == null)
        {
            Log.LogInfo("final mission: no start-raid marker, intro skipped");
            return;
        }

        var intro = _bank.timelines[0].cutsceneId;
        var ids = new Il2CppSystem.Collections.Generic.List<int>();
        ids.Add(_world.MainPlayer.PlayerId);
        var process = server.StartCutscene(intro, ids, false, false, 0f);
        Log.LogInfo($"final mission: intro '{intro}' {(process == null ? "refused" : "started")}");
    }

    /// <summary>
    ///     Move the player's gear into a free private container stamped with their id. The client's TryTakeAwayInventory
    ///     is a stub, so its steps run here
    /// </summary>
    /// <param name="player">Player to strip</param>
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
        var equipment = player.Profile.Inventory.Equipment;
        foreach (var name in InventoryEquipment.AllSlotNames)
        {
            var slot = kept.Contains(name.ToString()) ? null : equipment.GetSlot(name);
            var item = slot == null ? null : slot.ContainedItem;
            if (item == null)
            {
                continue;
            }

            // The game reads the special slots off the pockets item every frame, so pockets are emptied instead
            if (name == EquipmentSlot.Pockets)
            {
                taken.AddRange(Contents(item));
            }
            else if (name == EquipmentSlot.FirstPrimaryWeapon || name == EquipmentSlot.SecondPrimaryWeapon || name == EquipmentSlot.Holster || name == EquipmentSlot.Scabbard)
            {
                _pending.Add(item);
            }
            else
            {
                taken.Add(item);
            }
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

        if (_pending.Count > 0)
        {
            _pendingUntil = Time.time + PendingTimeoutSeconds;
            player.SetEmptyHands(null);
        }

        var target = container == null ? "no container" : container.Id;
        Log.LogInfo(
            $"final mission: moved {moved} and removed {removed} item(s), into {target}, {_pending.Count} weapon(s) waiting for empty hands, kept {string.Join(", ", kept)}"
        );
    }

    /// <summary>
    ///     Move the next pending weapon once the hands are empty, giving up after the timeout
    /// </summary>
    private static void RetryPending()
    {
        var player = _world.MainPlayer;
        var controller = player.InventoryController;
        var equipment = player.Profile.Inventory.Equipment;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (!IsEquipped(_pending[i], equipment))
            {
                Log.LogInfo($"final mission: took weapon '{_pending[i].TemplateId}'");
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
                Log.LogError($"final mission: could not take '{item.TemplateId}', left on the player, {_lastRefusal}");
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

    private static bool IsEquipped(Item item, InventoryEquipment equipment)
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

    /// <summary>
    ///     Mark the container as searched for the player so its contents show without a search
    /// </summary>
    /// <param name="container">Container the gear goes into</param>
    /// <param name="controller">Player's inventory controller</param>
    private static void Uncover(PrivateLootableContainer container, InventoryController controller)
    {
        var root = container.ItemOwner.RootItem.TryCast<LootContainer>();
        var search = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)controller.SearchController).TryCast<PlayerSearchController>();
        if (root == null || search == null)
        {
            Log.LogWarning($"final mission: could not uncover '{container.Id}' for the player");
            return;
        }

        search.SetItemAsKnown(root, false);
        search.SetItemAsSearched(root);
    }

    /// <summary>
    ///     Pick a free private container. The game's own pick can return one whose item owner is not loaded yet
    /// </summary>
    /// <param name="taker">Scene's take away component</param>
    /// <returns>Free container with an item owner, or null when there is none</returns>
    private static PrivateLootableContainer FreeContainer(TakeInventoryFromConnectedPlayer taker)
    {
        var chosen = taker.GetRandomFreeContainer();
        if (chosen != null && chosen.ItemOwner != null)
        {
            return chosen;
        }

        foreach (var container in taker.containers)
        {
            if (container != null && container.playerId == 0 && container.ItemOwner != null)
            {
                return container;
            }
        }

        Log.LogWarning($"final mission: none of the {taker.containers.Count} private container(s) is free with an item owner");
        return null;
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

            if (Run(result, controller, "the controller cannot execute the move"))
            {
                return true;
            }
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

        return Run(result, controller, "the controller cannot execute the removal");
    }

    /// <summary>
    ///     Run an inventory operation through the controller
    /// </summary>
    /// <param name="operation">Operation to run</param>
    /// <param name="controller">Player's inventory controller</param>
    /// <param name="refusal">Reason recorded when the controller cannot execute it</param>
    /// <returns>True when the operation was sent</returns>
    private static bool Run(OperationResult operation, InventoryController controller, string refusal)
    {
        if (!operation.Value.CanExecute(controller))
        {
            _lastRefusal = refusal;
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
            Log.LogError($"final mission: could not read the scene's kept slots: {ex.Message}");
        }

        if (kept.Count > 0)
        {
            return kept;
        }

        Log.LogWarning("final mission: the scene listed no slots to keep, using the known set");
        kept.UnionWith(FallbackKeptSlots);
        return kept;
    }

    /// <summary>
    ///     Arm the exit zone once the pier gate is open, which starts the evacuation clock. The gate is found by the key
    ///     it takes. Without one the zone arms on entry instead
    /// </summary>
    private static void ArmWhenGateOpens()
    {
        if (_frames % 60 != 0)
        {
            return;
        }

        _gates ??= FindGates();
        var open = _gates.Any(gate => gate.DoorState == EDoorState.Open);
        if (!open && !(_gates.Count == 0 && InZone(_world.MainPlayer.Position)))
        {
            return;
        }

        _zoneInitialised = true;
        _zone.InitZoneServer();
        var requirements = _zone.questItemsRequirements;
        for (var i = 0; requirements != null && i < requirements.Count; i++)
        {
            Log.LogInfo(
                $"final mission: quest '{requirements[i].questId}' needs '{requirements[i].itemId}', on it {OnQuest(_world.MainPlayer, requirements[i].questId)}"
            );
        }

        Log.LogInfo($"final mission: exit zone armed, seats={_zone._playersToEvacuate} timer={_zone.evacuateTimer}");
    }

    private static List<WorldInteractiveObject> FindGates()
    {
        var gates = new List<WorldInteractiveObject>();
        foreach (var door in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(true))
        {
            if (door.KeyId == PierDoorKey)
            {
                gates.Add(door);
                Log.LogInfo($"final mission: pier gate '{door.Id}' object '{door.name}' state={door.DoorState}");
            }
        }

        if (gates.Count == 0)
        {
            Log.LogError("final mission: no door takes the pier key, arming on zone entry instead");
        }

        return gates;
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

    /// <summary>
    ///     Check the player carries the items their ending needs. Each requirement belongs to one ending's finisher task,
    ///     and only the task the player is on asks for its item
    /// </summary>
    /// <param name="player">Player to check</param>
    /// <returns>True when every required item is carried</returns>
    private static bool HasRequiredItems(Player player)
    {
        var requirements = _zone.questItemsRequirements;
        for (var i = 0; requirements != null && i < requirements.Count; i++)
        {
            if (OnQuest(player, requirements[i].questId) && !HasItem(player, requirements[i].itemId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasItem(Player player, string templateId)
    {
        var items = player.Profile.Inventory.GetAllItemByTemplate(templateId);
        return items != null && items.Any();
    }

    private static bool OnQuest(Player player, string questId)
    {
        foreach (var quest in player.Profile.QuestsData)
        {
            if (quest.Id == questId)
            {
                return quest.Status == EQuestStatus.Started || quest.Status == EQuestStatus.AvailableForFinish;
            }
        }

        return false;
    }

    /// <summary>
    ///     Act on the zone's clock. When it runs out the server starts the ending cutscene for a player in the zone and
    ///     ends the raid for everyone else. The cutscene's own exit action ends the watching player's raid, so the
    ///     server's finaliser, which stops the raid twenty seconds in, is not used
    /// </summary>
    private static void Evacuation()
    {
        if (_frames % 60 != 0)
        {
            return;
        }

        var player = _world.MainPlayer;
        var inZone = InZone(player.Position);
        var hasItems = HasRequiredItems(player);

        // The zone's own enter callback is server only, so the exit panel state is sent on entry
        if (inZone != _stateSent)
        {
            _stateSent = inZone;
            if (inZone)
            {
                var status = hasItems ? EFinalExitStatus.available : EFinalExitStatus.noItem;
                new ChangeFinalExitStateEvent { playerId = player.PlayerId, exitStatus = status }.Invoke();
            }
        }

        if (_evacuated || !_zone._evacuateInitialized)
        {
            return;
        }

        _evacuated = true;
        if (!inZone || !hasItems)
        {
            Log.LogInfo($"final mission: {(inZone ? "no quest item" : "player outside the zone")}, bad end");
            _zone.ImmediateBadEndForPlayer(player);
            return;
        }

        var cutscene = _zone.GetVariantCutsceneId(player);
        Log.LogInfo($"final mission: evacuating with '{cutscene}'");
        _zone.ActivateCutsceneForPlayer(player, cutscene);
    }
}
