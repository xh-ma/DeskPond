using UnityEngine;

namespace DeskPond
{
    public enum CatState
    {
        Idle,
        Waiting,
        Catch
    }

    /// <summary>
    /// The cat's visuals. Owns no gameplay state; it is told which state to show.
    ///
    /// ANIMATION
    /// Optional sprite sheets are loaded from Resources at startup:
    ///     Assets/Resources/CatFrames/cat_idle.png
    ///     Assets/Resources/CatFrames/cat_wait.png
    ///     Assets/Resources/CatFrames/cat_catch.png
    /// Each is a horizontal strip of square frames. A 4-frame 24x24 animation is a 96x24 PNG.
    /// Frames are sliced at runtime, so no Sprite Editor work is needed. Any state without a sheet
    /// falls back to the single static sprite assigned in the scene, so partial art still runs.
    /// </summary>
    public class CatPresenter : MonoBehaviour
    {
        SpriteRenderer _sprite;
        Sprite _sourceSprite;   // untouched art from the scene
        Sprite _staticSprite;   // _sourceSprite with the current skin applied
        Vector3 _baseScale;
        SkinDef _skin = ShopDefs.CatSkins[0];
        float _bob;

        Sprite[] _idleFrames;
        Sprite[] _waitFrames;
        Sprite[] _catchFrames;
        float _frameRate = 6f;
        bool _pingPong = true;
        float _frameClock;

        float _catchSeconds = 1.6f;

        // Where the cat rests between hops. Held as a field because a hop that is interrupted
        // leaves the transform part way up its arc, and reading it back then would treat that
        // raised spot as ground level.
        Vector3 _restLocalPos;
        Quaternion _restLocalRot = Quaternion.identity;
        bool _restCaptured;
        float _catchHop = 0.16f;
        float _catchLean = 7f;
        Coroutine _catchRoutine;

        public CatState State { get; private set; } = CatState.Idle;

        public void Bind(SpriteRenderer sprite)
        {
            _sprite = sprite != null ? sprite : GetComponent<SpriteRenderer>();
            _baseScale = transform.localScale;
            if (_sprite != null)
                _sourceSprite = _sprite.sprite;

            LoadSheets();
            ApplyIdle();
        }

        public void ApplyConfig(GameConfig config)
        {
            if (config == null)
                return;
            _frameRate = Mathf.Max(1f, config.catFrameRate);
            _pingPong = config.catPingPong;
            _catchSeconds = Mathf.Max(0.05f, config.catchAnimDuration);
            _catchHop = config.catchHopHeight;
            _catchLean = config.catchLeanDegrees;
        }

        /// <summary>
        /// Rebuilds every sprite with the skin's palette, including the animation strips.
        /// </summary>
        public void SetSkin(int index)
        {
            var skins = ShopDefs.CatSkins;
            _skin = skins[Mathf.Clamp(index, 0, skins.Length - 1)];
            LoadSheets();
            SetState(State);
        }

        public void SetState(CatState state)
        {
            if (State != state)
            {
                _frameClock = 0f;
                if (_catchRoutine != null)
                {
                    StopCoroutine(_catchRoutine);
                    _catchRoutine = null;
                    RestoreRest();
                }
            }

            State = state;
            switch (state)
            {
                case CatState.Waiting:
                    ApplyWaiting();
                    break;
                case CatState.Catch:
                    ApplyCatch();
                    break;
                default:
                    ApplyIdle();
                    break;
            }
        }

        void Update()
        {
            if (_sprite == null)
                return;

            AdvanceFrames();

            // Breathing runs alongside the drawn frames: the frames carry the tail, this carries
            // the body. It is slower and smaller when frames exist, so the two motions read as one
            // animal rather than competing.
            if (State != CatState.Waiting)
                return;

            var hasFrames = CurrentFrames() != null;
            var speed = hasFrames ? 1.6f : 3f;
            var amount = hasFrames ? 0.022f : 0.04f;

            _bob += Time.deltaTime * speed;
            var squash = 1f + Mathf.Sin(_bob) * amount;
            transform.localScale = new Vector3(
                _baseScale.x * (1f - (squash - 1f) * 0.5f),
                _baseScale.y * squash,
                _baseScale.z);
        }

        Sprite[] CurrentFrames()
        {
            switch (State)
            {
                case CatState.Waiting: return _waitFrames;
                case CatState.Catch: return _catchFrames;
                default: return _idleFrames;
            }
        }

        void AdvanceFrames()
        {
            var frames = CurrentFrames();
            if (frames == null || frames.Length == 0)
                return;

            _frameClock += Time.deltaTime * _frameRate;
            var step = Mathf.FloorToInt(_frameClock);
            _sprite.sprite = frames[FrameIndex(step, frames.Length)];
        }

        /// <summary>
        /// Ping-pong walks the strip forwards then back down it, so a 4-frame sheet reads as
        /// 1-2-3-4-3-2 and never snaps from the last pose straight back to the first.
        /// </summary>
        int FrameIndex(int step, int count)
        {
            if (count <= 1)
                return 0;
            if (!_pingPong)
                return step % count;

            var cycle = count * 2 - 2;
            var i = step % cycle;
            return i < count ? i : cycle - i;
        }

