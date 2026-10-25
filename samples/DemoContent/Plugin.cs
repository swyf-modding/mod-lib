using BepInEx;
using BepInEx.Configuration;
using ScamWYF.Modding.Core;
using ScamWYF.Modding.Core.Content;
using ScamWYF.Modding.Core.Ui;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ScamWYF.DemoContent
{
    /// <summary>
    /// A working example of everything <see cref="ContentRegistry"/> and <see cref="ModDesktop"/> can
    /// add to the in-game computer: a game the mod draws itself, two whole scams, and a toast.
    /// </summary>
    /// <remarks>
    /// This is a sample rather than a mod anybody installs: nothing is released from here, the ids are
    /// prefixed <c>demo-</c> so they cannot collide with a real mod's, and it registers no patches of
    /// its own. Copy the shape, not the names.
    ///
    /// Where to look in game:
    ///
    ///  - <b>Lucky Sevens</b> and the two scam apps are on the desktop, marked with the desktop order
    ///    this file sets. The game has no builder for Lucky Sevens; every pixel of its window comes from
    ///    <see cref="LuckySevens.Draw"/>.
    ///  - The two scams are in Scamazon, free, unlocked from day one - a conversation scam whose last
    ///    step needs the caller to read out a tag number, and a form scam shaped like the bank login.
    ///  - The <b>Demo content</b> tab in the shared menu (F1) lists exactly what this mod registered, so
    ///    a rejection or a collision is visible instead of guessed at.
    ///  - F9 puts a message in the game's own notification stack, which is the smallest thing
    ///    <see cref="ModDesktop"/> does.
    /// </remarks>
    [BepInPlugin(PluginGuid, "Demo Content", PluginBuildInfo.Version)]
    public sealed class Plugin : ScamMod
    {
        public const string PluginGuid = "com.community.scamwyf.democontent";

        // The app ids live here because three different places have to agree on them: the app, the
        // scam that is shown in it, and the tab's open buttons. Ids are unique across ALL mods, so a
        // real mod would prefix them with its own name rather than "demo-".
        internal const string SlotsApp = "demo-lucky-sevens";
        internal const string DogApp = "demo-dog-walking";
        internal const string LibraryApp = "demo-library-card";

        private const string BuiltAgainst = "6000.3.26f1";

        private ConfigEntry<int> _startingCredits;
        private ConfigEntry<bool> _notifyOnWin;

        /// <summary>For the parts of the demo that are not the plugin itself.</summary>
        internal static Plugin Instance;

        protected override void OnModLoad()
        {
            Instance = this;
            GameBuild.CheckUnityVersion(ModLog, ModId, BuiltAgainst);

            _startingCredits = Settings.Bind("Lucky Sevens", "StartingCredits", 20, 5, 500,
                "Credits the machine starts a session with. A bound range is what turns this into a " +
                "slider in the generated settings form.");
            _notifyOnWin = Settings.Bind("Lucky Sevens", "NotifyOnWin", true,
                "Show a notification on the computer when a spin pays out.");
            WatchConfig();

            LuckySevens.Configure(Settings.Int(_startingCredits, 5, 500));

            RegisterContent();

            ModMenu.AddPage(this, "Demo content", BuildPage, 10);
            Hotkeys.Register(this, Key.F9, "Demo content: toast the computer", Poke);
        }

        protected override void OnConfigReloaded()
        {
            LuckySevens.Configure(Settings.Int(_startingCredits, 5, 500));
            ModLog.LogInfo("Starting credits are now " + LuckSummary() + ".");
        }

        private string LuckSummary()
        {
            return Settings.Int(_startingCredits, 5, 500).ToString();
        }

        /// <summary>
        /// Register the demo's content. Every call here is the whole API surface a mod needs.
        /// </summary>
        /// <remarks>
        /// Load time is the right time: the office scene reads the catalogs when it builds the
        /// desktop, and registering later still works but the icons wait for the next scene reload.
        /// </remarks>
        private void RegisterContent()
        {
            // 1. An app the game has no builder for. The spec is the same one a normal app uses, and
            //    the callback is what draws its window body.
            var slots = OsAppSpec.GameApp(SlotsApp, "Lucky Sevens", "KolkataOS/SVG/slot-machine",
                "#B8860B", 95);
            slots.WindowWidth = 520f;
            slots.WindowHeight = 470f;
            if (!ContentRegistry.RegisterGame(this, slots, LuckySevens.Draw))
            {
                ModLog.LogWarning("The demo game was not registered; see the warning above for why.");
            }

            // 2. A conversation scam: the caller value, the product that unlocks it, the app that shows
            //    the checklist and the scam itself, registered together so their ids cannot disagree.
            var dog = Scams.DogWalking();
            if (!ContentRegistry.RegisterConversationScam(this, dog))
            {
                ModLog.LogWarning("The dog walking scam was not registered; see the warning above.");
            }

            // 3. A form scam: the bank-login shape, where the app collects the values and submits them
            //    to the same completion source the scam type and the caller value are scored on.
            var library = Scams.LibraryCard();
            if (!ContentRegistry.RegisterFormScam(this, library))
            {
                ModLog.LogWarning("The library card scam was not registered; see the warning above.");
            }

            ModLog.LogInfo("Demo content registered: " + ContentRegistry.ForOwner(ModId).Length +
                           " entries. They appear on the desktop in the office.");
        }

        /// <summary>
        /// The tab this mod contributes to the shared menu. It is also the honest way to check the demo
        /// from inside the game: what registered, what collided, and whether the computer is up.
        /// </summary>
        private void BuildPage(VisualElement page)
        {
            var scroll = Widgets.Scroll(page);

            Widgets.Paragraph(scroll, "A game drawn by this mod, and two scams built from one call each. " +
                                      "Nothing here is released; it exists to exercise the content API.");

            var row = Widgets.WrapRow(scroll);
            Widgets.PrimaryButton(row, "Lucky Sevens", delegate { Open(SlotsApp); });
            Widgets.Button(row, "Dog walking website", delegate { Open(DogApp); });
            Widgets.Button(row, "City library", delegate { Open(LibraryApp); });
            Widgets.Button(row, "Toast the computer", Poke);

            Widgets.FieldRow(scroll, "Computer open", ModDesktop.IsOpen ? "yes" : "no");
            Widgets.FieldRow(scroll, "Credits in the machine", LuckySevens.Credits.ToString());
            Widgets.FieldRow(scroll, "Spins this session", LuckySevens.Spins.ToString());

            Widgets.Heading(scroll, "Registered content");
            var entries = ContentRegistry.ForOwner(ModId);
            if (entries.Length == 0)
            {
                Widgets.Note(scroll, "Nothing registered. The log above this line says why.");
            }
            else
            {
                foreach (var entry in entries) Widgets.FieldRow(scroll, entry.Kind, entry.Id);
            }

            var conflicts = ContentRegistry.ConflictReport();
            if (conflicts != null) Widgets.Warning(scroll, conflicts);
        }

        private void Open(string appId)
        {
            if (ModDesktop.OpenApp(this, appId)) return;
            ModLog.LogInfo("Could not open '" + appId + "'. The computer is closed, or this build has " +
                           "no app by that id.");
        }

        /// <summary>
        /// The smallest use of <see cref="ModDesktop"/>: a toast in the game's own notification stack,
        /// with one of its own icons. False when the computer is not on screen, which is not an error.
        /// </summary>
        internal void Poke()
        {
            if (!ModDesktop.Notify(this, "Hello from the demo mod.", "KolkataOS/SVG/star", "#B8860B",
                    null))
            {
                ModLog.LogInfo("The computer is not open, so the toast was skipped.");
            }
        }

        internal void NotifyWin(int won)
        {
            if (!Settings.Bool(_notifyOnWin, true)) return;

            ModDesktop.Notify(this, "Lucky Sevens paid out " + won + " credits.",
                "KolkataOS/SVG/slot-machine", "#B8860B", null);
        }
    }
}
