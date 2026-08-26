using UnityEngine;

namespace DeskPond
{
    public enum UpgradeKind
    {
        Rod,
        Basket,
        Lure
    }

    /// <summary>
    /// Static balance table for the shop. Levels are 0-based; level N costs Costs[N] to reach N+1.
    /// </summary>
    public static class ShopDefs
    {
        public const int BaseFishPrice = 5;
        public const int BaseCapacity = 20;

        // Inputs needed per fish before any rod upgrade.
        public const int BaseMinInputs = 10;
        public const int BaseMaxInputs = 1000;

        public static readonly int[] RodCosts = { 60, 180, 450, 1100, 2600 };
        public static readonly int[] BasketCosts = { 40, 120, 320, 800, 1900 };
        public static readonly int[] LureCosts = { 80, 240, 600, 1500, 3400 };

        /// <summary>
        /// Index 0 must always be the untouched original; see PaletteSwap for how these apply.
        /// </summary>
        public static readonly SkinDef[] CatSkins =
        {
            //             name       coat: hue    sat    val   cost   eyes: hue    sat    val
            new SkinDef("Ginger",         0f,    1f,    1f,     0),
            new SkinDef("Snow",           0f, 0.10f, 0.97f,   150,    0f, 1.25f, 1.05f),
            new SkinDef("Sky",         168f, 0.62f,    1f,   400, -153f, 1.30f, 1.05f),
            new SkinDef("Void",        254f, 0.50f, 0.46f,   900, -118f, 1.60f, 1.20f)
        };

        public static readonly SkinDef[] BasketSkins =
        {
            new SkinDef("Wicker", 0f, 1f, 1f, 0),
            new SkinDef("Copper", -14f, 1.25f, 1f, 200),
            new SkinDef("Mint", 132f, 0.70f, 1.05f, 500)
        };

        /// <summary>A representative mid-tone from each sprite, used to colour the shop swatches.</summary>
        public static readonly Color CatSwatchBase = new Color(0.957f, 0.765f, 0.529f);
        public static readonly Color BasketSwatchBase = new Color(0.62f, 0.38f, 0.18f);

        public static SkinDef[] Skins(bool isCat) => isCat ? CatSkins : BasketSkins;

        public static int MaxLevel(UpgradeKind kind) => Costs(kind).Length;

        public static int[] Costs(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Basket: return BasketCosts;
                case UpgradeKind.Lure: return LureCosts;
                default: return RodCosts;
            }
        }

        /// <summary>Cost to go from <paramref name="level"/> to the next tier, or -1 when maxed.</summary>
        public static int NextCost(UpgradeKind kind, int level)
        {
            var costs = Costs(kind);
            return level >= 0 && level < costs.Length ? costs[level] : -1;
        }

        public static string Label(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Basket: return "Basket";
                case UpgradeKind.Lure: return "Lure";
                default: return "Rod";
            }
        }

        public static string Effect(UpgradeKind kind, int level)
        {
            switch (kind)
            {
                case UpgradeKind.Basket: return "holds " + Capacity(level);
                case UpgradeKind.Lure: return FishPrice(level) + "c per fish";
                default: return "bite every " + MinInputs(level) + "-" + MaxInputs(level);
            }
        }

        public static int Capacity(int basketLevel) => BaseCapacity + basketLevel * 15;

        public static int FishPrice(int lureLevel) => BaseFishPrice + lureLevel * 4;

        /// <summary>Each rod tier cuts the inputs needed per fish by 18%.</summary>
        public static float RodFactor(int rodLevel) => Mathf.Pow(0.82f, Mathf.Max(0, rodLevel));

        public static int MinInputs(int rodLevel) =>
            Mathf.Max(10, Mathf.RoundToInt(BaseMinInputs * RodFactor(rodLevel)));

        public static int MaxInputs(int rodLevel) =>
            Mathf.Max(MinInputs(rodLevel) + 10, Mathf.RoundToInt(BaseMaxInputs * RodFactor(rodLevel)));

        // Skin ownership is a bitmask per category; bit 0 (the default skin) is always set.
        public static bool OwnsSkin(SaveData save, bool isCat, int index)
        {
            if (save == null || index < 0)
                return false;
            var mask = isCat ? save.ownedCatSkins : save.ownedBasketSkins;
            return (mask & (1 << index)) != 0;
        }

        public static void GrantSkin(SaveData save, bool isCat, int index)
        {
            if (save == null || index < 0)
                return;
            if (isCat)
                save.ownedCatSkins |= 1 << index;
            else
                save.ownedBasketSkins |= 1 << index;
        }

        public static int SkinCount(bool isCat) => Skins(isCat).Length;

        /// <summary>A skin the player does not own yet, or -1 when everything is unlocked.</summary>
        public static bool TryPickUnownedSkin(SaveData save, System.Random rng, out bool isCat, out int index)
        {
            var pool = new System.Collections.Generic.List<(bool cat, int idx)>();
            for (var i = 0; i < CatSkins.Length; i++)
                if (!OwnsSkin(save, true, i))
                    pool.Add((true, i));
            for (var i = 0; i < BasketSkins.Length; i++)
                if (!OwnsSkin(save, false, i))
                    pool.Add((false, i));

            if (pool.Count == 0)
            {
                isCat = true;
                index = -1;
                return false;
            }

            var pick = pool[rng.Next(pool.Count)];
            isCat = pick.cat;
            index = pick.idx;
            return true;
        }

        public static string SkinName(bool isCat, int index)
        {
            var skins = Skins(isCat);
            return index >= 0 && index < skins.Length ? skins[index].Name : "?";
        }

        public static int Level(SaveData save, UpgradeKind kind)
        {
            if (save == null)
                return 0;
            switch (kind)
            {
                case UpgradeKind.Basket: return save.basketLevel;
                case UpgradeKind.Lure: return save.lureLevel;
                default: return save.rodLevel;
            }
        }

        public static void SetLevel(SaveData save, UpgradeKind kind, int level)
        {
            if (save == null)
                return;
            switch (kind)
            {
                case UpgradeKind.Basket:
                    save.basketLevel = level;
                    break;
                case UpgradeKind.Lure:
                    save.lureLevel = level;
                    break;
                default:
                    save.rodLevel = level;
                    break;
            }
        }
    }
}
