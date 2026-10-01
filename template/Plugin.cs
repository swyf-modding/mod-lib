using System.Threading;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ScamWYF.Modding.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ExampleMod
{
    /// <summary>
    /// The smallest mod that uses every shared service. Copy this folder, rename the namespace and
    /// the guid, and you have a working mod.
    /// </summary>
    /// <remarks>
    /// Note what is not here: no Awake, no Update, no OnGUI. ScamMod seals those. OnModLoad runs
    /// instead, exceptions in it are logged instead of taking the session down, and anything the
    /// mod registered with the shared services is unregistered when it unloads.
    /// </remarks>
    [BepInPlugin(PluginGuid, "Example Mod", "1.0.0")]
    public sealed class Plugin : ScamMod
    {
        public const string PluginGuid = "com.example.scamwyf.examplemod";

        // Unity build this was written against. A mismatch is a warning, not an error.
        private const string BuiltAgainst = "6000.3.10f1";

        private ImGuiHost.Window _window;
        private HarmonyMethod _prefix;

        protected override void OnModLoad()
        {
            GameBuild.CheckUnityVersion(ModLog, ModId, BuiltAgainst);

            var settings = new ModSettings(base.Config, ModLog, ModId);
            var toggle = settings.BindKey("General", "ToggleKey", Key.F1, "Opens this mod's window.");
            var greetingLength = settings.Bind("General", "GreetingLength", 24, "How long the greeting is.");

            _prefix = new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(Prefix)));

            if (!PatchCoordinator.TryPatch(
                    this,
                    typeof(KolkataApi),
                    "CompleteOpenRouterAsync",
                    new[] { typeof(JObject), typeof(CancellationToken), typeof(bool) },
                    "count AI requests",
                    _prefix))
            {
                // The method is gone, so the game was updated. Carry on without the patch rather
                // than disabling the whole mod over one feature.
                ModLog.LogWarning("Running without the request counter.");
            }

            _window = ImGuiHost.AddWindow(this, "Example", new Rect(60f, 60f, 420f, 200f), Draw);

            Hotkeys.Register(this, toggle.Value, "Toggle the example window", _window.Toggle);

            ModLog.LogInfo("Greeting length is " + settings.Int(greetingLength, 1, 200) + ".");
        }

        protected override void OnModUnload()
        {
            // Harmony patches, hotkeys and windows are cleaned up by ScamMod. Anything else you
            // started - coroutines, sockets, DontDestroyOnLoad objects - is yours to stop here.
        }

        private static bool Prefix()
        {
            // Return false to skip the original method.
            return true;
        }

        private void Draw(int id)
        {
            GUILayout.Label("Hello. This window is drawn by the shared IMGUI host.");
            if (GUILayout.Button("Close")) _window.Visible = false;
        }
    }
}