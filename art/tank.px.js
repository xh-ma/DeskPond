// The aquarium cabinet shown in the panel's TANK tab: a little vending-machine fish tank with a
// neon sign, after the user's reference. Live fish are drawn by the game on top of the tank
// window (inner rect x 9..66, y 28..81, top-left origin).
pal({
  k: "#0c1618", K: "#16262a",
  c: "#3fd8f0", C: "#1a8ab0", m: "#3ff0b0", M: "#1aa880", l: "#c8fff0",
  b: "#1d3a3a", B: "#2a5050", d: "#123030",
  f: "#8fb3c0", F: "#5a8090",
  w: "#0e3a44", W: "#14505a", v: "#1a6a6a", s: "#2a8a78", S: "#5ab890",
  g: "#a8c040", G: "#6a8a2a", o: "#e0702c",
  r: "#c03a3a", R: "#7a1c24", y: "#e8c030", Y: "#a08020", p: "#d8dcc8", q: "#8a8e78",
})

sprite("tank_cabinet", { w: 96, h: 118 },
  // ---- neon sign
  rect(10, 0, 76, 17, "K"), box(10, 0, 76, 17, "C"), box(11, 1, 74, 15, "c"),
  // glyph blocks either side of the fish
  rect(14, 4, 6, 8, "c"), rect(16, 6, 2, 4, "K"), rect(76, 4, 6, 8, "c"), rect(78, 7, 2, 2, "K"),
  // the fish
  ellipse(46, 8, 16, 5, "m"), poly([60, 8, 70, 2, 70, 14], "m"), poly([40, 3, 46, 0, 50, 3], "M"),
  ellipse(46, 9, 13, 2, "M"), px(34, 7, "k"), line(38, 5, 38, 11, "M"), line(52, 6, 56, 10, "M"),
  hline(32, 8, 2, "l"),
  // posts
  vline(30, 17, 2, "k"), vline(66, 17, 2, "k"),
  glow("cm", { r: 4, strength: 0.6, bands: 3 }),

  // ---- roof bar
  rect(0, 19, 96, 5, "k"), hline(0, 20, 96, "K"),
  (c) => { for (let x = 5; x < 92; x += 10) c.px(x, 21, "B") },

  // ---- cabinet body
  rect(3, 24, 90, 90, "b"), vline(3, 24, 90, "B"), vline(92, 24, 90, "d"),

  // ---- tank window: frame, water, weeds, sand
  rect(6, 25, 64, 60, "F"), rect(7, 26, 62, 58, "f"), rect(9, 28, 58, 54, "w"),
  grad(9, 28, 58, 54, "wWv", "v", 0.5),
  (c) => { for (let y = 32; y < 80; y += 4) for (let x = 12; x < 66; x += 4) if ((x + y) % 8 === 0) c.px(x, y, "W") },
  rect(9, 76, 58, 6, "s"), noise(9, 76, 58, 6, "S", 0.25, 4, "s"),
  // weeds
  line(14, 80, 13, 64, "G"), line(16, 80, 18, 68, "g"), line(15, 72, 12, 66, "g"),
  line(58, 80, 60, 62, "G"), line(61, 80, 63, 70, "g"), line(57, 70, 55, 64, "g"),
  line(36, 80, 35, 72, "S"), line(40, 80, 42, 73, "S"),
  px(30, 79, "o"), px(31, 78, "o"), px(48, 79, "y"),
  // glass glint
  vline(11, 32, 14, "l"), vline(12, 34, 6, "f"),

  // ---- neon side menu
  rect(72, 26, 18, 62, "M"), box(72, 26, 18, 62, "k"), rect(73, 27, 16, 60, "m"),
  circ(80, 36, 6, "M"), line(76, 36, 85, 36, "l"), (c) => { for (let x = 77; x < 85; x += 2) { c.px(x, 34, "l"); c.px(x, 38, "l") } },
  poly([86, 36, 89, 33, 89, 39], "l"),
  (c) => {
    for (let i = 0; i < 3; i++) {
      const y = 46 + i * 13
      c.hline(73, y - 2, 16, "M")
      c.rect(75, y, 6, 3, "k"); c.rect(76, y + 4, 8, 1, "M")
      c.circ(86, y + 2, 2, "y")
    }
  },
  glow("m", { r: 2, strength: 0.4, bands: 2, where: [72, 26, 18, 62] }),

  // ---- control panel
  rect(4, 87, 88, 2, "k"), rect(28, 87, 14, 2, "m"),
  rect(5, 90, 66, 22, "q"), rect(6, 91, 64, 20, "p"),
  rect(8, 93, 9, 7, "r"), box(8, 93, 9, 7, "R"), rect(11, 95, 3, 3, "p"), rect(19, 93, 18, 2, "r"), rect(19, 97, 14, 2, "r"),
  circ(44, 94, 1, "r"), circ(48, 98, 4, "k"), line(46, 100, 50, 96, "q"),
  rect(55, 93, 13, 5, "k"), rect(56, 94, 11, 3, "W"),
  rect(8, 102, 30, 8, "k"), rect(9, 103, 28, 6, "K"),
  rect(42, 103, 5, 5, "C"), px(43, 104, "l"), px(45, 104, "l"),
  box(52, 101, 16, 10, "y"), hline(54, 104, 4, "y"), hline(54, 107, 10, "Y"),
  // vent on the side unit
  rect(74, 92, 16, 20, "k"), (c) => { for (let y = 94; y < 111; y += 3) c.hline(76, y, 12, "B") },

  // ---- feet
  rect(6, 114, 6, 4, "k"), rect(84, 114, 6, 4, "k"))
