using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;
using UnityEngine;

namespace SPTushonka.Debugging.Commands;

// "noclip" switches the main player's character controller off and moves the player along the
// camera, floating in place otherwise. Movement keys as usual, space up, left control down, left
// shift fast, left shift with left control faster. The position is written after the player's own
// late update so nothing fights it.
//
// The ground check is a cast, not the controller, so flying into a floor counts as landing from
// wherever the fall began and the fall damage kills. The landing and the health controller's fall
// damage are both dropped while noclip is on and once more after it, because the fall height
// still dates from before it was switched on.
//
// The map still culls by distance and occlusion around the disabled controller. Holding the
// culling systems open was tried and did not cover it, so the view is left as the game draws it.
public static class NoclipCommand
{
    private const float Speed = 8f;
    private const float FastSpeed = 30f;
    private const float FasterSpeed = 100f;

    private static bool _enabled;
    private static bool _skipLanding;
    private static Vector3 _position;

    public static void MoveTo(Vector3 position)
    {
        _position = position;
    }

    public static void Toggle()
    {
        var player = Singleton<GameWorld>.Instance?.MainPlayer;
        if (player == null || player.CharacterController == null)
        {
            ConsoleScreen.LogError("No player in raid");
            return;
        }

        _enabled = !_enabled;
        _position = player.Transform.position;
        // The 1.1 controller rejects the collision setter. Disabling it is enough to pass through geometry.
        player.CharacterController.isEnabled = !_enabled;
        _skipLanding = true;
        ConsoleScreen.Log($"Noclip {(_enabled ? "on" : "off")}");
    }

    // HandleFall is virtual and reached through the vtable, so it is patched on its own rather
    // than trusting the landing callback to be the only caller.
    public class FallDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.HandleFall));
        }

        [PatchPrefix]
        public static bool Prefix(ActiveHealthController __instance, ref float __result)
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            var mine = player != null && player.ActiveHealthController == __instance;
            if (!mine || (!_enabled && !_skipLanding))
            {
                return true;
            }

            _skipLanding = false;
            __result = 0f;
            return false;
        }
    }

    public class LandingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "OnGrounded");
        }

        [PatchPrefix]
        public static bool Prefix(Player __instance)
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || player != __instance || (!_enabled && !_skipLanding))
            {
                return true;
            }

            _skipLanding = false;
            return false;
        }
    }

    public class LateUpdatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), nameof(Player.LateUpdate));
        }

        [PatchPostfix]
        public static void Postfix(Player __instance)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                var player = Singleton<GameWorld>.Instance?.MainPlayer;
                if (player == null || player != __instance || player.CameraPosition == null)
                {
                    return;
                }

                var camera = player.CameraPosition;
                var shift = Input.GetKey(KeyCode.LeftShift);
                var control = Input.GetKey(KeyCode.LeftControl);
                var move = Vector3.zero;
                if (Input.GetKey(KeyCode.W)) move += camera.forward;
                if (Input.GetKey(KeyCode.S)) move -= camera.forward;
                if (Input.GetKey(KeyCode.D)) move += camera.right;
                if (Input.GetKey(KeyCode.A)) move -= camera.right;
                if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
                if (control && !shift) move -= Vector3.up;
                var speed = shift ? (control ? FasterSpeed : FastSpeed) : Speed;
                _position += move.normalized * speed * Time.deltaTime;
                player.Transform.position = _position;
            }
            catch (Exception ex)
            {
                Logger.LogError($"noclip failed: {ex.Message}");
                _enabled = false;
            }
        }
    }
}
