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
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using Il2CppInterop.Runtime;
using UnityEngine;
using MapMarker = DynamicMaps.UI.Components.MapMarker;

namespace DynamicMaps.DynamicMarkers
{
    /// <summary>
    /// Marks wishlist items that lie inside a corpse (dead player or bot) or inside a lootable
    /// container of the raid. Everything is refreshed once per second, so a marker disappears as
    /// soon as the item is taken, and appears as soon as a container or corpse holds one.
    /// </summary>
    public class ContainerLootMarkerProvider : IDynamicMarkerProvider
    {
        private const string _lootCategory = "Loot";
        private const string _fallbackMarkerImage = "Markers/dot.png";

        // containers are scanned in slices, so a full sweep stays cheap even on big maps
        private const int _containersPerTick = 40;

        private MapView _lastMapView;

        private readonly Dictionary<Corpse, Dictionary<string, MapMarker>> _markersByCorpse = new();
        private readonly List<LootableContainer> _containers = new();
        private readonly Dictionary<LootableContainer, Dictionary<string, MapMarker>> _markersByContainer = new();
        private int _containerCursor;

        public void OnShowInRaid(MapView map)
        {
            _lastMapView = map;

            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer += OnUnregisterPlayer;

            CollectExistingCorpses();
            CollectContainers();

            Tick();
        }

        public void OnHideInRaid(MapView map)
        {
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
        }

        public void OnRaidEnd(MapView map)
        {
            GameWorldUnregisterPlayerPatch.OnUnregisterPlayer -= OnUnregisterPlayer;
            RemoveAllMarkers();
        }

        public void OnDisable(MapView map)
        {
            OnRaidEnd(map);
        }

        public void OnMapChanged(MapView map, MapDef mapDef)
        {
            // corpses and containers belong to the location that was just left
            _lastMapView = map;
            _containers.Clear();
            RemoveAllMarkers();
        }

        public void OnShowOutOfRaid(MapView map)
        {
            // corpses and containers only exist in raid
        }

        public void OnHideOutOfRaid(MapView map)
        {
        }

        /// <summary>
        /// Called once per second while the game is running.
        /// </summary>
        public void Tick()
        {
            if (_lastMapView == null)
            {
                return;
            }

            if (Settings.ShowCorpseLootInRaid.Value)
            {
                foreach (var corpse in _markersByCorpse.Keys.ToList())
                {
                    ScanCorpse(corpse);
                }
            }
            else if (_markersByCorpse.Count > 0)
            {
                RemoveAllMarkers();
            }

            if (Settings.ShowContainerLootInRaid.Value)
            {
                ScanContainersSlice();
            }
            else if (_markersByContainer.Count > 0)
            {
                RemoveAllMarkers();
                _containers.Clear();
            }
        }

        private void OnUnregisterPlayer(IPlayer iPlayer)
        {
            if (iPlayer is not Player player || !player.HasCorpse())
            {
                return;
            }

            TrackCorpse(player.Corpse);
        }

        private void CollectExistingCorpses()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null)
            {
                return;
            }

