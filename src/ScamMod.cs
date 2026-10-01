using System;
using BepInEx;
using BepInEx.Logging;

namespace ScamWYF.Modding.Core
{
    /// <summary>Where a mod got to during load.</summary>
    public enum ModState
    {
        /// <summary>Registered, but OnModLoad has not finished.</summary>
        Loading,

        /// <summary>Loaded and running.</summary>
        Active,

        /// <summary>OnModLoad threw. The mod is inert and says why.</summary>
        Failed,

        /// <summary>ModRegistry rejected it, usually a duplicate id.</summary>
        Rejected
    }

    /// <summary>
    /// Base class for a Scam With Your Friends mod. Gives every mod the same identity, the same
    /// failure handling and the same shared services, so two mods cannot quietly fight over the
    /// same Harmony patch, the same hotkey or the same OnGUI.
    /// </summary>
    /// <remarks>
    /// Identity is read from the [BepInPlugin] attribute rather than restated in code, so the name
    /// the mod list shows and the name the rest of the session uses can never drift apart.
    ///
    /// Do not declare Awake/OnDestroy/Update: they are sealed here. Use OnModLoad and OnModUnload.
    /// </remarks>
    public abstract class ScamMod : BaseUnityPlugin
    {
        /// <summary>The [BepInPlugin] GUID. Unique per mod, enforced at load.</summary>
        public string ModId { get; private set; }

        public string DisplayName { get; private set; }

        public string ModVersion { get; private set; }

        public ModState State { get; private set; }

        /// <summary>Why the mod failed, or null. Shown in the mod list.</summary>
        public string FailureReason { get; private set; }

        /// <summary>This mod's own logger. The shared services log through it too.</summary>
        public ManualLogSource ModLog { get; private set; }

        private bool _hooksRan;

        private void Awake()
        {
            ModLog = Logger;
            State = ModState.Loading;

            var attribute = (BepInPlugin)Attribute.GetCustomAttribute(GetType(), typeof(BepInPlugin));
            if (attribute == null)
            {
                Fail("no [BepInPlugin] attribute on " + GetType().FullName +
                     ". Every mod needs one so the mod list can identify it.");
                return;
            }

            ModId = attribute.GUID ?? "";
            DisplayName = attribute.Name ?? "";
            ModVersion = attribute.Version != null ? attribute.Version.ToString() : "";

            if (ModId.Length == 0)
            {
                Fail("[BepInPlugin] has an empty GUID.");
                return;
            }

            if (!ModRegistry.Register(this))
            {
                State = ModState.Rejected;
                FailureReason = "duplicate mod id - another plugin already registered as " + ModId;
                ModLog.LogError(FailureReason + ". This copy is disabled; change the GUID in its [BepInPlugin].");
                enabled = false;
                return;
            }

            // From here on, everything this mod registered has to come back off on unload.
            _hooksRan = true;

            try
            {
                ModLog.LogInfo("Starting " + DisplayName + " " + ModVersion + " on " + GameBuild.Describe() + ".");
                OnModLoad();
                State = ModState.Active;
            }
            catch (Exception ex)
            {
                // A mod that throws during load must not take the session with it.
                Fail(ex);
                TearDown();
            }
        }

        private void OnDestroy()
        {
            if (!_hooksRan) return;
            _hooksRan = false;
            TearDown();
        }

        private void TearDown()
        {
            try
            {
                OnModUnload();
            }
            catch (Exception ex)
            {
                ModLog.LogError("OnModUnload threw: " + ex);
            }

            ModRegistry.Unregister(this);
            PatchCoordinator.UnpatchOwner(ModId);
            Hotkeys.RemoveOwner(ModId);
            ImGuiHost.RemoveOwner(ModId);
        }

        /// <summary>
        /// Do the work. Anything thrown here is logged and turns the mod inert instead of
        /// breaking the game.
        /// </summary>
        protected virtual void OnModLoad()
        {
        }

        /// <summary>Undo anything OnModLoad did. Shared services are cleaned up for you.</summary>
        protected virtual void OnModUnload()
        {
        }

        /// <summary>Give up cleanly: log why, record it for the mod list, and switch the mod off.</summary>
        protected void Fail(string reason)
        {
            FailureReason = reason;
            State = ModState.Failed;
            ModLog.LogError(DisplayName.Length > 0 ? DisplayName + ": " + reason : reason);
            enabled = false;
        }

        /// <summary>Give up cleanly because of an exception.</summary>
        protected void Fail(Exception ex)
        {
            Fail(ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
        }
    }
}