using System.Collections.Generic;
using System.IO;
using DynamicMaps.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DynamicMaps.Utils;

public static class ImageUtils
{
    private static readonly Dictionary<string, Sprite> MapCache = new();

    public static Sprite GetOrLoadCachedSprite(MapLayerDef def)
    {
        if (MapCache.TryGetValue(def.ImagePath, out var sprite))
            return sprite;

        var absolutePath = Path.Combine(Plugin.Path, def.ImagePath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        var imageData = new Il2CppStructArray<byte>(File.ReadAllBytes(absolutePath));
        if (!texture.LoadImage(imageData, markNonReadable: true))
        {
            Object.Destroy(texture);
            Plugin.Log.LogError($"Failed to load map image: {absolutePath}");
            return null;
        }

        sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 1f);

        MapCache[def.ImagePath] = sprite;
        return sprite;
    }
}