            foreach (var player in gameWorld.AllPlayersEverExisted)
            {
                if (player != null && player.HasCorpse())
                {
                    TrackCorpse(player.Corpse);
                }
            }
        }

        private void CollectContainers()
        {
            // remove the markers of the previous collection first: clearing the dictionary alone
            // left every old marker on the map, so each map show added a whole new set
            foreach (var oldMarkers in _markersByContainer.Values)
            {
                foreach (var oldMarker in oldMarkers.Values)
                {
                    RemoveMarker(oldMarker);
                }
            }

            _containers.Clear();
            _markersByContainer.Clear();
            _containerCursor = 0;

            try
            {
                var containers = LocationScene.GetAllObjects<LootableContainer>();
                if (containers == null)
                {
                    return;
                }

                foreach (var container in containers)
                {
                    if (container == null)
                    {
                        continue;
                    }

                    _containers.Add(container);
                    _markersByContainer[container] = new Dictionary<string, MapMarker>();
                }

                Plugin.Log.LogInfo($"DynamicMaps container loot: tracking {_containers.Count} lootable container(s)");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"DynamicMaps container loot: collecting containers failed ({e.Message})");
            }
        }

        private void TrackCorpse(Corpse corpse)
        {
            if (corpse == null || _markersByCorpse.ContainsKey(corpse))
            {
                return;
            }

            _markersByCorpse[corpse] = new Dictionary<string, MapMarker>();
            Plugin.Log.LogInfo($"DynamicMaps corpse loot: tracking a new corpse ({corpse.name})");
        }

        private void ScanContainersSlice()
        {
            if (_containers.Count == 0)
            {
                return;
            }

            var count = Mathf.Min(_containersPerTick, _containers.Count);
            for (var i = 0; i < count; i++)
            {
                if (_containerCursor >= _containers.Count)
                {
                    _containerCursor = 0;
                }

                ScanContainer(_containers[_containerCursor]);
                _containerCursor++;
            }
        }

        private void ScanCorpse(Corpse corpse)
        {
            if (corpse == null || !_markersByCorpse.TryGetValue(corpse, out var markers))
            {
                ForgetCorpse(corpse);
                return;
            }

            IEnumerable<Item> items;
            try
            {
                items = corpse.Item?.GetAllItems();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"DynamicMaps corpse loot: reading {corpse.name} failed ({e.Message})");
                ForgetCorpse(corpse);
                return;
            }

            UpdateMarkers(corpse.transform, items, markers, $"corpse loot ({corpse.name})");
        }

        private void ScanContainer(LootableContainer container)
        {
            if (container == null)
            {
                return;
            }

            if (!_markersByContainer.TryGetValue(container, out var markers))
            {
                markers = new Dictionary<string, MapMarker>();
                _markersByContainer[container] = markers;
            }

            IEnumerable<Item> items;
            try
            {
                items = container.ItemOwner?.RootItem?.GetAllItems();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"DynamicMaps container loot: reading {container.name} failed ({e.Message})");
                return;
            }

            UpdateMarkers(container.transform, items, markers, $"container loot ({container.name})");
        }

        private void UpdateMarkers(Transform anchor, IEnumerable<Item> items, Dictionary<string, MapMarker> markers, string label)
        {
            if (anchor == null || markers == null)
            {
                return;
            }

            var presentItemIds = new HashSet<string>();

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var itemId = item.Id.ToString();
                    presentItemIds.Add(itemId);

                    if (markers.ContainsKey(itemId))
                    {
                        continue;
                    }

                    if (!IsWishlistItemShown(item, out var group))
                    {
                        continue;
                    }

                    if (Settings.ShowWishListItemsIntelLevel.Value > GameUtils.GetIntelLevel())
                    {
                        continue;
                    }

                    markers[itemId] = AddMarker(anchor, item);
                    Plugin.Log.LogInfo($"DynamicMaps {label}: marker added for {item.TemplateId} ({group})");
                }
            }

            // drop markers whose item is no longer inside (taken by the player)
            foreach (var itemId in markers.Keys.ToList())
            {
                if (presentItemIds.Contains(itemId))
                {
                    continue;
                }

                RemoveMarker(markers[itemId]);
                markers.Remove(itemId);
                Plugin.Log.LogInfo($"DynamicMaps {label}: marker removed, item {itemId} is gone");
            }
        }

        private static bool IsWishlistItemShown(Item item, out EWishlistGroup group)
        {
            group = default;

            if (item == null)
            {
                return false;
            }

            var wishlistGroups = WishlistUtils.GetIndex();
            var templateId = item.TemplateId.ToString();

            if (wishlistGroups.Count > 0)
            {
                if (!wishlistGroups.TryGetValue(templateId, out group))
                {
                    return false;
                }
            }
            else if (!WishlistUtils.TryGetGroup(templateId, out group))
            {
                return false;
            }

            return WishlistUtils.IsGroupShown(group);
        }

        private MapMarker AddMarker(Transform anchor, Item item)
        {
            var staticIcons = EFTHardSettings.Instance.StaticIcons;
            var itemType = ItemViewFactory.GetItemType(Il2CppType.From(item.GetType()));
            var itemSprite = staticIcons.ItemTypeSprites.GetValueOrDefault(itemType)
                             ?? TextureUtils.GetOrLoadCachedSprite(_fallbackMarkerImage);

            var markerDef = new MapMarkerDef
            {
                Category = _lootCategory,
                Color = Settings.LootItemColor.Value,
                Sprite = itemSprite,
                Position = MathUtils.ConvertToMapPosition(anchor),
                Text = item.TemplateId.LocalizedName()
            };

            return _lastMapView.AddMapMarker(markerDef);
        }

        private void RemoveMarker(MapMarker marker)
        {
            if (marker == null)
            {
                return;
            }

            marker.ContainingMapView?.RemoveMapMarker(marker);
        }

        private void ForgetCorpse(Corpse corpse)
        {
            if (corpse == null || !_markersByCorpse.TryGetValue(corpse, out var markers))
            {
                return;
            }

            foreach (var marker in markers.Values)
            {
                RemoveMarker(marker);
            }

            markers.Clear();
            _markersByCorpse.Remove(corpse);
        }

        private void RemoveAllMarkers()
        {
            foreach (var corpse in _markersByCorpse.Keys.ToList())
            {
                ForgetCorpse(corpse);
            }

            _markersByCorpse.Clear();

            foreach (var markers in _markersByContainer.Values)
            {
                foreach (var marker in markers.Values)
                {
                    RemoveMarker(marker);
                }

                markers.Clear();
            }

            _markersByContainer.Clear();
        }
    }
}
