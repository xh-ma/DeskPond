using System;
using System.Collections.Generic;

namespace DeskPond
{
    /// <summary>What the cat knows when it sits down to write.</summary>
    public struct LetterContext
    {
        public float Hour;
        public WeatherKind Weather;
        public int Season;
        public int Days;            // days since the first launch
        public int Fish;            // lifetime catches
        public int DexFound, DexTotal;
        public float StreakMinutes; // the work stretch going on now
        public float BestStreakMinutes;
        public int Bond;            // 0..5
        public bool BucketFull;
        public float DryMinutes;
        public string LastCatch;    // name of the newest thing in the bucket, or null
        public int Coins;
    }

    public class CatLetter
    {
        public string Id;
        public string Text;
        public Func<LetterContext, bool> When;
        /// <summary>Up to two answers the player can send back: button label, then a tag.</summary>
        public (string Label, string Tag)[] Replies;
        /// <summary>The reply tag this letter is the answer to. Such letters are never random.</summary>
        public string Answers;
    }

    /// <summary>
    /// Letters the cat writes and floats over in a bottle. They are all written in advance, here,
    /// and nothing leaves the machine: what makes them feel like they are about today is that
    /// each one has a condition (the hour, the weather, a long work stretch, a full bucket) and
    /// blanks filled in from the game's own numbers. Some ask a question; the answer the player
    /// taps picks which letter comes next, so there is a little back and forth.
    /// </summary>
    public static class CatLetters
    {
        static CatLetter L(string id, string text, Func<LetterContext, bool> when = null,
            (string, string)[] replies = null, string answers = null) =>
            new CatLetter { Id = id, Text = text, When = when, Replies = replies, Answers = answers };

        static (string, string)[] R(string a, string ta, string b, string tb) => new[] { (a, ta), (b, tb) };

        public static readonly CatLetter[] All =
        {
            // ---- any time
            L("hello", "Hello. It's me, the cat. I found a bottle and a pen so now you get letters. That's all. Bye."),
            L("days", "We've been fishing together for {days} days. I counted on my toes. Twice.", c => c.Days >= 2),
            L("fish", "{fish} catches so far. I remember every one. Mostly the boots.", c => c.Fish >= 20),
            L("dex", "{found} of {total} in the dex. The rest are down there somewhere, looking smug.", c => c.DexFound >= 5),
            L("lastcatch", "About that {catch} in the bucket. I'd like it on record that I did the hard part.", c => c.LastCatch != null),
            L("water", "Reminder from the boat: drink some water. I'm surrounded by it and even I forget."),
            L("stretch", "Stand up. Stretch. Do the long one, with the yawn. I'll watch the line."),
            L("rich", "{coins} dried fish in the bank. We're rich. Buy me a hat.", c => c.Coins >= 400),
            L("poor", "We have {coins} dried fish. That's fine. I like the tub. The tub has character.", c => c.Coins < 60),
            L("typing", "I can hear you typing from here. It sounds like rain on the boat. Keep going."),
            L("proud", "No reason for this letter. I'm just proud of you. Don't make it weird."),
            L("secret", "Secret: the gold ! bites are the big ones. Click fast. I can't hold them alone."),

            // ---- time of day
            L("morning", "Good morning. The mist is still on the water and I've already yawned four times.",
                c => c.Hour >= 5f && c.Hour < 10f),
            L("noon", "It's the middle of the day. Have you eaten? I'm asking as someone who thinks about lunch constantly.",
                c => c.Hour >= 11.5f && c.Hour < 14f),
            L("evening", "The light's gone orange. Nice work today. Whatever's left can be tomorrow's problem.",
                c => c.Hour >= 17.5f && c.Hour < 20.5f),
            L("night", "It's late. The lantern's on and the stars are out. I'll keep fishing if you want to sleep.",
                c => c.Hour >= 22.5f || c.Hour < 4f),

            // ---- weather and season
            L("rain", "It's raining on the pond. I don't mind. The loach come up when it rains.", c => c.Weather == WeatherKind.Rain),
            L("fog", "Can't see the far bank today. Something keeps bumping the boat. Probably a file you deleted.",
                c => c.Weather == WeatherKind.Fog),
            L("snow", "Snow on my ears. Snow on the hat. Worth it: icefish only bite in this.", c => c.Weather == WeatherKind.Snow),
            L("spring", "Spring. The cherry salmon are running. I'm told this is a big deal.", c => c.Season == 0),
            L("summer", "Summer. A sunfish drifted past doing absolutely nothing. I respect it.", c => c.Season == 1),
            L("autumn", "Autumn. The salmon are heading home. Good time to finish things.", c => c.Season == 2),
            L("winter", "Winter. The cod don't mind the cold and neither do I. (I mind a little.)", c => c.Season == 3),

            // ---- about how the work is going
            L("streak", "You've been at it {streak} minutes without stopping. The fish noticed. So did I.",
                c => c.StreakMinutes >= 25f),
            L("best", "Your longest run is {best} minutes. I didn't blink the whole time. (I did.)",
                c => c.BestStreakMinutes >= 30f),
            L("full", "The bucket's full and a crow has been looking at it. Sell some to the gull, would you?",
                c => c.BucketFull),
            L("dry", "Nothing's biting. I've started a diary. Entry one: nothing's biting.", c => c.DryMinutes >= 20f),
            L("bondlow", "You haven't scratched my ears in a while. I'm not saying anything. I'm just saying.",
                c => c.Bond <= 1),
            L("bondhigh", "You always stop to pet me. I've decided you can stay.", c => c.Bond >= 4),

            // ---- questions, and the letters that answer them
            L("q_day", "Quick question from the boat: how's today going?", null,
                R("GOOD", "day_good", "ROUGH", "day_rough")),
            L("a_day_good", "Good! I had a feeling. I caught a feeling once. Threw it back.", answers: "day_good"),
            L("a_day_rough", "Rough days still end. Sit with me a minute. The water doesn't need anything from you.",
                answers: "day_rough"),

            L("q_break", "When did you last take a break? Be honest. I can see the clock.", c => c.StreakMinutes >= 40f,
                R("JUST NOW", "break_yes", "UM...", "break_no")),
            L("a_break_yes", "Good. I'll let it go. This time.", answers: "break_yes"),
            L("a_break_no", "Thought so. Five minutes. Go look out of a window. I'll hold the rod.", answers: "break_no"),

            L("q_fish", "Important survey. Which is better: fish, or more fish?", null,
                R("FISH", "fish_one", "MORE FISH", "fish_more")),
            L("a_fish_one", "A minimalist. I see. I'll note it in the diary.", answers: "fish_one"),
            L("a_fish_more", "Correct. You passed. There was no way to fail but well done anyway.", answers: "fish_more"),

            L("q_name", "I never asked. Do you like it here, on the desktop, with me?", c => c.Days >= 3,
                R("YES", "like_yes", "IT'S OK", "like_ok")),
            L("a_like_yes", "Me too. That's the whole letter. Me too.", answers: "like_yes"),
            L("a_like_ok", "'It's ok.' Bold thing to say to someone holding all your fish.", answers: "like_ok"),
        };

