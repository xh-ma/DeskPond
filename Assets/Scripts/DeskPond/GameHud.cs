using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    /// <summary>
    /// World-anchored labels over the pet: the speech bubble that announces catches, and the
    /// small "count/capacity" tag on the bucket. Nothing is pinned to a screen corner, since the
    /// window is only a band along the bottom of the screen.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        RectTransform _bubble;
        CanvasGroup _bubbleGroup;
        Text _bubbleText;
        Transform _bubbleFollow;
        float _bubbleSeconds;
        float _bubbleElapsed;
        Text _bucketTag;
        float _bucketFlash;
        float _giftFlash;
        RectTransform _giftTag;
        Text _giftText;
        Text _streakTag;

        GameSession _session;
        Transform _cat;
        Transform _bucket;
        Camera _camera;
        CanvasScaler _scaler;
        float _scale = 1f;

        public bool BubbleVisible => _bubble != null && _bubble.gameObject.activeSelf;

        public static GameHud Create()
        {
            var root = new GameObject("HUD");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the panels, so a sale message from the shop shows over the cat even while the
            // panel is open.
            canvas.sortingOrder = 60;
            var hud = root.AddComponent<GameHud>();
            hud._scaler = root.AddComponent<CanvasScaler>();
            hud._scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            hud.Build();
            return hud;
        }

        public void Bind(GameSession session, Transform cat, Transform bucket, Camera cam, float zoom)
        {
            _session = session;
            _cat = cat;
            _bucket = bucket;
            _camera = cam;
            // One UI design unit is one art pixel, so the pixel font lines up with the art.
            _scale = Mathf.Max(1f, zoom);
            if (_scaler != null)
                _scaler.scaleFactor = _scale;
        }

        void Update()
        {
            TickBubble();
            PlaceTag();
            PlaceGiftTag();
            PlaceStreakTag();
        }

        /// <summary>
        /// The work stretch going on now, shown over the cat while the cursor rests on it and
        /// only once the stretch is long enough to be raising the odds.
        /// </summary>
        void PlaceStreakTag()
        {
            if (_streakTag == null || _session == null || _cat == null || _camera == null)
                return;
            var tier = _session.StreakTier;
            var show = tier > 0 && _giftFlash > -0.05f && !BubbleVisible;
            _streakTag.enabled = show;
            if (!show)
                return;
            _streakTag.text = "STREAK " + Mathf.FloorToInt(_session.StreakSeconds / 60f) + "M  RARE +" + tier;
            var screen = _camera.WorldToScreenPoint(_cat.position) / _scale;
            _streakTag.rectTransform.anchoredPosition = new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y + 46f));
        }

        /// <summary>Shows the gift countdown over the cat (while the cursor is on it).</summary>
        public void FlashGiftTimer(float seconds) => _giftFlash = Mathf.Max(_giftFlash, seconds);

        /// <summary>Shows the bucket's count for a few seconds (after a catch, or on hover).</summary>
        public void FlashBucketCount(float seconds) => _bucketFlash = Mathf.Max(_bucketFlash, seconds);

        /// <summary>
        /// A small gift icon and countdown over the cat: play time left until the next gift box.
        /// Shown while the cursor rests on the cat and through the final minute, so it is there
        /// when wanted without sitting on screen all hour. The panel's top strip always has it.
        /// </summary>
        void PlaceGiftTag()
        {
            if (_giftTag == null || _session == null || _cat == null || _camera == null)
                return;
            var left = _session.GiftSecondsLeft;
            _giftFlash -= Time.deltaTime;
            var show = left > 0f && (_giftFlash > 0f || left <= 60f) && !BubbleVisible;
            if (_giftTag.gameObject.activeSelf != show)
                _giftTag.gameObject.SetActive(show);
            if (!show)
                return;
            var total = Mathf.CeilToInt(left);
            var text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
            if (_giftText.text != text)
            {
                _giftText.text = text;
                // The pill grows with the text so the digits never spill out of it.
                _giftTag.sizeDelta = new Vector2(Mathf.Ceil(_giftText.preferredWidth) + 20f, 14f);
            }
            var screen = _camera.WorldToScreenPoint(_cat.position) / _scale;
            _giftTag.anchoredPosition = new Vector2(Mathf.Round(screen.x), Mathf.Round(screen.y + 30f));
        }

        public void ShowBubble(string message, Transform follow, float seconds = 3f)
        {
            if (_bubble == null)
                return;
            _bubbleFollow = follow != null ? follow : _cat;
            _bubbleText.text = message;
            var width = Mathf.Clamp(_bubbleText.preferredWidth + 10f, 24f, 150f);
            _bubbleText.horizontalOverflow = width >= 150f ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            var height = width >= 150f ? _bubbleText.preferredHeight + 7f : 14f;
            _bubble.sizeDelta = new Vector2(width, Mathf.Max(14f, height));
            _bubbleSeconds = seconds;
            _bubbleElapsed = 0f;
            _bubble.gameObject.SetActive(true);
        }

        void TickBubble()
        {
            if (!BubbleVisible)
                return;
            _bubbleElapsed += Time.deltaTime;
            if (_bubbleElapsed >= _bubbleSeconds)
            {
                _bubble.gameObject.SetActive(false);
                return;
            }

            if (_bubbleFollow != null && _camera != null)
            {
                var screen = _camera.WorldToScreenPoint(_bubbleFollow.position) / _scale;
                var lift = Mathf.Lerp(0f, 3f, Mathf.Clamp01(_bubbleElapsed / 0.4f));
                var half = _bubble.sizeDelta.x * 0.5f;
                var maxX = Screen.width / _scale - half - 2f;
                // The cat's transform is at its feet: clear the whole cat (24 px) and a hat, so the
                // bubble sits above the head instead of over the face.
                var above = _bubbleFollow == _cat ? 30f : 10f;
                _bubble.anchoredPosition = new Vector2(Mathf.Clamp(screen.x, half + 2f, Mathf.Max(half + 2f, maxX)),
                    screen.y + above + lift);
            }

            var remaining = _bubbleSeconds - _bubbleElapsed;
            _bubbleGroup.alpha = Mathf.Clamp01(remaining / 0.4f) * Mathf.Clamp01(_bubbleElapsed / 0.12f);
        }

        void PlaceTag()
        {
            if (_bucketTag == null || _bucket == null || _camera == null || _session == null || _session.Save == null)
                return;
            var count = _session.BasketFish;
            _bucketFlash -= Time.deltaTime;
            _bucketTag.enabled = count > 0 && _bucketFlash > 0f;
            if (!_bucketTag.enabled)
                return;
            _bucketTag.text = count + "/" + _session.Capacity;
            _bucketTag.color = _session.BasketFull ? new Color(1f, 0.6f, 0.55f) : new Color(0.96f, 0.97f, 1f);
            var screen = _camera.WorldToScreenPoint(_bucket.position) / _scale;
            _bucketTag.rectTransform.anchoredPosition = new Vector2(screen.x + 4f, screen.y + 14f);
        }

        void Build()
        {
            var font = UiKit.PixelFont;

            var go = new GameObject("Bubble");
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<Image>();
            image.sprite = UiKit.Raised(UiKit.PanelFace);
            image.type = Image.Type.Sliced;
            image.raycastTarget = false;
            _bubble = image.rectTransform;
            _bubble.anchorMin = Vector2.zero;
            _bubble.anchorMax = Vector2.zero;
            _bubble.pivot = new Vector2(0.5f, 0f);
            _bubbleGroup = go.AddComponent<CanvasGroup>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_bubble, false);
            _bubbleText = textGo.AddComponent<Text>();
            _bubbleText.font = font;
            _bubbleText.fontSize = 8;
            _bubbleText.alignment = TextAnchor.MiddleCenter;
            _bubbleText.color = UiKit.TextDark;
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bubbleText.verticalOverflow = VerticalWrapMode.Overflow;
            _bubbleText.raycastTarget = false;
            var rt = _bubbleText.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 2f);
            rt.offsetMax = new Vector2(-4f, -2f);
            go.SetActive(false);

            var tagGo = new GameObject("BucketTag");
            tagGo.transform.SetParent(transform, false);
            _bucketTag = tagGo.AddComponent<Text>();
            _bucketTag.font = font;
            _bucketTag.fontSize = 8;
            _bucketTag.alignment = TextAnchor.LowerCenter;
            _bucketTag.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bucketTag.verticalOverflow = VerticalWrapMode.Overflow;
            _bucketTag.raycastTarget = false;
            var outline = tagGo.AddComponent<Outline>();
            outline.effectColor = new Color(0.15f, 0.19f, 0.42f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            var trt = _bucketTag.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.zero;
            trt.pivot = new Vector2(0.5f, 0f);
            trt.sizeDelta = new Vector2(40f, 10f);

            var streakGo = new GameObject("StreakTag");
            streakGo.transform.SetParent(transform, false);
            _streakTag = streakGo.AddComponent<Text>();
            _streakTag.font = font;
            _streakTag.fontSize = 8;
            _streakTag.alignment = TextAnchor.LowerCenter;
            _streakTag.color = new Color(1f, 0.92f, 0.55f);
            _streakTag.horizontalOverflow = HorizontalWrapMode.Overflow;
            _streakTag.verticalOverflow = VerticalWrapMode.Overflow;
            _streakTag.raycastTarget = false;
            var streakOutline = streakGo.AddComponent<Outline>();
            streakOutline.effectColor = new Color(0.15f, 0.19f, 0.42f, 1f);
            streakOutline.effectDistance = new Vector2(1f, -1f);
            var srt = _streakTag.rectTransform;
            srt.anchorMin = srt.anchorMax = Vector2.zero;
            srt.pivot = new Vector2(0.5f, 0f);
            srt.sizeDelta = new Vector2(120f, 10f);
            _streakTag.enabled = false;

            // Gift countdown: icon + mm:ss in a small sunken pill.
            var gift = new GameObject("GiftTag");
            gift.transform.SetParent(transform, false);
            var pill = gift.AddComponent<Image>();
            pill.sprite = UiKit.Raised(UiKit.PanelFace);
            pill.type = Image.Type.Sliced;
            pill.raycastTarget = false;
            _giftTag = pill.rectTransform;
            _giftTag.anchorMin = _giftTag.anchorMax = Vector2.zero;
            _giftTag.pivot = new Vector2(0.5f, 0f);
            _giftTag.sizeDelta = new Vector2(40f, 14f);
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(_giftTag, false);
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = Atlas.Ui("icon_gift");
            icon.raycastTarget = false;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(2f, 0f);
            irt.sizeDelta = new Vector2(12f, 12f);
            var tg = new GameObject("Time");
            tg.transform.SetParent(_giftTag, false);
            _giftText = tg.AddComponent<Text>();
            _giftText.font = font;
            _giftText.fontSize = 8;
            _giftText.color = UiKit.TextDark;
            _giftText.alignment = TextAnchor.MiddleLeft;
            _giftText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _giftText.raycastTarget = false;
            var ttr = _giftText.rectTransform;
            ttr.anchorMin = Vector2.zero;
            ttr.anchorMax = Vector2.one;
            ttr.offsetMin = new Vector2(16f, 0f);
            ttr.offsetMax = new Vector2(-3f, 0f);
            gift.SetActive(false);
        }
    }
}
