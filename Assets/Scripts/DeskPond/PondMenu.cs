using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeskPond
{
    /// <summary>
    /// The pet's one panel, opened by clicking the bucket: a little retro window with tabs.
    ///
    ///   Bucket    what was caught: sell to the gull, or keep live fish in the desktop tank
    ///   Shop      rod, bucket and lure upgrades
    ///   Wardrobe  coats, hats, boats, buckets and ponds, bongo-cat style: tap a tile to buy or wear
    ///   Tank      the fish you kept, swimming in a little neon aquarium cabinet
    ///   Dex       everything that can be caught, with counts, records and notes from bottles
    ///   Settings  sound, size, pond width, tank, fullscreen behaviour, ghost files, stats, quit
    ///
    /// One design unit is one art pixel; the canvas scales by the pet's zoom, so the pixel font
    /// and the icons share a grid. Buttons are hit-tested by hand against the OS cursor, because
    /// the overlay never has focus and Unity's event system never sees the mouse.
    /// </summary>
    public class PondMenu : MonoBehaviour
    {
        public enum Tab
        {
            Bucket,
            Shop,
            Wardrobe,
            Tank,
            Dex,
            Settings
        }

        const float W = 212f, H = 180f, HeaderH = 13f, TabH = 12f, InfoH = 12f;

        class Btn
        {
            public RectTransform Rect;
            public Image Face;
            public Color Color;
            public Action Click;
            public bool Enabled = true;
            public bool Hovered;
        }

        GameSession _session;
        DesktopWindow _window;
        CanvasScaler _scaler;
        RectTransform _panel, _header, _content;
        Text _coins, _bucketCount, _gift;
        Image _giftIcon;
        Font _font;
        readonly List<Btn> _chrome = new List<Btn>();
        readonly List<Btn> _buttons = new List<Btn>();
        Tab _tab = Tab.Bucket;
        LookSlot _slot = LookSlot.Coat;

        /// <summary>Switches the WEAR tab to one slot (used by snapshot mode).</summary>
        public void ShowSlot(LookSlot slot)
        {
            _slot = slot;
            Rebuild();
        }
        int _selected = -1;
        string _dexSelected;

        // The dex has two extra pages besides the grid: the sets and the cat's letters.
        enum DexPage { Entries, Sets, Mail }
        DexPage _dexPage = DexPage.Entries;

        /// <summary>Opens the dex on its sets or mail page (used by snapshot mode).</summary>
        public void ShowDexPage(int page)
        {
            _tab = Tab.Dex;
            _dexPage = (DexPage)page;
            Rebuild();
        }
        float _refreshClock;
        float _scale = 3f;
        Vector2 _screenPos;
        bool _placed;
        bool _moving;
        Vector2 _grab, _grabPos;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;
        public bool IsInteracting => _moving;

        public static PondMenu Create(GameSession session)
        {
            var root = new GameObject("PondMenu");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvas.pixelPerfect = true;
            var menu = root.AddComponent<PondMenu>();
            menu._session = session;
            menu._window = DesktopWindow.Instance;
            menu._scaler = root.AddComponent<CanvasScaler>();
            menu._scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            menu._font = UiKit.PixelFont;
            menu.Build();
            var save = session.Save;
            if (save != null && save.menuScreenX >= 0f)
            {
                menu._screenPos = new Vector2(save.menuScreenX, save.menuScreenY);
                menu._placed = true;
            }
            menu._panel.gameObject.SetActive(false);
            return menu;
        }

        // ------------------------------------------------------------------ open / close

        public void ToggleAt(Vector2 screenPoint)
        {
            if (IsOpen)
            {
                Close();
                return;
            }
            OpenTab(_tab, screenPoint);
        }

        public void OpenTab(Tab tab, Vector2 screenPoint)
        {
            _tab = tab;
            _panel.gameObject.SetActive(true);
            if (!_placed)
                _screenPos = new Vector2(screenPoint.x, screenPoint.y + 30f * Zoom);
            Rebuild();
            ApplyTransform();
        }

        public void Close()
        {
            _panel.gameObject.SetActive(false);
            _moving = false;
        }

        float Zoom => _window != null ? _window.Zoom : 3f;

        public bool ContainsScreenPoint(Vector2 screen) =>
            IsOpen && RectTransformUtility.RectangleContainsScreenPoint(_panel, screen, null);

        public bool PointerOverMenu() => _window != null && ContainsScreenPoint(_window.CursorScreen);

        public void ClampIntoScreen()
        {
            if (!IsOpen)
                return;
            ApplyTransform();
        }

        void ApplyTransform()
        {
            _scale = Zoom;
            _scaler.scaleFactor = _scale;
            // Keep the whole window on screen; the anchor is the panel's bottom centre.
            var halfW = W * _scale * 0.5f;
            var x = Mathf.Clamp(_screenPos.x, halfW, Mathf.Max(halfW, Screen.width - halfW));
            var maxY = Mathf.Max(0f, Screen.height - H * _scale);
            var y = Mathf.Clamp(_screenPos.y, 0f, maxY);
            _panel.anchoredPosition = new Vector2(Mathf.Round(x / _scale), Mathf.Round(y / _scale));
        }

        // ------------------------------------------------------------------ input

        void Update()
        {
            if (!IsOpen || _window == null)
                return;

            var mouse = _window.CursorScreen;
            var pressed = _window.PrimaryPressedThisFrame;

            if (_moving)
            {
                _screenPos = _grabPos + (mouse - _grab);
                ApplyTransform();
                if (!_window.PrimaryDown)
                {
                    _moving = false;
                    _placed = true;
                    _session.Save.menuScreenX = _screenPos.x;
                    _session.Save.menuScreenY = _screenPos.y;
                    _session.MarkDirty(false);
                }
                return;
            }

            if (pressed && !ContainsScreenPoint(mouse))
            {
                // A click elsewhere on the pet or desktop closes nothing: panels stay until X.
            }

            if (Hit(_chrome, mouse, pressed) || Hit(_buttons, mouse, pressed))
                return;

            if (pressed && RectTransformUtility.RectangleContainsScreenPoint(_header, mouse, null))
            {
                _moving = true;
                _grab = mouse;
                _grabPos = _screenPos;
            }

            TickSwimmers(Time.deltaTime);

            _refreshClock += Time.deltaTime;
            if (_refreshClock > 1f)
            {
                _refreshClock = 0f;
                RefreshInfo();
            }

            RefreshGift();
        }

        bool Hit(List<Btn> list, Vector2 mouse, bool pressed)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b.Rect == null)
                    continue;
                var inside = RectTransformUtility.RectangleContainsScreenPoint(b.Rect, mouse, null);
                if (inside != b.Hovered)
                {
                    b.Hovered = inside;
                    Paint(b);
                }

                if (inside && pressed && b.Enabled && b.Click != null)
                {
                    b.Click();
                    return true;
                }
            }
            return false;
        }

        void Paint(Btn b)
        {
            if (b.Face == null)
                return;
            var c = !b.Enabled ? UiKit.Dimmer(b.Color, 0.12f) : b.Hovered ? UiKit.Lighter(b.Color, 0.08f) : b.Color;
            b.Face.sprite = UiKit.Raised(c);
        }

        /// <summary>
        /// Shop and sale messages go to the cat's speech bubble, outside the panel, so the panel
        /// itself stays a fixed layout.
        /// </summary>
        void Say(string text) => _session?.Notify(text);

        // ------------------------------------------------------------------ building blocks

        RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        Image Surface(Transform parent, string name, float x, float y, float w, float h, Color face, bool raised = true)
        {
            var rt = Box(parent, name, x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = raised ? UiKit.Raised(face) : UiKit.Sunken(face);
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 1f;
            img.raycastTarget = false;
            return img;
        }

        const int MinFontSize = 5;

        Text Label(Transform parent, string text, float x, float y, float w, float h, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, int size = 8)
        {
            var rt = Box(parent, "Label", x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            // A box only one line tall never wraps: a name too long for it (EAR BOW, SUNGLASSES)
            // steps down a font size or two until it fits on its line.
            if (h < size * 2f && !string.IsNullOrEmpty(text))
            {
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                while (t.fontSize > MinFontSize && t.preferredWidth > w - 1f)
                    t.fontSize--;
            }
            return t;
        }

        Image Icon(Transform parent, string atlasName, float x, float y, float w, float h, Color? tint = null)
        {
            var rt = Box(parent, "Icon", x, y, w, h);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Atlas.Ui(atlasName);
            img.preserveAspect = true;
            img.raycastTarget = false;
            if (tint.HasValue)
                img.color = tint.Value;
            if (img.sprite != null)
            {
                // Native size, centred: pixel art should never be resampled.
                var sw = img.sprite.rect.width;
                var sh = img.sprite.rect.height;
                var k = Mathf.Max(1f, Mathf.Floor(Mathf.Min(w / sw, h / sh)));
                if (sw > w || sh > h)
                    k = Mathf.Min(w / sw, h / sh);
                rt.sizeDelta = new Vector2(sw * k, sh * k);
                rt.anchoredPosition = new Vector2(Mathf.Round(x + (w - sw * k) / 2f), -Mathf.Round(y + (h - sh * k) / 2f));
            }
            return img;
        }

        Btn Button(List<Btn> list, Transform parent, string text, float x, float y, float w, float h, Color face,
            Action click, bool enabled = true, Color? textColor = null)
        {
            var img = Surface(parent, "Button", x, y, w, h, face);
            var b = new Btn { Rect = img.rectTransform, Face = img, Color = face, Click = click, Enabled = enabled };
            if (!string.IsNullOrEmpty(text))
                Label(img.transform, text, 0f, 0f, w, h, textColor ?? (enabled ? UiKit.TextDark : UiKit.TextMuted),
                    TextAnchor.MiddleCenter);
            Paint(b);
            list.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ chrome

        void Build()
        {
            _panel = Box(transform, "Panel", 0f, 0f, W, H);
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = new Vector2(0.5f, 0f);
            var body = Surface(_panel, "Body", 0f, 0f, W, H, UiKit.PanelFace);
            body.rectTransform.anchoredPosition = Vector2.zero;
            body.rectTransform.anchorMin = Vector2.zero;
            body.rectTransform.anchorMax = Vector2.one;
            body.rectTransform.offsetMin = Vector2.zero;
            body.rectTransform.offsetMax = Vector2.zero;

            // Drop shadow, like the reference windows.
            var shadow = Surface(_panel, "Shadow", 3f, 3f, W, H, UiKit.Outline);
            shadow.transform.SetAsFirstSibling();
            shadow.color = new Color(1f, 1f, 1f, 0.35f);

            var header = Surface(_panel, "Header", 2f, 2f, W - 4f, HeaderH, UiKit.HeaderFace);
            _header = header.rectTransform;
            Icon(header.transform, "icon_app", 3f, 1f, 14f, 11f);
            Label(header.transform, "DESKPOND.EXE", 18f, 0f, 120f, HeaderH, UiKit.TextOnHeader);
            Button(_chrome, header.transform, "x", W - 4f - 13f, 1f, 11f, 11f, UiKit.ButtonFace, Close);

            // Info strip: dried fish and bucket fill.
            var info = Surface(_panel, "Info", 2f, HeaderH + 3f, W - 4f, InfoH, UiKit.FieldFace, false);
            Icon(info.transform, "icon_dried", 3f, 2f, 14f, 9f);
            _coins = Label(info.transform, "0", 20f, 0f, 70f, InfoH, UiKit.TextDark);
            Icon(info.transform, "bucket_wood", 92f, 1f, 12f, 10f);
            _bucketCount = Label(info.transform, "0/8", 106f, 0f, 40f, InfoH, UiKit.TextDark);
            _giftIcon = Icon(info.transform, "icon_gift", 146f, 1f, 12f, 10f);
            _gift = Label(info.transform, "", 160f, 0f, 44f, InfoH, UiKit.TextDark);

            // Tabs.
            var tabs = new[] { (Tab.Bucket, "icon_shop", "CATCH"), (Tab.Shop, "icon_cog", "SHOP"),
                (Tab.Wardrobe, "icon_wardrobe", "WEAR"), (Tab.Tank, "icon_tank", "TANK"), (Tab.Dex, "icon_dex", "DEX"),
                (Tab.Settings, "icon_cog", "SET") };
            var tw = (W - 4f) / tabs.Length;
            for (var i = 0; i < tabs.Length; i++)
            {
                var (tab, _, label) = tabs[i];
                var t = tab;
                Button(_chrome, _panel, label, 2f + i * tw, HeaderH + InfoH + 5f, tw - 1f, TabH, UiKit.TabFace,
                    () => { _tab = t; _selected = -1; Rebuild(); }, true, UiKit.TextLight);
            }

            var top = HeaderH + InfoH + TabH + 7f;
            var content = Surface(_panel, "Content", 2f, top, W - 4f, H - top - 2f, UiKit.PanelFace, false);
            _content = content.rectTransform;
        }

        void RefreshTabs()
        {
            // Chrome buttons after the close button are the tabs, in enum order.
            for (var i = 1; i < _chrome.Count; i++)
            {
                var active = (int)_tab == i - 1;
                _chrome[i].Color = active ? UiKit.TabActive : UiKit.TabFace;
                Paint(_chrome[i]);
            }
        }

        /// <summary>Gift countdown in the info strip: play time left until the next gift box.</summary>
        void RefreshGift()
        {
            if (_gift == null)
                return;
            var left = _session.GiftSecondsLeft;
            var show = left >= 0f;
            _gift.enabled = show;
            _giftIcon.enabled = show;
            if (!show)
                return;
            var total = Mathf.CeilToInt(left);
            _gift.text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        void RefreshInfo()
        {
            var s = _session.Save;
            if (s == null)
                return;
            _coins.text = s.coins.ToString();
            _bucketCount.text = s.catches.Count + "/" + _session.Capacity;
            _bucketCount.color = _session.BasketFull ? UiKit.Accent : UiKit.TextDark;
            RefreshGift();
        }

        /// <summary>Repaints after anything changes. Cheap enough at click rate.</summary>
        public void Refresh()
        {
            if (IsOpen)
                Rebuild();
        }

        void Rebuild()
        {
            _buttons.Clear();
            _swimmers.Clear();
            for (var i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);
            RefreshTabs();
            RefreshInfo();

            switch (_tab)
            {
                case Tab.Bucket: BuildBucket(); break;
                case Tab.Shop: BuildShop(); break;
                case Tab.Wardrobe: BuildWardrobe(); break;
                case Tab.Tank: BuildTank(); break;
                case Tab.Dex: BuildDex(); break;
                default: BuildSettings(); break;
            }
        }

        float CW => W - 4f;
        float CH => _content.sizeDelta.y;

        // ------------------------------------------------------------------ bucket

        void BuildBucket()
        {
            var s = _session.Save;
            var c = _content;
            const int cols = 9;
            const float cell = 21f;
            for (var i = 0; i < _session.Capacity; i++)
            {
                var x = 4f + (i % cols) * (cell + 1f);
                var y = 4f + (i / cols) * (cell + 1f);
                if (i < s.catches.Count)
                {
                    var def = Catalog.Get(s.catches[i]);
                    var idx = i;
                    var face = idx == _selected ? UiKit.TabActive : UiKit.ButtonFace;
                    var b = Button(_buttons, c, null, x, y, cell, cell, face, () => { _selected = idx; Rebuild(); });
                    if (def != null)
                        Icon(b.Rect, def.Icon, 2f, 2f, cell - 4f, cell - 4f);
                }
                else
                {
                    Surface(c, "Empty", x, y, cell, cell, UiKit.FieldFace, false);
                }
            }

            var rows = Mathf.CeilToInt(_session.Capacity / (float)cols);
            var y0 = 6f + rows * (cell + 1f);

            if (_selected >= 0 && _selected < s.catches.Count)
            {
                var def = Catalog.Get(s.catches[_selected]);
                var sel = _selected;
                Label(c, def.Name, 4f, y0, 90f, 10f, Catalog.RarityColor(def.Rarity));
                Label(c, def.Price + " dried", 4f, y0 + 9f, 90f, 10f, UiKit.TextMuted);
                Button(_buttons, c, "SELL", 96f, y0, 40f, 14f, UiKit.SellFace, () =>
                {
                    _session.SellAt(sel);
                    _selected = -1;
                    Rebuild();
                });
                var tankFull = s.tank.Count >= ShopDefs.TankCapacity;
                Button(_buttons, c, "TO TANK", 138f, y0, 64f, 14f, UiKit.ButtonFace, () =>
                {
                    if (_session.MoveToTank(sel))
                    {
                        _selected = -1;
                        Rebuild();
                    }
                }, def.IsFish && !tankFull);
            }
            else
            {
                var value = _session.BucketValue();
                Button(_buttons, c, "SELL ALL TO GULL +" + value, 4f, y0, 130f, 14f, UiKit.SellFace, () =>
                {
                    _session.SellAll();
                    Say("Pleasure doing business!");
                    Rebuild();
                }, s.catches.Count > 0);
                Label(c, "tap a catch", 138f, y0, 70f, 14f, UiKit.TextMuted);
            }

            // The tank row.
            var ty = y0 + 22f;
            Label(c, "TANK " + s.tank.Count + "/" + ShopDefs.TankCapacity, 4f, ty, 60f, 12f, UiKit.TextDark);
            for (var i = 0; i < ShopDefs.TankCapacity; i++)
            {
                var x = 64f + i * 23f;
                if (i < s.tank.Count)
                {
                    var def = Catalog.Get(s.tank[i]);
                    var idx = i;
                    var b = Button(_buttons, c, null, x, ty - 2f, 22f, 16f, UiKit.ButtonFace, () =>
                    {
                        _session.ReleaseFromTank(idx);
                        Say("Released. Bye!");
                        Rebuild();
                    });
                    if (def != null)
                        Icon(b.Rect, def.Icon, 1f, 1f, 20f, 14f);
                }
                else
                {
                    Surface(c, "TankEmpty", x, ty - 2f, 22f, 16f, UiKit.FieldFace, false);
                }
            }

            // The gull's request for today: sell it the right things for a bonus.
            if (ty + 19f + 16f <= CH)
                QuestRow(ty + 19f);
        }

        /// <summary>One line: what the gull wants today, how many are handed over, and the bonus.</summary>
        void QuestRow(float qy)
        {
            var s = _session.Save;
            var c = _content;
            var want = Catalog.Get(s.questItem);
            if (want != null)
            {
                Surface(c, "Quest", 4f, qy, CW - 8f, 16f, s.questDone ? UiKit.SellFace : UiKit.FieldFace, false);
                Icon(c, "gull", 6f, qy + 1f, 14f, 14f);
                Icon(c, want.Icon, 22f, qy + 2f, 14f, 12f);
                Label(c, s.questDone
                        ? "TODAY'S REQUEST DONE. THANKS!"
                        : "WANTS " + s.questNeed + " " + want.Name.ToUpperInvariant() + "  " + s.questHave + "/" + s.questNeed +
                          "  +" + _session.QuestReward,
                    39f, qy, CW - 46f, 16f, s.questDone ? UiKit.TextDark : UiKit.TextMuted);
            }
        }

        // ------------------------------------------------------------------ shop

        void BuildShop()
        {
            var s = _session.Save;
            var c = _content;
            Icon(c, "gull", 4f, 4f, 20f, 18f);
            Label(c, "\"Upgrades! Fresh upgrades! Paid in dried fish.\"", 28f, 3f, CW - 32f, 20f, UiKit.TextMuted);

            var kinds = new[] { UpgradeKind.Rod, UpgradeKind.Basket, UpgradeKind.Lure };
            var cfg = _session.Config;
            var minutes = cfg != null && cfg.adaptivePacing ? cfg.minutesPerFish : 0f;
            for (var i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var level = ShopDefs.Level(s, kind);
                var cost = ShopDefs.NextCost(kind, level);
                var y = 28f + i * 22f;
                Surface(c, "Row", 4f, y, CW - 8f, 20f, UiKit.ButtonFace);
                Label(c, ShopDefs.Label(kind).ToUpperInvariant() + " LV" + level, 8f, y, 60f, 20f, UiKit.TextDark);
                Label(c, ShopDefs.Effect(kind, level, minutes), 68f, y, 80f, 20f, UiKit.TextMuted);
                if (cost < 0)
                {
                    Label(c, "MAX", CW - 50f, y, 40f, 20f, UiKit.Accent, TextAnchor.MiddleCenter);
                    continue;
                }
                Button(_buttons, c, cost.ToString(), CW - 54f, y + 3f, 46f, 14f, UiKit.GoldFace, () =>
                {
                    if (!_session.TryBuyUpgrade(kind))
                        Say("Not enough dried fish");
                    Rebuild();
                }, s.coins >= cost);
            }

            Label(c, "Golden ! bite chance " + Mathf.RoundToInt(ShopDefs.GoldenChance(s.rodLevel) * 100) + "%  -  click it fast!",
                4f, 96f, CW - 8f, 12f, UiKit.TextMuted);
            QuestRow(112f);
        }

        // ------------------------------------------------------------------ wardrobe

        void BuildWardrobe()
        {
            var s = _session.Save;
            var c = _content;
            var slots = new[] { (LookSlot.Coat, "CAT"), (LookSlot.Hat, "HAT"), (LookSlot.Boat, "BOAT"),
                (LookSlot.Bucket, "PAIL"), (LookSlot.Pond, "POND") };
            var sw = (CW - 8f) / slots.Length;
            for (var i = 0; i < slots.Length; i++)
            {
                var (slot, label) = slots[i];
                var sl = slot;
                Button(_buttons, c, label, 4f + i * sw, 3f, sw - 2f, 11f, slot == _slot ? UiKit.TabActive : UiKit.FieldFace,
                    () => { _slot = sl; Rebuild(); });
            }

            var items = Looks.All(_slot);
            var worn = Looks.Worn(s, _slot);
            var boat = Looks.Find(Looks.Boats, Looks.Worn(s, LookSlot.Boat));
            const int cols = 5;
            const float tw = 39f, th = 34f;
            for (var i = 0; i < items.Length; i++)
            {
                var def = items[i];
                var x = 4f + (i % cols) * (tw + 1f);
                var y = 17f + (i / cols) * (th + 1f);
                var owned = Looks.Owns(s, _slot, def.Id);
                var locked = def is PondDef pd && boat.Tier < pd.MinBoatTier;
                var face = def.Id == worn ? UiKit.TabActive : owned ? UiKit.ButtonFace : UiKit.FieldFace;
                var d = def;
                var b = Button(_buttons, c, null, x, y, tw, th, face, () =>
                {
                    if (_session.TryWear(_slot, d))
                        Say(d.Name);
                    else if (d.RewardOnly)
                        Say("Dex prize: " + (DexSets.For(_slot, d.Id)?.Hint ?? "finish a set"));
                    else if (d is PondDef p && boat.Tier < p.MinBoatTier)
                        Say("Needs a bigger boat");
                    else
                        Say("Need " + d.Cost + " dried fish");
                    Rebuild();
                });

                if (_slot == LookSlot.Pond && def is PondDef pond)
                {
                    var sw2 = Surface(b.Rect, "Swatch", 6f, 3f, tw - 12f, 16f, (Color)pond.Day[2], false);
                    var hi = Surface(sw2.transform, "Hi", 2f, 2f, tw - 16f, 4f, (Color)pond.Day[0]);
                    hi.sprite = UiKit.White;
                    hi.type = Image.Type.Simple;
                    hi.color = pond.Day[0];
                }
                else if (def.Icon != null)
                {
                    Icon(b.Rect, def.Icon, 2f, 2f, tw - 4f, th - 13f);
                }
                else
                {
                    Label(b.Rect, "-", 0f, 2f, tw, th - 13f, UiKit.TextMuted, TextAnchor.MiddleCenter);
                }

                var caption = locked ? "BOAT+" : owned ? Short(def.Name) : def.RewardOnly ? "DEX" : def.Cost.ToString();
                var color = locked ? UiKit.TextMuted : owned ? UiKit.TextDark : (s.coins >= def.Cost ? UiKit.Accent : UiKit.TextMuted);
                Label(b.Rect, caption, 0f, th - 11f, tw, 10f, color, TextAnchor.MiddleCenter);
                if (locked || (def.RewardOnly && !owned))
                    Icon(b.Rect, "icon_lock", tw - 11f, 2f, 9f, 10f);
            }
        }

        static string Short(string name) => name.Length > 8 ? name.Substring(0, 8) : name;

        // ------------------------------------------------------------------ tank

        // The cabinet's water, inside the sprite (top-left origin): x 9..66, y 28..81.
        const float TankX = 4f, TankY = 3f, WaterX = 10f, WaterY = 29f, WaterW = 55f, WaterH = 46f;

        class Swimmer
        {
            public RectTransform Rect;
            public float X, Y, Speed, Phase;
        }

        readonly List<Swimmer> _swimmers = new List<Swimmer>();

        void BuildTank()
        {
            var s = _session.Save;
            var c = _content;
            var cab = Icon(c, "tank_cabinet", TankX, TankY, 96f, 118f);
            cab.rectTransform.anchoredPosition = new Vector2(TankX, -TankY);

            var rng = new System.Random(s.tank.Count * 7 + 1);
            for (var i = 0; i < s.tank.Count; i++)
            {
                var def = Catalog.Get(s.tank[i]);
                if (def == null)
                    continue;
                var img = Icon(c, def.Icon, 0f, 0f, 30f, 20f);
                var sw = new Swimmer
                {
                    Rect = img.rectTransform,
                    X = (float)rng.NextDouble() * WaterW,
                    Y = (float)rng.NextDouble() * (WaterH - 14f),
                    Speed = (rng.Next(2) == 0 ? -1f : 1f) * (5f + (float)rng.NextDouble() * 6f),
                    Phase = (float)rng.NextDouble() * 6f
                };
                _swimmers.Add(sw);
            }
            TickSwimmers(0f);

            // The list beside the cabinet: tap a fish to let it go.
            var x0 = TankX + 100f;
            Label(c, "TANK " + s.tank.Count + "/" + ShopDefs.TankCapacity, x0, 3f, 100f, 11f, UiKit.TextDark);
            for (var i = 0; i < ShopDefs.TankCapacity; i++)
            {
                var y = 15f + i * 16f;
                if (i < s.tank.Count)
                {
                    var def = Catalog.Get(s.tank[i]);
                    var idx = i;
                    var b = Button(_buttons, c, null, x0, y, CW - x0 - 4f, 15f, UiKit.ButtonFace, () =>
                    {
                        _session.ReleaseFromTank(idx);
                        Say("Released. Bye!");
                        Rebuild();
                    });
                    if (def != null)
                    {
                        Icon(b.Rect, def.Icon, 2f, 1f, 20f, 13f);
                        Label(b.Rect, def.Name, 24f, 0f, CW - x0 - 30f, 15f, Catalog.RarityColor(def.Rarity));
                    }
                }
                else
                {
                    Surface(c, "Slot", x0, y, CW - x0 - 4f, 15f, UiKit.FieldFace, false);
                }
            }

            Label(c, s.tank.Count == 0 ? "Keep live fish from the BUCKET tab." : "Tap a fish to release it.",
                x0, 15f + ShopDefs.TankCapacity * 16f + 2f, CW - x0 - 4f, 20f, UiKit.TextMuted, TextAnchor.UpperLeft);
        }

        void TickSwimmers(float dt)
        {
            var t = Time.time;
            for (var i = 0; i < _swimmers.Count; i++)
            {
                var f = _swimmers[i];
                if (f.Rect == null)
                    continue;
                var w = f.Rect.sizeDelta.x;
                f.X += f.Speed * dt;
                if (f.X < 0f || f.X > WaterW - w)
                {
                    f.Speed = -f.Speed;
                    f.X = Mathf.Clamp(f.X, 0f, Mathf.Max(0f, WaterW - w));
                }
                var bob = Mathf.Round(Mathf.Sin(t * 1.4f + f.Phase) * 1.5f);
                f.Rect.anchoredPosition = new Vector2(Mathf.Round(TankX + WaterX + f.X + w * 0.5f), -Mathf.Round(TankY + WaterY + f.Y + bob));
                // The art faces left; mirror it when swimming right.
                f.Rect.localScale = new Vector3(f.Speed > 0f ? -1f : 1f, 1f, 1f);
                f.Rect.pivot = new Vector2(0.5f, 1f);
            }
            if (_swimmers.Count > 0 && _window != null)
                _window.Fast("tankfish");
        }

        // ------------------------------------------------------------------ dex

        void BuildDex()
        {
            var s = _session.Save;
            var c = _content;
            if (_dexPage == DexPage.Sets)
            {
                BuildSets();
                return;
            }
            if (_dexPage == DexPage.Mail)
            {
                BuildMail();
                return;
            }

            var all = Catalog.All;
            var found = 0;
            foreach (var d in all)
                if (s.DexCount(d.Id) > 0)
                    found++;

            const int cols = 9;
            const float cell = 21f;
            for (var i = 0; i < all.Length; i++)
            {
                var def = all[i];
                var x = 4f + (i % cols) * (cell + 1f);
                var y = 3f + (i / cols) * (cell + 1f);
                var known = s.DexCount(def.Id) > 0;
                var d = def;
                var b = Button(_buttons, c, null, x, y, cell, cell,
                    _dexSelected == def.Id ? UiKit.TabActive : known ? UiKit.ButtonFace : UiKit.FieldFace,
                    () => { _dexSelected = d.Id; Rebuild(); });
                // Unknown entries are silhouettes.
                Icon(b.Rect, def.Icon, 2f, 2f, cell - 4f, cell - 4f, known ? (Color?)null : new Color(0.15f, 0.19f, 0.42f, 0.55f));
            }

            var rows = Mathf.CeilToInt(all.Length / (float)cols);
            var y0 = 5f + rows * (cell + 1f);
            var panel = Surface(c, "Entry", 4f, y0, CW - 8f, CH - y0 - 4f, UiKit.FieldFace, false);
            var sel = Catalog.Get(_dexSelected);
            if (sel == null)
            {
                Label(panel.transform, "FOUND " + found + "/" + all.Length + "   NOTES " + s.notesFound.Count + "/" +
                                       Catalog.BottleNotes.Length, 4f, 2f, CW - 16f, 11f, UiKit.TextDark);
                Label(panel.transform, "Tap an entry, or:", 4f, 17f, 80f, 14f, UiKit.TextMuted);
                Button(_buttons, panel.transform, "SETS " + s.setsDone.Count + "/" + DexSets.All.Length, 86f, 17f, 56f, 14f,
                    UiKit.GoldFace, () => { _dexPage = DexPage.Sets; Rebuild(); });
                Button(_buttons, panel.transform, "MAIL " + s.letters.Count, 145f, 17f, 50f, 14f,
                    UiKit.ButtonFace, () => { _dexPage = DexPage.Mail; Rebuild(); });
                return;
            }

            var count = s.DexCount(sel.Id);
            if (count <= 0)
            {
                Label(panel.transform, "???", 4f, 2f, 80f, 12f, UiKit.TextDark);
                Label(panel.transform, sel.Rarity.ToString().ToUpperInvariant() + "  -  not caught yet", 4f, 14f, CW - 16f,
                    12f, UiKit.TextMuted);
                return;
            }

            // Name, then the numbers on their own line, then the blurb: nothing shares a line, so
            // a long name or a big count can never push text past the box.
            Label(panel.transform, sel.Name.ToUpperInvariant(), 4f, 1f, CW - 34f, 10f, Catalog.RarityColor(sel.Rarity));
            Button(_buttons, panel.transform, "x", CW - 22f, 2f, 11f, 10f, UiKit.ButtonFace,
                () => { _dexSelected = null; Rebuild(); });
            var best = s.DexBest(sel.Id);
            Label(panel.transform, "caught x" + count + (best > 0f ? " · best " + best.ToString("0.#") + "cm" : "") +
                                   " · worth " + sel.Price + "d", 4f, 10f, CW - 16f, 10f, UiKit.TextMuted);
            var blurb = sel.Blurb;
            if (sel.Id == "bottle" && s.notesFound.Count > 0)
                blurb = "\"" + Catalog.BottleNotes[s.notesFound[(int)(Time.time) % s.notesFound.Count]] + "\"";
            Label(panel.transform, blurb, 4f, 20f, CW - 16f, 18f, UiKit.TextDark, TextAnchor.UpperLeft);
        }

        /// <summary>The sets page: what each one needs, how far along it is, and its prize.</summary>
        void BuildSets()
        {
            var s = _session.Save;
            var c = _content;
            Label(c, "DEX SETS  -  finish one for a prize you cannot buy", 4f, 2f, CW - 44f, 11f, UiKit.TextMuted);
            Button(_buttons, c, "BACK", CW - 38f, 2f, 34f, 11f, UiKit.ButtonFace, () => { _dexPage = DexPage.Entries; Rebuild(); });

            for (var i = 0; i < DexSets.All.Length; i++)
            {
                var set = DexSets.All[i];
                DexSets.Progress(set, s, out var have, out var need);
                var done = s.setsDone.Contains(set.Id);
                var y = 16f + i * 19f;
                Surface(c, "Set", 4f, y, CW - 8f, 18f, done ? UiKit.SellFace : UiKit.ButtonFace);

                var reward = set.Reward;
                if (reward.Icon != null)
                    Icon(c, reward.Icon, 6f, y + 1f, 22f, 16f, done ? (Color?)null : new Color(0.15f, 0.19f, 0.42f, 0.55f));
                Label(c, set.Name.ToUpperInvariant(), 31f, y, 84f, 10f, UiKit.TextDark);
                Label(c, set.Hint, 31f, y + 8f, 120f, 10f, UiKit.TextMuted, TextAnchor.MiddleLeft, 6);
                Label(c, done ? "GOT " + reward.Name.ToUpperInvariant() : have + "/" + need, CW - 76f, y, 68f, 18f,
                    done ? UiKit.TextDark : UiKit.Accent, TextAnchor.MiddleRight);

                if (!done && need > 0)
                {
                    // A thin progress line along the bottom of the row.
                    var fill = Surface(c, "Fill", 31f, y + 15f, Mathf.Round(100f * have / need), 2f, UiKit.ChartPeak);
                    fill.sprite = UiKit.White;
                    fill.type = Image.Type.Simple;
                    fill.color = UiKit.ChartPeak;
                }
            }
        }

        /// <summary>The mail page: letters the cat has sent, newest first. Tap one to read it again.</summary>
        void BuildMail()
        {
            var s = _session.Save;
            var c = _content;
            Label(c, "LETTERS FROM THE CAT", 4f, 2f, CW - 44f, 11f, UiKit.TextMuted);
            Button(_buttons, c, "BACK", CW - 38f, 2f, 34f, 11f, UiKit.ButtonFace, () => { _dexPage = DexPage.Entries; Rebuild(); });

            if (s.letters.Count == 0)
            {
                Label(c, "Nothing yet. When the cat writes, a bottle floats up beside the boat. Click it.",
                    6f, 22f, CW - 12f, 30f, UiKit.TextDark, TextAnchor.UpperLeft);
                return;
            }

            const int rows = 7;
            for (var i = 0; i < rows && i < s.letters.Count; i++)
            {
                var index = s.letters.Count - 1 - i;
                var text = CatLetters.TextOf(s.letters[index]);
                var y = 16f + i * 16f;
                var b = Button(_buttons, c, null, 4f, y, CW - 8f, 15f, UiKit.ButtonFace, () => _session.ReadLetter(index));
                Icon(b.Rect, "mail_bottle", 2f, 1f, 9f, 13f);
                Label(b.Rect, text.Length > 44 ? text.Substring(0, 43) + "..." : text, 14f, 0f, CW - 26f, 15f, UiKit.TextDark,
                    TextAnchor.MiddleLeft, 6);
            }
        }

        // ------------------------------------------------------------------ settings

        void BuildSettings()
        {
            var s = _session.Save;
            var c = _content;
            var y = 3f;

            void Row(string label, string value, Action click)
            {
                Label(c, label, 6f, y, 110f, 13f, UiKit.TextDark);
                Button(_buttons, c, value, CW - 82f, y, 76f, 12f, UiKit.ButtonFace, () => { click(); Rebuild(); });
                y += 14f;
            }

            void Stepper(string label, string value, Action minus, Action plus)
            {
                Label(c, label, 6f, y, 110f, 13f, UiKit.TextDark);
                Button(_buttons, c, "-", CW - 82f, y, 14f, 12f, UiKit.ButtonFace, () => { minus(); Rebuild(); });
                Label(c, value, CW - 66f, y, 44f, 12f, UiKit.TextDark, TextAnchor.MiddleCenter);
                Button(_buttons, c, "+", CW - 20f, y, 14f, 12f, UiKit.ButtonFace, () => { plus(); Rebuild(); });
                y += 14f;
            }

            Row("Sound", s.soundOn ? "ON" : "OFF", () => _session.SetSound(!s.soundOn));
            Stepper("Volume", Mathf.RoundToInt(s.volume * 100f) + "%", () => _session.SetVolume(s.volume - 0.1f),
                () => _session.SetVolume(s.volume + 0.1f));
            Stepper("Pet size", (s.sizeStep >= 0 ? "+" : "") + s.sizeStep, () => _session.SetSizeStep(s.sizeStep - 1),
                () => _session.SetSizeStep(s.sizeStep + 1));
            Row("Pond", s.fullPond ? "FULL WIDTH" : "AROUND CAT", () => _session.SetFullPond(!s.fullPond));
            Stepper("Pond depth", s.pondDepth.ToString(), () => _session.SetPondDepth(s.pondDepth - 8),
                () => _session.SetPondDepth(s.pondDepth + 8));
            Row("Hide for fullscreen", s.hideInFullscreen ? "YES" : "NO", () => _session.SetHideFullscreen(!s.hideInFullscreen));
            Row("Ghost file names", s.recycleGhosts ? "RECYCLE BIN" : "MADE UP", () => _session.SetRecycleGhosts(!s.recycleGhosts));

            y += 2f;
            Button(_buttons, c, "ACTIVITY STATS", 6f, y, 96f, 14f, UiKit.ButtonFace, () => _session.ToggleStats());
            Button(_buttons, c, "QUIT", CW - 56f, y, 50f, 14f, UiKit.QuitFace, () => _session.Quit());
        }
    }
}
