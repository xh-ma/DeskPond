"""Builds every .px.js file and packs the results into one atlas for Unity.

    python art/export.py

Writes Assets/Resources/Art/atlas.png and atlas.txt. One texture keeps the whole game to a
single small upload and lets every sprite batch together, which matters for a pet that sits
in the background all day.

atlas.txt, one asset per line:
    name x y w h            a sprite (x, y from the top-left of the atlas)
    name x y w h d1,d2,...  an animation: frames of w x h laid left to right, durations in ms
"""
import json
import os
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PXL = os.path.join(os.path.expanduser("~"), ".claude", "skills", "pxl-pixel-art", "bin", "pxl.js")
DEST = os.path.join(ROOT, "Assets", "Resources", "Art")
SOURCES = ["cat.px.js", "gear.px.js", "loot.px.js", "tank.px.js"]
ATLAS_W = 512


def build():
    for src in SOURCES:
        subprocess.run(["node", PXL, "build", src, "--no-preview"], cwd=HERE, check=True,
                       stdout=subprocess.DEVNULL)


def collect():
    items = []
    sprites = os.path.join(HERE, "out", "sprites")
    for f in sorted(os.listdir(sprites)):
        if f.endswith(".png"):
            items.append((f[:-4], Image.open(os.path.join(sprites, f)).convert("RGBA"), None))
    anims = os.path.join(HERE, "out", "anims")
    for f in sorted(os.listdir(anims)):
        if not f.endswith(".png"):
            continue
        name = f[:-4]
        meta = json.load(open(os.path.join(anims, name + ".json"), encoding="utf-8"))
        frames = list(meta["frames"].values())
        durations = [fr["duration"] for fr in frames]
        img = Image.open(os.path.join(anims, f)).convert("RGBA")
        fw, fh = frames[0]["frame"]["w"], frames[0]["frame"]["h"]
        items.append((name, img, (fw, fh, durations)))
    return items


def pack(items):
    # Shelf packing, tallest first. 1px gap so point sampling never bleeds a neighbour in.
    order = sorted(items, key=lambda it: (-it[1].height, -it[1].width))
    x = y = shelf = 0
    placed = []
    for name, img, anim in order:
        if x + img.width > ATLAS_W:
            x, y, shelf = 0, y + shelf + 1, 0
        placed.append((name, img, anim, x, y))
        x += img.width + 1
        shelf = max(shelf, img.height)
    height = 1
    while height < y + shelf:
        height *= 2
    atlas = Image.new("RGBA", (ATLAS_W, height), (0, 0, 0, 0))
    lines = []
    for name, img, anim, px, py in placed:
        atlas.paste(img, (px, py))
        if anim:
            fw, fh, durations = anim
            lines.append(f"{name} {px} {py} {fw} {fh} {','.join(str(d) for d in durations)}")
        else:
            lines.append(f"{name} {px} {py} {img.width} {img.height}")
    return atlas, sorted(lines)


def main():
    build()
    atlas, lines = pack(collect())
    os.makedirs(DEST, exist_ok=True)
    atlas.save(os.path.join(DEST, "atlas.png"), optimize=True)
    with open(os.path.join(DEST, "atlas.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"atlas {atlas.width}x{atlas.height}, {len(lines)} assets -> {DEST}")


if __name__ == "__main__":
    sys.exit(main())
