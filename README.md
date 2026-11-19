<img src="docs/icon.png" width="72" align="left" alt="DeskPond icon">

# DeskPond

A desktop pet for Windows. A pixel cat sits in a boat on your taskbar and fishes. The
more you use your computer, the faster the fish bite, and leaving it alone still earns
a slow trickle.

<br clear="left">

![The cat fishing over a deep pond while a whale swims past](docs/shot_pet.png)

## How it works

The taskbar is the pond. DeskPond runs as a transparent, always-on-top window that is
click-through everywhere except over the cat, the boat and anything floating beside
them, so the desktop underneath keeps working normally.

It finds the taskbar on any edge. With the taskbar on the left, right or top, the pond
still runs along the bottom of the screen. It follows display and resolution changes
while running, and hides itself while a fullscreen game or video is in front.
Screenshot tools don't count as fullscreen, so the cat stays in your screenshots.

## Fishing

- **Typing and clicking are bait.** Each input drops a crumb in the water and counts
  toward the next bite. Bites are spaced at least 90 seconds apart, however fast you
  type, so a burst of work can't fill the bucket in a minute.
- **Idle time counts too.** Leave the computer alone and the cat still lands a fish
  every five minutes or so.
- **Golden bites.** Now and then the bobber thrashes and a gold `!` appears. Click it
  quickly to land a legendary fish. Miss, and the fish drags the cat along the water and
  leaves an old boot behind.
- **Focus streaks.** An unbroken stretch of work raises the odds of rarer fish, in three
  steps at 15, 30 and 60 minutes. The cat's tail speeds up to match, and when a long
  stretch ends the cat says so.

## The pond

![Night on the deep sea, a kelp forest at dawn, fog with jellyfish, rain on the lily pond](docs/scenes.png)

- **Pull the tab** on the pond's edge upward to make the water deeper and see more of
  the seabed: rock walls, coral, kelp, passing schools of fish.
- **Time of day** follows your clock: morning mist, noon sparkle, dusk, and night stars
  with a lantern on the boat.
- **Weather** changes in two-hour blocks: clear, rain, fog, and snow in winter. Each
  brings its own fish. Seasons follow the calendar month, and each has a fish that only
  bites then.
- **Deep-water visitors.** When the pond is deep enough, a whale or a bloom of jellyfish
  sometimes swims through. Click it before it's gone for a tip of dried fish or a pearl.
- **The crow.** Leave a full bucket alone for too long and a crow comes for a fish. Click
  it to shoo it away.

## The cat

![Coats, from ginger to storm](docs/skins.png)

- **Click the cat to pet it.** It purrs and a heart floats up. Petting builds a bond.
  Go a few days without and the bond fades and the cat sulks. A cat you pet often
  sometimes finds you a few dried fish.
- **Drag the cat** to slide the boat along the taskbar.
- **Letters.** Every so often the cat gets out its notebook, corks a letter into a bottle
  and tosses it onto the water. Click the bottle to read it. Some letters ask a question
  with two answers to tap, and the next letter replies to your answer. Old letters are
  kept under DEX → MAIL.

![A letter from the cat](docs/shot_letter.png)

The letters are written in advance, about forty of them. Each one waits for a moment it
fits, such as the hour, the weather, a long work stretch or a full bucket, and fills
in your own numbers. No language model is involved and nothing is sent anywhere.

## The panel

Click the bucket to open the panel. Drag its title bar to move it.

![Wardrobe, tank and dex sets](docs/shot_panel.png)

- **CATCH:** sell to the seagull merchant, one at a time or all at once, or move a live
  fish into the tank. The gull also has a request each day, such as "wants 3 rusty
  cans", that pays a bonus when you sell them to it.
- **SHOP:** the gull's upgrades, paid in dried fish. A better rod means faster bites and
  more golden ones, a bigger bucket holds more, and a better lure means rarer catches.
- **WEAR:** coats, hats, boats, buckets and ponds (lily pond, deep sea, kelp forest,
  lava river). Deeper ponds need a bigger boat.
- **TANK:** a vending-machine aquarium for up to six fish you want to keep.
- **DEX:** everything you've caught, from fish to junk, treasure, message bottles and
  ghost files. **SETS** are groups of entries. Finishing one gives you a look you can't
  buy: a fish hat, a can crown, a gold pail, golden and storm coats, and a paper boat.
- **SET:** sound and volume, full-width pond, pond depth, hiding for fullscreen apps,
  the activity panel, and quit.

A gift box drifts in after every hour of play time with a look you don't own yet. The
countdown is in the panel's top strip, and over the cat when you hover it.

