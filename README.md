# ScamWYF.Modding.Core

The shared library for **Scam With Your Friends** mods. One repo of common code that every mod
compiles into itself, so two mods cannot quietly fight over the same Harmony patch, the same
hotkey or the same menu tab.

Made against **Unity 6000.3.10f1**, Mono, managed stripping **on**.

| Repo | What it is |
|---|---|
| **scam-wyf-modding-lib** (this one) | Shared library + the one build script every mod uses |
| **scam-wyf-aibackend** | Sends the game's AI calls to your own LLM |
| **scam-wyf-modhandler** | In-game list of installed plugin files, and the collision report |
| **scam-wyf-setup** | BepInEx, Doorstop and the corlib patches the game needs |

---

## Using it

A mod adds this repo as a submodule and calls its `build.ps1`. The library builds to its own dll,
`ScamWYF.Modding.Core.dll`, which the mod references rather than embeds.

```powershell
git submodule add ../scam-wyf-modding-lib vendor/ScamWYF.Modding.Core
```

**Why a separate dll.** The library owns singletons: one hotkey table, one menu, one panel, one hot
reload watcher per config. Compiled into every mod's dll, each mod got its own private copy of all of
them, so two mods meant two F1 bindings and two windows opening on top of each other. A shared dll makes
those singletons genuinely single.

Git refuses `file://`-style submodule clones by default, so for a local sibling path add
`-c protocol.file.allow=always`. That is also a reminder that the recorded URL is a relative
sibling path: when these repos get published, point the submodule at the real remote with
`git submodule set-url vendor/ScamWYF.Modding.Core <url>`, otherwise a fresh clone on another
machine will not find the library.

`build.ps1` in a mod repo is then about ten lines:

```powershell
& "$PSScriptRoot\vendor\ScamWYF.Modding.Core\build.ps1" `
    -Project ScamWYF.Whatever `
    -Sources "$PSScriptRoot\src" `
    -Refs "$managed\Newtonsoft.Json.dll"
```

The library's own sources, its reference list and the compiler flags live here and nowhere else.
Start from [`template/Plugin.cs`](template/Plugin.cs): rename the namespace and the guid and you
have a working mod.

### What gets deployed

| File | Where |
| --- | --- |
| `YourMod.dll` | `BepInEx\plugins` |
| `ScamWYF.Modding.Core.dll` | `BepInEx\core` |

`build.ps1` builds the library first and copies both, so a normal build leaves the game in a working
state. `BepInEx\core` is the right home for the library because BepInEx loads it first and every mod
can then resolve it; anything in `plugins` would be loaded as though it were a plugin.

---

## What a mod gets

### `ScamMod` — the base class

```csharp
[BepInPlugin(PluginGuid, "My Mod", "1.0.0")]
public sealed class Plugin : ScamMod
{
    public const string PluginGuid = "com.example.mymod";

