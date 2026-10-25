using System;
using System.Collections.Generic;
using System.Text;

namespace ScamWYF.Modding.Core.Content
{
    /// <summary>
    /// Every custom scam, app, store product and sentinel registered by any mod sharing this
    /// library, with ownership attached so two mods adding the same id is a clear message
    /// rather than a silent overwrite.
    /// </summary>
    /// <remarks>
    /// Register during <see cref="ScamMod"/> load (OnModLoad), before the office scene builds the
    /// OS. Entries are merged into the game's own catalogs by Harmony patches installed on first
    /// registration, so the game lists, sells, prompts and pays out custom content exactly like
    /// its own. A mod that registers after the catalogs already loaded still works: the entry is
    /// injected into the live catalogs immediately, and desktop icons appear on scene reload.
    ///
    /// Ids are unique across ALL mods, case-insensitively: the second registration of an id
    /// fails with both owners named, and the first keeps working. Prefix ids with something
    /// yours (e.g. "moar-jury-duty") rather than claiming a generic word.
    ///
    /// Unloading removes the registrations so a later load starts clean; entries already merged
    /// into a running session stay until the scene reloads, which is why <see cref="ScamMod"/>
    /// calls <see cref="UnregisterOwner"/> for you on unload.
    /// </remarks>
    public static class ContentRegistry
    {
        /// <summary>One registered thing, with its owner attached.</summary>
        public sealed class Entry
        {
            public string Kind;
            public string Id;
            public string OwnerId;
            public string OwnerName;
            public object Spec;
            public string Json;

            /// <summary>
            /// For an app the mod draws: handed the app's content root inside its window every time
            /// the game builds or rebuilds that window. Null for every other entry.
            /// </summary>
            public System.Action<UnityEngine.UIElements.VisualElement> Draw;

            public override string ToString()
            {
                return Kind + " '" + Id + "' by " + OwnerName + " [" + OwnerId + "]";
            }
        }

        private static readonly List<Entry> ConversationScams = new List<Entry>();
        private static readonly List<Entry> ScamTypes = new List<Entry>();
        private static readonly List<Entry> Apps = new List<Entry>();
        private static readonly List<Entry> Products = new List<Entry>();
        private static readonly List<Entry> Sentinels = new List<Entry>();
        private static readonly List<string> Rejections = new List<string>();

        /// <summary>Every registered conversation scam, in registration order.</summary>
        public static Entry[] RegisteredConversationScams
        {
            get { lock (ConversationScams) { return ConversationScams.ToArray(); } }
        }

        public static Entry[] RegisteredScamTypes
        {
            get { lock (ScamTypes) { return ScamTypes.ToArray(); } }
        }

        public static Entry[] RegisteredApps
        {
            get { lock (Apps) { return Apps.ToArray(); } }
        }

        public static Entry[] RegisteredProducts
        {
            get { lock (Products) { return Products.ToArray(); } }
        }

        public static Entry[] RegisteredSentinels
        {
            get { lock (Sentinels) { return Sentinels.ToArray(); } }
        }

        /// <summary>
        /// Register a conversation scam (objectives that check off as you talk). The scam's AppId
        /// must have a matching app registered (contentType "conversationScam") and its ProductId
        /// a matching product, or the scam has no window and no way to unlock.
        /// </summary>
        public static bool RegisterConversationScam(ScamMod owner, ConversationScamSpec spec)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (spec == null)
            {
                owner.ModLog.LogWarning("Ignoring a null conversation scam from " + owner.DisplayName + ".");
                return false;
            }

            var problem = ContentValidation.CheckConversationScam(spec);
            if (problem != null)
            {
                Reject(owner, "conversation scam", spec.Id, problem);
                return false;
            }

            lock (ConversationScams)
            {
                if (!Claim(ConversationScams, owner, "conversation scam", spec.Id)) return false;
                ConversationScams.Add(NewEntry(owner, "conversation scam", spec.Id, spec, null));
            }

            AfterRegistered(owner, "conversation scam '" + spec.Id + "'");
            return true;
        }

        /// <summary>Register a scam type (a script entry pointing at a form app).</summary>
        public static bool RegisterScamType(ScamMod owner, ScamTypeSpec spec)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (spec == null)
            {
                owner.ModLog.LogWarning("Ignoring a null scam type from " + owner.DisplayName + ".");
                return false;
            }

