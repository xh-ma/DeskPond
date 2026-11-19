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
    /// Balance table for the gull's upgrades. Levels are 0-based; level N costs Costs[N] to reach
    /// N+1. Cosmetics live in <see cref="Looks"/>, catches in <see cref="Catalog"/>.
    /// </summary>
    public static class ShopDefs
    {
        public const int BaseCapacity = 8;

        /// <summary>Live fish the tank in the panel can hold.</summary>
        public const int TankCapacity = 6;

        // Inputs needed per fish before any rod upgrade, while pacing is still warming up.
        public const int BaseMinInputs = 10;
        public const int BaseMaxInputs = 1000;

        public static readonly int[] RodCosts = { 60, 180, 450, 1100, 2600 };
        public static readonly int[] BasketCosts = { 40, 120, 320, 800, 1900 };
        public static readonly int[] LureCosts = { 80, 240, 600, 1500, 3400 };

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
                case UpgradeKind.Basket: return "Bucket";
                case UpgradeKind.Lure: return "Lure";
                default: return "Rod";
            }
        }

        /// <summary>What a tier does, in the words the shop row shows.</summary>
        public static string Effect(UpgradeKind kind, int level, float adaptiveMinutes = 0f)
        {
            switch (kind)
            {
                case UpgradeKind.Basket: return "holds " + Capacity(level);
                case UpgradeKind.Lure: return "rare odds +" + (level * 30) + "%";
                default:
                    if (adaptiveMinutes > 0f)
                        return "bite every " + (adaptiveMinutes * RodFactor(level)).ToString("0.#") + " min";
                    return "bite every " + MinInputs(level) + "-" + MaxInputs(level);
            }
        }

        public static int Capacity(int basketLevel) => BaseCapacity + basketLevel * 4;

        /// <summary>Each rod tier cuts the work needed per bite by 18%.</summary>
        public static float RodFactor(int rodLevel) => Mathf.Pow(0.82f, Mathf.Max(0, rodLevel));

        /// <summary>Golden bites get likelier with a better rod.</summary>
        public static float GoldenChance(int rodLevel) => 0.04f + rodLevel * 0.01f;

        public static int MinInputs(int rodLevel) =>
            Mathf.Max(10, Mathf.RoundToInt(BaseMinInputs * RodFactor(rodLevel)));

        public static int MaxInputs(int rodLevel) =>
            Mathf.Max(MinInputs(rodLevel) + 10, Mathf.RoundToInt(BaseMaxInputs * RodFactor(rodLevel)));

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
                case UpgradeKind.Basket: save.basketLevel = level; break;
                case UpgradeKind.Lure: save.lureLevel = level; break;
                default: save.rodLevel = level; break;
            }
        }
    }
}
