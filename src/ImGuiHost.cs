using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScamWYF.Modding.Core
{
    /// <summary>
    /// One OnGUI for every mod, one place where windows are stacked and focused.
    /// </summary>
    /// <remarks>
    /// Unity calls OnGUI on every enabled component, so two mods each with their own overlay
    /// works right up until they overlap and fight over focus and depth. Register windows here
    /// instead: the first one added is the top one, and both draw cleanly.
    /// </remarks>
    public static class ImGuiHost
    {
        /// <summary>A window one mod draws into.</summary>
        public sealed class Window
        {
            private readonly GUI.WindowFunction _draw;

            /// <summary>Cached delegate: GUILayout.Window takes one, and this runs every frame.</summary>
            internal GUI.WindowFunction DrawFunction { get { return _draw; } }

            public string OwnerId;
            public string OwnerName;
            public string Title;
            public Rect Rect;
            public bool Visible;

            /// <summary>
            /// Draw contents. The int is the window id, needed by GUILayout.Window.
            /// </summary>
            public Action<int> Draw;

            /// <summary>Area that drags the window, in window coordinates. Usually the title bar.</summary>
            public Rect DragRect = new Rect(0f, 0f, 100000f, 20f);

            internal Window(string ownerId, string ownerName, string title, Rect rect, Action<int> draw)
            {
                OwnerId = ownerId;
                OwnerName = ownerName;
                Title = title;
                Rect = rect;
                Draw = draw;
                _draw = Render;
            }

            private void Render(int id)
            {
                if (Draw != null) Draw(id);
            }

            public void Toggle()
            {
                Visible = !Visible;
            }

            public override string ToString()
            {
                return OwnerName + " window '" + Title + "'";
            }
        }

        private sealed class Runner : MonoBehaviour
        {
            private void OnGUI()
            {
                ImGuiHost.Render();
            }
        }

        private static readonly List<Window> Windows = new List<Window>();
        private static Runner _runner;

        /// <summary>Registered windows, topmost first.</summary>
        public static Window[] All
        {
            get { lock (Windows) { return Windows.ToArray(); } }
        }

        /// <summary>
        /// Add a window. It starts hidden; set Visible, or call Toggle from a hotkey.
        /// </summary>
        public static Window AddWindow(ScamMod owner, string title, Rect rect, Action<int> draw)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (draw == null) throw new ArgumentNullException("draw");

            var window = new Window(owner.ModId, owner.DisplayName, title, rect, draw);
            lock (Windows) { Windows.Add(window); }

            EnsureRunner();
            owner.ModLog.LogInfo("Registered window '" + title + "'.");
            return window;
        }

        /// <summary>Remove a mod's windows. Called for you when a mod unloads.</summary>
        public static void RemoveOwner(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (Windows)
            {
                Windows.RemoveAll(window => window.OwnerId == modId);
            }
        }

        // ---------------------------------------------------------------- internals

        private static void Render()
        {
            Window[] snapshot;
            lock (Windows) { snapshot = Windows.ToArray(); }

            // Unity 6000.3 exposes GUIUtility.guiDepth as read-only, so the host cannot force a
            // z-order. Windows are drawn in registration order and Unity stacks them; keeping
            // overlays from overlapping is the mod's job, not something the host can enforce.
            foreach (var window in snapshot)
            {
                if (!window.Visible) continue;

                window.Rect = GUILayout.Window(
                    window.GetHashCode(), window.Rect, window.DrawFunction, window.Title);

                // DragWindow only acts on the focused window, so every window can ask for it.
                GUI.DragWindow(window.DragRect);
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;

            var host = new GameObject("ScamWYF.Modding.ImGui");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            _runner = host.AddComponent<Runner>();
        }
    }
}