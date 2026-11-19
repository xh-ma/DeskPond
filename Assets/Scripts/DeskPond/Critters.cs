using System;
using System.Collections;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// The birds. A crow that raids a full bucket that has been left too long, and the seagull
    /// merchant who flies in to trade while the shop is open.
    /// </summary>
    public class Critters : MonoBehaviour
    {
        DesktopWindow _window;
        Angler _angler;
        SpriteRenderer _crow, _crowLoot, _gull;
        Atlas.Anim _crowFly, _gullFly, _gullStand;
        Coroutine _crowRoutine, _gullRoutine;
        bool _shooed;
        bool _gullWanted;
        bool _gullLanded;
        float _clock;

        public bool CrowActive => _crowRoutine != null;

        public static Critters Create(DesktopWindow window, Angler angler)
        {
            var go = new GameObject("Critters");
            var c = go.AddComponent<Critters>();
            c._window = window;
            c._angler = angler;
            c._crow = Atlas.MakeRenderer("Crow", go.transform, 30);
            c._crowLoot = Atlas.MakeRenderer("CrowLoot", c._crow.transform, 29);
            c._gull = Atlas.MakeRenderer("Gull", go.transform, 19);
            c._crowFly = Atlas.GetAnim("crow_fly", new Vector2(0.5f, 0.5f));
            c._gullFly = Atlas.GetAnim("gull_fly", new Vector2(0.5f, 0.5f));
            c._gullStand = Atlas.GetAnim("gull");
            c._crow.enabled = false;
            c._crowLoot.enabled = false;
            c._gull.enabled = false;
            return c;
        }

        void Update()
        {
            _clock += Time.deltaTime;
            if (_crow.enabled)
                _crow.sprite = _crowFly.Frames[_crowFly.FrameAt(_clock)];
            if (_gull.enabled)
            {
                var anim = _gullLanded ? _gullStand : _gullFly;
                _gull.sprite = anim.Frames[anim.FrameAt(_clock)];
            }

            if (_crowRoutine != null || (_gullRoutine != null && !_gullLanded))
                _window.Fast("birds");
        }

        public bool CrowContains(Vector3 world)
        {
            if (!_crow.enabled)
                return false;
            var b = _crow.bounds;
            b.Expand(new Vector3(0.6f, 0.6f, 1000f));
            return b.Contains(world);
        }

        /// <summary>Clicked: the crow flaps off empty-handed.</summary>
        public void Shoo() => _shooed = true;

        // ------------------------------------------------------------------ crow

        /// <summary>
        /// Sends the crow at the bucket. <paramref name="steal"/> is called on arrival and returns
        /// the icon of what it took, or null if the bucket was emptied in the meantime.
        /// </summary>
        public void SendCrow(Func<string> steal, Action<bool> finished)
        {
            if (_crowRoutine != null)
                return;
            _crowRoutine = StartCoroutine(CrowRoutine(steal, finished));
        }

        IEnumerator CrowRoutine(Func<string> steal, Action<bool> finished)
        {
            _shooed = false;
            var bucket = _angler.BucketTransform.position + new Vector3(0f, 0.9f, 0f);
            var w = _window.WorldWidth;
            var fromRight = bucket.x < w * 0.5f;
            var start = new Vector3(fromRight ? w + 2f : -2f, bucket.y + 4f, 0f);
            _crow.flipX = !fromRight;
            _crow.enabled = true;
            _crowLoot.enabled = false;
            Ambience.Play(Ambience.Sfx.Caw, 0.8f);

            const float approach = 4f;
            for (var t = 0f; t < approach; t += Time.deltaTime)
            {
                if (_shooed)
                    break;
                var k = t / approach;
                bucket = _angler.BucketTransform.position + new Vector3(0f, 0.9f, 0f);
                var p = Vector3.Lerp(start, bucket, k) + new Vector3(0f, Mathf.Sin(k * Mathf.PI) * 1.2f, 0f);
                _crow.transform.position = Atlas.Snap(p);
                yield return null;
            }

            var stolen = false;
            if (!_shooed)
            {
                var icon = steal?.Invoke();
                if (icon != null)
                {
                    stolen = true;
                    _crowLoot.sprite = Atlas.Get(icon, new Vector2(0.5f, 1f));
                    _crowLoot.enabled = true;
                    _crowLoot.transform.localPosition = new Vector3(0f, -0.15f, 0f);
                    _crowLoot.transform.localScale = Vector3.one * 0.7f;
                    Ambience.Play(Ambience.Sfx.Caw);
                }
            }

            // Away, up and out.
            var from = _crow.transform.position;
            var away = new Vector3(_shooed ? (fromRight ? w + 3f : -3f) : (fromRight ? -3f : w + 3f), from.y + 6f, 0f);
            _crow.flipX = away.x > from.x ? true : false;
            for (var t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                var k = t / 2.5f;
                _crow.transform.position = Atlas.Snap(Vector3.Lerp(from, away, k * k));
                yield return null;
            }

            _crow.enabled = false;
            _crowLoot.enabled = false;
            _crowRoutine = null;
            finished?.Invoke(stolen);
        }

        // ------------------------------------------------------------------ gull

        /// <summary>The merchant lands beside the bucket while the shop is open.</summary>
        public void SetGullVisiting(bool visiting)
        {
            if (visiting == _gullWanted)
                return;
            _gullWanted = visiting;
            if (_gullRoutine != null)
                StopCoroutine(_gullRoutine);
            _gullRoutine = StartCoroutine(visiting ? GullIn() : GullOut());
        }

        Vector3 GullPerch()
        {
            var b = _angler.BucketTransform.position;
            return Atlas.Snap(b + new Vector3(15f / Atlas.Ppu, -1f / Atlas.Ppu, 0f));
        }

        IEnumerator GullIn()
        {
            _gullLanded = false;
            _gull.enabled = true;
            _gull.flipX = false;
            var w = _window.WorldWidth;
            var start = new Vector3(Mathf.Min(w + 2f, GullPerch().x + 12f), GullPerch().y + 5f, 0f);
            for (var t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                var k = t / 1.6f;
                _gull.transform.position = Atlas.Snap(Vector3.Lerp(start, GullPerch() + new Vector3(0f, 0.35f, 0f), 1f - (1f - k) * (1f - k)));
                yield return null;
            }

            _gullLanded = true;
            while (_gullWanted)
            {
                _gull.transform.position = GullPerch();
                yield return null;
            }
        }

        IEnumerator GullOut()
        {
            if (!_gull.enabled)
                yield break;
            _gullLanded = false;
            var from = _gull.transform.position;
            var away = from + new Vector3(10f, 6f, 0f);
            for (var t = 0f; t < 1.4f; t += Time.deltaTime)
            {
                _gull.transform.position = Atlas.Snap(Vector3.Lerp(from, away, t / 1.4f));
                yield return null;
            }

            _gull.enabled = false;
            _gullRoutine = null;
        }
    }
}
