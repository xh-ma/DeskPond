using System.Collections;
using UnityEngine;

namespace DeskPond
{
    public class FishingRig : MonoBehaviour
    {
        const float RodOrder = 4f;

        Transform _cat;
        Transform _basket;
        Transform _rod;
        LineRenderer _line;
        Transform _bobber;
        Transform _ripple;
        Transform _fish;
        SpriteRenderer _rippleSprite;
        SpriteRenderer _fishSprite;

        float _waterY = -5.05f;
        Vector2 _rodBase = new Vector2(-0.28f, -0.38f);
        Vector2 _rodTip = new Vector2(-1.05f, 0.85f);
        float _bob;
        bool _waiting;
        Coroutine _routine;

        public static FishingRig Create(Transform cat, Transform basket, float waterY)
        {
            var rig = new GameObject("FishingRig").AddComponent<FishingRig>();
            rig.Bind(cat, basket, waterY);
            return rig;
        }

        /// <summary>Rod placement comes from GameConfig so it can be tuned without a rebuild.</summary>
        public void ApplyConfig(GameConfig config)
        {
            if (config == null)
                return;
            _rodBase = config.rodBaseOffset;
            _rodTip = config.rodTipOffset;
            LayoutStatic();
        }

        public void Bind(Transform cat, Transform basket, float waterY)
        {
            _cat = cat;
            _basket = basket;
            _waterY = waterY;

            var stick = MakeBlock(new Color(0.55f, 0.36f, 0.2f));
            _rod = MakePiece("Rod", stick, RodOrder).transform;
            _line = MakeLine();

            _bobber = MakePiece("Bobber", MakeBobber(), RodOrder + 1).transform;
            var rippleGo = MakePiece("Ripple", MakeRing(), RodOrder - 1);
            _rippleSprite = rippleGo.GetComponent<SpriteRenderer>();
            _ripple = rippleGo.transform;
            _ripple.gameObject.SetActive(false);

            var fishGo = MakePiece("Fish", MakeFish(), RodOrder + 1);
            _fishSprite = fishGo.GetComponent<SpriteRenderer>();
            _fish = fishGo.transform;
            _fish.gameObject.SetActive(false);

            LayoutStatic();
        }


