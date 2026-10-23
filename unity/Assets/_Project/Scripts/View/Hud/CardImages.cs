using System.Collections.Generic;
using UnityEngine;

namespace TableWorld.View.Hud
{
    /// <summary>Card faces as UI sprites (one cell of a sprite sheet each), shared by the hand bar and the Search window.</summary>
    public sealed class CardImages
    {
        private readonly Dictionary<(string Asset, int Index), Sprite> _sprites = new Dictionary<(string, int), Sprite>();
        private AssetLibrary _for;

        /// <summary>The cell <paramref name="index"/> of image asset <paramref name="assetId"/>, or null when the image is not loaded.</summary>
        public Sprite Face(AssetLibrary assets, string assetId, int index)
        {
            if (assets == null || assetId == null)
                return null;
            if (assets != _for)
            {
                _sprites.Clear();
                _for = assets;
            }
            var key = (assetId, index);
            if (_sprites.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var texture = assets.Texture(assetId);
            if (texture == null)
                return null;
            var st = assets.CellScaleOffset(assetId, index);
            var rect = new Rect(st.z * texture.width, st.w * texture.height, st.x * texture.width, st.y * texture.height);
            var sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"{assetId}#{index}";
            _sprites[key] = sprite;
            return sprite;
        }
    }
}
