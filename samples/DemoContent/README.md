# Demo Content

A working mod, kept beside the library, that adds things to the in-game computer with nothing but the
public API: a game the mod draws itself, a conversation scam, a form scam, a menu tab and a toast.

It is a sample, not something anyone installs. The ids are prefixed `demo-`, it registers no Harmony
patches of its own, and nothing is released from this folder. `./build.ps1 -LibraryOnly -Test` compiles
it on every run, so it cannot quietly rot.

## What it adds

| Thing | Where it comes from | What it is |
|---|---|---|
| **Lucky Sevens** (`demo-lucky-sevens`) | `Plugin.AddApp` → `ContentRegistry.RegisterGame` | A slot machine drawn by the mod: reels on a panel, credits/spins/best under it, one accent-coloured spin button. |
| **Calculator** (`demo-calculator`) | same | A working calculator: 20 keys, chained operators, `+/-`, `%`, and "Cannot divide by zero" instead of infinity. |
| **Stopwatch** (`demo-stopwatch`) | same | Start/stop/reset, ticking off the element's own scheduler - the demo of an app that keeps drawing after it is built. |
| **Notebook** (`demo-notebook`) | same | A multiline text field whose text survives a window rebuild, held in the mod rather than the element. |
| **Magic 8-Ball** (`demo-eight-ball`) | same | The smallest app that does something different every press. |
| **Dog Walking Scam** (`demo-dog-scam`) | `Scams.DogWalking` → `ContentRegistry.RegisterConversationScam` | A conversation scam: four objectives that check off as you talk, the last one a tag number the caller has to say. |
| **Library Card** (`demo-library-scam`) | `Scams.LibraryCard` → `ContentRegistry.RegisterFormScam` | A form scam, the bank-login shape: the app collects a card number and submits it to the source the scam type scores. |
| **Demo content** tab | `ModMenu.AddPage` | Lists what actually registered, plus buttons that open each app and a toast. |
| **F9** | `Hotkeys.Register` | Puts a message in the game's notification stack. |

The five apps are the point: each is one `OsAppSpec.GameApp` plus one callback, in
[`apps/`](apps), and they share [`apps/AppKit.cs`](apps/AppKit.cs) - a card using the game's own
`kolkata-panel` class, and ink, muted ink and key faces chosen for the desktop's pale windows. That is
the whole of what a drawn app needs to look like it belongs in the OS.

One thing to get right, and it is easy to get wrong: **the library's `UiTheme` colours are the mod
menu's**, read from the game's own dark menu theme. An OS window is light, so white text from `UiTheme`
disappears on it. The heading the library adds is fine - it carries the game's own
`kolkata-app-title` class - but anything a mod styles by hand has to pick colours for a pale window.

The two scams are free and unlocked from day one, so they appear in Scamazon without anything else being
arranged. The game app and both scam apps have desktop orders set in the specs.

## Build and run it

```
./build.ps1 -LibraryOnly                  # the library, into BepInEx\core
./build.ps1 -Project ScamWYF.DemoContent -Sources ./samples/DemoContent
```

Or from a checkout that is already set up, in one go:

```
./build.ps1 -Project ScamWYF.DemoContent -Sources ./samples/DemoContent -OutDir ./bin/demo
```

The second form copies the dll into `BepInEx/plugins`; the library itself has to be in `BepInEx/core`
(`-LibraryOnly` without `-NoCopy` puts it there, and every mod's build does it too). Start the game,
start a session, and open the office computer: the three apps are on the desktop.

To watch it work from outside the game, the log has what registered:

```
[Info   :Demo Content] Demo content registered: 9 entries. They appear on the desktop in the office.
[Info   :ScamWYF.Modding] Content patches installed (5 of 5 catalogs hooked).
```

A rejection appears as a warning naming the entry and the reason - a scam pointing at an app nobody
registered, a caller value missing its `FICTIONAL_` prefix, a duplicate id. Nothing reaches the game in
that case, which is the point of the validation.

## The two things a drawn app has to get right

**State does not live in the elements.** The game rebuilds an app's window whenever it likes, and every
rebuild calls the draw callback with an empty tree. `LuckySevens` keeps credits, spins and reels in
static fields, and `Draw` is a function of them; anything stored in a label is gone after a rebuild.

**The callback is the whole body.** The library gives it a content root inside the app's window, already
styled and scrolled, with the app's title above it. Build into that element and nothing else - and build
*all* of it, because there is no partial redraw: the calculator, the stopwatch display and the 8-ball's
answer are all re-created from the app's fields every time.
