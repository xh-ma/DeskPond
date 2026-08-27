using System;
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
    }

    public static class SaveSystem
    {
        public const int CurrentVersion = 3;
        const string FileName = "save.json";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

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
                data.coins += data.walletFish * ShopDefs.BaseFishPrice;
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
