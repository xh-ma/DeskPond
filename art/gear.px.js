// Boats, fish buckets, hats and small fishing props.
pal({
  k: "#2b2033",
  // wood
  a: "#e0a66a", A: "#b9783f", z: "#8a5230", Z: "#5e3424",
  // metal
  m: "#c9d3e0", M: "#8d99ad", n: "#5d6880",
  // cloth / paint
  r: "#e2585e", R: "#a63a4c", y: "#f6d65a", Y: "#d39a2c", w: "#fff6e0", W: "#d8cbb4",
  g: "#7fc77a", G: "#3f8a5a", b: "#6aa6e8", B: "#3d64b0", p: "#f4a6c4", P: "#c8648e",
  s: "#3a3440", S: "#1c1a22", o: "#ffb24a", l: "#fff3b0",
})
ramps("kZzAa", "knMm", "kRr", "kYy", "kGg", "kBb", "kPp", "kWw")

// ---------------------------------------------------------------- boats
// Every boat is two layers: _back draws behind the cat, _front in front of it. The gunwale (top
// edge of the front layer) is row 0 of the front sprite; the cat's rim line sits on it.

// Wash tub: the starter boat. 26 wide.
sprite("boat_tub_back", { w: 26, h: 3, outline: "k" }, rect(1, 1, 24, 2, "Z"))
sprite("boat_tub_front", { w: 26, h: 9, outline: "k" },
  poly([0, 0, 26, 0, 24, 9, 2, 9], "A"),
  (c) => { for (let x = 3; x < 24; x += 4) c.vline(x, 1, 8, "z") },
  hline(0, 0, 26, "a"), hline(1, 2, 24, "M"), hline(1, 3, 24, "n"), hline(2, 6, 22, "M"),
  light([0, 0, 8, 9]))

// Log raft: wider, flat, room for the bucket on deck.
sprite("boat_raft_back", { w: 2, h: 1 }, px(0, 0, "_"))
sprite("boat_raft_front", { w: 44, h: 6, outline: "k" },
  (c) => {
    for (let i = 0; i < 3; i++) {
      c.rect(0, i * 2, 44, 2, i % 2 ? "A" : "a")
      c.hline(0, i * 2 + 1, 44, "z")
    }
    for (const x of [6, 21, 37]) { c.vline(x, 0, 6, "W"); c.vline(x + 1, 0, 6, "Y") }
  })

// Sailboat: a long hull with three separate places, left to right: a bow pole the lantern
// hangs from, the cat's seat, the bucket, then the mast and sail at the stern. Nothing overlaps
// the cat. 76 wide; the boat's centre is x = 38.
sprite("boat_sail_back", { w: 76, h: 38, outline: "k" },
  // mast and boom
  rect(53, 0, 2, 38, "Z"), rect(54, 0, 1, 38, "z"), hline(55, 30, 18, "Z"),
  // sail
  poly([56, 3, 56, 30, 74, 30], "w"), poly([56, 18, 56, 30, 65, 30], "W"),
  hline(57, 12, 3, "r"), hline(57, 22, 9, "r"), hline(57, 23, 9, "R"),
  // pennant
  poly([55, 0, 62, 2, 55, 4], "r"),
  // bow pole with a hook for the lantern
  rect(5, 14, 2, 24, "Z"), hline(5, 14, 6, "Z"), vline(10, 14, 3, "n"))
sprite("boat_sail_front", { w: 76, h: 11, outline: "k" },
  poly([0, 0, 76, 0, 69, 11, 6, 11], "b"), hline(0, 0, 76, "w"), hline(1, 1, 75, "w"),
  hline(4, 7, 66, "B"), hline(5, 8, 64, "B"), hline(6, 9, 62, "B"), hline(6, 10, 61, "B"),
  circ(12, 4, 1, "y"), circ(64, 4, 1, "y"), hline(28, 4, 20, "B"), light([0, 0, 10, 11]))

