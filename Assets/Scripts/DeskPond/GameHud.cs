using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    public class GameHud : MonoBehaviour
    {
        Text _coins;
        Text _rate;
        Image _progressFill;
        RectTransform _progressTrack;

        // Labels that track the pet in world space rather than sitting in a screen corner.
        Text _basketTag;
        Text _catTag;

        // Reward bubble.
        RectTransform _bubble;
        CanvasGroup _bubbleGroup;
        Text _bubbleText;
        Transform _bubbleFollow;
        float _bubbleSeconds;
        float _bubbleElapsed;

        GameSession _session;
        Transform _cat;
        Transform _basket;
        Camera _camera;
        float _tagScale = 1f;

        public static GameHud Create()
        {
            var root = new GameObject("HUD");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            var hud = root.AddComponent<GameHud>();
            hud.Build();
            return hud;
        }

        public void Bind(GameSession session, Transform cat, Transform basket, Camera cam, float zoom)
        {
            _session = session;
            _cat = cat;
            _basket = basket;
            _camera = cam;
            _tagScale = Mathf.Max(0.5f, zoom / 3f);

            if (_basketTag != null)
                _basketTag.fontSize = Mathf.RoundToInt(13f * _tagScale);
            if (_catTag != null)
                _catTag.fontSize = Mathf.RoundToInt(13f * _tagScale);
        }

        public void Refresh(GameSession session)
        {
            if (session == null)
                return;

            if (_coins != null)
                _coins.text = session.Coins.ToString();

            if (_progressFill != null && _progressTrack != null)
            {
                var width = _progressTrack.sizeDelta.x * Mathf.Clamp01(session.CatchProgress);
                _progressFill.rectTransform.sizeDelta = new Vector2(width, _progressTrack.sizeDelta.y);
                _progressFill.color = session.BasketFull
                    ? new Color(0.8f, 0.35f, 0.3f)
                    : new Color(0.4f, 0.78f, 0.95f);
            }
        }

        void Update()
        {
            if (_session == null)
                _session = FindFirstObjectByType<GameSession>();
            if (_session == null)
                return;

            if (_rate != null && _session.Input != null)
            {
                _rate.text = _session.Input.Rate > 0.15f
                    ? "activity " + _session.Input.Rate.ToString("0.0") + "/s"
                    : "idle";
            }

            Refresh(_session);
            PlaceTags();
            TickBubble();
        }

        /// <summary>
        /// Pops a speech bubble above the pet announcing a reward, then fades it out. Follows the
        /// anchor each frame so it stays put if the player drags the cat while it is showing.
        /// </summary>
        public void ShowBubble(string message, Transform follow, float seconds = 3.5f)
        {
            if (_bubble == null)
                return;

            _bubbleFollow = follow != null ? follow : _cat;
            _bubbleText.text = message;

            // Size the bubble to its text rather than guessing a fixed width.
            var width = Mathf.Clamp(_bubbleText.preferredWidth + 24f, 80f, 320f);
            _bubble.sizeDelta = new Vector2(width, 30f);

            _bubbleSeconds = seconds;
            _bubbleElapsed = 0f;
            _bubble.gameObject.SetActive(true);
        }

        void TickBubble()
        {
            if (_bubble == null || !_bubble.gameObject.activeSelf)
                return;

            _bubbleElapsed += Time.deltaTime;
            if (_bubbleElapsed >= _bubbleSeconds)
            {
                _bubble.gameObject.SetActive(false);
                return;
            }

            if (_bubbleFollow != null && _camera != null)
            {
                var screen = _camera.WorldToScreenPoint(_bubbleFollow.position);
                // Rises slightly as it plays, which reads as a pop rather than a static label.
                var lift = Mathf.Lerp(0f, 14f, Mathf.Clamp01(_bubbleElapsed / 0.5f));
                _bubble.anchoredPosition = new Vector2(screen.x, screen.y + (62f * _tagScale) + lift);
            }

            // Fade over the last half second.
            var remaining = _bubbleSeconds - _bubbleElapsed;
            var alpha = Mathf.Clamp01(remaining / 0.5f) * Mathf.Clamp01(_bubbleElapsed / 0.15f);
            _bubbleGroup.alpha = alpha;
        }

        void PlaceTags()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            if (_basketTag != null && _basket != null)
            {
                _basketTag.text = _session.BasketFish + "/" + _session.Capacity;
                _basketTag.color = _session.BasketFull
                    ? new Color(1f, 0.55f, 0.45f)
                    : new Color(0.94f, 0.96f, 1f);
                Place(_basketTag.rectTransform, _basket.position, 34f);
            }

            if (_catTag != null && _cat != null)
            {
                var idle = _session.IsIdle;
                _catTag.enabled = idle;
                if (idle)
                {
                    _catTag.text = "idle  " + Clock(_session.IdleSeconds);
                    Place(_catTag.rectTransform, _cat.position, 40f);
                }
            }
        }

        void Place(RectTransform rt, Vector3 world, float pixelOffset)
        {
            var screen = _camera.WorldToScreenPoint(world);
            rt.anchoredPosition = new Vector2(screen.x, screen.y + pixelOffset * _tagScale);
        }

        static string Clock(float seconds)
        {
            var total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        void Build()
        {
            var font = UiKit.PixelFont;

            var coinGo = new GameObject("CoinIcon");
            coinGo.transform.SetParent(transform, false);
            var coinIcon = coinGo.AddComponent<Image>();
            coinIcon.sprite = UiKit.Coin;
            coinIcon.raycastTarget = false;
            var coinRt = coinIcon.rectTransform;
            coinRt.anchorMin = new Vector2(0f, 1f);
            coinRt.anchorMax = new Vector2(0f, 1f);
            coinRt.pivot = new Vector2(0f, 1f);
            coinRt.anchoredPosition = new Vector2(8f, -8f);
            coinRt.sizeDelta = new Vector2(16f, 16f);

            _coins = MakeLabel("Coins", new Vector2(28f, -6f), 16, TextAnchor.UpperLeft,
                new Color(1f, 0.85f, 0.35f), font);
            _rate = MakeLabel("Rate", new Vector2(-8f, -6f), 12, TextAnchor.UpperRight,
                new Color(0.55f, 0.75f, 0.9f), font);
            _rate.rectTransform.sizeDelta = new Vector2(200f, 22f);

            var trackGo = new GameObject("ProgressTrack");
            trackGo.transform.SetParent(transform, false);
            var trackImage = trackGo.AddComponent<Image>();
            trackImage.color = new Color(1f, 1f, 1f, 0.13f);
            _progressTrack = trackImage.rectTransform;
            _progressTrack.anchorMin = new Vector2(0f, 1f);
            _progressTrack.anchorMax = new Vector2(0f, 1f);
            _progressTrack.pivot = new Vector2(0f, 1f);
            _progressTrack.anchoredPosition = new Vector2(8f, -30f);
            _progressTrack.sizeDelta = new Vector2(140f, 5f);

            var fillGo = new GameObject("ProgressFill");
            fillGo.transform.SetParent(_progressTrack, false);
            _progressFill = fillGo.AddComponent<Image>();
            _progressFill.color = new Color(0.4f, 0.78f, 0.95f);
            var fillRt = _progressFill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            fillRt.sizeDelta = new Vector2(0f, 5f);

            _basketTag = MakeTag("BasketTag", font, new Color(0.94f, 0.96f, 1f));
            _catTag = MakeTag("CatTag", font, new Color(0.72f, 0.86f, 1f));
            _catTag.enabled = false;

            BuildBubble(font);
        }

        void BuildBubble(Font font)
        {
            var go = new GameObject("RewardBubble");
            go.transform.SetParent(transform, false);
            var image = go.AddComponent<Image>();
            image.sprite = UiKit.Raised(UiKit.PanelFace);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;

            _bubble = image.rectTransform;
            _bubble.anchorMin = Vector2.zero;
            _bubble.anchorMax = Vector2.zero;
            _bubble.pivot = new Vector2(0.5f, 0f);
            _bubble.sizeDelta = new Vector2(160f, 30f);

            _bubbleGroup = go.AddComponent<CanvasGroup>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_bubble, false);
            _bubbleText = textGo.AddComponent<Text>();
            _bubbleText.font = font;
            _bubbleText.fontSize = 13;
            _bubbleText.fontStyle = FontStyle.Bold;
            _bubbleText.alignment = TextAnchor.MiddleCenter;
            _bubbleText.color = UiKit.TextDark;
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bubbleText.verticalOverflow = VerticalWrapMode.Overflow;
            _bubbleText.raycastTarget = false;

            var rt = _bubbleText.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.SetActive(false);
        }

        /// <summary>A world-tracking label, outlined so it stays readable over any desktop content.</summary>
        Text MakeTag(string name, Font font, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = 13;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerCenter;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.4f, -1.4f);

            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(160f, 22f);
            return text;
        }

        Text MakeLabel(string name, Vector2 anchoredPos, int size, TextAnchor anchor, Color color, Font font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(anchor == TextAnchor.UpperRight ? 1f : 0f, 1f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(anchor == TextAnchor.UpperRight ? 1f : 0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(140f, 22f);
            return text;
        }

            }
}
