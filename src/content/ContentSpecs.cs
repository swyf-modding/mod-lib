using System;

namespace ScamWYF.Modding.Core.Content
{
    /// <summary>
    /// One objective inside a conversation scam: what the player must get the caller to do.
    /// </summary>
    /// <remarks>
    /// Mirrors the game's own objective JSON. The game's rules apply unchanged: at least two
    /// objectives per scam, exactly one mandatory final, prerequisites must name real objectives
    /// and must not cycle, and the final must require every mandatory milestone. Anything else
    /// is rejected at registration with the reason in the log, before the game ever sees it.
    /// </remarks>
    [Serializable]
    public sealed class ScamObjectiveSpec
    {
        public string Id;
        public string Text;
        public string Hint;
        public string Criteria;
        public string RequiredSentinel;
        public string[] Prerequisites;
        public bool Optional;
        public bool Final;
        public bool RequiresSubmission;
    }

    /// <summary>
    /// A conversation scam: objectives that check off as you talk, optionally with one submitted
    /// value. This is the romance / police / tech-support shape, shown in an app whose contentType
    /// is "conversationScam".
    /// </summary>
    [Serializable]
    public sealed class ConversationScamSpec
    {
        public string Id;
        public string ProductId;
        public string AppId;
        public string Title;
        public string CallerPrompt;
        public string Sentinel;
        public int Reward;
        public int[] RewardTiers;
        public bool EndCallOnCompletion;
        public ScamObjectiveSpec[] Objectives;
    }

    /// <summary>
    /// A scam type: the entry in the scammer script that points at an app and lists which caller
    /// values matter. This is the bank-login / gift-card shape, where the app itself collects the
    /// values through a form.
    /// </summary>
    [Serializable]
    public sealed class ScamTypeSpec
    {
        public string Id;
        public string DisplayName;
        public string IconName;
        public string AppId;
        public int DisplayOrder;
        public string[] RelevantSentinels;
        public string AppCompletionSource;
        public string CallerPrompt;
        public string TestOpeningPlayerLine;
    }

    /// <summary>One field in a form app, e.g. a bank username box.</summary>
    [Serializable]
    public sealed class OsFormFieldSpec
    {
        public string Id;
        public string Label;
        public string Placeholder;
        public string ItemId;
        public bool Multiline;
        public bool Password;
        public bool Required;
        public bool PersistLocal;
    }

    /// <summary>How a form app submits: which completion source it verifies against.</summary>
    [Serializable]
    public sealed class OsAppBindingSpec
    {
        public string Kind;
        public string CompletionSource;
        public string SubmitLabel;
    }

    /// <summary>The content block of an OS app definition.</summary>
    [Serializable]
    public sealed class OsAppContentSpec
    {
        public string Title;
        public string Subtitle;
        public string Body;
        public OsAppBindingSpec Binding;
        public OsFormFieldSpec[] Fields;
    }

    /// <summary>How an OS app is unlocked: always, by day, or through the store catalog.</summary>
    [Serializable]
    public sealed class OsAppAccessSpec
    {
        public string Mode;
        public string ProductId;
        public int UnlockDay;
    }

    /// <summary>
    /// An in-game OS app: a window on the desktop. Scam apps use contentType "form" (with a
    /// scenarioClaim binding) or "conversationScam"; anything else ("placeholder", "notes",
    /// "browser"...) is drawn by the game's existing builders.
    /// </summary>
    /// <remarks>
    /// Field names match the game's own application JSON so a spec serializes to a file the game
    /// would have accepted. Prefer the factories for scam apps; use raw JSON via
    /// <see cref="ContentRegistry.RegisterAppJson"/> only when the shape needs something the
    /// factories do not cover.
    /// </remarks>
    [Serializable]
    public sealed class OsAppSpec
    {
        public string Id;
        public string Title;
        public string IconResource;
        public string IconSvg;
        public string IconPath;
        public string ContentType;
        public bool Pinned;
        public int PinnedOrder;
        public bool ShowOnDesktop;
        public bool DesktopHiddenByDefault;
        public int DesktopOrder;
        public string DesktopAnchor;
        public bool LobbyOnly;
        public float WindowLeft;
        public float WindowTop;
        public float WindowWidth;
        public float WindowHeight;
        public float MinWindowWidth;
        public float MinWindowHeight;
        public string AccentColor;
        public string InitialState;
        public string PlayerPrefsKey;
        public OsAppAccessSpec Access;
        public OsAppContentSpec Content;

        /// <summary>The caller prompt shipped inside the app's "prompts" block, if any.</summary>
        public string CallerPrompt;

