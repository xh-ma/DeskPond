using System.Collections;
using UnityEngine;

namespace DeskPond
{
    /// <summary>
    /// The reward for a long idle stretch: a gift box that drifts in from one screen edge, parks
    /// itself in front of the cat, and waits to be clicked. Opening it unlocks a random skin and
    /// pops a bubble saying what was won.
    ///
    /// Spawned from GameSession.TickIdle and GameSession.WelcomeGift; timings live in GameConfig.
    /// </summary>
    public class DriftingGift : MonoBehaviour
    {
        /// <summary>Drawn larger than 1:1 so a 14px sprite still reads on a busy desktop.</summary>
        const float BoxScale = 1f;

        /// <summary>How far to one side of the cat the box parks, in world units.</summary>
        const float ParkOffset = 2.6f;

        SpriteRenderer _sprite;
        Atlas.Anim _anim;
        float _frameClock;
        BoxCollider2D _collider;
        Transform _target;
        GameSession _session;
        AnimationCurve _drift;
        float _duration;
        float _floatY;
        float _lift;
        float _parkOffset;
        float _bob;
        bool _waiting;
        bool _opening;

        /// <summary>True once the box has parked and is waiting for a click.</summary>
        public bool AwaitingClick => _waiting && !_opening;

        public static DriftingGift Spawn(GameSession session, Transform cat, Camera cam,
            float waterY, AnimationCurve drift, float duration)
        {
            if (session == null || cat == null || cam == null)
                return null;

            var go = new GameObject("DriftingGift");
            var gift = go.AddComponent<DriftingGift>();
            gift._session = session;
            gift._target = cat;
            // A curve that was never authored in the asset deserialises with zero keys and would
            // evaluate to 0 forever, leaving the box stuck at the screen edge.
            gift._drift = drift != null && drift.length > 0
                ? drift
                : AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            gift._duration = Mathf.Max(2f, duration);

            var sr = go.AddComponent<SpriteRenderer>();
            gift._anim = Atlas.GetAnim("gift", new Vector2(0.5f, 0.5f));
            sr.sprite = gift._anim.Frames[0];
            sr.sortingOrder = 21;
            sr.sharedMaterial = Atlas.Material;
            gift._sprite = sr;
            go.transform.localScale = Vector3.one * BoxScale;

            // Float the box ON the water rather than centred on it: a sprite centred on the
            // waterline sits half-buried in the taskbar and is nearly invisible.
            var halfHeight = sr.sprite.bounds.extents.y * BoxScale;
            gift._lift = halfHeight * 0.72f;
            gift._floatY = waterY + gift._lift;

            // Clickable once parked; sized generously so it is easy to hit.
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = sr.sprite.bounds.size * 1.25f;
            gift._collider = col;

            // Enter from whichever edge is further from the cat, so the drift is always visible,
            // and park on that same side so the box never lands on top of the cat.
            var halfWidth = cam.orthographicSize * cam.aspect;
            var camX = cam.transform.position.x;
            var leftEdge = camX - halfWidth - 1f;
            var rightEdge = camX + halfWidth + 1f;
            var fromLeft = Mathf.Abs(cat.position.x - leftEdge) > Mathf.Abs(cat.position.x - rightEdge);
            var startX = fromLeft ? leftEdge : rightEdge;
            // Always park beyond the bucket, on the stern side, clear of the rod and line.
            gift._parkOffset = ParkOffset;

            go.transform.position = new Vector3(startX, gift._floatY, 0f);
            session.ActiveGift = gift;
            gift.StartCoroutine(gift.DriftIn());
            Debug.Log($"DeskPond: gift box drifting in from the {(fromLeft ? "left" : "right")}.");
            return gift;
        }

        /// <summary>Also drives click-through, so the box is clickable through the overlay.</summary>
        public bool ContainsWorldPoint(Vector3 world)
        {
            return AwaitingClick && _collider != null && _collider.OverlapPoint(world);
        }

        /// <summary>Called by GameSession when the parked box is clicked.</summary>
        public void OpenNow()
        {
            if (!AwaitingClick)
                return;
            _opening = true;
            StartCoroutine(Open());
        }

        IEnumerator DriftIn()
        {
            var start = transform.position;

            for (var t = 0f; t < _duration; t += Time.deltaTime)
            {
                var k = Mathf.Clamp01(t / _duration);
                var eased = _drift.Evaluate(k);
                var end = ParkX();

                transform.position = Atlas.Snap(new Vector3(Mathf.Lerp(start.x, end, eased), Bob(), 0f));
                Animate();
                yield return null;
            }

            _waiting = true;
            Debug.Log("DeskPond: gift box parked, click it to open.");

            // Park and idle-bob until clicked, with a gentle pulse hinting it is interactive.
            while (!_opening)
            {
                transform.position = Atlas.Snap(new Vector3(ParkX(), Bob(), 0f));
                transform.rotation = Quaternion.identity;
                Animate();
                yield return null;
            }
        }

        void Animate()
        {
            _frameClock += Time.deltaTime;
            if (_anim != null && _anim.Frames.Length > 0)
                _sprite.sprite = _anim.Frames[_anim.FrameAt(_frameClock)];
        }

        /// <summary>The cat can be dragged at any time, so the park spot is recomputed every frame.</summary>
        float ParkX() => (_target != null ? _target.position.x : transform.position.x) + _parkOffset;

        float Bob()
        {
            _bob += Time.deltaTime * 2.4f;
            // Ride the surface even if the pond is made deeper while the box is out.
            if (_session != null && _session.Pond != null)
                _floatY = _session.Pond.SurfaceWorldY + _lift;
            return _floatY + Mathf.Sin(_bob) * 0.1f;
        }

        IEnumerator Open()
        {
            Sparkle(transform.position);

            var reward = _session != null ? _session.GrantGiftSkin() : null;
            if (!string.IsNullOrEmpty(reward))
            {
                Debug.Log("DeskPond: gift box opened, unlocked " + reward);
                if (_session != null)
                    _session.ShowGiftBubble(reward);
            }

            var t = 0f;
            var baseScale = Vector3.one * BoxScale;
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                var k = t / 0.45f;
                transform.localScale = baseScale * (1f + Mathf.Sin(k * Mathf.PI) * 0.5f);
                var c = _sprite.color;
                c.a = 1f - k;
                _sprite.color = c;
                yield return null;
            }

            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_session != null && _session.ActiveGift == this)
                _session.ActiveGift = null;
        }

        /// <summary>A short ParticleSystem burst, configured in code rather than in a prefab.</summary>
        void Sparkle(Vector3 at)
        {
            var go = new GameObject("GiftSparkle");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = 0.7f;
            main.loop = false;
            main.startLifetime = 0.6f;
            main.startSpeed = 2.2f;
            main.startSize = 0.12f;
            main.gravityModifier = 0.6f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.9f, 0.45f), new Color(1f, 0.75f, 0.85f));
            main.stopAction = ParticleSystemStopAction.Destroy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.15f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));
            renderer.sortingOrder = 22;

            ps.Play();
        }
    }
}
