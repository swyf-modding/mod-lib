using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ScamWYF.Modding.Core.Ui
{
    /// <summary>One tab in the universal menu.</summary>
    public sealed class MenuPage
    {
        internal MenuPage(string ownerId, string ownerName, string title, Action<VisualElement> build,
            int order)
        {
            OwnerId = ownerId;
            OwnerName = ownerName;
            Title = title;
            Build = build;
            Order = order;
        }

        /// <summary>The mod that registered this page.</summary>
        public string OwnerId { get; private set; }

        /// <summary>That mod's display name.</summary>
        public string OwnerName { get; private set; }

        public string Title { get; private set; }

        /// <summary>Fills the page. Called each time the tab is selected, so a page may rebuild freely.</summary>
        public Action<VisualElement> Build { get; private set; }

        /// <summary>Lower sorts first.</summary>
        public int Order { get; private set; }

        /// <summary>
        /// When false the tab is hidden but kept, for a mod that is loaded and has nothing to show right
        /// now. Call <see cref="ModMenu.Refresh"/> after changing it.
        /// </summary>
        public bool Available { get; set; }

        public override string ToString()
        {
            return OwnerName + " - " + Title;
        }
    }

    /// <summary>
    /// The one menu every mod shares.
    /// </summary>
    /// <remarks>
    /// Previously each mod drew its own window: one hotkey per mod, several windows to mis-click, and
    /// no way to see what else was installed. This is the opposite. One window, one hotkey, one tab per
    /// thing a mod wants to show.
    ///
    /// A mod contributes a page and nothing else. The menu owns the window, the tab strip, the hotkey and
    /// the layout, so pages cannot fight over any of it - the same argument this library makes about
    /// Harmony patches and hotkeys, applied to interfaces.
    ///
    /// The menu belongs to the library rather than to the mod handler. A user who only wants the AI
    /// backend should not have to install a plugin manager to reach its settings, and a user with no
    /// mods at all should still get somewhere to see what went wrong.
    ///
    /// Two kinds of tab exist. A mod page is registered by a mod and disappears when that mod unloads.
    /// A built-in page is registered by key, so registering a key twice replaces it rather than
    /// stacking: that is how the mod handler takes over the "Plugins" tab the library reserves for it.
    /// </remarks>
    public static class ModMenu
    {
        /// <summary>Built-in tab key for the list of loaded mods.</summary>
        public const string ModsTab = "mods";

        /// <summary>Built-in tab key describing this session and this library.</summary>
        public const string AboutTab = "about";

        /// <summary>
        /// Reserved for whoever wants to show installed plugins - in practice the mod handler. The
        /// library registers a placeholder so the tab strip has a sensible shape without it.
        /// </summary>
        public const string PluginsTab = "plugins";

        private sealed class BuiltInPage
        {
            public string Key;
            public string Title;
            public Action<VisualElement> Build;
            public Func<bool> Available;
        }

        /// <summary>
        /// One tab in the strip. A tab points at either a built-in page or a mod's page, never both,
        /// which keeps the selection logic down to one comparison.
        /// </summary>
        private sealed class Tab
        {
            public Button Button;
            public string BuiltInKey;
            public MenuPage Page;
        }

        private static readonly List<MenuPage> Pages = new List<MenuPage>();
        private static readonly List<BuiltInPage> BuiltIn = new List<BuiltInPage>();
        private static readonly List<Tab> Tabs = new List<Tab>();

        private static UiWindow _window;
        // Both scroll views rather than plain elements: a session with a dozen mods has more tabs than fit in a
        // short window, and a page that cannot reach its own footer is a bug nobody can report usefully.
        // Typed as ScrollView so their scroller visibility can be set; only ever used as VisualElement.
        private static ScrollView _tabStrip;
        private static ScrollView _pageHost;
        private static string _selectedBuiltIn;
        private static MenuPage _selected;
        private static int _nextOrder = 0;
        private static bool _refreshing;
        private static bool _ready;

        /// <summary>Whether the menu is on screen.</summary>
        public static bool IsOpen
        {
            get { return _window != null && _window.Visible; }
        }

        /// <summary>Every registered mod page, including ones currently hidden.</summary>
        public static MenuPage[] RegisteredPages
        {
            get { lock (Pages) { return Pages.ToArray(); } }
        }

        // ---------------------------------------------------------------- mod pages

        /// <summary>
        /// Contribute a tab. The build callback is handed a fresh container each time the tab is
        /// selected, so a page can be as simple as "make some labels" or as elaborate as it likes.
        /// </summary>
        public static MenuPage AddPage(ScamMod owner, string title, Action<VisualElement> build)
        {
            return AddPage(owner, title, build, _nextOrder++);
        }

        /// <summary>
        /// Contribute a tab at a chosen position, for a mod that wants to sit at the top or the bottom
        /// rather than wherever it happened to load.
        /// </summary>
        public static MenuPage AddPage(ScamMod owner, string title, Action<VisualElement> build, int order)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            if (string.IsNullOrEmpty(title)) throw new ArgumentNullException("title");
            if (build == null) throw new ArgumentNullException("build");

            var page = new MenuPage(owner.ModId, owner.DisplayName, title, build, order) { Available = true };

            lock (Pages) { Pages.Add(page); }

            owner.ModLog.LogInfo("Added the '" + title + "' tab to the mod menu.");
            Refresh();
            return page;
        }

        /// <summary>Take a mod's tabs away. Called for you when a mod unloads.</summary>
        public static void RemoveOwner(string modId)
        {
            if (string.IsNullOrEmpty(modId)) return;

            lock (Pages)
            {
                Pages.RemoveAll(page => page.OwnerId == modId);
            }

            // Whatever is on screen belonged to a mod that no longer exists.
            if (_selected != null && _selected.OwnerId == modId) _selected = null;
            Refresh();
        }

        /// <summary>True when this mod currently has at least one tab showing.</summary>
        public static bool HasPage(ScamMod owner)
        {
            if (owner == null) return false;

            foreach (var page in RegisteredPages)
            {
                if (page.OwnerId == owner.ModId && page.Available) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- built-in pages

        /// <summary>
        /// Register or replace a tab the library draws. Keyed, so a second call with the same key
        /// replaces the first rather than adding a duplicate.
        /// </summary>
        public static void SetBuiltInPage(string key, string title, Action<VisualElement> build)
        {
            SetBuiltInPage(key, title, build, null);
        }

        /// <summary>
        /// Register or replace a tab the library draws, showing it only while
        /// <paramref name="available"/> returns true. A hidden tab keeps its place in the order.
        /// </summary>
        public static void SetBuiltInPage(string key, string title, Action<VisualElement> build,
            Func<bool> available)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException("key");

            lock (BuiltIn)
            {
                BuiltIn.RemoveAll(page => page.Key == key);
                if (build != null)
                {
                    BuiltIn.Add(new BuiltInPage
                    {
                        Key = key,
                        Title = title,
                        Build = build,
                        Available = available
                    });
                }
            }

            Refresh();
        }

        // ---------------------------------------------------------------- opening

        /// <summary>Open the menu, creating the window if this is the first time.</summary>
        public static bool Open()
        {
            if (!EnsureWindow()) return false;

            Refresh();
            _window.Visible = true;
            _window.Focus();
            return true;
        }

        /// <summary>Close the menu.</summary>
        public static void Close()
        {
            if (_window != null) _window.Visible = false;

            // Not HideOverlay unconditionally: a mod may have a floating overlay open at the same time,
            // and closing the menu should not take that away with it.
            if (!UiWindows.AnyVisible) UiPanel.HideOverlay();
        }

        public static void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Open the menu on one of a mod's tabs. For a hotkey that means "open the AI backend's tab"
        /// rather than merely "open the menu".
        /// </summary>
        public static bool Show(ScamMod owner, string title)
        {
            if (owner == null) return false;

            foreach (var page in OrderedPages())
            {
                if (page.OwnerId != owner.ModId) continue;
                if (!string.Equals(page.Title, title, StringComparison.Ordinal)) continue;
                if (!page.Available) continue;

                if (!Open()) return false;
                Select(page, null);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Rebuild the tab strip and redraw the current page. A mod calls this after changing whether
        /// its pages are available, or when it has new things to show.
        /// </summary>
        public static void Refresh()
        {
            // Refresh rebuilds the strip and then selects a tab, which selects again. Guard against a
            // page whose build calls Refresh turning that into unbounded recursion.
            if (_refreshing) return;
            _refreshing = true;

            try
            {
                if (!_ready || _tabStrip == null) return;

                Tabs.Clear();
                _tabStrip.Clear();

                foreach (var builtIn in OrderedBuiltIn())
                {
                    if (!IsShowing(builtIn)) continue;
                    AddTab(builtIn.Title, builtIn.Key, null);
                }

                foreach (var page in OrderedPages())
                {
                    if (!page.Available) continue;
                    AddTab(page.Title, null, page);
                }

                if (Tabs.Count == 0)
                {
                    Widgets.Label(_tabStrip, "Nothing to show", true);
                    if (_pageHost != null) _pageHost.Clear();
                    return;
                }

                // Keep the current tab if it is still there, so editing a setting does not throw the
                // user back to the first page. Otherwise land on the first one.
                if (_selectedBuiltIn != null && HasBuiltIn(_selectedBuiltIn)) ShowBuiltIn(_selectedBuiltIn);
                else if (_selected != null && HasPage(_selected)) ShowPage(_selected);
                else Select(Tabs[0].Page, Tabs[0].BuiltInKey);
            }
            finally
            {
                _refreshing = false;
            }
        }

        // ---------------------------------------------------------------- startup

        /// <summary>
        /// Register the tabs the library draws itself and bind the menu hotkey. Called once during the
        /// library's startup, before any mod has finished loading.
        /// </summary>
        internal static void Start()
        {
            DefaultPages.Register();
            _ready = true;
        }

        // ---------------------------------------------------------------- internals

        private static bool EnsureWindow()
        {
            if (_window != null) return true;

            _window = UiWindow.Create("library", "Mods", 90f, 70f, 920f, 580f);
            if (_window.Element == null)
            {
                // One clear line in the log beats an invisible menu the user cannot explain.
                UiLog.Warn("The mod menu could not be created: " +
                           (_window.UnavailableReason ?? "unknown reason") + ". " +
                           "Mods will still load, but there will be no in-game interface.");
                _window = null;
                return false;
            }

            _window.OnClosed = Close;
            _window.ClearBody();

            // Everything between the title bar and the footer lives in here: the tab strip on the left,
            // the selected page on the right. Built below and added to the window once it is complete.
            var split = new VisualElement();
            split.style.flexGrow = 1f;
            split.style.flexShrink = 1f;
            split.style.flexDirection = FlexDirection.Row;
            split.style.alignItems = Align.Stretch;
            split.style.overflow = Overflow.Hidden;

            // The tab strip scrolls independently of the page. A session with a dozen mods has more tabs
            // than fit in a short window, and having the whole window scroll sideways to reach one is
            // worse than a strip that scrolls on its own.
            _tabStrip = new ScrollView(ScrollViewMode.Vertical);
            _tabStrip.style.width = 190f;
            _tabStrip.style.flexShrink = 0f;
            _tabStrip.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _tabStrip.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabStrip.style.paddingRight = 2f;
            _tabStrip.style.paddingTop = 4f;
            split.Add(_tabStrip);

            var divider = new VisualElement();
            divider.style.width = UiTheme.BorderWidth;
            divider.style.backgroundColor = UiTheme.WindowBorder;
            divider.style.flexShrink = 0f;
            split.Add(divider);

            // The page host is itself a scroll view, so a page that forgets to add its own still scrolls.
            // Pages in this library open with one already; nesting two scroll views is harmless in UI
            // Toolkit (the inner one takes the wheel), and an unscrollable page is not recoverable by
            // whoever wrote it.
            _pageHost = new ScrollView(ScrollViewMode.Vertical);
            _pageHost.style.flexGrow = 1f;
            _pageHost.style.flexShrink = 1f;
            _pageHost.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _pageHost.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            split.Add(_pageHost);

            // The missing piece that makes the window show anything at all: without this the tab strip
            // and the page are built into a detached element and the window is an empty box.
            _window.Body.Add(split);

            // A footer with the things that are true of the whole menu rather than of one tab.
            var footer = Widgets.Row(_window.Footer);
            footer.style.flexGrow = 0f;
            Widgets.Label(footer, GameBuild.Describe(), true);
            Widgets.Filler(footer);
            Widgets.DescribedButton(footer, "Reset size",
                "Put the window back to its default size and position",
                delegate { _window.ResetGeometry(); });
            Widgets.DescribedButton(footer, "Close", "Close the menu. F1 opens it again.",
                delegate { Close(); });

            // Deliberately no Refresh here. Open() does it, and doing it twice would rebuild the strip
            // and select the first tab all over again for no visible difference.
            _ready = true;
            return true;
        }

        private static void AddTab(string title, string builtInKey, MenuPage page)
        {
            var tab = new Tab { BuiltInKey = builtInKey, Page = page };

            // Captured into locals, because a delegate closing over the loop's own parameters would
            // otherwise bind every tab to whatever the last iteration left behind.
            var capturedKey = builtInKey;
            var capturedPage = page;

            tab.Button = Widgets.TabButton(_tabStrip, title, delegate { Select(capturedPage, capturedKey); });
            tab.Button.style.unityTextAlign = TextAnchor.MiddleLeft;
            Tabs.Add(tab);
        }

        private static void Select(MenuPage page, string builtInKey)
        {
            if (!Open()) return;

            _selected = page;
            _selectedBuiltIn = builtInKey;

            if (page != null) ShowPage(page);
            else ShowBuiltIn(builtInKey);

            Highlight();
        }

        private static void ShowPage(MenuPage page)
        {
            if (_pageHost == null) return;
            _pageHost.Clear();

            if (page.Build != null)
            {
                try
                {
                    page.Build(_pageHost);
                }
                catch (Exception ex)
                {
                    UiLog.Error("The '" + page + "' tab threw while building: " + ex);
                    Widgets.Error(_pageHost, "This tab failed to draw: " + ex.Message);
                }
            }

            _pageHost.MarkDirtyRepaint();
        }

        private static void ShowBuiltIn(string key)
        {
            if (_pageHost == null || string.IsNullOrEmpty(key)) return;

            foreach (var builtIn in OrderedBuiltIn())
            {
                if (builtIn.Key != key) continue;
                if (!IsShowing(builtIn)) continue;

                _pageHost.Clear();
                try
                {
                    if (builtIn.Build != null) builtIn.Build(_pageHost);
                }
                catch (Exception ex)
                {
                    UiLog.Error("The built-in '" + builtIn.Title + "' tab threw while building: " + ex);
                    Widgets.Error(_pageHost, "This tab failed to draw: " + ex.Message);
                }

                _pageHost.MarkDirtyRepaint();
                return;
            }
        }

        /// <summary>
        /// Paint the selected tab. The strip is rebuilt on every refresh, so selection is derived from
        /// what is selected rather than kept as per-button state that has to be re-applied.
        /// </summary>
        private static void Highlight()
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                var tab = Tabs[i];
                var selected = _selectedBuiltIn != null
                    ? tab.BuiltInKey == _selectedBuiltIn
                    : tab.Page != null && tab.Page == _selected;

                Widgets.SetSelected(tab.Button, selected);
            }
        }

        private static bool IsShowing(BuiltInPage page)
        {
            if (page.Available == null) return true;

            try
            {
                return page.Available();
            }
            catch (Exception)
            {
                // A tab that cannot say whether it is available is better hidden than crashing the
                // whole menu on every refresh.
                return false;
            }
        }

        private static bool HasBuiltIn(string key)
        {
            foreach (var builtIn in OrderedBuiltIn())
            {
                if (builtIn.Key == key) return IsShowing(builtIn);
            }
            return false;
        }

        private static bool HasPage(MenuPage page)
        {
            foreach (var candidate in OrderedPages())
            {
                if (candidate == page) return candidate.Available;
            }
            return false;
        }

        /// <summary>Built-in tabs keep registration order, which is how "Mods" ends up first.</summary>
        private static List<BuiltInPage> OrderedBuiltIn()
        {
            lock (BuiltIn) { return new List<BuiltInPage>(BuiltIn); }
        }

        private static List<MenuPage> OrderedPages()
        {
            lock (Pages)
            {
                var copy = new List<MenuPage>(Pages);
                copy.Sort(delegate (MenuPage a, MenuPage b)
                {
                    if (a.Order != b.Order) return a.Order.CompareTo(b.Order);
                    return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
                });
                return copy;
            }
        }
    }
}