        /// <summary>
        /// A form scam app: collects caller values and verifies them. Binding kind is always
        /// "scenarioClaim"; completionSource must match the scam type's appCompletionSource and
        /// the sentinels' appCompletionSource.
        /// </summary>
        public static OsAppSpec FormScamApp(string appId, string title, string productId,
            string completionSource, string submitLabel, OsFormFieldSpec[] fields)
        {
            return FormScamApp(appId, title, productId, completionSource, submitLabel, fields,
                null, null, "#0078D7", 40);
        }

        public static OsAppSpec FormScamApp(string appId, string title, string productId,
            string completionSource, string submitLabel, OsFormFieldSpec[] fields,
            string subtitle, string iconResource, string accentColor, int desktopOrder)
        {
            var spec = new OsAppSpec();
            spec.Id = appId;
            spec.Title = title;
            spec.IconResource = string.IsNullOrEmpty(iconResource) ? "KolkataOS/SVG/shopping-bag" : iconResource;
            spec.ContentType = "form";
            spec.ShowOnDesktop = true;
            spec.DesktopOrder = desktopOrder;
            spec.WindowLeft = 300f;
            spec.WindowTop = 120f;
            spec.WindowWidth = 680f;
            spec.WindowHeight = 560f;
            spec.AccentColor = string.IsNullOrEmpty(accentColor) ? "#0078D7" : accentColor;
            spec.InitialState = "closed";
            spec.Access = new OsAppAccessSpec();
            spec.Access.Mode = "catalog";
            spec.Access.ProductId = productId;
            spec.Access.UnlockDay = 1;
            spec.Content = new OsAppContentSpec();
            spec.Content.Title = title;
            spec.Content.Subtitle = subtitle != null ? subtitle : title;
            spec.Content.Binding = new OsAppBindingSpec();
            spec.Content.Binding.Kind = "scenarioClaim";
            spec.Content.Binding.CompletionSource = completionSource;
            spec.Content.Binding.SubmitLabel = string.IsNullOrEmpty(submitLabel) ? "Verify" : submitLabel;
            spec.Content.Fields = fields != null ? fields : new OsFormFieldSpec[0];
            return spec;
        }

        /// <summary>
        /// The contentType of an app the library draws on the mod's behalf. The game's own content
        /// types ("form", "notes", "browser"...) are built by its builders and must not be used for
        /// an app that carries a draw callback.
        /// </summary>
        public const string ModContentType = "modApp";

        /// <summary>
        /// A desktop app whose window is drawn by the mod: a game, a calculator, anything the game
        /// itself has no builder for. Register it with <see cref="ContentRegistry.RegisterGame"/>, or
        /// with the draw-taking <c>ContentRegistry.RegisterApp</c> overload, and the callback is
        /// handed a content root inside the app's window every time the game builds or rebuilds it.
        /// </summary>
        /// <remarks>
        /// The app is unlocked from day one and sits on the desktop, because the usual reason a mod
        /// adds a game is that the player should be able to play it. Change <c>Access</c> afterwards
        /// for a store-gated one, or <c>DesktopOrder</c> to place it.
        /// </remarks>
        public static OsAppSpec GameApp(string appId, string title)
        {
            return GameApp(appId, title, "KolkataOS/SVG/slot-machine", "#107C10", 90);
        }

        public static OsAppSpec GameApp(string appId, string title, string iconResource,
            string accentColor, int desktopOrder)
        {
            var spec = new OsAppSpec();
            spec.Id = appId;
            spec.Title = title;
            spec.IconResource = string.IsNullOrEmpty(iconResource)
                ? "KolkataOS/SVG/slot-machine"
                : iconResource;
            spec.ContentType = ModContentType;
            spec.ShowOnDesktop = true;
            spec.DesktopOrder = desktopOrder;
            spec.WindowLeft = 320f;
            spec.WindowTop = 110f;
            spec.WindowWidth = 720f;
            spec.WindowHeight = 520f;
            spec.AccentColor = string.IsNullOrEmpty(accentColor) ? "#107C10" : accentColor;
            spec.InitialState = "closed";
            spec.Access = new OsAppAccessSpec();
            spec.Access.Mode = "default";
            spec.Access.UnlockDay = 1;
            spec.Content = new OsAppContentSpec();
            spec.Content.Title = title;
            return spec;
        }

        /// <summary>
        /// A conversation scam app: shows the objective checklist for the given scam. The scam's
        /// appId must equal this app's id.
        /// </summary>
        public static OsAppSpec ConversationScamApp(string appId, string title, string productId)
        {
            return ConversationScamApp(appId, title, productId, null, null, "#0078D7", 40);
        }

