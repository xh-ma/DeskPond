using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// Development aid: DeskPond.exe -snap=C:\some\folder renders a set of PNGs of the pet in
    /// different looks, depths and times of day, then quits. It uses its own save file and no
    /// activity log, so a real player's data is never touched. Inert without the flag.
    ///
    /// The camera is rendered into a texture rather than screen-captured, so the shots show the
    /// game only, never whatever else is on the desktop.
    /// </summary>
    public class SnapshotMode : MonoBehaviour
    {
        static string _dir;

        public static bool Active
        {
            get
            {
                if (_dir == null)
                {
                    _dir = "";
                    foreach (var a in Environment.GetCommandLineArgs())
                        if (a.StartsWith("-snap="))
                            _dir = a.Substring(6).Trim('"');
                }
                return _dir.Length > 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Active)
                return;
            Directory.CreateDirectory(_dir);
            if (File.Exists(SaveSystem.FilePath))
                File.Delete(SaveSystem.FilePath);
            new GameObject("SnapshotMode").AddComponent<SnapshotMode>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSeconds(3f);
            var session = FindFirstObjectByType<GameSession>();
            session.Save.coins = 2500;
            session.Save.welcomeGiftGiven = true;

            yield return Shot(session, "1_tub_noon_shallow", "ginger", "straw", "tub", "pond", 7, 12f);
            yield return Shot(session, "2_sail_sea_deep_noon", "tuxedo", "captain", "sail", "sea", 64, 12f);
            yield return Shot(session, "3_sail_sea_deep_night", "tuxedo", "captain", "sail", "sea", 64, 22.5f);
            yield return Shot(session, "4_raft_kelp_dawn", "calico", "bow", "raft", "kelp", 46, 7f);
            yield return Shot(session, "5_raft_pond_dusk", "snow", "flower", "raft", "pond", 36, 18.7f);
            yield return Shot(session, "6_lava", "black", "sun", "sail", "lava", 40, 15f);
            session.DebugLook("ginger", "straw", "tub", "pond", 7, 12f, true);
            session.Notify("Sold 5 fish  +120");
            yield return new WaitForSeconds(0.6f);
            yield return new WaitForEndOfFrame();
            Capture(session, "6b_sale_message", true);
            yield return Shot(session, "7_menu_wardrobe", "ginger", "straw", "tub", "pond", 7, 12f, menu: true);
            session.Menu?.ShowSlot(LookSlot.Hat);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            Capture(session, "7b_menu_hats", true);
            yield return Shot(session, "8_menu_tank", "ginger", "straw", "tub", "pond", 7, 12f, menu: true, tab: PondMenu.Tab.Tank);

            // The activity panel, to check it fits inside the window.
            session.DebugLook("ginger", "straw", "tub", "pond", 7, 12f, false);
            session.ToggleStats();
            yield return new WaitForSeconds(2f);
            yield return new WaitForEndOfFrame();
            Capture(session, "9_stats", true);
            session.ToggleStats();

            // Weather, the dex prizes, a letter in a bottle, petting.
            session.Save.letters.Add(CatLetters.Pack("hello", CatLetters.Get("hello").Text));
            session.Save.letters.Add(CatLetters.Pack("water", CatLetters.Get("water").Text));
            session.DebugLook("gold", "fish", "paper", "pond", 22, 12f, false);
            session.DebugScene(WeatherKind.Rain, 0, true);
            yield return new WaitForSeconds(3.5f);
            session.DebugPet();
            yield return new WaitForSeconds(0.45f);
            yield return new WaitForEndOfFrame();
            Capture(session, "10_rain_mail_pet", true);
            session.DebugOpenMail();
            yield return new WaitForSeconds(1f);
            yield return new WaitForEndOfFrame();
            Capture(session, "11_letter", true);
            session.DebugCloseLetter();

            // Deep-water visitors, under snow and fog.
            session.DebugLook("storm", "can", "sail", "sea", 64, 12f, false);
            session.DebugScene(WeatherKind.Snow, 1, false);
            yield return new WaitForSeconds(9f);
            yield return new WaitForEndOfFrame();
            Capture(session, "12_snow_whale", false);
            session.DebugLook("storm", "can", "raft", "kelp", 56, 20.5f, false);
            session.DebugScene(WeatherKind.Fog, 2, false);
            yield return new WaitForSeconds(11f);
            yield return new WaitForEndOfFrame();
            Capture(session, "13_fog_jellyfish", false);

            // The new panel pages.
            session.DebugScene(WeatherKind.Clear, 0, false);
            session.Save.Record("minnow", 6f);
            session.Save.Record("boot", 0f);
            session.Save.Record("loach", 14f);
            yield return Shot(session, "14_dex", "ginger", "straw", "tub", "pond", 7, 12f, menu: true, tab: PondMenu.Tab.Dex);
            session.Menu?.ShowDexPage(1);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            Capture(session, "15_dex_sets", true);
            session.Menu?.ShowDexPage(2);
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            Capture(session, "16_dex_mail", true);
            session.Menu?.ShowDexPage(0);
            yield return Shot(session, "17_shop_request", "ginger", "straw", "tub", "pond", 7, 12f, menu: true, tab: PondMenu.Tab.Shop);
            yield return Shot(session, "18_catch_request", "ginger", "straw", "tub", "pond", 7, 12f, menu: true, tab: PondMenu.Tab.Bucket);

            Debug.Log("DeskPond snapshots written to " + _dir);
            Application.Quit();
        }

        IEnumerator Shot(GameSession s, string name, string coat, string hat, string boat, string pond, int depth,
            float hour, bool menu = false, PondMenu.Tab tab = PondMenu.Tab.Wardrobe)
        {
            s.DebugLook(coat, hat, boat, pond, depth, hour, menu, tab);
            yield return new WaitForSeconds(menu ? 2f : 3.5f);
            yield return new WaitForEndOfFrame();
            Capture(s, name, menu);
        }

        static void Capture(GameSession s, string name, bool withUi)
        {
            var cam = Camera.main;
            var w = Screen.width;
            var h = Screen.height;
            Debug.Log($"DeskPond snap {name}: screen={w}x{h} cam={cam.pixelWidth}x{cam.pixelHeight} " +
                      $"ortho={cam.orthographicSize:0.000} pos={cam.transform.position} zoom={DesktopWindow.Instance.Zoom}");
            var canvases = withUi ? FindObjectsByType<Canvas>(FindObjectsSortMode.None) : new Canvas[0];
            foreach (var c in canvases)
            {
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = cam;
                c.planeDistance = 1f;
            }

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;

            // Crop round the pet, and keep the panel in frame when it is open.
            var catX = Mathf.RoundToInt(cam.WorldToScreenPoint(s.Pet.CatTransform.position).x);
            var cw = Mathf.Min(w, 1500);
            var cx = Mathf.Clamp(catX - cw / 2, 0, w - cw);
            var ch = Mathf.Min(h, withUi ? 900 : 600);
            var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(cx, 0, cw, ch), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            foreach (var c in canvases)
                c.renderMode = RenderMode.ScreenSpaceOverlay;

            // Over a plain desktop colour, since the real background is transparent.
            var px = tex.GetPixels32();
            var bg = new Color32(0x3a, 0x40, 0x5a, 255);
            for (var i = 0; i < px.Length; i++)
                px[i] = Color32.Lerp(bg, new Color32(px[i].r, px[i].g, px[i].b, 255), px[i].a / 255f);
            tex.SetPixels32(px);
            File.WriteAllBytes(Path.Combine(_dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