// Paper boat: the reward for finding every bottle note. A folded hull and a paper peak behind
// the cat. 40 wide.
sprite("boat_paper_back", { w: 22, h: 15, outline: "k" },
  poly([0, 15, 11, 0, 22, 15], "w"), poly([11, 0, 22, 15, 11, 15], "W"), hline(4, 9, 5, "W"), hline(5, 12, 4, "W"))
sprite("boat_paper_front", { w: 44, h: 9, outline: "k" },
  poly([0, 0, 44, 0, 35, 9, 9, 9], "w"), poly([22, 0, 44, 0, 35, 9, 22, 9], "W"),
  hline(0, 0, 44, "w"), line(22, 0, 22, 8, "M"), hline(6, 4, 8, "W"), hline(8, 6, 6, "W"))

// ---------------------------------------------------------------- fish buckets
// 12 x 11 pails. _fill overlays (tails poking out) are shared by all skins.
for (const [name, body, dark, band] of [
  ["wood", "A", "z", "M"], ["tin", "m", "M", "n"], ["red", "r", "R", "w"], ["mint", "g", "G", "w"],
  ["gold", "y", "Y", "w"],
]) {
  sprite("bucket_" + name, { w: 12, h: 11, outline: "k" },
    poly([0, 1, 12, 1, 11, 11, 1, 11], body),
    hline(0, 1, 12, "k"), hline(1, 2, 10, dark), hline(1, 4, 10, band), hline(1, 8, 10, band),
    light([1, 2, 3, 9]), shade([9, 2, 3, 9]),
    // handle
    px(0, 0, "n"), px(11, 0, "n"))
}
sprite("bucket_fill_half", { w: 12, h: 4 },
  poly([3, 3, 5, 0, 6, 3], "o"), px(5, 0, "Y"), poly([7, 3, 9, 1, 9, 3], "b"))
sprite("bucket_fill_full", { w: 14, h: 6, outline: "k" },
  poly([2, 6, 4, 0, 5, 6], "o"), px(4, 0, "Y"), poly([6, 6, 8, 1, 9, 6], "b"), px(8, 1, "B"),
  poly([9, 6, 12, 2, 12, 6], "g"), poly([5, 6, 6, 3, 7, 6], "p"))

// ---------------------------------------------------------------- hats
// Hats are anchored bottom-centre on the top of the cat's head; glasses on the eye line.
sprite("hat_straw", { w: 22, h: 7, outline: "k" },
  ellipse(10, 5, 10, 1, "y"), rect(5, 1, 11, 4, "y"), rect(6, 0, 9, 1, "y"),
  hline(5, 3, 11, "r"), hline(5, 4, 11, "R"), shade([14, 0, 2, 5]),
  (c) => { for (let x = 2; x < 20; x += 3) c.px(x, 5, "Y") })
sprite("hat_rain", { w: 20, h: 7, outline: "k" },
  rect(4, 0, 12, 4, "y"), poly([0, 4, 20, 4, 18, 7, 2, 7], "y"), hline(0, 4, 20, "Y"),
  light([4, 0, 4, 4]), shade([14, 0, 2, 4]), px(9, 1, "Y"), px(10, 1, "Y"))
sprite("hat_beanie", { w: 16, h: 8, outline: "k" },
  ellipse(7, 6, 7, 6, "r"), clip([0, 6, 16, 3], rect(0, 0, 16, 9, "_")),
  rect(0, 5, 15, 2, "R"), (c) => { for (let x = 1; x < 15; x += 2) c.px(x, 5, "r") },
  circ(7, 1, 1, "w"))
sprite("hat_captain", { w: 18, h: 8, outline: "k" },
  rect(1, 1, 16, 4, "w"), rect(0, 0, 18, 1, "w"), rect(1, 4, 16, 2, "s"),
  poly([2, 6, 16, 6, 14, 8, 4, 8], "S"), circ(8, 2, 1, "y"), px(8, 2, "Y"), shade([14, 0, 3, 4]))