        /// <summary>
        /// Slices each strip into equal square frames. Pixels-per-unit is derived from the scene
        /// sprite's world height, so a sheet drawn at a higher resolution stays the same size on
        /// screen instead of doubling.
        /// </summary>
        void LoadSheets()
        {
            var targetHeight = _sourceSprite != null ? _sourceSprite.bounds.size.y : 1.5f;
            _staticSprite = PaletteSwap.Remap(_sourceSprite, _skin);
            _idleFrames = Slice("CatFrames/cat_idle", targetHeight, _skin);
            _waitFrames = Slice("CatFrames/cat_wait", targetHeight, _skin);
            _catchFrames = Slice("CatFrames/cat_catch", targetHeight, _skin);

            var found = (_idleFrames != null ? _idleFrames.Length : 0)
                        + (_waitFrames != null ? _waitFrames.Length : 0)
                        + (_catchFrames != null ? _catchFrames.Length : 0);
            if (found > 0)
                Debug.Log($"DeskPond cat sheets: idle={Count(_idleFrames)} wait={Count(_waitFrames)} " +
                          $"catch={Count(_catchFrames)}");
        }

        static int Count(Sprite[] a) => a != null ? a.Length : 0;

        static Sprite[] Slice(string resourcePath, float targetWorldHeight, SkinDef skin)
        {
            var tex = Resources.Load<Texture2D>(resourcePath);
            if (tex == null)
                return null;

            tex = PaletteSwap.Remap(tex, skin);

            var size = tex.height;
            var count = size > 0 ? tex.width / size : 0;
            if (count < 1)
            {
                Debug.LogWarning($"DeskPond: {resourcePath} is {tex.width}x{tex.height}; " +
                                 "expected a horizontal strip of square frames.");
                return null;
            }

            var ppu = size / Mathf.Max(0.01f, targetWorldHeight);
            var frames = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                frames[i] = Sprite.Create(tex, new Rect(i * size, 0, size, size),
                    new Vector2(0.5f, 0.5f), ppu);
                frames[i].name = resourcePath + "_" + i;
            }

            return frames;
        }

        void ApplyIdle()
        {
            _bob = 0f;
            transform.localScale = _baseScale;
            if (_sprite == null)
                return;
            RestoreStatic();
            _sprite.color = Color.white;
            _sprite.flipX = false;
        }

        void ApplyWaiting()
        {
            transform.localScale = _baseScale;
            if (_sprite == null)
                return;
            RestoreStatic();
            _sprite.color = Color.white;
            _sprite.flipX = false;
        }

        void ApplyCatch()
        {
            transform.localScale = _baseScale;
            if (_sprite == null)
                return;
            RestoreStatic();
            _sprite.color = Color.white;
            _sprite.flipX = false;

            // Drawn frames carry their own motion, so the hop is only for the static sprite.
            if (_catchFrames == null)
                PlayCatchHop();
        }

        /// <summary>
        /// A hop and a lean toward the rod, standing in for a drawn catch animation. It moves the
        /// cat rather than recolouring or enlarging it, so the artwork is never distorted.
        /// </summary>
        void PlayCatchHop()
        {
            if (_catchRoutine != null)
            {
                // Put the cat back down before measuring again, or the interrupted arc becomes
                // the new resting height and every catch leaves it a little further up.
                StopCoroutine(_catchRoutine);
                _catchRoutine = null;
                RestoreRest();
            }

            CaptureRest();
            _catchRoutine = StartCoroutine(CatchHopRoutine());
        }

        /// <summary>
        /// Records the resting pose the first time a hop runs. PetDragger reparents the cat after
        /// Bind, which rewrites localPosition, so this cannot be done at Bind time.
        /// </summary>
        void CaptureRest()
        {
            if (_restCaptured)
                return;
            _restLocalPos = transform.localPosition;
            _restLocalRot = transform.localRotation;
            _restCaptured = true;
        }

        void RestoreRest()
        {
            if (!_restCaptured)
                return;
            transform.localPosition = _restLocalPos;
            transform.localRotation = _restLocalRot;
        }

        System.Collections.IEnumerator CatchHopRoutine()
        {
            var basePos = _restLocalPos;
            var baseRot = _restLocalRot;

            for (var t = 0f; t < _catchSeconds; t += Time.deltaTime)
            {
                var k = t / _catchSeconds;

                // One smooth arc up and back down; sin(k * PI) is zero at both ends, so the cat
                // always lands exactly where it started.
                var arc = Mathf.Sin(k * Mathf.PI);

                // The lean peaks early, while the rod is being pulled, then eases out.
                var lean = Mathf.Sin(Mathf.Clamp01(k * 1.6f) * Mathf.PI);

                transform.localPosition = basePos + new Vector3(0f, arc * _catchHop, 0f);
                transform.localRotation = baseRot * Quaternion.Euler(0f, 0f, lean * _catchLean);
                yield return null;
            }

            RestoreRest();
            _catchRoutine = null;
        }

        /// <summary>Puts the scene sprite back when the new state has no sheet of its own.</summary>
        void RestoreStatic()
        {
            if (CurrentFrames() == null && _staticSprite != null)
                _sprite.sprite = _staticSprite;
        }
    }
}
