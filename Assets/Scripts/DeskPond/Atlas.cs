using System.Collections.Generic;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Every sprite in the game comes from one texture, Resources/Art/atlas.png, packed by
    /// art/export.py from the .px.js sources. atlas.txt names each rect; animations are strips of
    /// equal frames with a duration per frame.
    ///
    /// One texture means one upload and one batch, which is most of what keeps a pet that runs
    /// all day cheap to draw.
    /// </summary>
    public static class Atlas
    {
        /// <summary>Art pixels per world unit. One world unit is 16 art pixels everywhere.</summary>
        public const float Ppu = 16f;

        public class Anim
        {
            public Sprite[] Frames;
            public float[] Seconds;
            public float Total;

            /// <summary>Frame index at a time into a looping animation.</summary>
            public int FrameAt(float t)
            {
                if (Frames.Length <= 1 || Total <= 0f)
                    return 0;
                t %= Total;
                for (var i = 0; i < Seconds.Length; i++)
                {
                    if (t < Seconds[i])
                        return i;
                    t -= Seconds[i];
                }
                return Frames.Length - 1;
            }
        }

        struct Entry
        {
            public RectInt Rect;
            public float[] Durations;
        }

        static Texture2D _texture;
        static Dictionary<string, Entry> _entries;
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, Anim> Anims = new Dictionary<string, Anim>();
        static readonly Dictionary<string, Sprite> UiSprites = new Dictionary<string, Sprite>();
        static Material _material;

        public static Texture2D Texture
        {
            get
            {
                Load();
                return _texture;
            }
        }

        /// <summary>
        /// The one material every renderer shares. The project uses the built-in pipeline now, and
        /// Sprites/Default is the cheapest shader that does what pixel art needs.
        /// </summary>
        public static Material Material
        {
            get
            {
                if (_material == null)
                    _material = new Material(Shader.Find("Sprites/Default")) { name = "DeskPondSprites" };
                return _material;
            }
        }

        public static bool Has(string name)
        {
            Load();
            return _entries.ContainsKey(name);
        }

        /// <summary>A sprite (or an animation's first frame), pivoted at the bottom centre.</summary>
        public static Sprite Get(string name) => Get(name, new Vector2(0.5f, 0f));

        public static Sprite Get(string name, Vector2 pivot)
        {
            Load();
            var key = name + "@" + pivot.x + "," + pivot.y;
            if (Sprites.TryGetValue(key, out var cached))
                return cached;

            if (!_entries.TryGetValue(name, out var e))
            {
                Debug.LogWarning("DeskPond atlas: no sprite named " + name);
                return null;
            }

            var r = e.Rect;
            var sprite = Make(r.x, r.y, r.width, r.height, pivot, name);
            Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>A sprite for UI Images: centred pivot, cached separately.</summary>
        public static Sprite Ui(string name)
        {
            if (UiSprites.TryGetValue(name, out var cached))
                return cached;
            var s = Get(name, new Vector2(0.5f, 0.5f));
            UiSprites[name] = s;
            return s;
        }

        public static Anim GetAnim(string name) => GetAnim(name, new Vector2(0.5f, 0f));

        public static Anim GetAnim(string name, Vector2 pivot)
        {
            Load();
            var key = name + "@" + pivot.x + "," + pivot.y;
            if (Anims.TryGetValue(key, out var cached))
                return cached;

            if (!_entries.TryGetValue(name, out var e))
            {
                Debug.LogWarning("DeskPond atlas: no animation named " + name);
                return null;
            }

            var count = e.Durations != null ? e.Durations.Length : 1;
            var anim = new Anim { Frames = new Sprite[count], Seconds = new float[count] };
            for (var i = 0; i < count; i++)
            {
                anim.Frames[i] = Make(e.Rect.x + i * e.Rect.width, e.Rect.y, e.Rect.width,
                    e.Rect.height, pivot, name + "_" + i);
                anim.Seconds[i] = e.Durations != null ? e.Durations[i] / 1000f : 1f;
                anim.Total += anim.Seconds[i];
            }

            Anims[key] = anim;
            return anim;
        }

        static Sprite Make(int x, int yTop, int w, int h, Vector2 pivot, string name)
        {
            // atlas.txt counts from the top-left; Unity textures count from the bottom-left.
            var y = _texture.height - yTop - h;
            var s = Sprite.Create(_texture, new Rect(x, y, w, h), pivot, Ppu, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        static void Load()
        {
            if (_entries != null)
                return;

            _entries = new Dictionary<string, Entry>();
            _texture = Resources.Load<Texture2D>("Art/atlas");
            var text = Resources.Load<TextAsset>("Art/atlas");
            if (_texture == null || text == null)
            {
                Debug.LogError("DeskPond atlas: Resources/Art/atlas.png or atlas.txt is missing. " +
                               "Run python art/export.py.");
                _texture = Texture2D.whiteTexture;
                return;
            }

            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;

            foreach (var raw in text.text.Split('\n'))
            {
                var parts = raw.Trim().Split(' ');
                if (parts.Length < 5)
                    continue;
                var e = new Entry
                {
                    Rect = new RectInt(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]),
                        int.Parse(parts[4]))
                };
                if (parts.Length > 5)
                {
                    var ds = parts[5].Split(',');
                    e.Durations = new float[ds.Length];
                    for (var i = 0; i < ds.Length; i++)
                        e.Durations[i] = float.Parse(ds[i], System.Globalization.CultureInfo.InvariantCulture);
                }
                _entries[parts[0]] = e;
            }

            // The text is only needed once; let it go.
            Resources.UnloadAsset(text);
        }

        /// <summary>A SpriteRenderer on a new child, already on the shared material.</summary>
        public static SpriteRenderer MakeRenderer(string name, Transform parent, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = Material;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Rounds a world position to the art-pixel grid so nothing ever sits half a pixel off.</summary>
        public static Vector3 Snap(Vector3 p) =>
            new Vector3(Mathf.Round(p.x * Ppu) / Ppu, Mathf.Round(p.y * Ppu) / Ppu, p.z);
    }
}
