<img src="docs/icon.png" width="72" align="left" alt="DeskPond icon">

# DeskPond

A desktop pet for Windows. A cat sits on your taskbar and fishes, and the more you
actually use your computer, the faster the fish bite.

<br clear="left">

![The pet on the waterline](docs/shot_pet.png)

## How it works

The taskbar is the pond. DeskPond runs as a transparent, always-on-top window that
covers the screen but stays click-through everywhere except over the cat, so the
desktop underneath keeps working normally.

Fish come from two places:

| Source | Rate |
| --- | --- |
| Clicks and keystrokes, counted system-wide | one fish per 10 to 1000 inputs |
| Sitting idle | one fish every 5 minutes |
| Sitting idle for 30 minutes | a drifting gift box with a free skin |

Idle time is never written to the save file, and any input resets the streak, so
closing the game and coming back cannot be used to farm rewards.

## Playing

- **Drag the cat** to slide the pet along the taskbar.
- **Click the basket** to open the shop: sell your catch, buy upgrades, pick skins.
- **Click a gift box** once it drifts over and parks beside the cat.
- **Quit** from the taskbar button, or the `quit` button in the shop panel.

![The shop panel](docs/shot_panel.png)

The panel drags by its header and resizes from the bottom-right corner. Both are
remembered between sessions.

## Skins

Skins are palette swaps rather than tints. The sprite is rebuilt with its fur hues
shifted, so the black outline stays black and the eyes keep their own colour, which
a `SpriteRenderer` tint would wash out along with everything else.

![Cat skins](docs/skins.png)

Eyes shift on a separate curve from the coat, so the blue cat gets amber eyes instead
of disappearing into itself.

## Animation

Cat animations are horizontal strips of square frames, dropped into
`Assets/Resources/CatFrames/` and sliced at runtime. No Sprite Editor work is needed,
and a state with no strip falls back to the static sprite, so partial art still runs.

![Animation frames](docs/frames.png)

A 4-frame 24x24 animation is a 96x24 PNG. Frames play forwards then backwards by
default, so a tail that swings out returns smoothly instead of snapping back.

## Tuning without rebuilding

`Assets/Data/GameConfig.asset` is read at runtime. Restart the game and the change
takes effect:

| Field | Does |
| --- | --- |
| `petZoom` | how large the cat is drawn |
| `waterColor`, `waterRimThickness` | the waterline stripe |
| `idleSecondsPerFish`, `idleSecondsPerGift` | idle pacing |
| `catFrameRate`, `catPingPong` | animation speed and loop style |
| `rodBaseOffset`, `rodTipOffset` | where the rod is held |
| `menuScale`, `menuWidth` | shop panel size |
| `inputCostScale` | testing aid; set to 0.05 to make fish bite almost at once |

Numbers that form a system, such as upgrade costs and skin definitions, live in
`Assets/Scripts/DeskPond/ShopDefs.cs` instead and need a rebuild.

## Building

Requires Unity 6000.3.5f1 on Windows.

```
set DP_BUILD_DIR=C:\path\to\output
Unity.exe -batchmode -quit -projectPath . -executeMethod DeskPondBuild.Run
```

`DeskPondBuild` sets the things the overlay depends on and will not work without:

- **Non-flip swapchain on Direct3D 11.** Per-pixel window transparency does not work
  with the DXGI flip model.
- **Run in background.** A pet that freezes when you click another window is useless.
- **A taskbar button.** A borderless popup window gets none by default, which leaves
  no way to quit.

Close any running `DeskPond.exe` first; it locks `UnityPlayer.dll` and the build fails.

Command line flags for debugging:

| Flag | Does |
| --- | --- |
| `-nooverlay` | run as a normal opaque window, to separate render bugs from transparency bugs |
| `-debuginput` | log every resolved click |
| `-gift` | send a gift box on launch |

## Privacy

DeskPond installs a system-wide input hook to count how busy you are. It is a counter,
not a key logger.

The keyboard hook reads only `wParam`, the message id that says a key went down. It
never dereferences `lParam`, which is where the struct holding the virtual key code
lives, so which key you pressed is never available to the process at any point. Nothing
about your input is stored or sent anywhere; the save file holds a running total and
nothing else.

The relevant code is `Assets/Scripts/DeskPond/GlobalInputCounter.cs`, and it is short
enough to read in a minute.

## Layout

```
Assets/
  Scripts/DeskPond/   game code
  Editor/             build script and the pixel-art asset importer
  Art/Placeholders/   sprites
  Resources/          animation strips and the UI font, loaded by path at runtime
  Icon/               application icons
  Data/               GameConfig.asset
  Scenes/             DeskPond.unity
```

Everything except the cat, basket and waterline is created at runtime, so the scene
stays small.

## Credits

UI font is [Silkscreen](https://fonts.google.com/specimen/Silkscreen) by Jason Kottke,
SIL Open Font License, included in `Assets/Resources/Fonts/OFL.txt`.