sprite("hat_flower", { w: 18, h: 5, outline: "k" },
  hline(0, 3, 18, "G"), hline(1, 4, 16, "G"),
  circ(2, 2, 1, "p"), circ(7, 1, 1, "y"), circ(12, 1, 1, "w"), circ(16, 2, 1, "p"),
  px(2, 2, "y"), px(7, 1, "Y"), px(12, 1, "y"), px(16, 2, "y"))
// Ribbon bow tucked at the base of the cat's right ear (drawn with its own outline).
sprite("hat_bow", { w: 9, h: 5 }, `
  .kk...kk.
  kpwkkkpwk
  kppkpkppk
  kPPkkkPPk
  .kk...kk.`)
// Dex reward hats (drawn with their own outline).
sprite("hat_fish", { w: 16, h: 6 }, `
  ....kkkkkk....kk
  ..kkbbbbbbkk.kbk
  .kbwbbbbbbbbkbbk
  .kbSbbBBbbbbkBbk
  ..kkyyyyyykk.kbk
  ....kkkkkk....kk`)
sprite("hat_can", { w: 10, h: 7 }, `
  .kkkkkkkk.
  kmmmmmmmMk
  krrrrrrRRk
  krwwwwrRRk
  krrrrrrRRk
  kMMMMMMMnk
  .kkkkkkkk.`)
// The cat's letter, corked up and set afloat.
sprite("mail_bottle", { w: 8, h: 13, outline: "k" },
  rect(3, 0, 2, 2, "A"), rect(2, 2, 4, 2, "#a8e4ef"), rect(0, 4, 8, 9, "#a8e4ef"),
  rect(2, 6, 4, 6, "w"), hline(2, 8, 4, "r"), hline(3, 10, 2, "W"),
  light([0, 4, 2, 9]), shade([6, 4, 2, 9]))
sprite("glasses_sun", { w: 16, h: 3 },
  rect(0, 0, 6, 3, "S"), rect(10, 0, 6, 3, "S"), hline(6, 0, 4, "S"),
  px(1, 0, "n"), px(11, 0, "n"))

// ---------------------------------------------------------------- props
sprite("bobber", { w: 5, h: 6, outline: "k" },
  rect(1, 0, 3, 3, "r"), rect(1, 3, 3, 2, "w"), px(0, 2, "r"), px(4, 2, "R"), px(2, 0, "w"),
  vline(2, 5, 1, "n"))
sprite("bobber_gold", { base: "bobber", swap: { r: "y", R: "Y" } })

sprite("mark_gold", { w: 5, h: 10, outline: "k" },
  rect(1, 0, 3, 6, "y"), rect(1, 7, 3, 2, "y"), vline(3, 0, 6, "Y"), px(3, 8, "Y"), px(1, 0, "l"))
sprite("mark_bite", { base: "mark_gold", swap: { y: "w", Y: "W", l: "w" } })
sprite("zz", { w: 4, h: 4 }, `
  wwww
  ..w.
  .w..
  wwww`)

anim("lantern", { w: 7, h: 10, outline: "k", fps: 3 },
  rect(2, 0, 3, 1, "n"), rect(1, 1, 5, 1, "M"), rect(1, 8, 5, 1, "M"), vline(1, 2, 6, "n"),
  vline(5, 2, 6, "n"),
  frame(rect(2, 2, 3, 6, "o"), rect(3, 4, 1, 3, "l")),
  frame(rect(2, 2, 3, 6, "o"), rect(3, 3, 1, 4, "l"), px(2, 7, "y")),
)
sprite("lantern_off", { w: 7, h: 10, outline: "k" },
  rect(2, 0, 3, 1, "n"), rect(1, 1, 5, 1, "M"), rect(1, 8, 5, 1, "M"), vline(1, 2, 6, "n"),
  vline(5, 2, 6, "n"), rect(2, 2, 3, 6, "W"), px(3, 5, "z"))

