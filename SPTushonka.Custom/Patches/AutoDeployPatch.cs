using System;
using System.Reflection;
using System.Runtime.InteropServices;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPTushonka.Reflection.Patching;

namespace SPTushonka.Custom.Patches;

// Test harness: with SPTUSHONKA_AUTODEPLOY set to a location id, the client picks the PvE profile and
// deploys there on its own, so a raid load can be reproduced without driving the UI by hand.
public static class AutoDeployPatches
{
    // A file next to the exe rather than an environment variable: Start-Process inheritance
    // proved unreliable here.
    private static readonly string Location = ReadLocation();

    private static string ReadLocation()
    {
        try
        {
            var path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "autodeploy.txt");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static TarkovApplication _app;
    private static Il2CppSystem.Threading.Tasks.Task<CharacterSelectionDataResponse> _selectionTask;
    private static bool _selected;
    private static bool _menuReady;
    private static bool _fired;
    private static bool _ready;
    private static int _frames;

    [DllImport("GameAssembly.dll")] private static extern void il2cpp_gc_disable();

    public static bool Enabled
    {
        get { return !string.IsNullOrEmpty(Location); }
    }

    public static void Patch()
    {
        new TracePatch("GamePrepare").Enable();
        new TracePatch("LocalGameCreate").Enable();
        new TracePatch("GetProfileForLocalGame").Enable();
        new SelectionCapturePatch().Enable();
        new MenuReadyPatch().Enable();
        new TickPatch().Enable();
    }

    // ShowCharacterSelectionScreen cannot be patched: its Nullable<EGameMode> parameter breaks
    // Il2CppInterop's native->managed trampoline. This one takes no parameters.
    // Trace which raid-creation steps actually run.
    public class TracePatch : ModulePatch
    {
        private static string _name;
        private readonly string _target;

        public TracePatch(string target)
        {
            _target = target;
        }

        protected override MethodBase GetTargetMethod()
        {
            _name = _target;
            return AccessTools.Method(typeof(TarkovApplication), _target);
        }

        [PatchPrefix]
        private static void PatchPrefix(MethodBase __originalMethod)
        {
            Logger.LogMessage("autodeploy: -> " + __originalMethod.Name);
        }
    }

    public class SelectionCapturePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.LoadCharacterSelectionData));
        }

        [PatchPostfix]
        private static void PatchPostfix(TarkovApplication __instance, Il2CppSystem.Threading.Tasks.Task<CharacterSelectionDataResponse> __result)
        {
            _app = __instance;
            _selectionTask = __result;
            _frames = 0;
            Logger.LogMessage("autodeploy: character selection data requested");
        }
    }

    public class MenuReadyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.MainMenu));
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            _menuReady = true;
            _frames = 0;
        }
    }

    public class TickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TarkovApplication), "Update");
        }

        [PatchPostfix]
        private static void PatchPostfix(TarkovApplication __instance)
        {
            if (!_selected && _selectionTask != null)
            {
                if (!_selectionTask.IsCompleted || ++_frames < 240)
                {
                    return;
                }

                _selected = true;
                _frames = 0;
                SelectPve();
                return;
            }

            if (_fired)
            {
                // LocalGameMatching stops on the "time has come" screen, which waits for READY.
                if (!_ready && ++_frames > 300)
                {
                    _ready = true;
                    try
                    {
                        ClickReady();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"autodeploy: ready failed: {ex}");
                    }
                }

                return;
            }

            if (!_menuReady)
            {
                return;
            }

            // The menu keeps loading after MainMenu returns; deploying too early lands in a
            // half-built session.
            if (++_frames < 600)
            {
                return;
            }

            _fired = true;
            _frames = 0;
            Deploy(__instance);
        }

        private static void SelectPve()
        {
            try
            {
                var data = _selectionTask.Result;
                if (data == null || !data.TryGetValue(EGameMode.Pve, out var profile))
                {
                    Logger.LogError("autodeploy: no PvE slot in the selection data");
                    return;
                }

                Logger.LogMessage("autodeploy: selecting PvE profile");
                _app.ExecuteCharacterSelection(
                    new CharacterSelectionScreen.CharacterSelectionResult(EGameMode.Pve, profile, false),
                    true
                );
            }
            catch (Exception ex)
            {
                Logger.LogError($"autodeploy: selection failed: {ex}");
            }
        }

        // The real READY button goes through a UnityEvent; driving it keeps the game on the same
        // path the UI uses, which is what actually builds the world.
        private static void ClickReady()
        {
            var buttons = UnityEngine.Object.FindObjectsOfType<EFT.UI.DefaultUIButton>();
            EFT.UI.DefaultUIButton ready = null;

            foreach (var button in buttons)
            {
                if (button == null || !button.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var name = button.gameObject.name ?? string.Empty;
                Logger.LogMessage("autodeploy: button '" + name + "' interactable=" + button.Interactable);

                if (button.Interactable && name.IndexOf("ready", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ready = button;
                }
            }

            if (ready == null)
            {
                Logger.LogError("autodeploy: no READY button found");
                return;
            }

            Logger.LogMessage("autodeploy: clicking '" + ready.gameObject.name + "'");
            ready.OnClick.Invoke();
        }

        private static void Deploy(TarkovApplication app)
        {
            try
            {
                if (!app.TryGetLocationById(Location, out var location))
                {
                    Logger.LogError($"autodeploy: location '{Location}' not found");
                    return;
                }

                var raidSettings = app._raidSettings;
                raidSettings.SelectedLocation = location;
                raidSettings.RaidMode = ERaidMode.Local;
                raidSettings.IsPveOffline = true;

                if (Environment.GetEnvironmentVariable("SPTUSHONKA_NOGC") != null || System.IO.File.Exists("nogc.txt"))
                {
                    il2cpp_gc_disable();
                    Logger.LogMessage("autodeploy: il2cpp GC disabled for the load");
                }

                Logger.LogMessage($"autodeploy: deploying to {Location}");
                app.LocalGameMatching(new TimeAndWeatherSettings(), false);
            }
            catch (Exception ex)
            {
                Logger.LogError($"autodeploy: deploy failed: {ex}");
            }
        }
    }
}
