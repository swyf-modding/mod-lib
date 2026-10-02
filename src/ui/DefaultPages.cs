using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using BepInEx;
using System.IO;

namespace ScamWYF.Modding.Core.Ui
{
    /// <summary>
    /// The tabs the library draws itself: which mods are loaded, what they patched and bound, what is
    /// colliding, and what this session is made of.
    /// </summary>
    /// <remarks>
    /// These belong to the library rather than to the mod handler because they are diagnostics about the
    /// shared services, so they have to be available whether or not a plugin manager is installed. A
    /// user with one mod and no mod handler still gets somewhere to see what went wrong.
    /// </remarks>
internal static class DefaultPages
    {
        /// <summary>
        /// Register the library's own tabs and bind the menu hotkey. Called once, from
        /// <see cref="LibraryRuntime.Start"/>, which runs before any mod finishes loading.
        /// </summary>
        internal static void Register()
        {
            // Order matters: these are the tabs that appear before any mod's own.
            ModMenu.SetBuiltInPage(ModMenu.ModsTab, "Mods", BuildMods);
            ModMenu.SetBuiltInPage(ModMenu.AboutTab, "About", BuildAbout);

            // Reserved for whoever wants to show installed plugins. The library registers a placeholder
            // so the strip has a sensible shape without it, and registering by key means the mod
            // handler's real version replaces this rather than adding a second tab.
            ModMenu.SetBuiltInPage(ModMenu.PluginsTab, "Plugins", BuildNoPlugins, delegate
            {
                return !PluginsClaimed;
            });
        }

private static bool _pluginsClaimed;

        /// <summary>
        /// Withdraw the library's placeholder Plugins tab because a mod handler has registered a real
        /// one under the same key.
        /// </summary>
        /// <remarks>
        /// The placeholder exists so the tab strip has a sensible shape when no plugin manager is
        /// installed. A mod handler that calls this and then registers its own tab under the same key
        /// replaces the placeholder outright - the two registrations never appear together, because
        /// SetBuiltInPage is keyed.
        /// </remarks>
        public static void ClaimPluginsTab()
        {
            _pluginsClaimed = true;
            ModMenu.Refresh();
        }

        private static bool PluginsClaimed
        {
            get { return _pluginsClaimed; }
        }

        private static void BuildNoPlugins(VisualElement host)
        {
            Widgets.Note(host,
                "The mod handler is not installed, so there is no list of installed plugins here. " +
                "Every mod sharing this library is on the Mods tab.", false, false);
        }

        // ---------------------------------------------------------------- Mods

        private static void BuildMods(VisualElement host)
        {
            // The menu's page host already scrolls; a nested one would take the wheel first.
            var scroll = host;

            var mods = ModRegistry.Mods;
            Widgets.Heading(scroll, "Loaded mods (" + mods.Length + ")");

            if (mods.Length == 0)
            {
                Widgets.Note(scroll, "No mods have registered with this library.");
            }
            else
            {
                foreach (var mod in mods) BuildModRow(scroll, mod);
            }

            Widgets.Spacer(scroll, 8f);
            BuildCollisions(scroll);
            Widgets.Spacer(scroll, 8f);
            BuildServices(scroll);
        }

        private static void BuildModRow(VisualElement parent, ScamMod mod)
        {
            var row = Widgets.Column(parent);
            row.style.marginBottom = 4f;

            var header = Widgets.Row(row);
            var name = Widgets.Label(header, mod.DisplayName, false);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.fontSize = 14f;

            Widgets.Filler(header);

            var version = Widgets.Label(header, mod.ModVersion, true);
            version.style.fontSize = 11f;

            Widgets.FieldRow(row, "Id", mod.ModId);
            Widgets.FieldRow(row, "Config", mod.Config.ConfigFilePath);

            switch (mod.State)
            {
                case ModState.Active:
                    break;
                case ModState.Failed:
                    Widgets.Error(row, "Failed to load: " + mod.FailureReason);
                    break;
                case ModState.Rejected:
                    Widgets.Error(row, "Rejected: " + (mod.FailureReason ?? "duplicate mod id"));
                    break;
                default:
                    Widgets.Warning(row, "Still loading.");
                    break;
            }

            var watcher = mod.Watcher;
            if (watcher != null)
            {
                var when = watcher.LastChangeUtc.HasValue
                    ? watcher.LastChangeUtc.Value.ToLocalTime().ToString("HH:mm:ss")
                    : "never";
                Widgets.FieldRow(row, "Config reloads", watcher.ReloadCount + " so far, last at " + when);
            }

            var tabs = new List<string>();
            foreach (var page in ModMenu.RegisteredPages)
            {
                if (page.OwnerId == mod.ModId && page.Available) tabs.Add(page.Title);
            }
            if (tabs.Count > 0) Widgets.FieldRow(row, "Menu tabs", string.Join(", ", tabs.ToArray()));

var windows = UiWindows.ForOwner(mod.ModId);
            if (windows.Length > 0) Widgets.FieldRow(row, "Windows", string.Join(", ", windows));
        }

        private static void BuildCollisions(VisualElement parent)
        {
            Widgets.Heading(parent, "Collisions");

            var collisions = ModRegistry.CollisionReport();
            if (collisions == null)
            {
                Widgets.Note(parent, "None. Every patch, hotkey and window here belongs to one mod.");
                return;
            }

            // One note per line rather than one blob: the point is that each line can be matched up with
            // the log and with the mod it names.
            foreach (var line in collisions.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                Widgets.Warning(parent, trimmed);
            }

            Widgets.Paragraph(parent,
                "Patches and hotkeys both still work when two mods want the same one - they compose " +
                "rather than one silently swallowing the other. This is listed because a prefix quietly " +
                "not firing is a miserable thing to debug, not because it is an error.");
        }

