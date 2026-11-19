// Everything that can come out of the water, plus the birds. Icons are 16 x 12 (fish) or
// 12 x 12 (junk) before the outline, drawn facing left.
pal({
  k: "#2b2033", w: "#fff6e0", W: "#d8cbb4", h: "#ffffff", e: "#1c1a22",
  o: "#ffb24a", O: "#e0702c", y: "#f6d65a", Y: "#d39a2c", l: "#fff3b0",
  r: "#e2585e", R: "#a63a4c", b: "#6aa6e8", B: "#3d64b0", c: "#a8e4ef", C: "#5aa7c0",
  g: "#7fc77a", G: "#3f8a5a", t: "#5b8c6a", T: "#35594a", p: "#f4a6c4", P: "#c8648e",
  v: "#b49ae6", V: "#7158b8", s: "#9aa3b5", S: "#5d6880", n: "#8a5230", N: "#5e3424",
  m: "#c9d3e0", M: "#8d99ad", u: "#e8e0f0", x: "#3a3440", q: "#f08a3a",
})
ramps("kNn", "kSsm", "kRr", "kOo", "kYyl", "kBbc", "kGg", "kTt", "kPp", "kVv", "kWwh")

// A fish from a few numbers: body length/height, colours, and a pattern painter.
function fish(name, { len = 11, hgt = 6, body, belly, fin, pattern, big = false } = {}) {
  const W2 = len + 6, H2 = hgt + 4
  sprite("fish_" + name, { w: W2, h: H2, outline: "k" },
    ellipse(Math.floor(len / 2) + 1, Math.floor(H2 / 2), Math.floor(len / 2), Math.floor(hgt / 2), body),
    poly([len, Math.floor(H2 / 2), W2, 1, W2, H2 - 1], fin),
    poly([4, Math.floor(H2 / 2) - Math.floor(hgt / 2), 8, 0, 9, Math.floor(H2 / 2) - Math.floor(hgt / 2) + 1], fin),
    clip([0, Math.floor(H2 / 2) + 1, W2, H2], ellipse(Math.floor(len / 2) + 1, Math.floor(H2 / 2), Math.floor(len / 2) - 1, Math.floor(hgt / 2) - 1, belly)),
    ...(pattern ? [pattern] : []),
    px(3, Math.floor(H2 / 2) - 1, "h"), px(3, Math.floor(H2 / 2), "e"),
    big ? px(1, Math.floor(H2 / 2) + 1, "k") : px(0, 0, "."))
}

const stripes = (cols, c) => (cv) => { for (const x of cols) cv.vline(x, 1, cv.h - 2, c); cv.recolor({ [c]: c }) }
const spots = (pts, c) => (cv) => { for (const [x, y] of pts) cv.px(x, y, c) }

// Common
fish("minnow", { len: 8, hgt: 4, body: "s", belly: "m", fin: "S" })
fish("bluegill", { len: 10, hgt: 7, body: "b", belly: "y", fin: "B", pattern: stripes([5, 7], "B") })
fish("perch", { len: 11, hgt: 6, body: "g", belly: "y", fin: "q", pattern: stripes([4, 6, 8], "G") })
fish("carp", { len: 12, hgt: 7, body: "Y", belly: "l", fin: "O" })
// Uncommon
fish("trout", { len: 13, hgt: 6, body: "t", belly: "p", fin: "T", pattern: spots([[5, 3], [8, 4], [10, 3], [7, 2]], "e") })
fish("koi", { len: 12, hgt: 6, body: "w", belly: "w", fin: "o", pattern: spots([[4, 3], [5, 3], [8, 4], [9, 4], [9, 5], [6, 6]], "O") })
fish("puffer", { len: 9, hgt: 8, body: "y", belly: "l", fin: "Y", pattern: spots([[4, 3], [6, 2], [7, 5], [3, 6]], "n") })
fish("catfish", { len: 14, hgt: 6, body: "x", belly: "s", fin: "x", pattern: (c) => { c.line(0, 6, -1, 9, "s"); c.px(1, 7, "s"); c.px(1, 8, "s") } })
// Rare
fish("goldfish", { len: 9, hgt: 6, body: "o", belly: "y", fin: "O" })
fish("eel", { len: 18, hgt: 4, body: "T", belly: "t", fin: "T" })
fish("angel", { len: 9, hgt: 9, body: "c", belly: "u", fin: "C", pattern: stripes([4, 7], "B") })
// Legendary (golden-! only)
fish("golden_koi", { len: 15, hgt: 8, body: "y", belly: "l", fin: "Y", big: true, pattern: spots([[5, 4], [6, 4], [10, 5], [11, 5]], "o") })
fish("moon_carp", { len: 15, hgt: 8, body: "v", belly: "u", fin: "V", big: true, pattern: (c) => { c.circ(9, 5, 2, "u"); c.px(10, 4, "v") } })
fish("coelacanth", { len: 17, hgt: 8, body: "B", belly: "C", fin: "B", big: true, pattern: spots([[5, 4], [8, 6], [11, 4], [13, 6], [9, 3]], "w") })
// Weather fish: only bite in rain / snow.
fish("loach", { len: 15, hgt: 4, body: "n", belly: "l", fin: "N", pattern: spots([[5, 3], [8, 3], [11, 3]], "N") })
fish("icefish", { len: 11, hgt: 5, body: "c", belly: "h", fin: "C", pattern: spots([[5, 3], [8, 4]], "h") })
// Seasonal fish: one per season of the real calendar.
fish("cherry_salmon", { len: 13, hgt: 6, body: "p", belly: "u", fin: "P", pattern: spots([[5, 3], [8, 3], [10, 4]], "R") })
fish("sunfish", { len: 9, hgt: 10, body: "m", belly: "u", fin: "S", pattern: spots([[5, 4], [7, 6]], "s") })
fish("salmon", { len: 14, hgt: 6, body: "r", belly: "m", fin: "R", pattern: spots([[6, 3], [9, 3], [11, 4]], "R") })
fish("cod", { len: 13, hgt: 6, body: "S", belly: "m", fin: "s", pattern: spots([[5, 3], [7, 4], [10, 3], [9, 5]], "x") })

