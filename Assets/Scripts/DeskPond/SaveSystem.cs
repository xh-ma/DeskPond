using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DeskPond
{
    [Serializable]
    public class SaveData
    {
        public int basketFish;
        public int coins;

        // Progress toward the next bite, in counted input events.
        public int inputsTowardCatch;
        public int nextCatchTarget;
        public long lifetimeInputs;
        public int lifetimeFish;

        // Learned estimate of this user's input rate, kept so pacing is not relearned each launch.
        public P2Quantile pacing = new P2Quantile(0.5d);

        // Upgrade tiers (0 = unbought).
        public int rodLevel;
        public int basketLevel;
        public int lureLevel;

        // Cosmetic selections (index into ShopDefs) and which skins have been unlocked.
        // Bitmasks: bit N set means skin N is owned. Bit 0 (the default) is always owned.
        public int catSkin;
        public int basketSkin;
        public int ownedCatSkins = 1;
        public int ownedBasketSkins = 1;

        // Whether window titles are being recorded. Kept here rather than in GameConfig so a
        // shipped build starts with it off and each person turns it on for themselves, from the
        // panel, instead of it being baked into whatever the developer happened to commit.
        // -1 not chosen yet, 0 off, 1 on.
        public int recordTitles = -1;

        // One-off welcome gift. Flagged so restarting cannot farm the gift box's fallback payout.
        public bool welcomeGiftGiven;

        // Legacy v1 field, folded into coins on load. Kept so old saves still parse.
        public int walletFish;

        public string lastQuitUtc;
        public int version = SaveSystem.CurrentVersion;

        // Popup panel placement and size, in screen pixels. -1 means "not placed yet".
        public float menuScale = -1f;
        public float menuScreenX = -1f;
        public float menuScreenY = -1f;

        // Same, for the activity panel. It is placed and sized independently of the shop.
        public float statsScale = -1f;
        public float statsScreenX = -1f;
        public float statsScreenY = -1f;

        // Where the pet sits along the water, as a 0..1 fraction of screen width.
        // Normalised rather than pixels so it survives a resolution or camera change.
        // -1 means "not placed yet".
        public float petNormX = -1f;

        // ---- v4: the pond update ----

        // What is in the fish bucket right now, as catalog ids. Capacity comes from basketLevel.
        public List<string> catches = new List<string>();

        // Worn looks (ids into Looks) and everything bought, as "slot:id".
        public string coat = "ginger";
        public string hat = "none";
        public string boat = "tub";
        public string bucket = "wood";
        public string pond = "pond";
        public List<string> owned = new List<string>();

        // The dex: parallel lists, because JsonUtility cannot serialise a dictionary.
        public List<string> dexIds = new List<string>();
        public List<int> dexCounts = new List<int>();
        public List<float> dexBestCm = new List<float>();
        public List<int> notesFound = new List<int>();

        // Live fish in the desktop tank.
        public List<string> tank = new List<string>();

        // Minutes since the last catch, so a dry spell survives a restart.
        public float dryMinutes;

        // Settings.
        public bool soundOn = true;
        public float volume = 0.45f;
        public bool fullPond;
        public bool showTank = true;
        public bool recycleGhosts;
        public bool hideInFullscreen = true;
        public int sizeStep;
        public int pondDepth = 7;

        // Play time banked toward the next gift box, in seconds. Saved so a restart keeps it.
        public float giftSeconds;

        // ---- sets, weather, streaks, petting, visitors, the gull's request, letters ----

        // Dex sets already rewarded, by set id.
        public List<string> setsDone = new List<string>();

        // Bond with the cat, 0..100, and the local day (yyyyMMdd) it was last petted.
        public float affection = 30f;
        public int lastPetDay;

        // Longest unbroken work stretch, in seconds.
        public float bestStreak;

        // The gull's daily request: what it wants, how many, how many handed over so far.
        public int questDay;
        public string questItem;
        public int questNeed;
        public int questHave;
        public bool questDone;

        // Letters from the cat: play time toward the next one, the ones received (newest last),
        // and the reply the player picked for the last question, which the next letter answers.
        public float letterSeconds;
        public List<string> letters = new List<string>();
        public string lastReply;
        public int firstRunDay;

        public int DexCount(string id)
        {
            var i = dexIds.IndexOf(id);
            return i >= 0 ? dexCounts[i] : 0;
        }

        public float DexBest(string id)
        {
            var i = dexIds.IndexOf(id);
            return i >= 0 && i < dexBestCm.Count ? dexBestCm[i] : 0f;
        }

        public void Record(string id, float cm)
        {
            var i = dexIds.IndexOf(id);
            if (i < 0)
            {
                dexIds.Add(id);
                dexCounts.Add(0);
                dexBestCm.Add(0f);
                i = dexIds.Count - 1;
            }
            while (dexBestCm.Count < dexIds.Count)
                dexBestCm.Add(0f);
            dexCounts[i]++;
            if (cm > dexBestCm[i])
                dexBestCm[i] = cm;
        }
    }

    public static class SaveSystem
    {
        public const int CurrentVersion = 4;
        const string FileName = "save.json";

        /// <summary>Snapshot runs (-snap=dir) use a throwaway save so the player's own is never touched.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath,
            SnapshotMode.Active ? "save_snapshot.json" : FileName);

        public static SaveData LoadOrCreate()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var data = JsonUtility.FromJson<SaveData>(json);
                    if (data != null)
                        return Migrate(data);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not load save. " + ex.Message);
            }

            return new SaveData { version = CurrentVersion };
        }

        static SaveData Migrate(SaveData data)
        {
            if (data.version < 2)
            {
                // v1 banked whole fish in "walletFish"; v2 banks coins instead.
                data.coins += data.walletFish * 5;
                data.walletFish = 0;
                data.version = 2;
                Debug.Log("DeskPond: migrated save v1 -> v2.");
            }

            if (data.version < 3)
            {
                // v2 had no ownership tracking and charged for a skin every time it was equipped.
                // Grandfather in the default skin plus whatever was equipped at the time.
                data.ownedCatSkins |= 1 | (1 << Mathf.Max(0, data.catSkin));
                data.ownedBasketSkins |= 1 | (1 << Mathf.Max(0, data.basketSkin));
                data.version = 3;
                Debug.Log("DeskPond: migrated save v2 -> v3 (skin ownership).");
            }

            if (data.version < 4)
            {
                // v3 kept an anonymous fish count and indexed skins into a short palette list.
                // Old fish become minnows (worth less than before, so nothing is minted) and the
                // nearest new coat stands in for each old palette skin.
                if (data.catches == null)
                    data.catches = new List<string>();
                for (var i = 0; i < data.basketFish; i++)
                    data.catches.Add("minnow");
                data.basketFish = 0;

                string[] coatFor = { "ginger", "snow", "gray", "black" };
                for (var i = 0; i < coatFor.Length; i++)
                    if ((data.ownedCatSkins & (1 << i)) != 0)
                        Looks.Grant(data, LookSlot.Coat, coatFor[i]);
                data.coat = coatFor[Mathf.Clamp(data.catSkin, 0, coatFor.Length - 1)];
                if ((data.ownedBasketSkins & 2) != 0)
                    Looks.Grant(data, LookSlot.Bucket, "red");
                if ((data.ownedBasketSkins & 4) != 0)
                    Looks.Grant(data, LookSlot.Bucket, "mint");
                data.version = 4;
                Debug.Log("DeskPond: migrated save v3 -> v4 (pond update).");
            }

            data.catches ??= new List<string>();
            data.owned ??= new List<string>();
            data.dexIds ??= new List<string>();
            data.dexCounts ??= new List<int>();
            data.dexBestCm ??= new List<float>();
            data.notesFound ??= new List<int>();
            data.tank ??= new List<string>();
            data.setsDone ??= new List<string>();
            data.letters ??= new List<string>();
            return data;
        }

        public static void Write(SaveData data)
        {
            if (data == null)
                return;

            data.lastQuitUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            data.version = CurrentVersion;

            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("DeskPond: could not save. " + ex.Message);
            }
        }

        public static bool TryParseQuitUtc(SaveData data, out DateTime quitUtc)
        {
            quitUtc = default;
            if (data == null || string.IsNullOrWhiteSpace(data.lastQuitUtc))
                return false;

            if (!DateTime.TryParse(
                    data.lastQuitUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var parsed))
                return false;

            quitUtc = parsed.Kind == DateTimeKind.Utc
                ? parsed
                : parsed.ToUniversalTime();
            return true;
        }
    }
}
