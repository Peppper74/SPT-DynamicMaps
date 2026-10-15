using System;
using System.Collections.Generic;
using DynamicMaps.Config;
using EFT;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace DynamicMaps.Utils;

/// <summary>
/// Wishlist access shared by the marker providers. Lookups by MongoID through the read-only
/// interface proxy do not match reliably on this build, so the wishlist is indexed once by
/// template id string (backing dictionary first, then the interface, then per item).
/// </summary>
public static class WishlistUtils
{
    private const float CacheSeconds = 2f;

    private static Dictionary<string, EWishlistGroup> _cachedIndex;
    private static float _cachedAt;
    private static string _lastSource = "none";

    public static string LastIndexSource => _lastSource;

    public static Dictionary<string, EWishlistGroup> GetIndex()
    {
        var now = Time.realtimeSinceStartup;
        if (_cachedIndex != null && now - _cachedAt < CacheSeconds)
        {
            return _cachedIndex;
        }

        _cachedIndex = BuildIndex();
        _cachedAt = now;

        return _cachedIndex;
    }

    public static Dictionary<string, EWishlistGroup> BuildIndex()
    {
        var index = new Dictionary<string, EWishlistGroup>(StringComparer.OrdinalIgnoreCase);
        _lastSource = "per-item";

        var wishlist = GameUtils.PlayerProfile?.WishlistManager?.GetWishlist();
        if (wishlist == null)
        {
            return index;
        }

        try
        {
            var backing = wishlist.TryCast<Il2CppSystem.Collections.Generic.Dictionary<MongoID, EWishlistGroup>>();
            if (backing != null)
            {
                var enumerator = backing.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current;
                    index[entry.Key.ToString()] = entry.Value;
                }

                if (index.Count > 0)
                {
                    _lastSource = "backing-dictionary";
                    return index;
                }
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"DynamicMaps wishlist: reading the backing dictionary failed: {e.Message}");
        }

        try
        {
            foreach (var entry in wishlist)
            {
                index[entry.Key.ToString()] = entry.Value;
            }

            if (index.Count > 0)
            {
                _lastSource = "interface";
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"DynamicMaps wishlist: enumerating the interface failed: {e.Message}");
        }

        return index;
    }

    /// <summary>
    /// Checks a single item against the wishlist - used when the index could not be built at all.
    /// </summary>
    public static bool TryGetGroup(string templateId, out EWishlistGroup group)
    {
        group = default;

        var wishlistManager = GameUtils.PlayerProfile?.WishlistManager;
        if (wishlistManager == null || string.IsNullOrEmpty(templateId))
        {
            return false;
        }

        return wishlistManager.IsInWishlist(new MongoID(templateId), true, out group);
    }

    public static bool IsGroupShown(EWishlistGroup group)
    {
        return !Settings.ShowOnlyOtherWishListItemsInRaid.Value || group == EWishlistGroup.Other;
    }
}
