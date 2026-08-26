using UnityEngine;

namespace DeskPond
{
    public class BasketPresenter : MonoBehaviour
    {
        SpriteRenderer _sprite;
        Transform _fullCue;
        Sprite _sourceSprite;
        SkinDef _skin = ShopDefs.BasketSkins[0];
        bool _full;

        public void Bind(SpriteRenderer sprite, Transform fullCue)
        {
            _sprite = sprite != null ? sprite : GetComponent<SpriteRenderer>();
            _sourceSprite = _sprite != null ? _sprite.sprite : null;
            _fullCue = fullCue;
            var col = GetComponent<BoxCollider2D>();
            if (col != null && _sprite != null && _sprite.sprite != null)
                col.size = _sprite.sprite.bounds.size * 1.2f;
            SetFull(false);
        }

        /// <summary>
        /// Rebuilds the sprite with the skin's palette.
        /// </summary>
        public void SetSkin(int index)
        {
            var skins = ShopDefs.BasketSkins;
            _skin = skins[Mathf.Clamp(index, 0, skins.Length - 1)];
            if (_sprite != null && _sourceSprite != null)
                _sprite.sprite = PaletteSwap.Remap(_sourceSprite, _skin);
            SetFull(_full);
        }

        public void SetFull(bool full)
        {
            _full = full;
            // The "full" cue stays a tint: it is a transient state, not a different basket.
            if (_sprite != null)
                _sprite.color = full ? new Color(1f, 0.62f, 0.52f, 1f) : Color.white;
            if (_fullCue != null)
                _fullCue.gameObject.SetActive(full);
        }
    }
}
