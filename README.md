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

How much work a fish costs is set from your own pace rather than a fixed number, so a
fast typist and a slow one wait a comparable number of minutes for a bite.

## Playing

- **Drag the cat** to slide the pet along the taskbar.
- **Click the basket** to sell your catch, buy upgrades and pick skins.
- **Click a gift box** once it parks beside the cat.
- **Click `stats`** in the panel for where the day went.
- **Quit** from the taskbar button, or the `quit` button in the panel.

![The shop panel](docs/shot_panel.png)

![Cat skins](docs/skins.png)

## Activity

![The activity panel](docs/shot_stats.png)

The activity panel shows today hour by hour, which applications took the time, and how
many clicks and keystrokes went into each. Work stretches are found with changepoint
detection (PELT) rather than a fixed busy/idle threshold, so the boundaries come from
where your own pace actually shifts. Application icons and names come from the
executables themselves. The `sites` button turns window titles on, which is what lets
the list name pages and documents instead of just the program; see Privacy below.

Pacing reads the same signal. DeskPond keeps a running estimate of your median input
rate, one sample per active minute, and prices the next fish at roughly five minutes of
work at that rate. The estimate uses the P-square algorithm, which tracks a quantile
with five markers instead of the whole history, so it costs a fixed 132 bytes however
long the program runs. Idle minutes are skipped, or stepping away would drag the median
toward zero and make fish nearly free on return. It takes twelve active minutes to warm
up; until then a fixed range applies.

## Privacy

Nothing DeskPond records ever leaves your machine. It makes no network requests at
all. Everything below is written to two files in your local app data folder, and
deleting them costs you nothing but statistics.

**Input is counted, not read.** DeskPond installs a system-wide input hook to tell how
busy you are. The keyboard hook reads only the message id saying that a key went down.
It never reads which key, so that information is not available to the program at any
point. The code is `Assets/Scripts/DeskPond/GlobalInputCounter.cs`, short enough to
read in a minute.

**Activity is logged locally.** To pace fish against your own working rhythm and to show
where your time went, DeskPond records five minute slices of how much input happened and
which applications were in front. Process names only, kept for 30 days.

**Window titles are off by default.** A title says far more than a process name does:
document names, page titles, who you were talking to. Without them the panel can tell you
that you spent two hours in a browser, but not which sites. Turn them on with the
`sites` button in the activity panel; the choice is stored per player, so a build you hand
to someone else still starts with it off. Turning it back off stops recording and deletes
the titles already captured, and they expire after 7 days regardless.

Set `trackActivity` to false to record nothing at all. `activity.json` sits next to
`save.json` and can be deleted at any time; the game does not need it.

## Building

Requires Unity 6000.3.5f1 on Windows.

```
set DP_BUILD_DIR=C:\path\to\output
Unity.exe -batchmode -quit -projectPath . -executeMethod DeskPondBuild.Run
```

Close any running `DeskPond.exe` first; it locks `UnityPlayer.dll` and the build fails.

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md). Free to read, build, modify and share
for personal projects, study, and any non-profit purpose. Selling it, or shipping it
inside something sold, needs permission.

## Credits

UI font is [Silkscreen](https://fonts.google.com/specimen/Silkscreen) by Jason Kottke,
SIL Open Font License, included in `Assets/Resources/Fonts/OFL.txt`. The font keeps
its own licence and is not covered by the one above.