        public static OsAppSpec ConversationScamApp(string appId, string title, string productId,
            string subtitle, string iconResource, string accentColor, int desktopOrder)
        {
            var spec = new OsAppSpec();
            spec.Id = appId;
            spec.Title = title;
            spec.IconResource = string.IsNullOrEmpty(iconResource) ? "KolkataOS/SVG/shopping-bag" : iconResource;
            spec.ContentType = "conversationScam";
            spec.ShowOnDesktop = true;
            spec.DesktopOrder = desktopOrder;
            spec.WindowLeft = 300f;
            spec.WindowTop = 100f;
            spec.WindowWidth = 680f;
            spec.WindowHeight = 560f;
            spec.AccentColor = string.IsNullOrEmpty(accentColor) ? "#0078D7" : accentColor;
            spec.InitialState = "closed";
            spec.Access = new OsAppAccessSpec();
            spec.Access.Mode = "catalog";
            spec.Access.ProductId = productId;
            spec.Access.UnlockDay = 1;
            spec.Content = new OsAppContentSpec();
            spec.Content.Title = title;
            spec.Content.Subtitle = subtitle != null ? subtitle : title;
            return spec;
        }
    }

    /// <summary>
    /// A store product: what Scamazon sells and what gates an app when its access mode is
    /// "catalog". Scam products have category "scam" and ownership "run".
    /// </summary>
    [Serializable]
    public sealed class StoreProductSpec
    {
        public string Id;
        public string AppId;
        public string ScamId;
        public string Title;
        public string Description;
        public string IconResource;
        public string IconSvg;
        public string IconPath;
        public string IconText;
        public int Price;
        public int SellPrice;
        public string Ownership;
        public bool Sellable;
        public string Category;
        public string AppCategory;
        public string AccessMode;
        public int UnlockDay;
        public string AccentColor;

        /// <summary>A free scam unlock, the usual shape for a mod-added scam.</summary>
        public static StoreProductSpec FreeScam(string productId, string appId, string scamId,
            string title, string description, int unlockDay)
        {
            return PaidScam(productId, appId, scamId, title, description, 0, unlockDay, null);
        }

        public static StoreProductSpec PaidScam(string productId, string appId, string scamId,
            string title, string description, int price, int unlockDay, string accentColor)
        {
            var spec = new StoreProductSpec();
            spec.Id = productId;
            spec.AppId = appId;
            spec.ScamId = scamId;
            spec.Title = title;
            spec.Description = description != null ? description : title;
            spec.Price = Math.Max(0, price);
            spec.SellPrice = 0;
            spec.Ownership = "run";
            spec.Sellable = false;
            spec.Category = "scam";
            spec.AppCategory = "scam";
            spec.AccessMode = "purchase";
            spec.UnlockDay = Math.Max(1, unlockDay);
            spec.AccentColor = accentColor;
            return spec;
        }
    }

    /// <summary>
    /// A caller value definition: the tag the caller says ([[TAG]]), what it is called, what it
    /// is worth, and how values are generated. Reuse an existing tag when one fits; register a
    /// new one only for genuinely new information.
    /// </summary>
    [Serializable]
    public sealed class SentinelSpec
    {
        public string Tag;
        public string Label;
        public int ValueEach;
        public string AppCompletionSource;
        public string GeneratorType;
        public int[] GeneratorGroups;
        public string GeneratorSeparator;
        public int GeneratorDigits;
        public string SpokenFormat;

        /// <summary>Two groups of 2-4 characters, e.g. bank usernames (XX-XX).</summary>
        public static SentinelSpec Code(string tag, string label, int valueEach,
            string appCompletionSource, int[] groups, string separator)
        {
            var spec = new SentinelSpec();
            spec.Tag = tag;
            spec.Label = label;
            spec.ValueEach = Math.Max(0, valueEach);
            spec.AppCompletionSource = appCompletionSource;
            spec.GeneratorType = "alphanumeric_groups";
            spec.GeneratorGroups = groups != null ? groups : new[] { 3, 3 };
            spec.GeneratorSeparator = separator != null ? separator : "-";
            spec.SpokenFormat = "char_groups";
            return spec;
        }

        /// <summary>Groups of digits, e.g. a 6-digit code (XXX-XXX).</summary>
        public static SentinelSpec Digits(string tag, string label, int valueEach,
            string appCompletionSource, int[] groups, string separator)
        {
            var spec = new SentinelSpec();
            spec.Tag = tag;
            spec.Label = label;
            spec.ValueEach = Math.Max(0, valueEach);
            spec.AppCompletionSource = appCompletionSource;
            spec.GeneratorType = "numeric_groups";
            spec.GeneratorGroups = groups != null ? groups : new[] { 3, 3 };
            spec.GeneratorSeparator = separator != null ? separator : "-";
            spec.SpokenFormat = "digit_groups";
            return spec;
        }
    }

