using UnityEngine;

namespace DeskPond
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "DeskPond/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("Gameplay")]
        [Min(0.05f)]
        [Tooltip("How long the catch sequence (bite, reel, drop into basket) lasts.")]
        public float catchAnimDuration = 1.6f;

        [Min(0.01f)]
        [Tooltip("Testing aid: scales the inputs needed per fish. " +
                 "1 = normal, 0.05 = bites almost instantly.")]
        public float inputCostScale = 1f;

        [Header("Pacing")]
        [Tooltip("Set the cost of a fish from how fast this user actually works, instead of " +
                 "rolling in a fixed range. Turn off to compare the two by feel.")]
        public bool adaptivePacing = true;

        [Range(1f, 30f)]
        [Tooltip("Minutes of active work one fish should take, once pacing has learned the user's " +
                 "rate. Rod upgrades cut this.")]
        public float minutesPerFish = 5f;

        [Range(0f, 0.9f)]
        [Tooltip("How much the cost of each fish varies around the target, so bites stay a little " +
                 "unpredictable. 0.3 = plus or minus 30%.")]
        public float pacingSpread = 0.3f;

        [Min(1)]
        [Tooltip("Hard floor on inputs per fish, before rod upgrades.")]
        public int pacingMinInputs = 10;

        [Min(50)]
        [Tooltip("Hard ceiling on inputs per fish, before rod upgrades.")]
        public int pacingMaxInputs = 4000;

        [Header("Activity log")]
        [Tooltip("Record which applications are in front, for the activity stats. " +
                 "Turning this off stops all logging.")]
        public bool trackActivity = true;

        [Tooltip("Also record window titles, not just process names. Off by default: a title can " +
                 "carry document names, page titles and who you were talking to, which is not " +
                 "something to collect quietly from anyone else running a build.")]
        public bool recordWindowTitles;

        [Range(1f, 30f)]
        [Tooltip("Seconds between foreground window samples.")]
        public float foregroundPollSeconds = 5f;

        [Range(1, 365)]
        [Tooltip("Days of activity history to keep. Older slices are deleted on write.")]
        public int retentionDays = 30;

        [Range(0, 90)]
        [Tooltip("Days to keep window titles. Shorter than the history above on purpose: rhythm " +
                 "stats want the long view, titles do not. 0 deletes them at the next write.")]
        public int titleRetentionDays = 7;

        [Header("Idle fishing")]
        [Min(0.5f)]
        [Tooltip("Seconds without any input before the cat counts as idle and the timer starts.")]
        public float idleThresholdSeconds = 3f;

        [Min(5f)]
        [Tooltip("Seconds of unbroken idle time that yield one fish. The timer resets on any input.")]
        public float idleSecondsPerFish = 300f;

        [Min(10f)]
        [Tooltip("Seconds of unbroken idle time before a drifting gift box appears with a free skin.")]
        public float idleSecondsPerGift = 1800f;

        [Min(0f)]
        [Tooltip("Delay before the one-off welcome gift drifts in on a brand new save.")]
        public float welcomeGiftDelaySeconds = 5f;

        [Header("Animation")]
        [Tooltip("Eases the gift box in from the screen edge. X = 0..1 progress, Y = 0..1 distance.")]
        public AnimationCurve giftDrift = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Min(2f)]
        [Tooltip("Seconds the gift box takes to drift from the screen edge to the cat.")]
        public float giftDriftSeconds = 14f;

        [Tooltip("Pop curve for the basket popup opening. X = 0..1 time, Y = scale multiplier.")]
        public AnimationCurve popupOpen = new AnimationCurve(
            new Keyframe(0f, 0.85f), new Keyframe(0.6f, 1.03f), new Keyframe(1f, 1f));

        [Min(0.05f)]
        public float popupOpenSeconds = 0.18f;

        [Header("Performance")]
        [Range(10, 144)]
        [Tooltip("Frames per second cap. Without one the scene renders as fast as the machine " +
                 "allows, which burns a whole CPU core to animate a cat at 3fps. Only takes " +
                 "effect while vSync is off, which it is.")]
        public int targetFrameRate = 30;

        [Header("Pet")]
        [Range(1f, 8f)]
        [Tooltip("On-screen magnification of the pet. 3 = each sprite pixel covers 3x3 screen pixels.")]
        public float petZoom = 3f;

        [Tooltip("Where the rod is held, relative to the cat's centre. Lower Y = rod sits lower.")]
        public Vector2 rodBaseOffset = new Vector2(-0.28f, -0.38f);

        [Tooltip("Rod tip relative to the rod base. Sets the rod's length and angle.")]
        public Vector2 rodTipOffset = new Vector2(-1.05f, 0.85f);

        [Range(1f, 24f)]
        [Tooltip("Frames per second for the cat's animation sheets.")]
        public float catFrameRate = 3f;

        [Range(0f, 0.6f)]
        [Tooltip("How high the cat hops when a fish bites, in world units. Only used while there " +
                 "is no cat_catch animation strip.")]
        public float catchHopHeight = 0.16f;

        [Range(0f, 25f)]
        [Tooltip("How far the cat leans toward the rod when a fish bites, in degrees.")]
        public float catchLeanDegrees = 7f;

        [Tooltip("Play sheets forwards then backwards (1-2-3-4-3-2) instead of looping 1-2-3-4-1. " +
                 "Suits a tail that swings out and back, and avoids the snap at the loop point.")]
        public bool catPingPong = true;

        [Header("Water")]
        [Tooltip("Colour of the waterline stripe that sits on top of the taskbar.")]
        public Color waterColor = new Color(0.45f, 0.72f, 0.95f);

        [Range(0.02f, 1f)]
        [Tooltip("Thickness of the waterline stripe, in world units.")]
        public float waterRimThickness = 0.08f;

        [Header("Popup panel")]
        [Range(1f, 5f)]
        [Tooltip("Starting size of the basket popup. Drag its bottom-right corner to change it live.")]
        public float menuScale = 2f;

        [Range(240, 720)]
        [Tooltip("Panel width in design units, before menuScale is applied.")]
        public int menuWidth = 340;
    }
}
