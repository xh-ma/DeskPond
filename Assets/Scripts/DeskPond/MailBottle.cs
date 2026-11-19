using System.Collections;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// A letter from the cat: it corks a note into a bottle and tosses it onto the water beside
    /// the boat, where it bobs until clicked. Clicking it opens the letter.
    /// </summary>
    public class MailBottle : MonoBehaviour
    {
        /// <summary>World units to the stern side of the cat, beyond where a gift box parks.</summary>
        const float ParkOffset = 4.4f;

        GameSession _session;
        Transform _cat;
        SpriteRenderer _sprite;
        float _bob;
        bool _landed;

        public CatLetter Letter { get; private set; }
        public string Text { get; private set; }

        public static MailBottle Spawn(GameSession session, Transform cat, CatLetter letter, string text)
        {
            var go = new GameObject("MailBottle");
            var m = go.AddComponent<MailBottle>();
            m._session = session;
            m._cat = cat;
            m.Letter = letter;
            m.Text = text;
            m._sprite = go.AddComponent<SpriteRenderer>();
            m._sprite.sprite = Atlas.Get("mail_bottle", new Vector2(0.5f, 0.3f));
            m._sprite.sharedMaterial = Atlas.Material;
            m._sprite.sortingOrder = 21;
            m.StartCoroutine(m.Toss());
            return m;
        }

        float ParkX => (_cat != null ? _cat.position.x : transform.position.x) + ParkOffset;
        float WaterY => _session != null && _session.Pond != null ? _session.Pond.SurfaceWorldY : 0f;

        IEnumerator Toss()
        {
            var from = _cat.position + new Vector3(0.3f, 1.2f, 0f);
            const float seconds = 0.9f;
            for (var t = 0f; t < seconds; t += Time.deltaTime)
            {
                var k = t / seconds;
                var p = Vector3.Lerp(from, new Vector3(ParkX, WaterY, 0f), k);
                p.y += Mathf.Sin(k * Mathf.PI) * 1.6f;
                transform.position = Atlas.Snap(p);
                transform.rotation = Quaternion.Euler(0f, 0f, -90f * Mathf.Round(k * 4f));
                DesktopWindow.Instance?.Fast("mail");
                yield return null;
            }

            transform.rotation = Quaternion.identity;
            _landed = true;
            _session.Pond.Splash(ParkX, 6);
            Ambience.Play(Ambience.Sfx.Plop, 0.7f);
        }

        void Update()
        {
            if (!_landed)
                return;
            _bob += Time.deltaTime * 2f;
            var dip = Mathf.Sin(_bob) > 0.3f ? 1f / Atlas.Ppu : 0f;
            transform.position = Atlas.Snap(new Vector3(ParkX, WaterY + dip, 0f));
        }

        public bool ContainsWorldPoint(Vector3 world)
        {
            if (!_landed || _sprite == null)
                return false;
            var b = _sprite.bounds;
            b.Expand(new Vector3(0.5f, 0.5f, 1000f));
            return b.Contains(world);
        }

        public void Take()
        {
            if (_session != null && _session.Pond != null)
                _session.Pond.Ripple(transform.position.x);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_session != null && _session.ActiveMail == this)
                _session.ActiveMail = null;
        }
    }
}
