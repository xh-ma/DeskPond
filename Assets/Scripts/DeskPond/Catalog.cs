using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Legendary
    }

    public enum CatchKind
    {
        Fish,
        Junk,
        Treasure
    }

    public class CatchDef
    {
        public string Id;
        public string Name;
        public string Icon;
        public CatchKind Kind;
        public Rarity Rarity;
        public int Price;
        public int Weight;       // relative odds within its rarity
        public float MinCm, MaxCm;
        public string Blurb;
        public bool NightOnly;
        public string Pond;      // null = any pond
        public WeatherKind? Weather; // only bites in this weather
        public int Season = -1;  // 0 spring, 1 summer, 2 autumn, 3 winter; -1 = all year

        public bool IsFish => Kind == CatchKind.Fish;
    }

    /// <summary>
    /// The "anything can be caught" table: fish, junk, cyber treasure. Odds are rolled as a
    /// rarity first and then an item within it, so adding an item never shifts other tiers.
    /// </summary>
    public static class Catalog
    {
        public static readonly CatchDef[] All =
        {
            Fish("minnow", "Minnow", Rarity.Common, 3, 4, 9, "Small, silver, everywhere. Mostly courage."),
            Fish("bluegill", "Bluegill", Rarity.Common, 5, 10, 22, "Bites anything. Including the bobber."),
            Fish("perch", "Perch", Rarity.Common, 6, 12, 30, "Stripy and slightly smug about it."),
            Fish("carp", "Carp", Rarity.Common, 7, 25, 60, "Has been in this pond longer than you."),
            Fish("trout", "Trout", Rarity.Uncommon, 14, 20, 55, "Likes cold water and clean code."),
            Fish("koi", "Koi", Rarity.Uncommon, 18, 25, 70, "Someone's pet. Probably fine."),
            Fish("puffer", "Pufferfish", Rarity.Uncommon, 20, 8, 25, "Do not startle. Too late."),
            Fish("catfish", "Catfish", Rarity.Uncommon, 16, 30, 90, "The cat considers this a cousin."),
            Fish("goldfish", "Goldfish", Rarity.Rare, 40, 6, 15, "Forgot it was caught. Happy anyway."),
            Fish("eel", "Eel", Rarity.Rare, 45, 40, 120, "Slippery. Like a deadline."),
            Fish("angel", "Angelfish", Rarity.Rare, 50, 10, 20, "Glows a little at night.", night: true),
            Fish("golden_koi", "Golden Koi", Rarity.Legendary, 300, 60, 110, "Brings luck. And a sore arm."),
            Fish("moon_carp", "Moon Carp", Rarity.Legendary, 360, 70, 120, "Only rises when the moon does.", night: true),
            Fish("coelacanth", "Coelacanth", Rarity.Legendary, 420, 120, 190, "Thought extinct. Isn't.", pond: "sea"),

            // Weather and season fish. Uncommon/rare tiers, so they turn up within a session
            // when the conditions are right.
            Fish("loach", "Weather loach", Rarity.Uncommon, 24, 10, 28, "Only comes up when it rains. Knew before you did.",
                weather: WeatherKind.Rain),
            Fish("icefish", "Icefish", Rarity.Rare, 60, 12, 30, "Clear as glass. Only bites in the snow.",
                weather: WeatherKind.Snow),
            Fish("cherry_salmon", "Cherry salmon", Rarity.Rare, 55, 30, 60, "Spring only. Arrives with the blossoms.", season: 0),
            Fish("sunfish", "Sunfish", Rarity.Rare, 65, 60, 180, "Summer only. Floats about doing nothing. A role model.", season: 1),
            Fish("salmon", "Salmon", Rarity.Rare, 55, 50, 100, "Autumn only. On its way home.", season: 2),
            Fish("cod", "Winter cod", Rarity.Rare, 55, 40, 110, "Winter only. Does not mind the cold.", season: 3),

            Junk("boot", "Old boot", Rarity.Common, 1, "One boot. The other is still down there."),
            Junk("can", "Rusty can", Rarity.Common, 1, "Fizzy drink, vintage."),
            Junk("weed", "Pondweed", Rarity.Common, 1, "Green, wet, unwanted."),
            Junk("sock", "Lost sock", Rarity.Uncommon, 2, "So that's where they go."),
            Junk("duck", "Rubber duck", Rarity.Uncommon, 8, "Debugged a lot of code in its time."),
            Junk("bottle", "Message bottle", Rarity.Uncommon, 6, "There's a note inside."),
            Junk("ghost", "Ghost file", Rarity.Rare, 12, "Deleted, but not gone. Spooky."),
            Treasure("keycap", "Lost keycap", Rarity.Rare, 30, "The Z key. Explains a lot."),
            Treasure("floppy", "Floppy disk", Rarity.Rare, 35, "1.44 MB of someone's homework."),
            Treasure("pearl", "Pearl", Rarity.Rare, 80, "Pretty. Very pretty."),
            Treasure("chest", "Sunken chest", Rarity.Legendary, 250, "Full of dried fish. Obviously."),
        };

        static readonly int[] RarityOdds = { 70, 22, 7, 1 };

        public static readonly string[] BottleNotes =
        {
            "You are doing better than you think.",
            "Drink some water. The cat insists.",
            "Why do fish live in salt water? Pepper makes them sneeze.",
            "Whoever finds this: hi. Have a nice day.",
            "Tiny progress is still progress.",
            "What do you call a fish with no eyes? A fsh.",
            "Remember to stand up and stretch.",
            "The bug is on the line you're sure is fine.",
            "It's okay to take a break.",
            "I told a joke to a fish. It went right over its head.",
            "Somebody is proud of you.",
            "Save your work. Right now. Go.",
        };

        public static readonly string[] GhostNames =
        {
            "final_FINAL_v3.docx", "untitled(7).png", "todo_old.txt", "New Folder (2)",
            "budget_real.xlsx", "essay_draft_dont_open.doc", "Screenshot 0412.png", "backup.bak",
            "notes_2019.txt", "setup(1).exe", "resume_v12.pdf", "temp_temp.tmp",
        };

        static readonly Dictionary<string, CatchDef> ById = new Dictionary<string, CatchDef>();

        public static CatchDef Get(string id)
        {
            if (ById.Count == 0)
                foreach (var d in All)
                    ById[d.Id] = d;
            return id != null && ById.TryGetValue(id, out var def) ? def : null;
        }

        public static int Index(string id)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Id == id)
                    return i;
            return -1;
        }

        /// <summary>
        /// Rolls an ordinary bite. Legendary fish only come from golden bites, so they are
        /// skipped here; a lucky roll at that tier gives the chest instead.
        /// </summary>
        public static CatchDef RollNormal(System.Random rng, string pond, bool night, int lureLevel,
            WeatherKind weather = WeatherKind.Clear, int season = -1)
        {
            // The lure tilts the odds a little toward the rarer tiers.
            var odds = (int[])RarityOdds.Clone();
            odds[0] = Mathf.Max(30, odds[0] - lureLevel * 6);
            odds[1] += lureLevel * 4;
            odds[2] += lureLevel * 2;

            var total = 0;
            foreach (var o in odds)
                total += o;
            var roll = rng.Next(total);
            var tier = Rarity.Common;
            for (var i = 0; i < odds.Length; i++)
            {
                if (roll < odds[i])
                {
                    tier = (Rarity)i;
                    break;
                }
                roll -= odds[i];
            }

            return Pick(rng, tier, pond, night, legendaryFish: false, weather, season) ?? Get("boot");
        }

        /// <summary>The prize for landing a golden bite: a legendary fish that suits the moment.</summary>
        public static CatchDef RollLegendary(System.Random rng, string pond, bool night) =>
            Pick(rng, Rarity.Legendary, pond, night, legendaryFish: true) ?? Get("golden_koi");

        static CatchDef Pick(System.Random rng, Rarity tier, string pond, bool night, bool legendaryFish,
            WeatherKind weather = WeatherKind.Clear, int season = -1)
        {
            var pool = new List<CatchDef>();
            foreach (var d in All)
            {
                if (d.Rarity != tier)
                    continue;
                if (tier == Rarity.Legendary && d.IsFish != legendaryFish)
                    continue;
                if (d.NightOnly && !night)
                    continue;
                if (d.Pond != null && d.Pond != pond)
                    continue;
                if (d.Weather.HasValue && d.Weather.Value != weather)
                    continue;
                if (d.Season >= 0 && d.Season != season)
                    continue;
                // The fish of the moment are the point of the weather, so they crowd the pool;
                // rain also stirs up catfish, and fog brings deleted files drifting closer.
                var weight = Mathf.Max(1, d.Weight);
                if (d.Weather.HasValue || d.Season >= 0)
                    weight *= 3;
                else if (weather == WeatherKind.Rain && d.Id == "catfish")
                    weight *= 3;
                else if (weather == WeatherKind.Fog && d.Id == "ghost")
                    weight *= 3;
                for (var w = 0; w < weight; w++)
                    pool.Add(d);
            }

            return pool.Count > 0 ? pool[rng.Next(pool.Count)] : null;
        }

        static CatchDef Fish(string id, string name, Rarity r, int price, float min, float max, string blurb,
            bool night = false, string pond = null, WeatherKind? weather = null, int season = -1) => new CatchDef
        {
            Id = id, Name = name, Icon = "fish_" + id, Kind = CatchKind.Fish, Rarity = r, Price = price,
            Weight = 1, MinCm = min, MaxCm = max, Blurb = blurb, NightOnly = night, Pond = pond,
            Weather = weather, Season = season
        };

        static CatchDef Junk(string id, string name, Rarity r, int price, string blurb) => new CatchDef
        {
            Id = id, Name = name, Icon = "junk_" + id, Kind = CatchKind.Junk, Rarity = r, Price = price,
            Weight = 1, Blurb = blurb
        };

        static CatchDef Treasure(string id, string name, Rarity r, int price, string blurb) => new CatchDef
        {
            Id = id, Name = name, Icon = "junk_" + id, Kind = CatchKind.Treasure, Rarity = r, Price = price,
            Weight = 1, Blurb = blurb
        };

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Uncommon: return new Color32(0x4a, 0x9a, 0x5a, 255);
                case Rarity.Rare: return new Color32(0x3d, 0x64, 0xb0, 255);
                case Rarity.Legendary: return new Color32(0xc0, 0x80, 0x10, 255);
                default: return new Color32(0x55, 0x5a, 0x70, 255);
            }
        }
    }
}
