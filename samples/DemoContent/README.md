# Demo Content

A working mod, kept beside the library, that adds things to the in-game computer with nothing but the
public API: a game the mod draws itself, a conversation scam, a form scam, a menu tab and a toast.

It is a sample, not something anyone installs. The ids are prefixed `demo-`, it registers no Harmony
patches of its own, and nothing is released from this folder. `./build.ps1 -LibraryOnly -Test` compiles
it on every run, so it cannot quietly rot.

## What it adds

| Thing | Where it comes from | What it is |
|---|---|---|
| **Lucky Sevens** (`demo-lucky-sevens`) | `Plugin.RegisterContent` → `ContentRegistry.RegisterGame` | A desktop app the game has no builder for. Every element in its window comes from `LuckySevens.Draw`. |
| **Dog Walking Scam** (`demo-dog-scam`) | `Scams.DogWalking` → `ContentRegistry.RegisterConversationScam` | A conversation scam: four objectives that check off as you talk, the last one a tag number the caller has to say. |
| **Library Card** (`demo-library-scam`) | `Scams.LibraryCard` → `ContentRegistry.RegisterFormScam` | A form scam, the bank-login shape: the app collects a card number and submits it to the source the scam type scores. |
| **Demo content** tab | `ModMenu.AddPage` | Lists what actually registered, plus buttons that open each app and a toast. |
| **F9** | `Hotkeys.Register` | Puts a message in the game's notification stack. |

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
styled and scrolled, with the app's title above it. Build into that element and nothing else.
