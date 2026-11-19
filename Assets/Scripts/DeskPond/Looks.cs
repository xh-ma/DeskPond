using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    /// <summary>The wardrobe categories, in the order the tabs show them.</summary>
    public enum LookSlot
    {
        Coat,
        Hat,
        Boat,
        Bucket,
        Pond
    }

    public class LookDef
    {
        public string Id;
        public string Name;
        /// <summary>Price in dried fish. Negative means it cannot be bought: it is a dex reward.</summary>
        public int Cost;
        public bool RewardOnly => Cost < 0;
        /// <summary>Atlas name for the wardrobe tile; may be null (the "none" hat).</summary>
        public string Icon;
        public LookDef(string id, string name, int cost, string icon)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Icon = icon;
        }
    }

    /// <summary>Per-boat layout: where the cat sits, where the bucket goes, how deep it floats.</summary>
    public class BoatDef : LookDef
    {
        public int CatX;      // cat centre, art px from the boat centre
        public int BucketX;   // bucket centre
        public int BucketY;   // bucket bottom, art px above the waterline
        public int Sink;      // rows of the hull below the waterline
        public int LanternX;
        public int LanternY;  // lantern bottom, art px above the gunwale
        public int Tier;      // deeper boats unlock rarer water
        public BoatDef(string id, string name, int cost, int catX, int bucketX, int bucketY, int sink,
            int lanternX, int lanternY, int tier) : base(id, name, cost, "boat_" + id + "_front")
        {
            CatX = catX;
            BucketX = bucketX;
            BucketY = bucketY;
            Sink = sink;
            LanternX = lanternX;
            LanternY = lanternY;
            Tier = tier;
        }
    }

    /// <summary>
    /// Water colours for one pond theme at the four times of day. Each palette is
    /// highlight, surface, mid, deep, sparkle.
    /// </summary>
    public class PondDef : LookDef
    {
        public Color32[] Dawn, Day, Dusk, Night;

        /// <summary>Seabed colours: rock dark, rock, rock light, sand, sand light, plant,
        /// plant dark, coral A, coral B.</summary>
        public Color32[] Bed;
        public bool Reeds;
        public bool Lilies;
        public int MinBoatTier;
        public PondDef(string id, string name, int cost, int minTier, bool reeds, bool lilies,
            string dawn, string day, string dusk, string night, string bed) : base(id, name, cost, "icon_tank")
        {
            Bed = Hexes(bed);
            MinBoatTier = minTier;
            Reeds = reeds;
            Lilies = lilies;
            Dawn = Hexes(dawn);
            Day = Hexes(day);
            Dusk = Hexes(dusk);
            Night = Hexes(night);
        }

        static Color32[] Hexes(string s)
        {
            var parts = s.Split(' ');
            var c = new Color32[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                ColorUtility.TryParseHtmlString("#" + parts[i], out var col);
                c[i] = col;
            }
            return c;
        }
    }

    /// <summary>
    /// Everything the cat can wear or ride, bought once with dried fish and swapped freely after,
    /// like a pet wardrobe. Ownership is a list of "slot:id" strings in the save so new items can
    /// be added without migrating bitmasks.
    /// </summary>
    public static class Looks
    {
        public static readonly LookDef[] Coats =
        {
            new LookDef("ginger", "Ginger", 0, "cat_ginger"),
            new LookDef("orange", "Marmalade", 120, "cat_orange"),
            new LookDef("gray", "Smoke", 120, "cat_gray"),
            new LookDef("snow", "Snow", 200, "cat_snow"),
            new LookDef("tuxedo", "Tuxedo", 260, "cat_tuxedo"),
            new LookDef("calico", "Calico", 320, "cat_calico"),
            new LookDef("brown", "Tabby", 320, "cat_brown"),
            new LookDef("silver", "Silver", 380, "cat_silver"),
            new LookDef("siamese", "Siamese", 480, "cat_siamese"),
            new LookDef("pink", "Sakura", 600, "cat_pink"),
            new LookDef("black", "Midnight", 700, "cat_black"),
            new LookDef("gold", "Golden", -1, "cat_gold"),
            new LookDef("storm", "Storm", -1, "cat_storm"),
        };

        public static readonly LookDef[] Hats =
        {
            new LookDef("none", "None", 0, null),
            new LookDef("straw", "Straw hat", 90, "hat_straw"),
            new LookDef("rain", "Rain hat", 150, "hat_rain"),
            new LookDef("sun", "Sunglasses", 180, "glasses_sun"),
            new LookDef("beanie", "Beanie", 220, "hat_beanie"),
            new LookDef("bow", "Ear bow", 260, "hat_bow"),
            new LookDef("flower", "Flower crown", 300, "hat_flower"),
            new LookDef("captain", "Captain", 550, "hat_captain"),
            new LookDef("fish", "Fish hat", -1, "hat_fish"),
            new LookDef("can", "Can crown", -1, "hat_can"),
        };

        public static readonly BoatDef[] Boats =
        {
            //          id      name         cost  catX bktX bktY sink lanX lanY tier
            new BoatDef("tub", "Wash tub", 0, 0, 20, 0, 4, -14, -1, 0),
            new BoatDef("raft", "Log raft", 900, -6, 12, 4, 3, -20, -1, 1),
            new BoatDef("sail", "Sailboat", 3200, -12, 7, 3, 5, -28, 8, 2),
            new BoatDef("paper", "Paper boat", -1, -6, 11, 3, 3, -18, -1, 1),
        };

        public static readonly LookDef[] Buckets =
        {
            new LookDef("wood", "Wooden pail", 0, "bucket_wood"),
            new LookDef("tin", "Tin pail", 140, "bucket_tin"),
            new LookDef("red", "Red bucket", 220, "bucket_red"),
            new LookDef("mint", "Mint bucket", 220, "bucket_mint"),
            new LookDef("gold", "Gold pail", -1, "bucket_gold"),
        };

        // highlight, surface, mid, deep, sparkle
        public static readonly PondDef[] Ponds =
        {
            new PondDef("pond", "Lily pond", 0, 0, true, true,
                "e8f0f4 b8d8e0 7fb0c0 5a8aa0 ffffff",
                "d8fbff 7fd8e8 3fa8c8 2a7aa8 ffffff",
                "ffd8b0 e8a890 a87890 6a5a88 fff0c0",
                "8aa0d8 3a4a88 26306a 161c48 fff6c8",
                "34463e 52665a 7a8e74 8a7a58 b0a07a 4a9a4a 2f6a3a f4a6c4 f6d65a"),
            new PondDef("sea", "Deep sea", 400, 1, false, false,
                "dce8ff a8c0f0 5878c8 2a4498 ffffff",
                "c8f0ff 4aa0f0 1f68d0 0f3a98 ffffff",
                "ffc8a8 d8889a 6a4a9a 2a2a6a fff0c0",
                "7088e0 22348a 121e5e 080e36 fff6c8",
                "16244e 263c7c 3e5ca8 4a6ab0 8ab0e8 2a5aa0 1a3a78 7a5ae8 f07a4a"),
            new PondDef("kelp", "Kelp forest", 650, 1, true, false,
                "e8f8d8 b0e0b8 6ab89a 3a8070 ffffe0",
                "f0ffc0 7ae0c8 2ab0a0 1a7a78 fff6a0",
                "ffe0a0 d8b080 7a8a70 3a5a58 fff0c0",
                "90c0a8 2a5a5a 18403e 0c2a2a e8ffd0",
                "1a3c3c 2a5e52 4e8a5a b8b880 e8e0a8 b8cc3a 5e8a2a f08a3a e04a6a"),
            new PondDef("lava", "Lava river", 2400, 2, false, false,
                "fff0a0 ffc040 f07020 a02818 ffffd0",
                "fff8b0 ffd040 ff7a20 b02a14 ffffe0",
                "ffe0a0 ffa040 e05020 801a14 fff0c0",
                "ffd060 ff8020 c03010 600c0c ffe8a0",
                "220e10 3e1a1a 5e2a24 4a1e14 7a3018 ff8a20 c04a10 ff6a20 ffd060"),
        };

        public static LookDef[] All(LookSlot slot)
        {
            switch (slot)
            {
                case LookSlot.Hat: return Hats;
                case LookSlot.Boat: return Boats;
                case LookSlot.Bucket: return Buckets;
                case LookSlot.Pond: return Ponds;
                default: return Coats;
            }
        }

        public static T Find<T>(T[] list, string id) where T : LookDef
        {
            foreach (var d in list)
                if (d.Id == id)
                    return d;
            return list[0];
        }

        public static string Key(LookSlot slot, string id) => slot.ToString().ToLowerInvariant() + ":" + id;

        public static bool Owns(SaveData save, LookSlot slot, string id)
        {
            if (save == null)
                return false;
            if (All(slot)[0].Id == id)
                return true;
            return save.owned != null && save.owned.Contains(Key(slot, id));
        }

        public static void Grant(SaveData save, LookSlot slot, string id)
        {
            if (save == null)
                return;
            if (save.owned == null)
                save.owned = new List<string>();
            var key = Key(slot, id);
            if (!save.owned.Contains(key))
                save.owned.Add(key);
        }

        public static string Worn(SaveData save, LookSlot slot)
        {
            if (save == null)
                return All(slot)[0].Id;
            string id;
            switch (slot)
            {
                case LookSlot.Hat: id = save.hat; break;
                case LookSlot.Boat: id = save.boat; break;
                case LookSlot.Bucket: id = save.bucket; break;
                case LookSlot.Pond: id = save.pond; break;
                default: id = save.coat; break;
            }
            return string.IsNullOrEmpty(id) ? All(slot)[0].Id : id;
        }

        public static void Wear(SaveData save, LookSlot slot, string id)
        {
            switch (slot)
            {
                case LookSlot.Hat: save.hat = id; break;
                case LookSlot.Boat: save.boat = id; break;
                case LookSlot.Bucket: save.bucket = id; break;
                case LookSlot.Pond: save.pond = id; break;
                default: save.coat = id; break;
            }
        }

        /// <summary>Something not owned yet, for the idle gift box. False when everything is owned.</summary>
        public static bool TryPickUnowned(SaveData save, System.Random rng, out LookSlot slot, out LookDef def)
        {
            var pool = new List<(LookSlot, LookDef)>();
            foreach (LookSlot s in System.Enum.GetValues(typeof(LookSlot)))
            {
                // Boats and ponds are progression, not prizes.
                if (s == LookSlot.Boat || s == LookSlot.Pond)
                    continue;
                foreach (var d in All(s))
                    if (!d.RewardOnly && !Owns(save, s, d.Id))
                        pool.Add((s, d));
            }

            if (pool.Count == 0)
            {
                slot = LookSlot.Coat;
                def = null;
                return false;
            }

            (slot, def) = pool[rng.Next(pool.Count)];
            return true;
        }
    }
}