            var problem = ContentValidation.CheckScamType(spec);
            if (problem != null)
            {
                Reject(owner, "scam type", spec.Id, problem);
                return false;
            }

            lock (ScamTypes)
            {
                if (!Claim(ScamTypes, owner, "scam type", spec.Id)) return false;
                ScamTypes.Add(NewEntry(owner, "scam type", spec.Id, spec, null));
            }

            AfterRegistered(owner, "scam type '" + spec.Id + "'");
            return true;
        }

        /// <summary>
        /// Register an OS app. Scam apps should use the OsAppSpec factories; an app whose window the
        /// mod draws itself uses the overload that takes a callback.
        /// </summary>
        public static bool RegisterApp(ScamMod owner, OsAppSpec spec)
        {
            return RegisterApp(owner, spec, null);
        }

        /// <summary>
        /// Register an OS app whose window body is drawn by the mod, for content the game has no
        /// builder for: a game, a calculator, a shop of your own.
        /// </summary>
        /// <remarks>
        /// The callback runs on the main thread every time the game builds the app's window - first
        /// open, a rebuild after an in-game event, a desktop rebuild - with a fresh content root
        /// inside it. Build into that element and nothing else: the previous contents are gone by
        /// the time it is called, so anything kept across rebuilds belongs in the mod, not in the
        /// tree. A callback that throws is logged and the app falls back to the game's placeholder
        /// body rather than leaving a half-built window.
        ///
        /// The spec must use <see cref="OsAppSpec.GameApp"/>, whose contentType is
        /// <see cref="OsAppSpec.ModContentType"/>: the game builds its own content types itself, and
        /// a callback attached to one of those would fight the game's builder.
        /// </remarks>
        public static bool RegisterApp(ScamMod owner, OsAppSpec spec,
            System.Action<UnityEngine.UIElements.VisualElement> draw)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (spec == null)
            {
                owner.ModLog.LogWarning("Ignoring a null app from " + owner.DisplayName + ".");
                return false;
            }

            if (draw == null && string.Equals(spec.ContentType, OsAppSpec.ModContentType,
                    StringComparison.Ordinal))
            {
                Reject(owner, "app", spec.Id, "contentType '" + OsAppSpec.ModContentType +
                                             "' means the mod draws this window, but no draw " +
                                             "callback was given. Use RegisterGame, or a " +
                                             "contentType the game builds itself.");
                return false;
            }

            var problem = draw != null
                ? ContentValidation.CheckGameApp(spec)
                : ContentValidation.CheckApp(spec);
            if (problem != null)
            {
                Reject(owner, draw != null ? "game" : "app", spec.Id, problem);
                return false;
            }

            lock (Apps)
            {
                if (!Claim(Apps, owner, "app", spec.Id)) return false;
                var entry = NewEntry(owner, "app", spec.Id, spec, null);
                entry.Draw = draw;
                Apps.Add(entry);
            }

            AfterRegistered(owner, draw != null
                ? "app '" + spec.Id + "' drawn by the mod"
                : "app '" + spec.Id + "'");
            return true;
        }

        /// <summary>
        /// Register a desktop app whose window the mod draws: a game or anything else the game has
        /// no builder for. Same as the draw-taking <see cref="RegisterApp"/> overload, named for
        /// what such a mod is usually adding.
        /// </summary>
        public static bool RegisterGame(ScamMod owner, OsAppSpec spec,
            System.Action<UnityEngine.UIElements.VisualElement> draw)
        {
            if (draw == null)
            {
                if (owner != null && spec != null)
                    Reject(owner, "game", spec.Id, "no draw callback, so the window would be empty.");
                return false;
            }

            return RegisterApp(owner, spec, draw);
        }

        /// <summary>
        /// Register an OS app from raw JSON, for shapes the spec factories do not cover. The JSON
        /// must be a single app definition object; appId names it for duplicate detection.
        /// </summary>
        public static bool RegisterAppJson(ScamMod owner, string appId, string json)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            var problem = ContentValidation.CheckId(appId, "app");
            if (problem != null)
            {
                Reject(owner, "app", appId, problem);
                return false;
            }
            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
            {
                Reject(owner, "app", appId, "empty JSON.");
                return false;
            }

            lock (Apps)
            {
                if (!Claim(Apps, owner, "app", appId)) return false;
                Apps.Add(NewEntry(owner, "app", appId, null, json));
            }

            AfterRegistered(owner, "app '" + appId + "' (raw JSON)");
            return true;
        }

        /// <summary>Register a store product (what gates a catalog app and what Scamazon sells).</summary>
        public static bool RegisterProduct(ScamMod owner, StoreProductSpec spec)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (spec == null)
            {
                owner.ModLog.LogWarning("Ignoring a null product from " + owner.DisplayName + ".");
                return false;
            }

            var problem = ContentValidation.CheckProduct(spec);
            if (problem != null)
            {
                Reject(owner, "product", spec.Id, problem);
                return false;
            }

            lock (Products)
            {
                if (!Claim(Products, owner, "product", spec.Id)) return false;
                Products.Add(NewEntry(owner, "product", spec.Id, spec, null));
            }

            AfterRegistered(owner, "product '" + spec.Id + "'");
            return true;
        }

        /// <summary>Register a product from raw JSON. productId names it for duplicate detection.</summary>
        public static bool RegisterProductJson(ScamMod owner, string productId, string json)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            var problem = ContentValidation.CheckId(productId, "product");
            if (problem != null)
            {
                Reject(owner, "product", productId, problem);
                return false;
            }
            if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
            {
                Reject(owner, "product", productId, "empty JSON.");
                return false;
            }

            lock (Products)
            {
                if (!Claim(Products, owner, "product", productId)) return false;
                Products.Add(NewEntry(owner, "product", productId, null, json));
            }

            AfterRegistered(owner, "product '" + productId + "' (raw JSON)");
            return true;
        }

        /// <summary>
        /// Register a caller value. Prefer reusing an existing tag; add one only for genuinely new
        /// information, with an appCompletionSource matching the app that collects it.
        /// </summary>
        public static bool RegisterSentinel(ScamMod owner, SentinelSpec spec)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (spec == null)
            {
                owner.ModLog.LogWarning("Ignoring a null sentinel from " + owner.DisplayName + ".");
                return false;
            }

            var problem = ContentValidation.CheckSentinel(spec);
            if (problem != null)
            {
                Reject(owner, "sentinel", spec.Tag, problem);
                return false;
            }

            lock (Sentinels)
            {
                if (!Claim(Sentinels, owner, "sentinel", spec.Tag)) return false;
                Sentinels.Add(NewEntry(owner, "sentinel", spec.Tag, spec, null));
            }

            AfterRegistered(owner, "sentinel '" + spec.Tag + "'");
            return true;
        }

        /// <summary>
        /// Register a whole conversation scam from a plan: the caller values it scores, the product
        /// that unlocks it, the app that shows it and the scam itself.
        /// </summary>
        /// <remarks>
        /// All of it or none of it. The plan is checked as a whole first - every part against the
        /// game's rules, and the ids and completion sources against each other - so the usual
        /// mistake of four separate registrations (a scam pointing at an app nobody registered) is
        /// one rejection with the reason, before anything reaches the game. A part that still fails
        /// to register takes the parts already added by this call back out with it.
        /// </remarks>
        public static bool RegisterConversationScam(ScamMod owner, ConversationScamPlan plan)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (plan == null)
            {
                owner.ModLog.LogWarning("Ignoring a null conversation scam plan from " +
                                        owner.DisplayName + ".");
                return false;
            }

            var problem = ContentValidation.CheckConversationScamPlan(plan);
            if (problem != null)
            {
                Reject(owner, "conversation scam plan", plan.Scam != null ? plan.Scam.Id : null,
                    problem);
                return false;
            }

            var added = new List<Added>();
            try
            {
                if (plan.Sentinels != null)
                {
                    foreach (var sentinel in plan.Sentinels)
                    {
                        if (!RegisterSentinel(owner, sentinel)) return RollBack(added, owner.ModId);
                        added.Add(new Added(Sentinels, sentinel.Tag));
                    }
                }
                if (!RegisterProduct(owner, plan.Product)) return RollBack(added, owner.ModId);
                added.Add(new Added(Products, plan.Product.Id));
                if (!RegisterApp(owner, plan.App)) return RollBack(added, owner.ModId);
                added.Add(new Added(Apps, plan.App.Id));
                if (!RegisterConversationScam(owner, plan.Scam)) return RollBack(added, owner.ModId);
            }
            catch (Exception ex)
            {
                owner.ModLog.LogWarning("Could not register conversation scam plan '" + plan.Scam.Id +
                                        "': " + ex.Message);
                return RollBack(added, owner.ModId);
            }

            return true;
        }

        /// <summary>
        /// Register a whole form scam from a plan: the caller value, the product, the app that
        /// collects it and the scam type that points the caller script at all of them. All of it or
        /// none of it, for the same reasons as <see cref="RegisterConversationScam(ScamMod,
        /// ConversationScamPlan)"/>.
        /// </summary>
        public static bool RegisterFormScam(ScamMod owner, FormScamPlan plan)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (plan == null)
            {
                owner.ModLog.LogWarning("Ignoring a null form scam plan from " + owner.DisplayName +
                                        ".");
                return false;
            }

            var problem = ContentValidation.CheckFormScamPlan(plan);
            if (problem != null)
            {
                Reject(owner, "form scam plan", plan.ScamType != null ? plan.ScamType.Id : null,
                    problem);
                return false;
            }

            var added = new List<Added>();
            try
            {
                if (plan.Sentinel != null)
                {
                    if (!RegisterSentinel(owner, plan.Sentinel)) return RollBack(added, owner.ModId);
                    added.Add(new Added(Sentinels, plan.Sentinel.Tag));
                }
                if (!RegisterProduct(owner, plan.Product)) return RollBack(added, owner.ModId);
                added.Add(new Added(Products, plan.Product.Id));
                if (!RegisterApp(owner, plan.App)) return RollBack(added, owner.ModId);
                added.Add(new Added(Apps, plan.App.Id));
                if (!RegisterScamType(owner, plan.ScamType)) return RollBack(added, owner.ModId);
            }
            catch (Exception ex)
            {
                owner.ModLog.LogWarning("Could not register form scam plan '" + plan.ScamType.Id +
                                        "': " + ex.Message);
                return RollBack(added, owner.ModId);
            }

            return true;
        }

        /// <summary>One mod's entries, for its own menu page.</summary>
        public static Entry[] ForOwner(string ownerId)
        {
            var mine = new List<Entry>();
            if (string.IsNullOrEmpty(ownerId)) return mine.ToArray();
            lock (ConversationScams) { Collect(ConversationScams, ownerId, mine); }
            lock (ScamTypes) { Collect(ScamTypes, ownerId, mine); }
            lock (Apps) { Collect(Apps, ownerId, mine); }
            lock (Products) { Collect(Products, ownerId, mine); }
            lock (Sentinels) { Collect(Sentinels, ownerId, mine); }
            return mine.ToArray();
        }

        /// <summary>Drop everything one mod registered. Called for you when a mod unloads.</summary>
        public static void UnregisterOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return;
            int removed = 0;
            lock (ConversationScams) { removed += RemoveOwned(ConversationScams, ownerId); }
            lock (ScamTypes) { removed += RemoveOwned(ScamTypes, ownerId); }
            lock (Apps) { removed += RemoveOwned(Apps, ownerId); }
            lock (Products) { removed += RemoveOwned(Products, ownerId); }
            lock (Sentinels) { removed += RemoveOwned(Sentinels, ownerId); }
            if (removed > 0)
            {
                LibraryLog.Info("ContentRegistry dropped " + removed + " entr" +
                                (removed == 1 ? "y" : "ies") + " from " + ownerId +
                                ". Already-built catalogs keep them until the scene reloads.");
            }
        }

        /// <summary>Rejected registrations, one line each, or null when there are none.</summary>
        public static string ConflictReport()
        {
            lock (Rejections)
            {
                if (Rejections.Count == 0) return null;
                var report = new StringBuilder();
                foreach (var rejection in Rejections) report.AppendLine(rejection);
                return report.ToString();
            }
        }

        // ---------------------------------------------------------------- internals

        /// <summary>Something one registration call added, so a later failure can take it back out.</summary>
        private struct Added
        {
            public readonly List<Entry> Entries;
            public readonly string Id;

            public Added(List<Entry> entries, string id)
            {
                Entries = entries;
                Id = id;
            }
        }

        /// <summary>Undo a bundle that failed part way through. False, for the caller's return.</summary>
        private static bool RollBack(List<Added> added, string ownerId)
        {
            for (int i = added.Count - 1; i >= 0; i--)
            {
                var list = added[i].Entries;
                var id = added[i].Id;
                lock (list)
                {
                    list.RemoveAll(delegate (Entry entry)
                    {
                        return string.Equals(entry.Id, id, StringComparison.Ordinal) &&
                               string.Equals(entry.OwnerId, ownerId, StringComparison.Ordinal);
                    });
                }
            }
            added.Clear();
            return false;
        }

        /// <summary>
        /// The draw callback registered for an app id, or null. Used by the runtime patch that draws
        /// mod apps into the game's own window.
        /// </summary>
        internal static System.Action<UnityEngine.UIElements.VisualElement> FindDraw(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            lock (Apps)
            {
                foreach (var entry in Apps)
                {
                    if (entry.Draw == null) continue;
                    if (!string.Equals(entry.Id, appId, StringComparison.OrdinalIgnoreCase)) continue;
                    return entry.Draw;
                }
            }
            return null;
        }

        private static Entry NewEntry(ScamMod owner, string kind, string id, object spec, string json)
        {
            var entry = new Entry();
            entry.Kind = kind;
            entry.Id = id;
            entry.OwnerId = owner.ModId;
            entry.OwnerName = owner.DisplayName;
            entry.Spec = spec;
            entry.Json = json;
            return entry;
        }

        /// <summary>
        /// Claim an id across every owner. Must hold the kind's lock. False means someone else has
        /// it; the rejection names both mods.
        /// </summary>
        private static bool Claim(List<Entry> entries, ScamMod owner, string kind, string id)
        {
            foreach (var existing in entries)
            {
                if (!string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(existing.OwnerId, owner.ModId, StringComparison.Ordinal))
                {
                    Reject(owner, kind, id, "already registered by this mod.");
                    return false;
                }
                var message = kind + " '" + id + "' from " + owner.DisplayName + " [" + owner.ModId +
                              "] is already registered by " + existing.OwnerName + " [" +
                              existing.OwnerId + "]. Rename yours; the first registration keeps working.";
                lock (Rejections) { Rejections.Add("content collision: " + message); }
                owner.ModLog.LogWarning(message);
                return false;
            }
            return true;
        }

        private static void Reject(ScamMod owner, string kind, string id, string reason)
        {
            var message = "Ignoring " + kind + " '" + (id != null ? id : "?") + "' from " +
                          owner.DisplayName + ": " + reason;
            lock (Rejections) { Rejections.Add(message); }
            owner.ModLog.LogWarning(message);
        }

        private static void AfterRegistered(ScamMod owner, string what)
        {
            owner.ModLog.LogInfo("Registered " + what + " for " + owner.DisplayName + ".");
            ContentPatches.EnsureInstalled(owner);
            ContentPatches.ApplyToLiveGame(owner);
        }

        private static void Collect(List<Entry> entries, string ownerId, List<Entry> mine)
        {
            foreach (var entry in entries)
            {
                if (entry.OwnerId == ownerId) mine.Add(entry);
            }
        }

        private static int RemoveOwned(List<Entry> entries, string ownerId)
        {
            int before = entries.Count;
            entries.RemoveAll(delegate (Entry entry) { return entry.OwnerId == ownerId; });
            return before - entries.Count;
        }

        internal static class LibraryLog
        {
            internal static void Info(string message)
            {
                Write(BepInEx.Logging.LogLevel.Info, message);
            }

            internal static void Warn(string message)
            {
                Write(BepInEx.Logging.LogLevel.Warning, message);
            }

            private static void Write(BepInEx.Logging.LogLevel level, string message)
            {
                try
                {
                    BepInEx.Logging.Logger.CreateLogSource("ScamWYF.Modding").Log(level, message);
                }
                catch (Exception)
                {
                    // Logging must never break content handling.
                }
            }
        }
    }
}
