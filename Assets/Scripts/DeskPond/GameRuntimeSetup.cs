using UnityEngine;

namespace DeskPond
{
    public static class GameRuntimeSetup
    {
        const string ConfigPath = "Assets/Data/GameConfig.asset";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
            if (Object.FindFirstObjectByType<GameSession>() != null)
            {
                EnsurePresenters();
                return;
            }

            BuildMissingWorld();
            var session = new GameObject("GameSession").AddComponent<GameSession>();
            session.Bind(
                LoadConfig(),
                Object.FindFirstObjectByType<CatPresenter>(),
                Object.FindFirstObjectByType<BasketPresenter>(),
                Camera.main);
        }

        public static GameConfig LoadConfig()
        {
            var loaded = Resources.Load<GameConfig>("GameConfig");
            if (loaded != null)
                return loaded;

#if UNITY_EDITOR
            loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigPath);
            if (loaded != null)
                return loaded;
#endif
            return ScriptableObject.CreateInstance<GameConfig>();
        }

        static void EnsurePresenters()
        {
            var catGo = GameObject.Find("Cat");
            if (catGo != null && catGo.GetComponent<CatPresenter>() == null)
            {
                var cat = catGo.AddComponent<CatPresenter>();
                cat.Bind(catGo.GetComponent<SpriteRenderer>());
            }

            if (Object.FindFirstObjectByType<BasketPresenter>() == null)
                CreateBasket();
        }

        static void BuildMissingWorld()
        {
            EnsurePresenters();
        }

        public static BasketPresenter CreateBasket()
        {
            var go = GameObject.Find("Basket");
            if (go == null)
            {
                go = new GameObject("Basket");
                go.transform.position = new Vector3(2.3f, -4.5f, 0f);
            }

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null)
                sr = go.AddComponent<SpriteRenderer>();
            if (sr.sprite == null)
                sr.sprite = LoadOrMakeBasketSprite();
            sr.sortingOrder = 2;

            var col = go.GetComponent<BoxCollider2D>();
            if (col == null)
                col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;

            var cue = go.transform.Find("FullCue");
            if (cue == null)
            {
                var cueGo = new GameObject("FullCue");
                cueGo.transform.SetParent(go.transform, false);
                cueGo.transform.localPosition = new Vector3(0.15f, 1.05f, 0f);
                var cueSr = cueGo.AddComponent<SpriteRenderer>();
                cueSr.sprite = MakeBlockSprite(new Color(1f, 0.92f, 0.25f, 1f), 6, 8);
                cueSr.sortingOrder = 6;
                cueGo.SetActive(false);
                cue = cueGo.transform;
            }

            var presenter = go.GetComponent<BasketPresenter>();
            if (presenter == null)
                presenter = go.AddComponent<BasketPresenter>();
            presenter.Bind(sr, cue);
            return presenter;
        }

        static Sprite LoadOrMakeBasketSprite()
        {
#if UNITY_EDITOR
            var sprite = UnityEditor.AssetDatabase
                .LoadAssetAtPath<Sprite>("Assets/Art/Sprites/basket.png");
            if (sprite != null)
                return sprite;
#endif
            return MakeBlockSprite(new Color(0.62f, 0.38f, 0.18f, 1f), 20, 16);
        }

        public static Sprite MakeBlockSprite(Color color, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 16f);
        }
    }
}
