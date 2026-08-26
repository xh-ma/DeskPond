<img src="docs/icon.png" width="72" align="left" alt="DeskPond icon">

# DeskPond

A desktop pet for Windows. A cat sits on your taskbar and fishes, and the more you
actually use your computer, the faster the fish bite.

<br clear="left">

![The pet on the waterline](docs/shot_pet.png)

## How it works

The taskbar is the pond. DeskPond runs as a transparent, always-on-top window that
stays click-through everywhere except over the cat, so the desktop underneath keeps
working normally.

Fish come from clicks and keystrokes, counted system-wide, or from sitting idle. A
long enough idle stretch also sends a gift box drifting over with a free skin.

## Playing

- **Drag the cat** to slide the pet along the taskbar.
- **Click the basket** to sell your catch, buy upgrades and pick skins.
- **Click a gift box** once it parks beside the cat.
- **Quit** from the taskbar button, or the `quit` button in the panel.

![The shop panel](docs/shot_panel.png)

![Cat skins](docs/skins.png)

## Privacy

DeskPond installs a system-wide input hook to count how busy you are. It is a counter,
not a key logger.

The keyboard hook reads only the message id saying that a key went down. It never
reads which key, so that information is not available to the program at any point.
Nothing about your input is stored or sent anywhere; the save file holds a running
total and nothing else.

The code is `Assets/Scripts/DeskPond/GlobalInputCounter.cs`, short enough to read in
a minute.

## Building

Requires Unity 6000.3.5f1 on Windows.

```
set DP_BUILD_DIR=C:\path\to\output
Unity.exe -batchmode -quit -projectPath . -executeMethod DeskPondBuild.Run
```

Close any running `DeskPond.exe` first; it locks `UnityPlayer.dll` and the build fails.

## Credits

UI font is [Silkscreen](https://fonts.google.com/specimen/Silkscreen) by Jason Kottke,
SIL Open Font License, included in `Assets/Resources/Fonts/OFL.txt`.
