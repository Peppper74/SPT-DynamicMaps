using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using DynamicMaps.Config;
using DynamicMaps.Data;
using DynamicMaps.UI.Components;
using DynamicMaps.Utils;
using EFT;
using EFT.Interactive;
using EFT.UI.DragAndDrop;
using Il2CppInterop.Runtime;

namespace DynamicMaps.DynamicMarkers
{
    public class LootMarkerProvider : IDynamicMarkerProvider
    {
        private MapView _lastMapView;
        private Dictionary<LootItem, MapMarker> _lootMarkers = [];
        
        public void OnShowInRaid(MapView map)
        {
            _lastMapView = map;
            AddWishlistedLootMarkers();
        }

        public void OnHideInRaid(MapView map)
        {
            // Do nothing
        }

        public void OnRaidEnd(MapView map)
        {
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

        private void AddWishlistedLootMarkers()
        {
            if (_lastMapView == null) return;

            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            var wishlist = GameUtils.PlayerProfile.WishlistManager.GetWishlist();
            foreach (var item in gameWorld.LootList)
            {
                if (item is not LootItem loot) continue;
                if (!wishlist.TryGetValue(new MongoID(loot.TemplateId), out var group)) continue;
                if (Settings.ShowOnlyOtherWishListItemsInRaid.Value && group != EWishlistGroup.Other) continue;

                TryAddMarker(loot);
            }
        }

        private void TryAddMarker(LootItem item)
        {
            if (item == null || item.Item == null)
                return;
            
            if (_lootMarkers.ContainsKey(item)) return;
            if (Settings.ShowWishListItemsIntelLevel.Value > GameUtils.GetIntelLevel()) return;
            
            var staticIcons = EFTHardSettings.Instance.StaticIcons;
            var itemType = ItemViewFactory.GetItemType(Il2CppType.From(item.Item.GetType()));
            var itemSprite = staticIcons.ItemTypeSprites.GetValueOrDefault(itemType);

            var transform = item.transform;
            if (transform == null)
                return;
            
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
