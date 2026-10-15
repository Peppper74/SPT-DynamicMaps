using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DynamicMaps.Utils
{
    public static class TextureUtils
    {
        private static Dictionary<string, Sprite> _spriteCache = new();

        private static Texture2D LoadTexture2DFromPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(absolutePath));

            // textures created at runtime are unloaded when the location changes, which left the
            // cached sprites destroyed and made every marker a plain coloured quad
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        public static Sprite GetOrLoadCachedSprite(string path)
        {
            if (_spriteCache.TryGetValue(path, out var cached) && IsAlive(cached))
                return cached;

            var absolutePath = Path.Combine(Plugin.Path, path);
            var texture = LoadTexture2DFromPath(absolutePath);
            if (texture == null)
            {
                Plugin.Log.LogError($"TextureUtils: image not found '{absolutePath}'");
                _spriteCache.Remove(path);
                return null;
            }

            var sprite = Sprite.Create(texture,
                                       new Rect(0f, 0f, texture.width, texture.height),
                                       new Vector2(texture.width / 2, texture.height / 2));
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _spriteCache[path] = sprite;

            return sprite;
        }

        private static bool IsAlive(Sprite sprite)
        {
            try
            {
                return sprite != null && sprite.texture != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
