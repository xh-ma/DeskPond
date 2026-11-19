<img src="docs/icon.png" width="72" align="left" alt="DeskPond icon">

# DeskPond

A desktop pet for Windows. A pixel cat sits in a boat on your taskbar and fishes. The
more you type and click, the faster the fish bite.

<br clear="left">

![The cat fishing over a deep pond while a whale swims past](docs/shot_pet.png)

## Features

- Fish, junk and treasure to collect, with dex sets that unlock special looks
- Coats, hats, boats, buckets and ponds to buy from the seagull merchant
- Time of day, weather and seasons, each with their own fish
- Pull the pond deeper to see more of the seabed
- Pet the cat, and it writes you letters in bottles

![Night, dawn, fog and rain](docs/scenes.png)

## Privacy

Everything stays on your machine. DeskPond makes no network requests. It counts key
presses and clicks but never reads which key was pressed. Window titles are off by
default.

## Building

Requires Unity 6000.3.5f1 on Windows. Close any running `DeskPond.exe` first.

```
set DP_BUILD_DIR=C:\path\to\output
Unity.exe -batchmode -quit -projectPath . -executeMethod DeskPondBuild.Run
```

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md). Free for personal, study and non-profit
use.

## Credits

UI font is [Silkscreen](https://fonts.google.com/specimen/Silkscreen) by Jason Kottke,
SIL Open Font License.
