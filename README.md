# ScamWYF.Modding.Core

The shared library for **Scam With Your Friends** mods. One repo of common code that every mod
compiles into itself, so two mods cannot quietly fight over the same Harmony patch, the same
hotkey or the same `OnGUI`.

Made against **Unity 6000.3.10f1**, Mono, managed stripping **on**.

| Repo | What it is |
|---|---|
| **scam-wyf-modding-lib** (this one) | Shared library + the one build script every mod uses |
| **scam-wyf-aibackend** | Sends the game's AI calls to your own LLM |
| **scam-wyf-modhandler** | In-game list of installed mods, and the collision report |
| **scam-wyf-setup** | BepInEx, Doorstop and the corlib patches the game needs |

---

## Using it

A mod adds this repo as a submodule and calls its `build.ps1`. The library's sources are compiled
**into the mod's dll**, so there is no extra file to deploy and no version to keep in step.

```powershell
git submodule add ../scam-wyf-modding-lib vendor/ScamWYF.Modding.Core
```

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

---

## What it gives a mod

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
* On unload, the mod's Harmony patches, hotkeys and windows are removed for it.

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
  reported in-game by the mod handler. Patches do compose, but a prefix quietly not firing is a
  miserable thing to debug, so it says so out loud.
* When the target method has moved or changed shape — the normal consequence of a game update —
  the log says what was expected, what the running build is, and which same-named methods do
  exist, instead of throwing an `AccessTools` stack trace.

### `Hotkeys` — one keyboard poller

```csharp
Hotkeys.Register(this, Key.F7, "Toggle my overlay", _window.Toggle, Key.LeftControl);
```

Mods do not poll `Keyboard.current` themselves. Two mods on the same key is reported in the log and
in-game; both still fire, because silently ignoring one just moves the confusion.

### `ImGuiHost` — one `OnGUI`

```csharp
_window = ImGuiHost.AddWindow(this, "My mod", new Rect(60f, 60f, 420f, 300f), Draw);
Hotkeys.Register(this, Key.F7, "Toggle", _window.Toggle);
```

Windows are drawn by a single hidden component, so overlays stack instead of fighting over focus.

Note: Unity 6000.3 exposes `GUIUtility.guiDepth` as read-only, so a mod cannot force its own
z-order. The host draws windows in registration order and leaves it there — keep overlays from
overlapping.

### `ModSettings` — config that survives being hand-edited

```csharp
var s = new ModSettings(base.Config, ModLog, ModId);
var timeout = s.Bind("General", "Timeout", 90, "Seconds per request.");
var seconds = s.Int(timeout, 5, 600);   // clamped, warned about, never throws
```

BepInEx already gives every plugin its own `.cfg`, so keys cannot collide across mods. What this
adds is range checking and a fallback: `Timeout = ages` is a warning in the log, not a mod that
throws during load.

### `ModRegistry` — who else is here

`ModRegistry.Mods` lists the mods sharing the library, and `ModRegistry.CollisionReport()` returns
duplicate ids, patch collisions and hotkey collisions as text, or null when there are none. The
mod handler renders it.

### `GameBuild` — what build is this

`GameBuild.Describe()` names the build for a startup log. `CheckUnityVersion(log, modId, "6000.3.10f1")`
warns when a mod is running on a different Unity than it was written against — the first thing to
check when something misbehaves after a game update.

---

## Building

```powershell
.\build.ps1 -LibraryOnly                        # just check the library compiles
.\build.ps1 -Project MyMod -Sources .\src        # a mod, via a mod repo's wrapper
```

No .NET SDK required. It runs Roslyn directly: `-CscDll <path to csc.dll>` to point at one, or it
finds `csc.exe` from VS Build Tools, or the copy inside a .NET SDK.

`-GameDir` is auto-detected (Steam paths, plus `SWYG_GAME_DIR`); pass it explicitly if the game is
somewhere unusual.

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

C# 7.3, no third-party packages, no `.csproj`. The library is source-only on purpose: it has to
compile inside a raw `csc` invocation against a stripped BCL, and there is no reason for a mod to
deploy two files to get a base class.