// ---------------------------------------------------------------- junk and treasure
sprite("junk_boot", { w: 12, h: 12, outline: "k" },
  rect(1, 0, 6, 8, "n"), poly([1, 8, 12, 8, 12, 12, 1, 12], "n"), rect(1, 10, 11, 2, "N"),
  hline(2, 1, 4, "N"), hline(2, 4, 4, "N"), px(9, 9, "N"), light([1, 0, 2, 12]), px(4, 6, "c"))
sprite("junk_can", { w: 8, h: 11, outline: "k" },
  rect(0, 1, 8, 10, "r"), hline(0, 0, 8, "m"), hline(0, 10, 8, "M"), rect(1, 4, 6, 3, "w"),
  px(2, 5, "r"), px(5, 5, "r"), shade([6, 1, 2, 10]), px(1, 2, "n"), px(6, 8, "n"), px(2, 9, "N"))
sprite("junk_weed", { w: 10, h: 12, outline: "k" },
  line(3, 11, 2, 0, "G", 2), line(6, 11, 8, 2, "g", 2), line(4, 11, 5, 4, "G"))
sprite("junk_bottle", { w: 8, h: 12, outline: "k" },
  rect(3, 0, 2, 2, "n"), rect(2, 2, 4, 2, "c"), rect(0, 4, 8, 8, "c"), rect(2, 6, 4, 4, "w"),
  hline(2, 7, 3, "W"), hline(2, 8, 4, "W"), light([0, 4, 2, 8]), shade([6, 4, 2, 8]))
sprite("junk_ghost", { w: 11, h: 13, outline: "k" },
  poly([0, 0, 8, 0, 11, 3, 11, 13, 0, 13], "u"), poly([8, 0, 11, 3, 8, 3], "s"),
  hline(2, 3, 4, "s"), hline(2, 5, 6, "s"),
  // a little ghost face peeking off the page
  px(3, 8, "e"), px(7, 8, "e"), hline(4, 10, 3, "P"), px(0, 12, "_"), px(3, 12, "_"), px(7, 12, "_"))
sprite("junk_duck", { w: 12, h: 10, outline: "k" },
  ellipse(6, 7, 5, 3, "y"), circ(3, 3, 3, "y"), poly([0, 3, -2, 4, 0, 5], "o"), px(2, 2, "e"),
  shade([7, 7, 4, 3]))
sprite("junk_floppy", { w: 11, h: 11, outline: "k" },
  rect(0, 0, 11, 11, "x"), rect(2, 0, 6, 4, "m"), rect(6, 1, 1, 2, "x"), rect(2, 6, 7, 5, "w"),
  hline(3, 7, 5, "s"), hline(3, 9, 4, "s"))
sprite("junk_keycap", { w: 10, h: 9, outline: "k" },
  poly([1, 0, 9, 0, 10, 9, 0, 9], "W"), rect(2, 1, 6, 5, "w"), text(3, 1, "Z", "x"), shade([0, 7, 10, 2]))
sprite("junk_sock", { w: 9, h: 12, outline: "k" },
  rect(3, 0, 5, 8, "r"), poly([3, 7, 8, 7, 8, 12, 0, 12, 0, 9], "r"), hline(3, 1, 5, "w"),
  hline(3, 3, 5, "w"), rect(0, 10, 3, 2, "w"))