## Activity

The activity panel shows today hour by hour, which applications took the time, and how
many clicks and keystrokes went into each. Work stretches are found with changepoint
detection (PELT) rather than a fixed busy/idle threshold, so the boundaries come from
where your own pace actually shifts. Application icons and names come from the
executables themselves. The `sites` button turns window titles on, which lets the list
name pages and documents instead of just the program. See Privacy below.

Pacing reads the same signal. DeskPond keeps a running estimate of your median input
rate, one sample per active minute, and prices the next fish at roughly five minutes of
work at that rate. The estimate uses the P-square algorithm, which tracks a quantile
with five markers instead of the whole history, so it uses a fixed 132 bytes however
long the program runs. Idle minutes are skipped, or stepping away would drag the median
toward zero and make fish nearly free on return. It takes twelve active minutes to warm
up. Until then a fixed range applies.

## Light on resources

A pet that runs all day shouldn't cost anything noticeable. DeskPond ticks its logic at
30 frames a second and draws only about 10 when nothing is moving. All its art is one
512 × 512 texture atlas, the water is drawn into small textures at 8 frames a second,
and its sound is generated at a low sample rate. While a fullscreen app is in front it
drops to 2 frames a second and draws nothing.

On a 2560 × 1440 laptop with integrated graphics it idles at about 0.3% of one CPU core.

## Privacy

Nothing DeskPond records ever leaves your machine. It makes no network requests at
all. Everything below is written to files in your local app data folder, and deleting
them costs you nothing but statistics.

**Input is counted, not read.** DeskPond installs a system-wide input hook to tell how
busy you are. The keyboard hook reads only the message id saying that a key went down.
It never reads which key, so that information is never available to the program. Bait,
pacing and focus streaks all use this count and nothing else. The code is
`Assets/Scripts/DeskPond/GlobalInputCounter.cs`, short enough to read in a minute.

**Activity is logged locally.** To pace fish against your own working rhythm and to show
where your time went, DeskPond records five-minute slices of how much input happened
and which applications were in front. It records process names only and keeps them for
30 days.

**Window titles are off by default.** A title says far more than a process name does:
document names, page titles, who you were talking to. Without them the panel can tell
you that you spent two hours in a browser, but not on which sites. Turn them on with
the `sites` button in the activity panel. The choice is stored per player, so a build
you hand to someone else still starts with it off. Turning it back off stops recording
and deletes the titles already captured, and they expire after 7 days regardless.

**Ghost files are opt-in.** By default the ghost files you fish up have made-up names.
A setting lets them use the names of files in your own Recycle Bin. It reads file names
only, never contents or folders, and only shows them in the catch bubble.

Set `trackActivity` to false to record nothing at all. `activity.json` sits next to
`save.json` and can be deleted at any time; the game does not need it.

## Building

Requires Unity 6000.3.5f1 on Windows.

```
set DP_BUILD_DIR=C:\path\to\output
Unity.exe -batchmode -quit -projectPath . -executeMethod DeskPondBuild.Run
```

Close any running `DeskPond.exe` first. It locks `UnityPlayer.dll` and the build fails.

The art is drawn with [pxl](https://github.com/crsolver/claude-pxl), a small JavaScript DSL, in
`art/*.px.js`. `python art/export.py` renders them and packs everything into
`Assets/Resources/Art/atlas.png`.

### Testing flags

| Flag | What it does |
| --- | --- |
| `-snap=<dir>` | Renders a set of screenshots into `<dir>` using a throwaway save, then quits. |
| `-hour=<h>` | Pins the time of day, e.g. `-hour=22.5`. |
| `-weather=rain\|fog\|snow\|clear` | Pins the weather. |
| `-gift` | Sends a gift box right away. |
| `-giftevery=<s>` | Seconds of play time between gift boxes. |
| `-mail` | The cat writes a letter within a few seconds. |
| `-mailevery=<s>` | Seconds of play time between letters. |
| `-visitor` | Sends a deep-water visitor within a few seconds (needs a deep pond). |
| `-perflog` | Logs ticks and renders per second every 10 seconds. |

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md). Free to read, build, modify and share
for personal projects, study, and any non-profit purpose. Selling it, or shipping it
inside something sold, needs permission.

## Credits

UI font is [Silkscreen](https://fonts.google.com/specimen/Silkscreen) by Jason Kottke,
SIL Open Font License, included in `Assets/Resources/Fonts/OFL.txt`. The font keeps
its own licence and is not covered by the one above.
