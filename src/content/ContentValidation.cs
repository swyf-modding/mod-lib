using System;
using System.Collections.Generic;

namespace ScamWYF.Modding.Core.Content
{
    /// <summary>
    /// The rules a custom scam, app, product or sentinel must follow. Mirrors the checks the
    /// game itself runs when it loads its own files, so a spec rejected here would also have
    /// been rejected (or misbehaved) there.
    /// </summary>
    /// <remarks>
    /// Deliberately dependency-free: no Unity, no BepInEx, no JSON. The behaviour tests compile
    /// this file directly and run it on plain .NET, the same arrangement as the watcher and
    /// window geometry tests.
    ///
    /// Every method returns null when the spec is acceptable, or a single human-readable reason
    /// when it is not. The registry logs that reason with the owning mod's name; the game never
    /// sees the bad entry.
    /// </remarks>
    public static class ContentValidation
    {
        /// <summary>An id the game will accept: non-empty, no whitespace-only, under 128 chars.</summary>
        public static string CheckId(string value, string what)
        {
            if (string.IsNullOrEmpty(value)) return what + " has no id.";
            if (value.Trim().Length == 0) return what + " id is blank.";
            if (value.Length > 128) return what + " id '" + value + "' is over 128 characters.";
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c)) return what + " id '" + value + "' contains whitespace.";
            }
            return null;
        }

        public static string CheckScamObjective(ScamObjectiveSpec objective)
        {
            if (objective == null) return "objective is null.";
            var bad = CheckId(objective.Id, "objective");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(objective.Text) || objective.Text.Trim().Length == 0)
                return "objective '" + objective.Id + "' has no text.";
            if (string.IsNullOrEmpty(objective.Criteria) && string.IsNullOrEmpty(objective.Text))
                return "objective '" + objective.Id + "' has no criteria.";
            return null;
        }

        /// <summary>
        /// The game's own catalog rules: 2+ objectives, exactly one mandatory final, prerequisites
        /// name real objectives, no cycles, and the final requires every mandatory milestone.
        /// CallerPrompt is optional: current game files carry prompts in the app, not the scam.
        /// </summary>
        public static string CheckConversationScam(ConversationScamSpec spec)
        {
            if (spec == null) return "conversation scam is null.";
            var bad = CheckId(spec.Id, "conversation scam");
            if (bad != null) return bad;
            bad = CheckId(spec.ProductId, "conversation scam '" + spec.Id + "' product");
            if (bad != null) return bad;
            bad = CheckId(spec.AppId, "conversation scam '" + spec.Id + "' app");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(spec.Title) || spec.Title.Trim().Length == 0)
                return "conversation scam '" + spec.Id + "' has no title.";
            if (spec.Reward <= 0 || spec.Reward > 10000)
                return "conversation scam '" + spec.Id + "' reward " + spec.Reward +
                       " is outside 1-10000.";
            if (spec.RewardTiers != null)
            {
                if (spec.RewardTiers.Length == 0)
                    return "conversation scam '" + spec.Id + "' has empty reward tiers.";
                bool containsBase = false;
                foreach (int tier in spec.RewardTiers)
                {
                    if (tier == spec.Reward) containsBase = true;
                    if (tier < spec.Reward || tier > 10000)
                        return "conversation scam '" + spec.Id + "' has invalid reward tier " + tier + ".";
                }
                if (!containsBase)
                    return "conversation scam '" + spec.Id + "' reward tiers must contain the base reward.";
            }
            if (spec.Objectives == null || spec.Objectives.Length < 2)
                return "conversation scam '" + spec.Id + "' needs at least 2 objectives.";

            var ids = new Dictionary<string, ScamObjectiveSpec>(StringComparer.Ordinal);
            foreach (var objective in spec.Objectives)
            {
                bad = CheckScamObjective(objective);
                if (bad != null) return "conversation scam '" + spec.Id + "': " + bad;
                if (ids.ContainsKey(objective.Id))
                    return "conversation scam '" + spec.Id + "' has duplicate objective '" +
                           objective.Id + "'.";
                ids.Add(objective.Id, objective);
            }

            int finals = 0;
            foreach (var objective in spec.Objectives)
            {
                if (!objective.Final) continue;
                finals++;
                if (objective.Optional)
                    return "conversation scam '" + spec.Id + "' final objective '" + objective.Id +
                           "' must not be optional.";
            }
            if (finals != 1)
                return "conversation scam '" + spec.Id + "' needs exactly one mandatory final " +
                       "objective, found " + finals + ".";

            foreach (var objective in spec.Objectives)
            {
                var prereqs = objective.Prerequisites != null
                    ? objective.Prerequisites
                    : new string[0];
                foreach (var prereq in prereqs)
                {
                    if (prereq == objective.Id || !ids.ContainsKey(prereq))
                        return "conversation scam '" + spec.Id + "' objective '" + objective.Id +
                               "' has unknown or self prerequisite '" + prereq + "'.";
                }
                if (!string.IsNullOrEmpty(objective.RequiredSentinel) &&
                    !string.IsNullOrEmpty(spec.Sentinel) &&
                    !string.Equals(objective.RequiredSentinel, spec.Sentinel, StringComparison.Ordinal))
                    return "conversation scam '" + spec.Id + "' objective '" + objective.Id +
                           "' confirmation must use the scam's allowed sentinel.";
                if (objective.RequiresSubmission &&
                    (!objective.Final || string.IsNullOrEmpty(objective.RequiredSentinel) ||
                     spec.RewardTiers != null))
                    return "conversation scam '" + spec.Id + "' objective '" + objective.Id +
                           "': manual entry requires a final objective, a caller token and a fixed reward.";
            }

            foreach (var objective in spec.Objectives)
            {
                if (HasCycle(objective, ids, new Dictionary<string, bool>(StringComparer.Ordinal)))
                    return "conversation scam '" + spec.Id + "' has cyclic objectives at '" +
                           objective.Id + "'.";
            }

            ScamObjectiveSpec final = null;
            foreach (var objective in spec.Objectives)
            {
                if (objective.Final) final = objective;
            }
            if (final != null)
            {
                var required = new Dictionary<string, bool>(StringComparer.Ordinal);
                var finalPrereqs = final.Prerequisites != null
                    ? final.Prerequisites
                    : new string[0];
                foreach (var prereq in finalPrereqs) required[prereq] = true;
                foreach (var objective in spec.Objectives)
                {
                    if (objective.Final || objective.Optional) continue;
                    if (!required.ContainsKey(objective.Id))
                        return "conversation scam '" + spec.Id +
                               "': final objective must require all mandatory milestones " +
                               "(missing '" + objective.Id + "').";
                }
            }

            return null;
        }

        private static bool HasCycle(ScamObjectiveSpec current,
            Dictionary<string, ScamObjectiveSpec> ids, Dictionary<string, bool> visiting)
        {
            if (visiting.ContainsKey(current.Id)) return true;
            visiting[current.Id] = true;
            try
            {
                var prereqs = current.Prerequisites != null
                    ? current.Prerequisites
                    : new string[0];
                foreach (var prereq in prereqs)
                {
                    ScamObjectiveSpec next;
                    if (!ids.TryGetValue(prereq, out next)) continue;
                    if (HasCycle(next, ids, visiting)) return true;
                }
                return false;
            }
            finally
            {
                visiting.Remove(current.Id);
            }
        }

        /// <summary>
        /// The game's scam-type rules: every sentinel in the prompt must be listed, every listed
        /// FICTIONAL_* tag in the prompt must exist, and the AI configuration must be complete.
        /// </summary>
        public static string CheckScamType(ScamTypeSpec spec)
        {
            if (spec == null) return "scam type is null.";
            var bad = CheckId(spec.Id, "scam type");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(spec.DisplayName) || spec.DisplayName.Trim().Length == 0)
                return "scam type '" + spec.Id + "' has no display name.";
            bad = CheckId(spec.AppId, "scam type '" + spec.Id + "' app");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(spec.TestOpeningPlayerLine) ||
                spec.TestOpeningPlayerLine.Trim().Length == 0)
                return "scam type '" + spec.Id + "' has no testOpeningPlayerLine.";
            if (spec.RelevantSentinels == null || spec.RelevantSentinels.Length == 0)
                return "scam type '" + spec.Id + "' has no relevantSentinels.";
            if (string.IsNullOrEmpty(spec.CallerPrompt) || spec.CallerPrompt.Trim().Length == 0)
                return "scam type '" + spec.Id + "' has no callerPrompt.";
            foreach (var sentinel in spec.RelevantSentinels)
            {
                if (string.IsNullOrEmpty(sentinel) || sentinel.Trim().Length == 0)
                    return "scam type '" + spec.Id + "' lists a blank sentinel.";
                if (spec.CallerPrompt.IndexOf("[[" + sentinel + "]]", StringComparison.Ordinal) < 0)
                    return "scam type '" + spec.Id + "' does not map [[" + sentinel +
                           "]] in callerPrompt.";
            }
            return null;
        }

        public static string CheckApp(OsAppSpec spec)
        {
            if (spec == null) return "app is null.";
            var bad = CheckId(spec.Id, "app");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(spec.Title) || spec.Title.Trim().Length == 0)
                return "app '" + spec.Id + "' has no title.";
            if (string.IsNullOrEmpty(spec.ContentType) || spec.ContentType.Trim().Length == 0)
                return "app '" + spec.Id + "' has no contentType.";
            if (spec.Access != null && !string.IsNullOrEmpty(spec.Access.Mode) &&
                string.Equals(spec.Access.Mode, "catalog", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(spec.Access.ProductId) ||
                 spec.Access.ProductId.Trim().Length == 0))
                return "app '" + spec.Id + "' uses catalog access with no productId.";
            if (spec.Content != null && spec.Content.Binding != null &&
                string.Equals(spec.Content.Binding.Kind, "scenarioClaim", StringComparison.Ordinal) &&
                (string.IsNullOrEmpty(spec.Content.Binding.CompletionSource) ||
                 spec.Content.Binding.CompletionSource.Trim().Length == 0))
                return "app '" + spec.Id + "' scenarioClaim binding has no completionSource.";
            return null;
        }

        public static string CheckProduct(StoreProductSpec spec)
        {
            if (spec == null) return "product is null.";
            var bad = CheckId(spec.Id, "product");
            if (bad != null) return bad;
            bad = CheckId(spec.AppId, "product '" + spec.Id + "' app");
            if (bad != null) return bad;
            if (string.IsNullOrEmpty(spec.Title) || spec.Title.Trim().Length == 0)
                return "product '" + spec.Id + "' has no title.";
            if (spec.Price < 0) return "product '" + spec.Id + "' has a negative price.";
            return null;
        }

        public static string CheckSentinel(SentinelSpec spec)
        {
            if (spec == null) return "sentinel is null.";
            if (string.IsNullOrEmpty(spec.Tag) || spec.Tag.Trim().Length == 0)
                return "sentinel has no tag.";
            if (!spec.Tag.StartsWith("FICTIONAL_", StringComparison.Ordinal))
                return "sentinel tag '" + spec.Tag + "' must start with FICTIONAL_.";
            if (string.IsNullOrEmpty(spec.Label) || spec.Label.Trim().Length == 0)
                return "sentinel '" + spec.Tag + "' has no label.";
            if (spec.ValueEach < 0 || spec.ValueEach > 10000)
                return "sentinel '" + spec.Tag + "' valueEach is outside 0-10000.";
            if (string.IsNullOrEmpty(spec.AppCompletionSource) ||
                spec.AppCompletionSource.Trim().Length == 0)
                return "sentinel '" + spec.Tag + "' has no appCompletionSource.";
            return null;
        }

        /// <summary>
        /// The rules for an app the mod draws itself: the ordinary app rules, plus the contentType
        /// the library intercepts and a way for the app to appear at all.
        /// </summary>
        public static string CheckGameApp(OsAppSpec spec)
        {
            var bad = CheckApp(spec);
            if (bad != null) return bad;
            if (!string.Equals(spec.ContentType, OsAppSpec.ModContentType, StringComparison.Ordinal))
                return "app '" + spec.Id + "' carries a draw callback but its contentType is '" +
                       spec.ContentType + "', which the game builds itself. Use OsAppSpec.GameApp, " +
                       "whose contentType is '" + OsAppSpec.ModContentType + "'.";
            if (!spec.ShowOnDesktop && !spec.Pinned)
                return "app '" + spec.Id + "' is neither on the desktop nor pinned to the taskbar, " +
                       "so nothing would ever draw it.";
            if (spec.DesktopOrder < 0)
                return "app '" + spec.Id + "' has a negative desktopOrder.";
            return null;
        }

        /// <summary>
        /// The rules a <see cref="ConversationScamPlan"/> must follow: every part valid on its own,
        /// and the three ids and the completion source pointing at each other.
        /// </summary>
        public static string CheckConversationScamPlan(ConversationScamPlan plan)
        {
            if (plan == null) return "conversation scam plan is null.";
            if (plan.Scam == null) return "conversation scam plan has no Scam.";
            var bad = CheckConversationScam(plan.Scam);
            if (bad != null) return bad;
            if (plan.App == null)
                return "conversation scam '" + plan.Scam.Id + "' has no App to show it.";
            if (plan.Product == null)
                return "conversation scam '" + plan.Scam.Id + "' has no Product to unlock it.";
            bad = CheckApp(plan.App);
            if (bad != null) return bad;
            bad = CheckProduct(plan.Product);
            if (bad != null) return bad;

            if (!string.Equals(plan.App.Id, plan.Scam.AppId, StringComparison.Ordinal))
                return "conversation scam '" + plan.Scam.Id + "' names app '" + plan.Scam.AppId +
                       "' but the plan registers app '" + plan.App.Id + "'.";
            if (!string.Equals(plan.Product.Id, plan.Scam.ProductId, StringComparison.Ordinal))
                return "conversation scam '" + plan.Scam.Id + "' names product '" +
                       plan.Scam.ProductId + "' but the plan registers product '" +
                       plan.Product.Id + "'.";
            if (!string.Equals(plan.Product.AppId, plan.App.Id, StringComparison.Ordinal))
                return "product '" + plan.Product.Id + "' unlocks app '" + plan.Product.AppId +
                       "' but the plan registers app '" + plan.App.Id + "'.";
            if (plan.Product.ScamId != null &&
                !string.Equals(plan.Product.ScamId, plan.Scam.Id, StringComparison.Ordinal))
                return "product '" + plan.Product.Id + "' sells scam '" + plan.Product.ScamId +
                       "' but the plan registers scam '" + plan.Scam.Id + "'.";
            if (!string.Equals(plan.App.ContentType, "conversationScam", StringComparison.Ordinal))
                return "app '" + plan.App.Id + "' has contentType '" + plan.App.ContentType +
                       "', so it cannot show a conversation scam. Use OsAppSpec.ConversationScamApp.";

            return CheckTagList(plan.Sentinels, "conversation scam '" + plan.Scam.Id + "'");
        }

        /// <summary>
        /// The rules a <see cref="FormScamPlan"/> must follow: every part valid on its own, and the
        /// completion source plus the sentinel tag agreeing across the app, the scam type and the
        /// caller value. A mismatch here is a form that submits values nothing scores.
        /// </summary>
        public static string CheckFormScamPlan(FormScamPlan plan)
        {
            if (plan == null) return "form scam plan is null.";
            if (plan.ScamType == null) return "form scam plan has no ScamType.";
            var bad = CheckScamType(plan.ScamType);
            if (bad != null) return bad;
            if (plan.App == null)
                return "scam type '" + plan.ScamType.Id + "' has no App to collect the values.";
            if (plan.Product == null)
                return "scam type '" + plan.ScamType.Id + "' has no Product to unlock it.";
            bad = CheckApp(plan.App);
            if (bad != null) return bad;
            bad = CheckProduct(plan.Product);
            if (bad != null) return bad;

            if (!string.Equals(plan.App.Id, plan.ScamType.AppId, StringComparison.Ordinal))
                return "scam type '" + plan.ScamType.Id + "' names app '" + plan.ScamType.AppId +
                       "' but the plan registers app '" + plan.App.Id + "'.";
            if (!string.Equals(plan.Product.AppId, plan.App.Id, StringComparison.Ordinal))
                return "product '" + plan.Product.Id + "' unlocks app '" + plan.Product.AppId +
                       "' but the plan registers app '" + plan.App.Id + "'.";
            if (plan.Product.ScamId != null &&
                !string.Equals(plan.Product.ScamId, plan.ScamType.Id, StringComparison.Ordinal))
                return "product '" + plan.Product.Id + "' sells scam type '" + plan.Product.ScamId +
                       "' but the plan registers scam type '" + plan.ScamType.Id + "'.";
            if (!string.Equals(plan.App.ContentType, "form", StringComparison.Ordinal))
                return "app '" + plan.App.Id + "' has contentType '" + plan.App.ContentType +
                       "', so it cannot collect form values. Use OsAppSpec.FormScamApp.";
            if (plan.App.Content == null || plan.App.Content.Binding == null ||
                string.IsNullOrEmpty(plan.App.Content.Binding.CompletionSource))
                return "app '" + plan.App.Id + "' has no completionSource, so nothing it submits is scored.";
            if (!string.Equals(plan.App.Content.Binding.CompletionSource,
                    plan.ScamType.AppCompletionSource, StringComparison.Ordinal))
                return "app '" + plan.App.Id + "' submits '" +
                       plan.App.Content.Binding.CompletionSource + "' but scam type '" +
                       plan.ScamType.Id + "' scores '" + plan.ScamType.AppCompletionSource + "'.";
            if (plan.App.Content.Fields == null || plan.App.Content.Fields.Length == 0)
                return "app '" + plan.App.Id + "' has no fields, so the caller's values cannot be entered.";

            if (plan.Sentinel != null)
            {
                bad = CheckSentinel(plan.Sentinel);
                if (bad != null) return bad;
                if (!string.Equals(plan.Sentinel.AppCompletionSource,
                        plan.ScamType.AppCompletionSource, StringComparison.Ordinal))
                    return "sentinel '" + plan.Sentinel.Tag + "' is collected as '" +
                           plan.Sentinel.AppCompletionSource + "' but scam type '" +
                           plan.ScamType.Id + "' scores '" + plan.ScamType.AppCompletionSource + "'.";
                bool listed = false;
                foreach (var tag in plan.ScamType.RelevantSentinels)
                {
                    if (string.Equals(tag, plan.Sentinel.Tag, StringComparison.Ordinal)) listed = true;
                }
                if (!listed)
                    return "sentinel '" + plan.Sentinel.Tag + "' is not listed in scam type '" +
                           plan.ScamType.Id + "' relevantSentinels.";
            }

            return null;
        }

        /// <summary>Each sentinel valid, and no tag claimed twice.</summary>
        private static string CheckTagList(SentinelSpec[] sentinels, string what)
        {
            if (sentinels == null) return null;
            var tags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var sentinel in sentinels)
            {
                var bad = CheckSentinel(sentinel);
                if (bad != null) return what + ": " + bad;
                if (tags.ContainsKey(sentinel.Tag))
                    return what + " registers caller value '" + sentinel.Tag + "' twice.";
                tags[sentinel.Tag] = true;
            }
            return null;
        }
    }
}
