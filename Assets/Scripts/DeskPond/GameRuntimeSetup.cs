using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Builds the game at runtime. The scene only needs a camera and the GameSession object that
    /// references the config; everything visible is created in code from the atlas.
    /// </summary>
    public static class GameRuntimeSetup
    {
        const string ConfigPath = "Assets/Data/GameConfig.asset";

        /// <summary>Scene objects from the old placeholder build, removed if a scene still has them.</summary>
        static readonly string[] Legacy = { "Cat", "Basket", "Pond", "TaskbarRim", "Global Light 2D", "PetRoot" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
            foreach (var name in Legacy)
            {
                var go = GameObject.Find(name);
                if (go != null && go.GetComponent<GameSession>() == null)
                    Object.Destroy(go);
            }

            if (Camera.main == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                camGo.transform.position = new Vector3(0f, 0f, -10f);
            }

            if (Object.FindFirstObjectByType<GameSession>() == null)
            {
                var session = new GameObject("GameSession").AddComponent<GameSession>();
                session.Bind(LoadConfig(), null, null, Camera.main);
            }
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
    }
}
