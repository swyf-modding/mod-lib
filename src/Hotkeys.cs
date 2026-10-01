using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ScamWYF.Modding.Core
{
    /// <summary>
    /// One keyboard poller for every mod.
    /// </summary>
    /// <remarks>
    /// Each mod polling Keyboard.current itself works until two mods want the same key, and then
    /// neither one gets it cleanly and there is nothing in the log saying why. Bindings are
    /// registered here instead, duplicates are reported, and the poll happens in one place.
    ///
    /// Both fire if two mods bind the same key: silently ignoring the second one would just move
    /// the confusion somewhere less obvious. The conflict is reported in the log and by the mod
    /// handler.
    /// </remarks>
    public static class Hotkeys
    {
        /// <summary>One key, optionally with modifiers, owned by one mod.</summary>
        public sealed class Binding
        {
            public string OwnerId;
            public string OwnerName;
            public string Description;
            public Key Key;
            public readonly List<Key> Modifiers = new List<Key>();
            public Action OnPressed;

            public string Signature
            {
                get { return Humanize() + (Modifiers.Count == 0 ? "" : " + " + Join(Modifiers)); }
            }

            /// <summary>What a person would call this binding, for logs and the mod list.</summary>
            public string Humanize()
            {
                return Key.ToString();
            }

            private string Join(List<Key> keys)
            {
                var parts = new string[keys.Count];
                for (int i = 0; i < keys.Count; i++) parts[i] = keys[i].ToString();
                return string.Join("+", parts);
            }

            public override string ToString()
            {
                return OwnerName + ": " + Signature + " - " + Description;
            }
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                Hotkeys.Pump();
            }
        }

        private static readonly List<Binding> Registry = new List<Binding>();
        private static Runner _runner;

        /// <summary>Every live binding, in registration order.</summary>
        public static Binding[] Bindings
        {
            get { lock (Registry) { return Registry.ToArray(); } }
        }

        /// <summary>
        /// Claim a key. <paramref name="modifiers"/> must all be held for the binding to fire;
        /// pass none for a bare key.
        /// </summary>
        public static Binding Register(ScamMod owner, Key key, string description, Action onPressed,
            params Key[] modifiers)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (onPressed == null) throw new ArgumentNullException("onPressed");

            var binding = new Binding
            {
                OwnerId = owner.ModId,
                OwnerName = owner.DisplayName,
                Description = description ?? "",
                Key = key,
                OnPressed = onPressed
            };

            if (modifiers != null)
            {
                foreach (var modifier in modifiers)
                {
                    if (modifier != Key.None && !binding.Modifiers.Contains(modifier))
                        binding.Modifiers.Add(modifier);
                }
            }

            List<Binding> clashes;
            lock (Registry)
            {
                clashes = Clashes(binding);
                Registry.Add(binding);
            }

            EnsureRunner();
            owner.ModLog.LogInfo("Hotkey " + binding.Signature + " - " + binding.Description + ".");

            if (clashes.Count > 0)
            {
                owner.ModLog.LogWarning(
                    binding.Signature + " is already bound by " + Join(clashes) +
                    ". Both will fire; give one of them a different key or a modifier.");
            }

            return binding;
        }

        /// <summary>Drop every binding a mod owns. Called for you when a mod unloads.</summary>
        public static void RemoveOwner(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;
            lock (Registry)
            {
                Registry.RemoveAll(binding => binding.OwnerId == modId);
            }
        }

        /// <summary>Keys claimed by more than one mod, as text, or null when there are none.</summary>
        public static string ConflictReport()
        {
            var report = new StringBuilder();

            foreach (var group in ClashGroups())
            {
                var owners = new List<string>();
                foreach (var binding in group) owners.Add(binding.OwnerName);
                report.AppendLine("hotkey collision on " + group[0].Signature + ": " +
                                  string.Join(" + ", owners.ToArray()));
            }

            return report.Length == 0 ? null : report.ToString();
        }

        // ---------------------------------------------------------------- internals

        private static void Pump()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Binding[] snapshot;
            lock (Registry) { snapshot = Registry.ToArray(); }

            foreach (var binding in snapshot)
            {
                var control = keyboard[binding.Key];
                if (control == null || !control.wasPressedThisFrame) continue;
                if (!ModifiersHeld(keyboard, binding)) continue;

                try
                {
                    binding.OnPressed();
                }
                catch (Exception ex)
                {
                    Log.LogError("Hotkey " + binding.Signature + " (" + binding.OwnerName + ") threw: " + ex);
                }
            }
        }

        private static bool ModifiersHeld(Keyboard keyboard, Binding binding)
        {
            for (int i = 0; i < binding.Modifiers.Count; i++)
            {
                var control = keyboard[binding.Modifiers[i]];
                if (control == null || !control.isPressed) return false;
            }
            return true;
        }

        private static List<Binding> Clashes(Binding candidate)
        {
            var clashes = new List<Binding>();
            foreach (var existing in Registry)
            {
                if (existing.OwnerId == candidate.OwnerId) continue;
                if (existing.Key != candidate.Key) continue;
                if (existing.Modifiers.Count != candidate.Modifiers.Count) continue;

                var same = true;
                for (int i = 0; i < existing.Modifiers.Count && same; i++)
                    same = existing.Modifiers.Contains(candidate.Modifiers[i]) &&
                           candidate.Modifiers.Contains(existing.Modifiers[i]);
                if (same) clashes.Add(existing);
            }
            return clashes;
        }

        /// <summary>
        /// Bindings sharing a key with a binding from a different mod, one list per key. A mod
        /// binding the same key twice is its own business and is not a collision.
        /// </summary>
        private static List<List<Binding>> ClashGroups()
        {
            var groups = new List<List<Binding>>();

            lock (Registry)
            {
                var seen = new List<Binding>();
                foreach (var binding in Registry)
                {
                    if (seen.Contains(binding)) continue;

                    var group = new List<Binding>();
                    foreach (var candidate in Registry)
                    {
                        if (candidate.Key != binding.Key) continue;
                        if (candidate.Modifiers.Count != binding.Modifiers.Count) continue;
                        if (Clashes(candidate).Count == 0) continue;
                        group.Add(candidate);
                    }

                    if (group.Count == 0) continue;
                    foreach (var member in group) seen.Add(member);
                    groups.Add(group);
                }
            }

            return groups;
        }

        private static string Join(List<Binding> bindings)
        {
            var names = new List<string>();
            foreach (var binding in bindings) names.Add(binding.OwnerName + " (" + binding.OwnerId + ")");
            return string.Join(", ", names.ToArray());
        }

        private static ManualLogSource _log;

        private static ManualLogSource Log
        {
            get
            {
                if (_log == null) _log = BepInEx.Logging.Logger.CreateLogSource("ScamWYF.Modding.Hotkeys");
                return _log;
            }
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;

            var host = new GameObject("ScamWYF.Modding.Hotkeys");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            _runner = host.AddComponent<Runner>();
        }
    }
}