using System;
using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using DynamicMaps.Config;
using DynamicMaps.Data;
using DynamicMaps.Patches;
using DynamicMaps.UI.Components;
using DynamicMaps.Utils;
using EFT;
using EFT.Interactive;
using EFT.UI.DragAndDrop;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DynamicMaps.DynamicMarkers
{
    public class LootMarkerProvider : IDynamicMarkerProvider
    {
        private const string FallbackMarkerImage = "Markers/dot.png";

        private MapView _lastMapView;
        private Dictionary<LootItem, MapMarker> _lootMarkers = [];
        
        public void OnShowInRaid(MapView map)
        {
            _lastMapView = map;

            // loot that appears or disappears while the map is open: dropped items show up, picked up
            // items disappear, without waiting for the map to be reopened
            LootItemInitPatch.OnRegisterLoot += OnRegisterLoot;
            GameWorldDestroyLootPatch.OnDestroyLoot += OnDestroyLoot;

            AddWishlistedLootMarkers();
        }

        public void OnHideInRaid(MapView map)
        {
            LootItemInitPatch.OnRegisterLoot -= OnRegisterLoot;
            GameWorldDestroyLootPatch.OnDestroyLoot -= OnDestroyLoot;
        }

        public void OnRaidEnd(MapView map)
        {
            LootItemInitPatch.OnRegisterLoot -= OnRegisterLoot;
            GameWorldDestroyLootPatch.OnDestroyLoot -= OnDestroyLoot;

            TryRemoveMarkers();
        }

        public void OnMapChanged(MapView map, MapDef mapDef)
        {
            _lastMapView = map;
            TryRemoveMarkers();
            AddWishlistedLootMarkers();
        }

        public void OnDisable(MapView map)
        {
            OnRaidEnd(map);
        }

        public void RefreshMarkers()
        {
            if (!GameUtils.IsInRaid()) return;

            TryRemoveMarkers();
            AddWishlistedLootMarkers();
        }

        // a loot item that appeared in the world while the map is open (dropped item, airdrop, ...)
        private void OnRegisterLoot(LootItem lootItem)
        {
            if (_lastMapView == null || lootItem == null) return;
            if (_lootMarkers.ContainsKey(lootItem)) return;
            if (!IsWishlistLootShown(lootItem)) return;

            TryAddMarker(lootItem);
            Plugin.Log.LogInfo($"DynamicMaps wishlist loot: marker added for {lootItem.TemplateId} (registered while the map was open)");
        }

        // a loot item that no longer exists (picked up or destroyed)
        private void OnDestroyLoot(LootItem lootItem)
        {
            if (lootItem == null || !_lootMarkers.ContainsKey(lootItem)) return;

            TryRemoveMarker(lootItem);
            Plugin.Log.LogInfo($"DynamicMaps wishlist loot: marker removed for {lootItem.TemplateId} (no longer in the world)");
        }

        private bool IsWishlistLootShown(LootItem loot)
        {
            if (loot?.Item == null) return false;

            var wishlistGroups = WishlistUtils.GetIndex();
            EWishlistGroup group;

            if (wishlistGroups.Count > 0)
            {
                if (!wishlistGroups.TryGetValue(loot.TemplateId.ToString(), out group)) return false;
            }
            else if (!WishlistUtils.TryGetGroup(loot.TemplateId, out group))
            {
                return false;
            }

            return WishlistUtils.IsGroupShown(group);
        }

        private void AddWishlistedLootMarkers()
        {
            if (_lastMapView == null) return;

            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            var wishlistGroups = WishlistUtils.BuildIndex();
            var indexSource = WishlistUtils.LastIndexSource;

            var wishlistByGroup = new Dictionary<EWishlistGroup, int>();
            foreach (var pair in wishlistGroups)
            {
                wishlistByGroup.TryGetValue(pair.Value, out var wishCount);
                wishlistByGroup[pair.Value] = wishCount + 1;
            }

            var matchedByGroup = new Dictionary<EWishlistGroup, int>();
            var matchedTotal = 0;
            var shown = 0;
            var sample = new List<string>();
            var shownDetails = new List<string>();

            foreach (var item in gameWorld.LootList)
            {
                if (item is not LootItem loot) continue;

                EWishlistGroup group;
                if (wishlistGroups.Count > 0)
                {
                    if (!wishlistGroups.TryGetValue(loot.TemplateId.ToString(), out group)) continue;
                }
                else if (!WishlistUtils.TryGetGroup(loot.TemplateId, out group))
                {
                    continue;
                }

                matchedTotal++;
                matchedByGroup.TryGetValue(group, out var matchedCount);
                matchedByGroup[group] = matchedCount + 1;

                if (sample.Count < 10)
                {
                    sample.Add($"{loot.TemplateId}={group}");
                }

                if (!WishlistUtils.IsGroupShown(group)) continue;

                TryAddMarker(loot);
                shown++;

                if (shownDetails.Count < 10)
                {
                    var position = loot.transform != null ? MathUtils.ConvertToMapPosition(loot.transform).ToString() : "<no transform>";
                    shownDetails.Add($"{loot.TemplateId}={group} pos={position} item={loot.Item != null}");
                }
            }

            Plugin.Log.LogInfo($"DynamicMaps wishlist loot: index={indexSource}, wishlist={wishlistGroups.Count} [{FormatWishlistGroups(wishlistByGroup)}], raid matches={matchedTotal} [{FormatWishlistGroups(matchedByGroup)}], shown={shown}, onlyOther={Settings.ShowOnlyOtherWishListItemsInRaid.Value}, sample=[{string.Join(", ", sample)}]");
            Plugin.Log.LogInfo($"DynamicMaps wishlist loot shown: markers={_lootMarkers.Count}, details=[{string.Join("; ", shownDetails)}]");
        }

        private static string FormatWishlistGroups(Dictionary<EWishlistGroup, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return "none";
            }

            var parts = new List<string>();
            foreach (var pair in counts)
            {
                parts.Add($"{pair.Key}={pair.Value}");
            }

            return string.Join(", ", parts);
        }

        private void TryAddMarker(LootItem item)
        {
            if (item == null || item.Item == null)
            {
                Plugin.Log.LogWarning($"DynamicMaps loot marker skipped: item data is null ({item?.TemplateId})");
                return;
            }
            
            if (_lootMarkers.ContainsKey(item)) return;
            if (Settings.ShowWishListItemsIntelLevel.Value > GameUtils.GetIntelLevel())
            {
                Plugin.Log.LogWarning($"DynamicMaps loot marker skipped: intel level too low for {item.TemplateId}");
                return;
            }
            
            var staticIcons = EFTHardSettings.Instance.StaticIcons;
            var itemType = ItemViewFactory.GetItemType(Il2CppType.From(item.Item.GetType()));
            var itemSprite = staticIcons.ItemTypeSprites.GetValueOrDefault(itemType);
            if (itemSprite == null)
            {
                // Items the game has no icon for (typically container or corpse contents) used to get
                // invisible markers, so fall back to the generic dot.
                Plugin.Log.LogWarning($"DynamicMaps loot marker without a sprite: {item.TemplateId} ({itemType}), using the fallback dot");
                itemSprite = TextureUtils.GetOrLoadCachedSprite(FallbackMarkerImage);
            }

            var transform = item.transform;
            if (transform == null)
            {
                Plugin.Log.LogWarning($"DynamicMaps loot marker skipped: transform is null for {item.TemplateId}");
                return;
            }
            
            var markerDef = new MapMarkerDef
            {
                Category = "Loot",
                Color = Settings.LootItemColor.Value,
                Sprite = itemSprite,
                Position = MathUtils.ConvertToMapPosition(transform),
                Text = item.Item.TemplateId.LocalizedName()
            };
            
            var marker = _lastMapView.AddMapMarker(markerDef);
            _lootMarkers[item] = marker;
        }

        private void TryRemoveMarkers()
        {
            foreach (var item in _lootMarkers.Keys.ToList())
            {
                TryRemoveMarker(item);
            }
        }

        private void TryRemoveMarker(LootItem item)
        {
            if (!_lootMarkers.ContainsKey(item)) return;
            
            _lootMarkers[item].ContainingMapView.RemoveMapMarker(_lootMarkers[item]);
            _lootMarkers.Remove(item);
        }
        
        public void OnShowOutOfRaid(MapView map)
        {
            // Do nothing
        }

        public void OnHideOutOfRaid(MapView map)
        {
            // Do nothing
        }
    }
}
