using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.GameTriggers;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using SPTushonka.Reflection.Commands;
using UnityEngine;

namespace SPTushonka.Debugging.Commands;

public static class TeleportCommands
{
    private static void Go(Vector3 position, string label)
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player == null)
        {
            ConsoleScreen.LogError("No player in raid");
            return;
        }

        position += Vector3.up * 0.5f;
        player.Teleport(position, false);
        NoclipCommand.MoveTo(position);
        ConsoleScreen.Log($"Teleported to {label} at {position}");
    }

    [ConsoleCommand("tp", description: "Teleport to a scene object by name")]
    public static void ToObject(string name = "")
    {
        for (var sceneIndex = 0; sceneIndex < UnityEngine.SceneManagement.SceneManager.sceneCount; sceneIndex++)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    Go(child.position, $"'{child.name}' in {scene.name}");
                    return;
                }
            }
        }

        ConsoleScreen.LogError($"No object named '{name}'");
    }

    [ConsoleCommand("tpzone", description: "Teleport to a quest trigger by id")]
    public static void ToZone(string id = "")
    {
        foreach (var trigger in UnityEngine.Object.FindObjectsOfType<TriggerWithId>(true))
        {
            if (string.Equals(trigger.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                Go(trigger.transform.position, $"zone '{trigger.Id}'");
                return;
            }
        }

        ConsoleScreen.LogError($"No trigger with id '{id}'");
    }

    [ConsoleCommand("tpbot", description: "Teleport next to a living bot by role or nickname")]
    public static void ToBot(string text = "")
    {
        var world = Singleton<GameWorld>.Instance;
        var players = world == null ? null : world.AllAlivePlayersList;
        var roles = new System.Collections.Generic.Dictionary<string, int>();
        for (var i = 0; players != null && i < players.Count; i++)
        {
            var bot = players[i];
            if (bot == null || bot == world.MainPlayer)
            {
                continue;
            }

            var role = bot.Profile.Info.Settings.Role.ToString();
            roles[role] = roles.TryGetValue(role, out var count) ? count + 1 : 1;
            if (role.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || bot.Profile.Nickname.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Go(bot.Position + Vector3.right * 2f, $"{role} '{bot.Profile.Nickname}'");
                return;
            }
        }

        var summary = new System.Collections.Generic.List<string>();
        foreach (var pair in roles)
        {
            summary.Add($"{pair.Key} x{pair.Value}");
        }

        ConsoleScreen.LogError($"No living bot matching '{text}'. Alive: {(summary.Count == 0 ? "none" : string.Join(", ", summary))}");
    }

    [ConsoleCommand("tpdoor", description: "Teleport to a door or other interactive object by id")]
    public static void ToDoor(string id = "")
    {
        foreach (var door in UnityEngine.Object.FindObjectsOfType<WorldInteractiveObject>(true))
        {
            if (string.Equals(door.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                Go(door.transform.position - door.transform.forward, $"door '{door.Id}' ({door.DoorState})");
                return;
            }
        }

        ConsoleScreen.LogError($"No interactive object with id '{id}'");
    }

    private static bool Holds(LootableContainer container, string text)
    {
        var root = container.ItemOwner == null ? null : container.ItemOwner.RootItem;
        var items = root == null ? null : root.GetAllItems();
        return items != null && items.Any(item => item != null && item.TemplateId.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase));
    }

    [ConsoleCommand("tpcontainer", description: "Teleport to a container by its template, name, or an item template inside it")]
    public static void ToContainer(string text = "")
    {
        var scanned = 0;
        foreach (var container in UnityEngine.Object.FindObjectsOfType<LootableContainer>(true))
        {
            scanned++;
            var template = container.Template ?? "";
            if (template.StartsWith(text, StringComparison.OrdinalIgnoreCase) || container.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || Holds(container, text))
            {
                Go(container.transform.position - container.transform.forward, $"container {template} '{container.name}'");
                return;
            }
        }

        ConsoleScreen.LogError($"No container matching '{text}' among {scanned}");
    }

    private static bool Matches(LootItem loot, string text)
    {
        var item = loot == null ? null : loot.Item;
        if (item == null)
        {
            return false;
        }

        var template = item.TemplateId.ToString();
        return template.StartsWith(text, StringComparison.OrdinalIgnoreCase) || loot.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    [ConsoleCommand("tpitem", description: "Teleport to a loose item by template id or name")]
    public static void ToItem(string text = "")
    {
        var world = Singleton<GameWorld>.Instance;
        if (world == null)
        {
            ConsoleScreen.LogError("No raid");
            return;
        }

        // Quest items live in their own set. The world list holds the rest of the loose loot.
        var quest = world.QuestItemsList;
        foreach (var loot in quest ?? Enumerable.Empty<LootItem>())
        {
            if (Matches(loot, text))
            {
                Go(loot.transform.position, $"quest item {loot.Item.TemplateId} '{loot.name}'");
                return;
            }
        }

        var items = world.LootItems;
        for (var i = 0; items != null && i < items.Count; i++)
        {
            var loot = items.GetByIndex(i);
            if (Matches(loot, text))
            {
                Go(loot.transform.position, $"item {loot.Item.TemplateId} '{loot.name}'");
                return;
            }
        }

        ConsoleScreen.LogError($"No loose item matching '{text}' among {(quest == null ? 0 : quest.Count)} quest and {(items == null ? 0 : items.Count)} world items");
    }

    // Fires a trigger the way a zone would, so a chain can be tested without standing in it.
    [ConsoleCommand("trigger", description: "Fire a game trigger by id as the main player")]
    public static void FireTrigger(string id = "")
    {
        var world = Singleton<GameWorld>.Instance;
        var player = world == null ? null : world.MainPlayer;
        if (player == null || world.TriggersEmitter == null)
        {
            ConsoleScreen.LogError("No raid");
            return;
        }

        world.TriggersEmitter.Emit(id, player.RaidId);
        ConsoleScreen.Log($"Fired trigger '{id}'");
    }

    [ConsoleCommand("zones", description: "Report every rally zone and whether the player is inside it")]
    public static void ReportZones()
    {
        var world = Singleton<GameWorld>.Instance;
        var player = world == null ? null : world.MainPlayer;
        if (player == null)
        {
            ConsoleScreen.LogError("No raid");
            return;
        }

        var emitter = world.TriggersEmitter;
        var ignoreSet = emitter == null ? null : emitter._ignoreTriggers;
        var ignored = ignoreSet == null ? new System.Collections.Generic.List<string>() : ignoreSet.ToList();

        ConsoleScreen.Log($"world {world.GetIl2CppType().Name}, emitter {(emitter == null ? "none" : emitter.GetIl2CppType().Name)}, ignored triggers: {(ignored.Count == 0 ? "none" : string.Join(", ", ignored))}");
        foreach (var zone in UnityEngine.Object.FindObjectsOfType<TriggerRallyZone>(true))
        {
            var inside = false;
            var colliders = zone.GetComponentsInChildren<Collider>(true);
            foreach (var collider in colliders)
            {
                inside |= collider.enabled && collider.bounds.Contains(player.Position);
            }

            ConsoleScreen.Log($"rally '{zone._triggerId}' object '{zone.name}' active {zone.gameObject.activeInHierarchy} enabled {zone.enabled} authority {zone.HasAuthority()} colliders {colliders.Length} player inside {inside} localInZone {zone._localPlayerInZone} triggered {zone._wasTriggered} at {zone.transform.position} player at {player.Position}");
            foreach (var collider in colliders)
            {
                ConsoleScreen.Log($"  collider '{collider.name}' {collider.GetIl2CppType().Name} enabled {collider.enabled} trigger {collider.isTrigger} layer {collider.gameObject.layer} centre {collider.bounds.center} size {collider.bounds.size}");
            }
        }

        foreach (var flare in UnityEngine.Object.FindObjectsOfType<TriggerFlareSuccess>(true))
        {
            ConsoleScreen.Log($"flare trigger '{flare._triggerId}' object '{flare.name}' wants {flare._flareEventType} at {flare.transform.position}");
        }

        foreach (var handler in UnityEngine.Object.FindObjectsOfType<HandlerExfiltration>(true))
        {
            var exit = handler._exfiltrationPoint;
            ConsoleScreen.Log($"exfil handler '{handler._triggerId}' sets '{(exit == null || exit.Settings == null ? "none" : exit.Settings.Name)}' to {handler._targetStatus}");
        }

        foreach (var gate in UnityEngine.Object.FindObjectsOfType<HandlerEnsureAllPlayersHasQuest>(true))
        {
            ConsoleScreen.Log($"quest gate '{gate._triggerId}' needs quest '{gate._questTemplateId}', then '{gate._outputAllPlayersHasQuestTriggerId}' else '{gate._outputAnyPlayerHasNoQuestTriggerId}'");
        }

        foreach (var exit in UnityEngine.Object.FindObjectsOfType<ExfiltrationPoint>(true))
        {
            ConsoleScreen.Log($"exit '{(exit.Settings == null ? exit.name : exit.Settings.Name)}' status {exit.Status} at {exit.transform.position}");
        }

        foreach (var handler in UnityEngine.Object.FindObjectsOfType<BaseTriggerHandler>(true))
        {
            ConsoleScreen.Log($"handler {handler.GetIl2CppType().Name} '{handler.name}' in [{Join(handler.InputTriggerIds)}] out [{Join(handler.OutputTriggerIds)}]");
        }
    }

    [ConsoleCommand("tprally", description: "Teleport into the first rally zone collider")]
    public static void ToRally()
    {
        foreach (var zone in UnityEngine.Object.FindObjectsOfType<TriggerRallyZone>(true))
        {
            foreach (var collider in zone.GetComponentsInChildren<Collider>(true))
            {
                Go(collider.bounds.center - Vector3.up * (collider.bounds.extents.y - 0.2f), $"rally zone '{zone._triggerId}' collider '{collider.name}'");
                return;
            }
        }

        ConsoleScreen.LogError("No rally zone collider found");
    }

    [ConsoleCommand("tpexit", description: "Teleport to an extraction point by name")]
    public static void ToExit(string name = "")
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var exit in UnityEngine.Object.FindObjectsOfType<ExfiltrationPoint>(true))
        {
            var exitName = exit.Settings == null ? exit.name : exit.Settings.Name;
            names.Add(exitName);
            if (exitName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Go(exit.transform.position, $"exit '{exitName}' ({exit.Status})");
                return;
            }
        }

        ConsoleScreen.LogError($"No exit matching '{name}'. Exits: {string.Join(", ", names)}");
    }

    private static string Join(Il2CppSystem.Collections.Generic.IEnumerable<string> ids)
    {
        return ids == null ? "" : string.Join(", ", ids);
    }
}
