using System;
using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.UI;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;

namespace SPTushonka.Debugging.Scripts;

// Top right overlay listing the alive bots per zone with role, side, difficulty and distance.
// Injected into il2cpp so Unity drives OnGUI. The list is rebuilt every frame instead of tracking
// spawn events, which keeps it correct when bots are removed outside the spawner.
public class BotMonitor(IntPtr pointer) : MonoBehaviour(pointer)
{
    private static GameObject _host;
    private GUIStyle _style;
    private GUIContent _content;
    private readonly StringBuilder _text = new();

    public static void Toggle()
    {
        if (_host != null)
        {
            Destroy(_host);
            _host = null;
            ConsoleScreen.Log("Bot monitor off");
            return;
        }

        if (Singleton<GameWorld>.Instance == null)
        {
            ConsoleScreen.LogError("No raid");
            return;
        }

        _host = new GameObject("SPTushonka.BotMonitor");
        _host.AddComponent<BotMonitor>();
        ConsoleScreen.Log("Bot monitor on");
    }

    public void OnGUI()
    {
        try
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null || world.MainPlayer == null)
            {
                Stop();
                return;
            }

            _style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                margin = new RectOffset(3, 3, 3, 3),
            };
            _content ??= new GUIContent();
            _content.text = Describe(world);

            var size = _style.CalcSize(_content);
            GUI.Box(new Rect(Screen.width - size.x - 5f, 60f, size.x, size.y), _content, _style);
        }
        catch (Exception ex)
        {
            ConsoleScreen.LogError($"Bot monitor stopped: {ex.Message}");
            Stop();
        }
    }

    [HideFromIl2Cpp]
    private void Stop()
    {
        Destroy(gameObject);
        _host = null;
    }

    [HideFromIl2Cpp]
    private string Describe(GameWorld world)
    {
        _text.Clear();

        var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
        if (spawner != null)
        {
            _text.Append($"Alive & loading = {spawner.AliveAndLoadingBotsCount}\n");
            _text.Append($"Delayed = {spawner.BotsDelayed}\n");
            _text.Append($"All with delayed = {spawner.AllBotsWithDelayed}\n");
        }

        var eye = world.MainPlayer.CameraPosition;
        var origin = eye == null ? world.MainPlayer.Position : eye.position;
        var zones = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var players = world.AllAlivePlayersList;
        for (var i = 0; players != null && i < players.Count; i++)
        {
            var bot = players[i];
            if (bot == null || bot.IsYourPlayer)
            {
                continue;
            }

            var zone = bot.AIData?.BotOwner?.BotsGroup?.BotZone?.NameZone ?? "no zone";
            var settings = bot.Profile.Info.Settings;
            var line = $"> [{Vector3.Distance(bot.Position, origin):n1}m] [{settings.Role}] [{bot.Profile.Side}] [{settings.BotDifficulty}] {bot.Profile.Nickname}";
            if (!zones.TryGetValue(zone, out var lines))
            {
                zones[zone] = lines = [];
            }

            lines.Add(line);
        }

        foreach (var pair in zones)
        {
            _text.Append($"{pair.Key} = {pair.Value.Count}\n");
            foreach (var line in pair.Value)
            {
                _text.Append(line).Append('\n');
            }
        }

        return _text.ToString().TrimEnd('\n');
    }
}