sprite("junk_pearl", { w: 12, h: 10, outline: "k" },
  ellipse(6, 7, 6, 3, "p"), clip([0, 0, 12, 6], ellipse(6, 5, 6, 5, "P")), circ(6, 6, 2, "h"),
  px(5, 5, "u"))
sprite("junk_chest", { w: 12, h: 10, outline: "k" },
  rect(0, 3, 12, 7, "n"), rect(0, 0, 12, 4, "N"), hline(0, 4, 12, "y"), vline(5, 3, 3, "y"),
  vline(6, 3, 3, "y"), px(5, 6, "Y"), noise(0, 0, 12, 10, "t", 0.12, 3, "nN"))

// ---------------------------------------------------------------- birds
// Seagull merchant: stands on the bow with a satchel of dried fish.
anim("gull", { w: 16, h: 14, outline: "k", fps: 3 },
  ellipse(8, 9, 6, 4, "h"), circ(4, 4, 3, "h"), poly([0, 4, -3, 5, 0, 6], "y"), px(3, 3, "e"),
  poly([9, 7, 16, 8, 13, 11], "s"), rect(6, 13, 1, 1, "o"), rect(10, 13, 1, 1, "o"),
  rect(8, 9, 4, 3, "n"), hline(8, 8, 4, "N"), px(10, 10, "y"),
  frame(), frame(px(3, 3, "h"), hline(3, 3, 2, "e")))
anim("gull_fly", { w: 18, h: 10, outline: "k", fps: 6 },
  ellipse(9, 6, 5, 2, "h"), circ(4, 5, 2, "h"), poly([1, 5, -1, 6, 1, 6], "y"), px(3, 4, "e"),
  frame(poly([7, 5, 4, 0, 11, 5], "s"), poly([11, 5, 16, 1, 14, 6], "s")),
  frame(poly([7, 6, 4, 9, 11, 6], "s"), poly([11, 6, 16, 9, 14, 6], "s")))
// The thief.
anim("crow_fly", { w: 18, h: 10, outline: "k", fps: 6 },
  ellipse(9, 6, 5, 2, "x"), circ(4, 5, 2, "x"), poly([1, 5, -1, 6, 1, 6], "Y"), px(3, 4, "y"),
  frame(poly([7, 5, 4, 0, 11, 5], "e"), poly([11, 5, 16, 1, 14, 6], "e")),
  frame(poly([7, 6, 4, 9, 11, 6], "e"), poly([11, 6, 16, 9, 14, 6], "e")))

// ---------------------------------------------------------------- ui icons (10 x 10)
sprite("icon_dried", { w: 12, h: 7, outline: "k" },
  ellipse(5, 3, 5, 2, "q"), poly([9, 3, 12, 0, 12, 6], "O"), px(2, 2, "e"), hline(3, 4, 5, "O"))
sprite("icon_shop", { w: 10, h: 10, outline: "k" },
  rect(0, 3, 10, 7, "w"), poly([0, 3, 2, 0, 8, 0, 10, 3], "r"), rect(3, 5, 4, 5, "n"))
sprite("icon_wardrobe", { w: 10, h: 10, outline: "k" },
  poly([0, 2, 3, 0, 7, 0, 10, 2, 10, 5, 8, 5, 8, 10, 2, 10, 2, 5, 0, 5], "b"), rect(4, 0, 2, 2, "w"))
sprite("icon_dex", { w: 10, h: 10, outline: "k" },
  rect(0, 0, 10, 10, "g"), rect(1, 1, 8, 8, "w"), hline(2, 3, 6, "s"), hline(2, 5, 6, "s"),
  hline(2, 7, 4, "s"))
sprite("icon_tank", { w: 10, h: 9, outline: "k" },
  rect(0, 0, 10, 9, "c"), hline(0, 0, 10, "h"), rect(0, 7, 10, 2, "W"), poly([3, 4, 6, 2, 6, 6], "o"),
  px(7, 4, "o"))
sprite("icon_cog", { w: 10, h: 10, outline: "k" },
  circ(5, 5, 4, "s"), circ(5, 5, 1, "w"), rect(4, 0, 2, 10, "s"), rect(0, 4, 10, 2, "s"), circ(5, 5, 1, "w"))
sprite("icon_chart", { w: 10, h: 10, outline: "k" },
  rect(0, 0, 10, 10, "w"), rect(1, 6, 2, 3, "b"), rect(4, 3, 2, 6, "b"), rect(7, 5, 2, 4, "b"))
sprite("icon_lock", { w: 8, h: 9, outline: "k" },
  ring(4, 3, 2, "s"), rect(0, 4, 8, 5, "y"), px(3, 6, "x"), px(4, 6, "x"))