        /// <summary>
        /// A LineRenderer rather than a stretched sprite, which gives width tapering for free.
        /// </summary>
        LineRenderer MakeLine()
        {
            var go = new GameObject("Line");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.sortingOrder = Mathf.RoundToInt(RodOrder);

            // A slight taper reads as line tension from rod tip down to the bobber.
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1f, 0.03f));
            lr.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.97f, 1f), 0f),
                    new GradientColorKey(new Color(0.78f, 0.86f, 1f), 1f)
                },
                alphaKeys = new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.6f, 1f) }
            };
            return lr;
        }

        void DrawLine(Vector3 a, Vector3 b)
        {
            if (_line == null)
                return;
            _line.SetPosition(0, a);
            _line.SetPosition(1, b);
        }

        /// <summary>Water splash when the fish breaks the surface, as a ParticleSystem burst.</summary>
        void Splash(Vector3 at)
        {
            var go = new GameObject("Splash");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 0.6f;
            main.loop = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 1.8f;
            main.startSize = 0.07f;
            main.gravityModifier = 1.4f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.9f, 1f), new Color(0.95f, 0.98f, 1f));
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.06f;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = Mathf.RoundToInt(RodOrder) + 2;
            ps.Play();
        }

        public void SetWaiting(bool waiting)
        {
            _waiting = waiting;
        }

        public void PlayCatch(float duration)
        {
            if (_routine != null)
                StopCoroutine(_routine);
            _fish.gameObject.SetActive(false);
            _routine = StartCoroutine(CatchRoutine(Mathf.Max(0.3f, duration)));
        }

        Vector3 RodBase =>
            (_cat != null ? _cat.position : Vector3.zero) + new Vector3(_rodBase.x, _rodBase.y, 0f);
        Vector3 RodTip => RodBase + new Vector3(_rodTip.x, _rodTip.y, 0f);
        Vector3 CastPoint => new Vector3(RodTip.x - 0.2f, _waterY - 0.22f, 0f);
        Vector3 BasketMouth =>
            (_basket != null ? _basket.position : Vector3.zero) + new Vector3(0f, 0.3f, 0f);

        void Update()
        {
            if (_routine != null || _bobber == null || _cat == null)
                return;

            LayoutStatic();
            if (!_waiting)
                return;

            _bob += Time.deltaTime * 2.2f;
            var p = _bobber.position;
            _bobber.position = new Vector3(p.x, CastPoint.y + Mathf.Sin(_bob) * 0.07f, p.z);
            DrawLine(RodTip, _bobber.position);
        }

        void LayoutStatic()
        {
            Stretch(_rod, RodBase, RodTip, 0.09f);
            _bobber.position = CastPoint;
            DrawLine(RodTip, _bobber.position);
        }

        IEnumerator CatchRoutine(float duration)
        {
            var biteTime = duration * 0.22f;
            var reelTime = duration * 0.3f;
            var dropTime = duration - biteTime - reelTime;

            var rest = CastPoint;
            var sunk = rest + new Vector3(0f, -0.32f, 0f);

            _ripple.position = new Vector3(rest.x, _waterY, 0f);
            _ripple.gameObject.SetActive(true);

            for (var t = 0f; t < biteTime; t += Time.deltaTime)
            {
                var k = t / biteTime;
                _bobber.position = Vector3.Lerp(rest, sunk, Mathf.Sin(k * Mathf.PI));
                _ripple.localScale = Vector3.one * Mathf.Lerp(0.25f, 1.3f, k);
                _rippleSprite.color = new Color(1f, 1f, 1f, 1f - k);
                Stretch(_rod, RodBase, RodTip, 0.09f);
                DrawLine(RodTip, _bobber.position);
                yield return null;
            }

            _ripple.gameObject.SetActive(false);
            Splash(new Vector3(rest.x, _waterY, 0f));

            var lift = RodTip + new Vector3(0.15f, 0.55f, 0f);
            _fish.gameObject.SetActive(true);
            _fishSprite.color = Color.white;

            for (var t = 0f; t < reelTime; t += Time.deltaTime)
            {
                var k = t / reelTime;
                _fish.position = Arc(sunk, lift, 0.5f, k);
                _fish.localScale = Vector3.one;
                _fish.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(-70f, 25f, k));
                _bobber.position = Vector3.Lerp(sunk, rest, k);
                DrawLine(RodTip, _fish.position);
                yield return null;
            }

            var target = BasketMouth;
            for (var t = 0f; t < dropTime; t += Time.deltaTime)
            {
                var k = t / dropTime;
                _fish.position = Arc(lift, target, 0.75f, k);
                _fish.rotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(25f, -110f, k));
                _fish.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, k);
                _fishSprite.color = new Color(1f, 1f, 1f, k > 0.8f ? Mathf.InverseLerp(1f, 0.8f, k) : 1f);
                _bobber.position = rest;
                DrawLine(RodTip, _bobber.position);
                yield return null;
            }

            _fish.gameObject.SetActive(false);
            _routine = null;
            LayoutStatic();
        }

        static Vector3 Arc(Vector3 a, Vector3 b, float height, float k)
        {
            var flat = Vector3.Lerp(a, b, k);
            flat.y += Mathf.Sin(k * Mathf.PI) * height;
            return flat;
        }

        static void Stretch(Transform t, Vector3 a, Vector3 b, float thickness)
        {
            var d = b - a;
            t.position = (a + b) * 0.5f;
            t.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            t.localScale = new Vector3(Mathf.Max(0.001f, d.magnitude), thickness, 1f);
        }

        GameObject MakePiece(string name, Sprite sprite, float order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = Mathf.RoundToInt(order);
            return go;
        }

        static Sprite MakeBlock(Color color)
        {
            return Paint(1, 1, (x, y) => color, 1f);
        }

        static Sprite MakeBobber()
        {
            return Paint(8, 8, (x, y) =>
            {
                var dx = x - 3.5f;
                var dy = y - 3.5f;
                if (dx * dx + dy * dy > 14f)
                    return Color.clear;
                return y >= 4 ? new Color(0.93f, 0.29f, 0.26f) : Color.white;
            }, 16f);
        }

        static Sprite MakeRing()
        {
            return Paint(24, 12, (x, y) =>
            {
                var dx = (x - 11.5f) / 11.5f;
                var dy = (y - 5.5f) / 5.5f;
                var r = dx * dx + dy * dy;
                return r < 1f && r > 0.5f ? new Color(0.85f, 0.95f, 1f, 0.9f) : Color.clear;
            }, 16f);
        }

        static Sprite MakeFish()
        {
            var body = new Color(0.99f, 0.72f, 0.35f);
            var fin = new Color(0.95f, 0.52f, 0.24f);
            return Paint(16, 10, (x, y) =>
            {
                var dx = (x - 5.5f) / 5.5f;
                var dy = (y - 4.5f) / 3.4f;
                if (dx * dx + dy * dy <= 1f)
                    return x == 3 && y == 6 ? Color.black : body;

                var tail = x - 11;
                return tail >= 0 && tail <= 4 && Mathf.Abs(y - 4.5f) <= tail * 0.9f ? fin : Color.clear;
            }, 16f);
        }

        static Sprite Paint(int w, int h, System.Func<int, int, Color> shade, float ppu)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[y * w + x] = shade(x, y);
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), ppu);
        }
    }
}