    /// <summary>
    /// A whole conversation scam in one object: the product that unlocks it, the app that shows it
    /// and the objectives that make it up.
    /// </summary>
    /// <remarks>
    /// Registering the parts one at a time is four calls that share three ids and two completion
    /// sources, and every one of those agreements is a place to get it wrong: a scam whose appId
    /// names an app nobody registered has no window, and a product whose appId names a different app
    /// sells a scam that cannot be played. <see cref="ContentRegistry.RegisterConversationScam"/>
    /// takes the plan, checks the agreements and registers the parts together, or none of them.
    ///
    /// Everything is a plain field, so <see cref="New"/> gives a working starting point and the
    /// fields can be overwritten afterwards.
    /// </remarks>
    public sealed class ConversationScamPlan
    {
        public ConversationScamSpec Scam;
        public OsAppSpec App;
        public StoreProductSpec Product;

        /// <summary>Caller values the objectives score. Optional, but a new tag must go here.</summary>
        public SentinelSpec[] Sentinels;

        /// <summary>
        /// A free scam, unlocked from the given day, with the app and the store entry built from the
        /// same ids so they already agree.
        /// </summary>
        public static ConversationScamPlan New(string appId, string productId, string scamId,
            string title, int reward, ScamObjectiveSpec[] objectives, int unlockDay)
        {
            return New(appId, productId, scamId, title, reward, objectives, unlockDay,
                null, null, null, 40);
        }

        public static ConversationScamPlan New(string appId, string productId, string scamId,
            string title, int reward, ScamObjectiveSpec[] objectives, int unlockDay,
            string subtitle, string iconResource, string accentColor, int desktopOrder)
        {
            var plan = new ConversationScamPlan();
            plan.Scam = new ConversationScamSpec();
            plan.Scam.Id = scamId;
            plan.Scam.ProductId = productId;
            plan.Scam.AppId = appId;
            plan.Scam.Title = title;
            plan.Scam.Reward = reward;
            plan.Scam.Objectives = objectives;
            plan.Scam.EndCallOnCompletion = false;

            plan.App = OsAppSpec.ConversationScamApp(appId, title, productId, subtitle,
                iconResource, accentColor, desktopOrder);
            plan.Product = StoreProductSpec.FreeScam(productId, appId, scamId, title,
                subtitle != null ? subtitle : title, unlockDay);
            return plan;
        }
    }

    /// <summary>
    /// A whole form scam in one object: the app that collects the values, the store entry that
    /// unlocks it, the caller value the victim reads out, and the scam type that points the caller
    /// script at all three.
    /// </summary>
    /// <remarks>
    /// This is the bank-login / gift-card shape. The three objects have to agree on the completion
    /// source and on the sentinel tag, or the app submits values nothing scores and the caller never
    /// says the tag the form verifies. See <see cref="ConversationScamPlan"/> for why that is one
    /// call rather than four.
    /// </remarks>
    public sealed class FormScamPlan
    {
        public ScamTypeSpec ScamType;
        public OsAppSpec App;
        public StoreProductSpec Product;
        public SentinelSpec Sentinel;

        /// <summary>
        /// A free form scam with the app, the store entry, the caller value and the scam type built
        /// from one completion source and one tag. The caller prompt and the opening line are
        /// written from the title and the sentinel label; overwrite them for a real scam.
        /// </summary>
        public static FormScamPlan New(string appId, string productId, string scamTypeId,
            string title, string completionSource, SentinelSpec sentinel,
            OsFormFieldSpec[] fields, int unlockDay)
        {
            return New(appId, productId, scamTypeId, title, completionSource, sentinel, fields,
                unlockDay, null, null, null, 40);
        }

        public static FormScamPlan New(string appId, string productId, string scamTypeId,
            string title, string completionSource, SentinelSpec sentinel,
            OsFormFieldSpec[] fields, int unlockDay, string subtitle, string iconResource,
            string accentColor, int desktopOrder)
        {
            var plan = new FormScamPlan();
            plan.Sentinel = sentinel;
            plan.App = OsAppSpec.FormScamApp(appId, title, productId, completionSource, "Submit",
                fields, subtitle, iconResource, accentColor, desktopOrder);
            plan.Product = StoreProductSpec.FreeScam(productId, appId, scamTypeId, title,
                subtitle != null ? subtitle : title, unlockDay);

            plan.ScamType = new ScamTypeSpec();
            plan.ScamType.Id = scamTypeId;
            plan.ScamType.DisplayName = title;
            plan.ScamType.IconName = "shopping-bag";
            plan.ScamType.AppId = appId;
            plan.ScamType.DisplayOrder = 100;
            plan.ScamType.AppCompletionSource = completionSource;
            plan.ScamType.RelevantSentinels = sentinel != null ? new[] { sentinel.Tag } : new string[0];
            plan.ScamType.CallerPrompt = sentinel != null
                ? "Read back the " + sentinel.Label + " as [[" + sentinel.Tag + "]] if asked."
                : null;
            plan.ScamType.TestOpeningPlayerLine = "I'm calling about your " + title + ".";
            return plan;
        }
    }
}