    protected override void OnModLoad() { /* do the work */ }
    protected override void OnModUnload() { /* undo anything OnModLoad started */ }
}
```

* Identity comes from `[BepInPlugin]`, read by reflection. The name the mod list shows and the name
  the session uses cannot drift apart, and there is nothing to keep in sync by hand.
* `Awake`, `OnDestroy` and `Update` are sealed. Anything thrown from `OnModLoad` is logged with
  the mod's name and turns that mod inert instead of taking the session down.
* Two plugins with the same guid: the second is rejected, says so, and switches itself off.
* On unload, the mod's patches, hotkeys, windows and menu tabs are removed for it.
* A mod gets a tab in the universal menu whether it asks for one or not, so a mod nothing can reach
  is not a mod nobody can configure.

### The menu — one window, one hotkey, every mod

Press **F1**. One window with a tab strip: each mod that shares this library has a tab, and the
library adds **Mods** (what is loaded, what is colliding) and **About** (this session, where the
files are). The mod handler takes over a **Plugins** tab for the list of plugin files on disk.

```csharp
ModMenu.AddPage(this, "Status", page =>
{
    // The page host is already a scroll view. Do not add another: a nested one takes the wheel first,
    // which reads as a menu that has stopped responding.
    Widgets.FieldRow(page, "Endpoint", Config.BaseUrl.Value);

    var fold = Widgets.Section(page, "All settings", false);
    var body = Widgets.SectionBody(fold);
    ConfigEditor.Build(body ?? page);   // every setting, from the config file itself, one group per section
});
```

A mod contributes a page and nothing else. The menu owns the window, the tab strip, the hotkey and
the layout, so pages cannot fight over any of it — the same argument this library makes about
patches and hotkeys, applied to interfaces. The menu hotkey is in
`BepInEx\config\scamwyf-modding.cfg`, and is editable in game on the About tab.

The window itself is a normal window: drag the title bar to move it, drag the bottom-right corner to
resize, and both stick between sessions. Size and position are saved per window title in
`scamwyf-modding.cfg`, and clamped so a window cannot be shrunk into nothing or dragged out of reach —
the rules live in [`src/ui/WindowGeometry.cs`](src/ui/WindowGeometry.cs) and are covered by
`tests/window`.

The menu belongs to the library rather than to the mod handler on purpose: a user who only wants
the AI backend should not have to install a plugin manager to reach its settings.

### The UI — the base game's own, not a lookalike

The game builds its menus with **UI Toolkit** (`UIDocument`, `PanelSettings`, a theme
stylesheet). So does this. The shared panel clones the game's own `PanelSettings`, so a mod window
inherits its theme, fonts and scaling rules, and the colours are read out of the game's live theme
rather than guessed at:

```csharp
var colour = UiTheme.Accent;      // sampled from the game's theme
Widgets.Note(page, "Something to note.");   // styled to match the rest of the game
```

Two things this gets that an IMGUI overlay could not:

* **Text input works properly.** A text field behaves like a text field — focus, selection, the
  on-screen keyboard — which is what makes an in-game config editor usable.
* **Focus.** Clicking a window brings it to the front.

A mod wanting a floating overlay rather than a tab can still have a window:

```csharp
_window = UiWindows.GetOrCreate(this, "Overlay", 420f, 300f);
Hotkeys.Register(this, Key.F7, "Toggle", _window.Toggle);
```

If the shared panel cannot be created — a stripped build with no theme and no font to be found —
that is logged once and named in the menu, and every mod still loads. A mod with no interface is
degraded; a mod that takes the session down is not acceptable.

### Config — hot reload and an in-game editor

```csharp
var settings = Settings;   // range-checked, and builds the editor for the same file
var timeout = settings.Bind("2 - Endpoint", "TimeoutSeconds", 90, 1, 3600,
    "Per-attempt timeout in seconds.");
var seconds = settings.Int(timeout, 1, 3600);   // clamped, warned about, never throws

WatchConfig();   // edits to the .cfg now take effect without a restart
```

* **`Settings.Bind` with a min and max** is what makes the editor useful: the setting becomes a
  slider, and the description becomes its help text. Nothing extra to declare.
* **Editing in game writes straight to the file**, so the text file and what the mod is using
  cannot drift apart. Editing the file works too, and is picked up within about half a second.
* **Ranges are enforced on every read.** `Timeout = ages` is a warning in the log, not a mod that
  throws during load.

Hot reload is done by polling the file's timestamp rather than with `FileSystemWatcher`, on
purpose: managed stripping has left `System.IO.FileSystemWatcher` in this build with only `Path`
and `Created` — no `Changed`, no way to set its filter — so a watcher here would silently never
fire. Polling costs one stat call per tick and always works. A save is only acted on once the
file's timestamp *and* length have been stable for a moment, so a half-finished write is never read.

### `PatchCoordinator` — Harmony, with collisions made visible

```csharp
PatchCoordinator.TryPatch(this, typeof(KolkataApi), "CompleteOpenRouterAsync",
    new[] { typeof(JObject), typeof(CancellationToken), typeof(bool) },
    "route AI calls",
    new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(Prefix))));