// A fish's shadow under the surface, gathering where the bait lands.
sprite("fish_shadow", { w: 7, h: 3 }, `
  .sss..s
  ssssss.
  .sss..s`)

// The cat's catch ledger, held up while it sulks through a dry spell.
sprite("notebook", { w: 12, h: 8, outline: "k" },
  rect(0, 0, 12, 8, "w"), rect(0, 0, 6, 8, "W"), vline(6, 0, 8, "R"), rect(0, 0, 12, 1, "r"),
  hline(1, 2, 4, "M"), hline(1, 4, 3, "M"), hline(7, 2, 4, "M"), hline(7, 4, 4, "M"), hline(7, 6, 2, "M"))
sprite("heart", { mx: "c", outline: "k" }, `
  rr.
  rrr
  .rr
  ..r`)

// Grab tab on the pond's edge: drag it up to make the water deeper and see more of the seabed.
sprite("grip", { w: 7, h: 11, outline: "k" },
  rect(0, 0, 7, 11, "w"), rect(0, 0, 7, 1, "W"), rect(6, 0, 1, 11, "W"), rect(0, 10, 7, 1, "W"),
  px(3, 1, "B"), hline(2, 2, 3, "B"), hline(1, 3, 5, "B"),
  hline(1, 7, 5, "B"), hline(2, 8, 3, "B"), px(3, 9, "B"), hline(2, 5, 3, "M"))

// App icon for the panel header: the cat's face, so it no longer matches the dried-fish
// currency icon next to the coin count.
sprite("icon_app", { mx: true, outline: "k" }, `
  .a....
  .aa...
  .aWa..
  .aaaaa
  aaaaaa
  aakaaa
  aaaaaw
  .awwww`)

// The idle gift box: a lavender box, pink ribbon and bow, sparkles that twinkle. Frames: the
// bow wiggles and a sparkle travels round the box.
anim("gift", { w: 18, h: 18, outline: "k", fps: 5 },
  // box body and lid
  rect(2, 8, 14, 9, "#c4cbe8"), rect(1, 6, 16, 3, "#d9def2"), hline(1, 6, 16, "w"),
  shade([12, 8, 4, 9]), rect(2, 9, 14, 1, "#9aa3cc"),
  // polka dots
  px(4, 11, "w"), px(6, 14, "w"), px(12, 12, "w"), px(14, 15, "w"), px(4, 15, "w"),
  // ribbon
  rect(8, 6, 2, 11, "p"), rect(8, 6, 2, 1, "P"), vline(9, 7, 10, "P"),
  frame(poly([8, 6, 3, 1, 4, 6], "p"), poly([10, 6, 15, 1, 14, 6], "p"), rect(8, 4, 2, 2, "P"),
    px(4, 2, "w"), px(3, 7, "w"), px(4, 7, "y")),
  frame(poly([8, 6, 3, 2, 4, 6], "p"), poly([10, 6, 15, 0, 14, 6], "p"), rect(8, 4, 2, 2, "P"),
    px(14, 1, "w"), px(13, 12, "y")),
  frame(poly([8, 6, 3, 1, 4, 6], "p"), poly([10, 6, 15, 1, 14, 6], "p"), rect(8, 4, 2, 2, "P"),
    px(4, 2, "w"), px(14, 7, "w"), px(13, 7, "y")),
  frame(poly([8, 6, 3, 0, 4, 6], "p"), poly([10, 6, 15, 2, 14, 6], "p"), rect(8, 4, 2, 2, "P"),
    px(14, 1, "w"), px(4, 13, "y")))
sprite("icon_gift", { w: 10, h: 10, outline: "k" },
  rect(1, 4, 8, 6, "#c4cbe8"), rect(0, 3, 10, 2, "#d9def2"), rect(4, 3, 2, 7, "p"),
  poly([4, 3, 1, 0, 2, 3], "p"), poly([6, 3, 9, 0, 8, 3], "p"))