        public static CatLetter Get(string id)
        {
            foreach (var l in All)
                if (l.Id == id)
                    return l;
            return null;
        }

        /// <summary>
        /// The next letter: the answer to the player's last reply if there is one waiting,
        /// otherwise something that fits right now, preferring letters not yet received.
        /// </summary>
        public static CatLetter Pick(SaveData save, LetterContext ctx, Random rng)
        {
            if (!string.IsNullOrEmpty(save.lastReply))
                foreach (var l in All)
                    if (l.Answers == save.lastReply)
                        return l;

            if (save.letters.Count == 0)
                return Get("hello");

            var fresh = new List<CatLetter>();
            var seen = new List<CatLetter>();
            foreach (var l in All)
            {
                if (l.Answers != null || (l.When != null && !l.When(ctx)))
                    continue;
                (Received(save, l.Id) ? seen : fresh).Add(l);
            }

            var pool = fresh.Count > 0 ? fresh : seen;
            return pool.Count > 0 ? pool[rng.Next(pool.Count)] : Get("hello");
        }

        static bool Received(SaveData save, string id)
        {
            foreach (var s in save.letters)
                if (IdOf(s) == id)
                    return true;
            return false;
        }

        public static string Render(CatLetter letter, LetterContext c)
        {
            return letter.Text
                .Replace("{days}", c.Days.ToString())
                .Replace("{fish}", c.Fish.ToString())
                .Replace("{found}", c.DexFound.ToString())
                .Replace("{total}", c.DexTotal.ToString())
                .Replace("{catch}", (c.LastCatch ?? "fish").ToLowerInvariant())
                .Replace("{coins}", c.Coins.ToString())
                .Replace("{streak}", ((int)c.StreakMinutes).ToString())
                .Replace("{best}", ((int)c.BestStreakMinutes).ToString());
        }

        // Kept letters are stored as "id|text", so a re-read shows what was written that day.
        public static string Pack(string id, string text) => id + "|" + text;
        public static string IdOf(string packed) => packed.Substring(0, Math.Max(0, packed.IndexOf('|')));
        public static string TextOf(string packed) => packed.Substring(packed.IndexOf('|') + 1);
    }
}