```

* One Harmony instance per mod, keyed by the mod's guid, so unloading a mod removes exactly its
  own patches.
* If another mod has already patched the same method, that is logged, with both owners named, and
  reported in the menu. Patches do compose, but a prefix quietly not firing is a miserable thing
  to debug, so it says so out loud.
* When the target method has moved or changed shape — the normal consequence of a game update —
  the log says what was expected, what the running build is, and which same-named methods do
  exist, instead of throwing an `AccessTools` stack trace.

### `Hotkeys` — one keyboard poller

```csharp
Hotkeys.Register(this, Key.F7, "Toggle my overlay", _window.Toggle, Key.LeftCtrl);
```

Mods do not poll `Keyboard.current` themselves. Two mods on the same key is reported in the log
and in the menu; both still fire, because silently ignoring one just moves the confusion. Hotkeys
are suppressed while a text field in the menu has focus, so typing "f1" into the menu-key setting
does not toggle the menu mid-keystroke.

### `GameBuild` — what build is this

`GameBuild.Describe()` names the build for a startup log. `CheckUnityVersion(log, modId,
"6000.3.10f1")` warns when a mod is running on a different Unity than it was written against. It
also resolves game types, fields and methods by name, reporting a miss as the game update it
almost always is.

### `ModRegistry` — who else is here

`ModRegistry.Mods` lists the mods sharing the library, and `ModRegistry.CollisionReport()` returns
duplicate ids, patch collisions and hotkey collisions as text, or null when there are none. The
menu's Mods tab renders both.

---

## Building

```powershell
.\build.ps1 -LibraryOnly                        # just check the library compiles
.\build.ps1 -LibraryOnly -Test                  # ...and run the tests
.\build.ps1 -Project MyMod -Sources .\src        # a mod, via a mod repo's wrapper
```

`-Test` does two things:

1. **Compiles [`tests/api`](tests/api)** together with the library against the game's own
   assemblies. That file is the library's public surface written out once, so every UI Toolkit and
   BepInEx call a mod is expected to make is checked against what actually shipped. Because the
   build uses `-nostdlib+` against the game's own `mscorlib.dll` (see below), anything managed
   stripping removed is a build error here rather than a `MissingMethodException` in somebody's
   session.
2. **Runs the behaviour tests**, each compiling the real source file it covers rather than a copy:

   | Suite | Covers |
   |---|---|
   | [`tests/watcher`](tests/watcher) | config polling, waiting for a write to settle, surviving a file that does not parse |
   | [`tests/window`](tests/window) | window size and position clamping: minimums, screen limits, idempotence |

   These need the .NET SDK rather than just Roslyn, and are skipped with a warning if `dotnet` is not
   on `PATH`.

No .NET SDK required to build. It runs Roslyn directly: `-CscDll <path to csc.dll>` to point at
one, or it finds `csc.exe` from VS Build Tools, or the copy inside a .NET SDK.

`-GameDir` is auto-detected (Steam paths, plus `SWYG_GAME_DIR`); pass it explicitly if the game is
somewhere unusual.

### On a build agent

The build compiles against the game's own assemblies, so it needs
`Scam With Your Friends_Data\Managed` and `BepInEx\core` to exist somewhere. The game is not
redistributable, so a runner has to be given them another way — a cached artifact, a mounted copy, or
a self-hosted runner with the game installed. Set `SWYG_GAME_DIR` to the folder containing both and
everything else is found. `.\build.ps1 -NoCopy -Test` is the command to run: `-NoCopy` because an agent
should not be writing to a game install it does not own, `-Test` because a build that only compiles is
not much of a check.

Because the library is a submodule, a build of a mod needs
`git clone --recurse-submodules`, or `git submodule update --init --recursive` in an existing clone.

### The one build rule that matters

Compilation uses `-nostdlib+` against **the game's own `mscorlib.dll`**, not a reference assembly.
The compiler sees exactly the API surface Unity shipped, so code using a method that managed
stripping removed fails to build rather than failing at runtime. This is why mods here do not need
an unstripped corlib of their own.

Verified compiling against the real game build:

```bash
dotnet /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll -target:library -nostdlib+ -langversion:7.3 \
  -r:"<game>/Scam With Your Friends_Data/Managed/mscorlib.dll" ... src/*.cs template/*.cs
```

C# 7.3, no third-party packages, no `.csproj`. A raw `csc` invocation against a stripped BCL is the
constraint that shapes this: it has to work without MSBuild or NuGet in the loop.

### Why the UI is inline styles rather than a stylesheet

A `.uss` would have to ship as an asset loaded at runtime by path, which means working out where the
game was installed at runtime and handling it being wrong. Setting `style.backgroundColor` and friends
in code costs a few more bytes and keeps the whole styling story in one readable place.