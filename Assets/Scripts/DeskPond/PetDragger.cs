using UnityEngine;
using UnityEngine.InputSystem;

namespace DeskPond
{
    /// <summary>
    /// Lets the player grab the cat and slide the pet along the water.
    ///
    /// Movement is constrained to the horizontal waterline on purpose: the pond is the taskbar, so
    /// the pet belongs on it. Dragging the cat up into empty space would leave the fishing line
    /// hanging in mid-air. Relax <see cref="lockToWaterline"/> if free 2D placement is wanted.
    /// </summary>
    public class PetDragger : MonoBehaviour
    {
        /// <summary>Pixels the cursor must travel before a click counts as a drag.</summary>
        const float DragThresholdPx = 4f;

        [SerializeField] bool lockToWaterline = true;

        Transform _petRoot;
        Transform _cat;
        Transform _basket;
        Camera _camera;
        GameSession _session;
        DesktopWindow _window;

        bool _grabbed;
        bool _dragging;
        Vector2 _grabScreen;
        float _grabOffsetX;
        float _grabOffsetY;
        float _homeY;
        bool _restored;

        public Transform PetRoot => _petRoot;

        public static PetDragger Create(GameSession session, Transform cat, Transform basket, Camera cam)
        {
            var go = new GameObject("PetDragger");
            var dragger = go.AddComponent<PetDragger>();
            dragger.Bind(session, cat, basket, cam);
            return dragger;
        }

        public void Bind(GameSession session, Transform cat, Transform basket, Camera cam)
        {
            _session = session;
            _cat = cat;
            _basket = basket;
            _camera = cam != null ? cam : Camera.main;

            // A shared parent so the cat and its basket travel together.
            var rootGo = GameObject.Find("PetRoot") ?? new GameObject("PetRoot");
            _petRoot = rootGo.transform;
            _petRoot.position = Vector3.zero;
            if (_cat != null)
                _cat.SetParent(_petRoot, true);
            if (_basket != null)
                _basket.SetParent(_petRoot, true);

            _homeY = _petRoot.position.y;
            EnsureCatCollider();
            // Restore happens on the first Update, not here: DesktopWindow re-sizes and re-positions
            // the camera in its own Start, and converting a saved position against the pre-layout
            // camera lands the pet in the wrong place.
        }

        void EnsureCatCollider()
        {
            if (_cat == null || _cat.GetComponent<Collider2D>() != null)
                return;

            var box = _cat.gameObject.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            var sr = _cat.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
                box.size = sr.sprite.bounds.size;
        }

        /// <summary>Re-places the pet for the current screen, after the display changes.</summary>
        public void ReapplyForScreen()
        {
            if (_petRoot == null || _camera == null)
                return;
            RestorePosition();
            _petRoot.position = ClampToView(_petRoot.position);
            _homeY = _petRoot.position.y;
        }

        void RestorePosition()
        {
            if (_session == null || _session.Save == null || _camera == null)
                return;

            var save = _session.Save;
            if (save.petNormX < 0f)
                return;

            var screenX = Mathf.Clamp01(save.petNormX) * Screen.width;
            var world = _camera.ScreenToWorldPoint(new Vector3(screenX, Screen.height * 0.5f, 0f));
            var target = _petRoot.position;
            target.x = world.x;
            _petRoot.position = ClampToView(target);
        }

        void Update()
        {
            if (_camera == null)
                return;

            if (!_restored)
            {
                _restored = true;
                RestorePosition();
            }

            // While the overlay is up the window never takes focus, so pointer and button state
            // come from the OS rather than from Unity's Input System.
            var overlay = _window != null && _window.IsActive;
            var mouse = Mouse.current;
            if (!overlay && mouse == null)
                return;

            var screen = overlay && _window.CursorValid
                ? _window.CursorScreen
                : mouse.position.ReadValue();
            var pressed = overlay ? _window.PrimaryPressedThisFrame : mouse.leftButton.wasPressedThisFrame;
            var held = overlay ? _window.PrimaryDown : mouse.leftButton.isPressed;

            var world = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

            if (pressed && !_grabbed && OverCat(world))
            {
                _grabbed = true;
                _grabScreen = screen;
                _grabOffsetX = _petRoot.position.x - world.x;
                _grabOffsetY = _petRoot.position.y - world.y;
            }

            if (_grabbed)
            {
                // A click only becomes a drag once the cursor actually travels, so tapping the cat
                // (or clicking through to something else) does not nudge the pet or overwrite the
                // saved position.
                if (!_dragging && Vector2.Distance(screen, _grabScreen) > DragThresholdPx)
                    _dragging = true;

                if (_dragging)
                {
                    var target = _petRoot.position;
                    target.x = world.x + _grabOffsetX;
                    target.y = lockToWaterline ? _homeY : world.y + _grabOffsetY;
                    _petRoot.position = ClampToView(target);
                }

                if (!held)
                {
                    if (_dragging)
                        StorePosition();
                    _grabbed = false;
                    _dragging = false;
                }
            }

            // OR-in rather than assign: the menu also contributes to this flag each frame.
            if (_window != null && (_dragging || OverPet(world)))
                _window.PointerOverPet = true;
        }

        Vector3 ClampToView(Vector3 target)
        {
            if (_camera == null || !_camera.orthographic)
                return target;

            var halfHeight = _camera.orthographicSize;
            var halfWidth = halfHeight * _camera.aspect;
            var centre = _camera.transform.position;

            target.x = Mathf.Clamp(target.x, centre.x - halfWidth + 1f, centre.x + halfWidth - 1f);
            if (!lockToWaterline)
                target.y = Mathf.Clamp(target.y, centre.y - halfHeight + 1f, centre.y + halfHeight - 1f);
            return target;
        }

        void StorePosition()
        {
            if (_session == null || _session.Save == null || _camera == null || Screen.width <= 0)
                return;

            var screen = _camera.WorldToScreenPoint(_petRoot.position);
            _session.Save.petNormX = Mathf.Clamp01(screen.x / Screen.width);
            SaveSystem.Write(_session.Save);
        }

        bool OverCat(Vector3 world)
        {
            if (_cat == null)
                return false;
            var col = _cat.GetComponent<Collider2D>();
            return col != null && col.OverlapPoint(world);
        }

        bool OverPet(Vector3 world)
        {
            if (OverCat(world))
                return true;
            if (_basket == null)
                return false;
            var col = _basket.GetComponent<Collider2D>();
            return col != null && col.OverlapPoint(world);
        }

        public void SetWindow(DesktopWindow window) => _window = window;
    }
}