        private static void BuildServices(VisualElement parent)
        {
            Widgets.Heading(parent, "Harmony patches");

            var patches = PatchCoordinator.Installed;
            if (patches.Length == 0)
            {
                Widgets.Note(parent, "No patches installed through the coordinator.");
            }
            else
            {
                foreach (var patch in patches)
                {
                    Widgets.FieldRow(parent, patch.OwnerName, patch.Method + "  [" + patch.Kinds + "]");
                }
            }

            Widgets.Spacer(parent, 6f);
            Widgets.Heading(parent, "Hotkeys");

            var hotkeys = Hotkeys.Bindings;
            if (hotkeys.Length == 0)
            {
                Widgets.Note(parent, "No hotkeys registered.");
            }
            else
            {
                foreach (var binding in hotkeys)
                {
                    Widgets.FieldRow(parent, binding.OwnerName, binding.Signature + "  -  " + binding.Description);
                }
            }
        }

        // ---------------------------------------------------------------- About

        private static void BuildAbout(VisualElement host)
        {
            // The menu's page host already scrolls; a nested one would take the wheel first.
            var scroll = host;

            Widgets.Heading(scroll, "This session");
            Widgets.FieldRow(scroll, "Game", GameBuild.Describe());
            Widgets.FieldRow(scroll, "Game folder", BepInEx.Paths.GameRootPath);
            Widgets.FieldRow(scroll, "Mods sharing this library", ModRegistry.Count.ToString());

            Widgets.Spacer(scroll, 8f);
            Widgets.Heading(scroll, "This library");
            Widgets.FieldRow(scroll, "Menu hotkey", MenuHotkeyText());
            Widgets.FieldRow(scroll, "Menu config", LibraryConfigPath());

            // Which panel the theme came from is the first thing to check when a mod's window looks
            // unlike the rest of the game, so it is worth saying outright rather than leaving the user
            // to compare screenshots.
            Widgets.FieldRow(scroll, "UI theme", UiTheme.Source);
            Widgets.FieldRow(scroll, "UI panel", UiPanel.PanelSource);

            var problem = UiPanel.UnavailableReason;
            if (problem != null)
            {
                Widgets.Note(scroll, "The in-game menu is unavailable: " + problem +
                                ". Mods still load and work; only the interface is missing.", true, true);
            }

Widgets.Spacer(scroll, 8f);

            // The library's own settings - currently just the menu hotkey - get the same generated editor
            // every mod gets. The hotkey a person is most likely to want to change, editable in game,
            // without anything having been declared for it.
            if (LibraryRuntime.MenuPage != null && LibraryRuntime.MenuPage.SettingCount > 0)
            {
                Widgets.Heading(scroll, "Menu settings");
                LibraryRuntime.MenuPage.Build(scroll);
            }

            Widgets.Spacer(scroll, 8f);
            Widgets.Heading(scroll, "Files");
            Widgets.FieldRow(scroll, "BepInEx", BepInEx.Paths.BepInExRootPath);
            Widgets.FieldRow(scroll, "Config folder", BepInEx.Paths.ConfigPath);
            Widgets.FieldRow(scroll, "Plugin folder", BepInEx.Paths.PluginPath);
            Widgets.FieldRow(scroll, "Log", LogPath);

            Widgets.Spacer(scroll, 6f);
            Widgets.Paragraph(scroll,
                "The log is where anything this menu cannot show ends up: a mod that failed during load, " +
                "a Harmony patch that no longer exists after a game update, a config value that could " +
                "not be parsed. It is the first thing worth attaching to a bug report.");

            var row = Widgets.WrapRow(scroll);
            Widgets.DescribedButton(row, "Open config folder", "Open BepInEx\\config in a file browser",
                delegate { Open(BepInEx.Paths.ConfigPath); });
            Widgets.DescribedButton(row, "Open plugin folder", "Open BepInEx\\plugins in a file browser",
                delegate { Open(BepInEx.Paths.PluginPath); });
            Widgets.DescribedButton(row, "Open log", "Open LogOutput.log in a text editor",
                delegate { Open(LogPath); });
        }

private static void Open(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                Application.OpenURL("file://" + path.Replace("\\", "/"));
            }
            catch (Exception ex)
            {
                UiLog.Warn("Could not open " + path + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Where BepInEx writes its log. Not exposed on Paths, so it is composed from the root - which is
        /// also how BepInEx itself decides where to put it.
        /// </summary>
        private static string LogPath
        {
            get
            {
                try
                {
                    return Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
                }
                catch (Exception)
                {
                    return BepInEx.Paths.BepInExRootPath + "\\LogOutput.log";
                }
            }
        }

        private static string MenuHotkeyText()
        {
            var text = new StringBuilder();

            foreach (var binding in Hotkeys.Bindings)
            {
                if (!Hotkeys.IsMenuDescription(binding.Description)) continue;
                if (text.Length > 0) text.Append(" or ");
                text.Append(binding.Signature);
            }

            return text.Length == 0
                ? "not bound - set Menu/Key in " + LibraryConfigPath()
                : text.ToString();
        }

        private static string LibraryConfigPath()
        {
            try
            {
                return LibraryRuntime.Config.ConfigFilePath;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}