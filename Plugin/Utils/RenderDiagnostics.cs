using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicMaps.Utils;

// Temporary port diagnostics for SPT 5: the map screen opens and the layers load, but
// nothing shows up on screen, so dump the exact UI state and grab a screenshot per show.
// ScreenCapture is not exposed by the interop assembly of this build, so the screenshot is
// taken from the next rendered frame with Texture2D.ReadPixels (see ProcessPendingCapture).
public static class RenderDiagnostics
{
    private const int MaxCaptures = 6;
    private static int _captures;
    private static string _pendingCapturePath;
    private static float _pendingCaptureTime;

    public static void RequestCapture(string reason)
    {
        if (_captures >= MaxCaptures || _pendingCapturePath != null)
        {
            return;
        }

        _captures++;
        var directory = Path.Combine(Plugin.Path, "render-dumps");
        Directory.CreateDirectory(directory);
        _pendingCapturePath = Path.Combine(directory, $"render-dump-{_captures}-{reason}.png");
        _pendingCaptureTime = Time.realtimeSinceStartup + 0.5f;

        Plugin.Log.LogInfo($"Render dump: screenshot queued -> {_pendingCapturePath}");
    }

    /// <summary>
    /// Called from Update: by that point the frame containing the map screen has been rendered,
    /// so ReadPixels returns the picture the player actually sees.
    /// </summary>
    public static void ProcessPendingCapture()
    {
        if (_pendingCapturePath == null || Time.realtimeSinceStartup < _pendingCaptureTime)
        {
            return;
        }

        var path = _pendingCapturePath;
        _pendingCapturePath = null;

        try
        {
            var width = Screen.width;
            var height = Screen.height;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            texture.Apply();

            var png = texture.EncodeToPNG();
            File.WriteAllBytes(path, png);
            UnityEngine.Object.Destroy(texture);

            Plugin.Log.LogInfo($"Render dump: screenshot written ({width}x{height}) -> {path}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Render dump: screenshot failed: {e.Message}");
        }
    }

    public static string State(GameObject go)
    {
        if (go == null)
        {
            return "<null>";
        }

        return $"activeInHierarchy={go.activeInHierarchy} activeSelf={go.activeSelf} layer={go.layer}";
    }

    public static string RectState(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return "<null>";
        }

        var world = rectTransform.position;
        var scale = rectTransform.lossyScale;

        return $"anchoredPos={rectTransform.anchoredPosition} size={rectTransform.rect.size} localScale={rectTransform.localScale} lossyScale={scale} worldPos=({world.x:F0},{world.y:F0},{world.z:F0})";
    }

    public static string ImageState(Image image)
    {
        if (image == null)
        {
            return "<null>";
        }

        var sprite = image.sprite;
        var texture = sprite != null ? sprite.texture : null;
        var spriteName = sprite != null ? sprite.name : "<null>";
        var textureSize = texture != null ? $"{texture.width}x{texture.height}" : "<null>";

        return $"enabled={image.enabled} color={image.color} sprite={spriteName} texture={textureSize}";
    }

    public static string MaskState(Mask mask)
    {
        if (mask == null)
        {
            return "<null>";
        }

        return $"enabled={mask.enabled} showMaskGraphic={mask.showMaskGraphic}";
    }
}
