using System;
using System.Collections.Generic;

namespace DeskPond
{
    /// <summary>
    /// A group of dex entries with a prize for finding them all. The prizes are looks that cannot
    /// be bought, so the dex is the only way to get them.
    /// </summary>
    public class DexSet
    {
        public string Id;
        public string Name;
        public string Hint;
        public LookSlot Slot;
        public string RewardId;
        public Func<CatchDef, bool> Includes;   // null for the bottle-note set

        public LookDef Reward => Looks.Find(Looks.All(Slot), RewardId);
    }

    public static class DexSets
    {
        static bool Special(CatchDef d) => d.Weather.HasValue || d.Season >= 0;

        public static readonly DexSet[] All =
        {
            new DexSet
            {
                Id = "regulars", Name = "Pond regulars", Hint = "every common and uncommon fish",
                Slot = LookSlot.Hat, RewardId = "fish",
                Includes = d => d.IsFish && !Special(d) && (d.Rarity == Rarity.Common || d.Rarity == Rarity.Uncommon)
            },
            new DexSet
            {
                Id = "trash", Name = "Trash king", Hint = "every piece of junk",
                Slot = LookSlot.Hat, RewardId = "can",
                Includes = d => d.Kind == CatchKind.Junk
            },
            new DexSet
            {
                Id = "shiny", Name = "Shiny things", Hint = "every treasure",
                Slot = LookSlot.Bucket, RewardId = "gold",
                Includes = d => d.Kind == CatchKind.Treasure
            },
            new DexSet
            {
                Id = "trophy", Name = "Trophy wall", Hint = "every rare and legendary fish",
                Slot = LookSlot.Coat, RewardId = "gold",
                Includes = d => d.IsFish && !Special(d) && (d.Rarity == Rarity.Rare || d.Rarity == Rarity.Legendary)
            },
            new DexSet
            {
                Id = "almanac", Name = "Almanac", Hint = "every weather and season fish",
                Slot = LookSlot.Coat, RewardId = "storm",
                Includes = d => d.IsFish && Special(d)
            },
            new DexSet
            {
                Id = "notes", Name = "Bottle post", Hint = "every message-bottle note",
                Slot = LookSlot.Boat, RewardId = "paper",
                Includes = null
            },
        };

        public static void Progress(DexSet set, SaveData save, out int have, out int need)
        {
            have = 0;
            need = 0;
            if (set.Includes == null)
            {
                need = Catalog.BottleNotes.Length;
                have = save.notesFound != null ? save.notesFound.Count : 0;
                return;
            }

            foreach (var d in Catalog.All)
            {
                if (!set.Includes(d))
                    continue;
                need++;
                if (save.DexCount(d.Id) > 0)
                    have++;
            }
        }

        /// <summary>The set a look is the prize of, or null if it is an ordinary shop item.</summary>
        public static DexSet For(LookSlot slot, string lookId)
        {
            foreach (var s in All)
                if (s.Slot == slot && s.RewardId == lookId)
                    return s;
            return null;
        }

        /// <summary>
        /// Grants the prize of every set that has just been completed. Returns them so the
        /// caller can announce each one.
        /// </summary>
        public static List<DexSet> ClaimFinished(SaveData save)
        {
            List<DexSet> done = null;
            foreach (var s in All)
            {
                if (save.setsDone.Contains(s.Id))
                    continue;
                Progress(s, save, out var have, out var need);
                if (need <= 0 || have < need)
                    continue;
                save.setsDone.Add(s.Id);
                Looks.Grant(save, s.Slot, s.RewardId);
                (done ??= new List<DexSet>()).Add(s);
            }
            return done;
        }
    }
}
