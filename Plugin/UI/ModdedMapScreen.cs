using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Comfort.Common;
using DG.Tweening;
using DynamicMaps.Config;
using DynamicMaps.Data;
using DynamicMaps.DynamicMarkers;
using DynamicMaps.Patches;
using DynamicMaps.UI.Components;
using DynamicMaps.UI.Controls;
using DynamicMaps.Utils;
using Il2CppInterop.Runtime;
using DynamicMaps.ExternalModSupport;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using DynamicMaps.ExternalModSupport.SamSWATHeliCrash;
using EFT;
using UnityEngine.EventSystems;

namespace DynamicMaps.UI
{
    public class ModdedMapScreen : MonoBehaviour
    {
        public ModdedMapScreen(IntPtr ptr) : base(ptr) { }

        #region Variables and Declarations
        private EventHandler _adjustMiniMapHandler;
        private Action<Vector2> _scrollHandler;
        
        private const string MapRelPath = "Maps";

        private bool _initialized = false;
        
        // zoom stuff
        private static readonly float PositionTweenTime = 0.25f;
        private static readonly float ScrollZoomScaler = 1.75f;
        private float _targetZoom = 0f;
        private Vector2 _zoomFocusPoint = Vector2.zero;
        private static readonly float ScrollZoomLerpSpeed = 8f;

        // vectors
        private static readonly Vector2 LevelSliderPosition = new Vector2(15f, 750f);
        private static readonly Vector2 MapSelectDropdownPosition = new Vector2(-780f, -50f);
        private static readonly Vector2 MapSelectDropdownSize = new Vector2(360f, 31f);
        private static readonly Vector2 MaskSizeModifierInRaid = new Vector2(0, -42f);
        private static readonly Vector2 MaskPositionInRaid = new Vector2(0, -20f);
        private static readonly Vector2 MaskSizeModifierOutOfRaid = new Vector2(0, -70f);
        private static readonly Vector2 MaskPositionOutOfRaid = new Vector2(0, -5f);
        private static readonly Vector2 TextAnchor = new Vector2(0f, 1f);
        private static readonly Vector2 CursorPositionTextOffset = new Vector2(15f, -52f);
        private static readonly Vector2 PlayerPositionTextOffset = new Vector2(15f, -68f);
        private static readonly float PositionTextFontSize = 15f;

        private RectTransform RectTransform => gameObject.GetRectTransform();

        private RectTransform ParentTransform => gameObject.transform.parent as RectTransform;

        private bool _isShown = false;

        // map and transport mechanism
        private MapScrollRect _scrollRect;
        private Mask _scrollMask;
        internal MapView MapView => _mapView;
        private MapView _mapView;

        // map controls
        private LevelSelectSlider _levelSelectSlider;
        private MapSelectDropdown _mapSelectDropdown;
        private CursorPositionText _cursorPositionText;
        private PlayerPositionText _playerPositionText;

        // peek
        private MapPeekComponent _peekComponent;
        internal bool HasPeekComponent => _peekComponent is not null;
        internal bool IsPeeked => IsPeeking || ShowingMiniMap;
        private bool IsPeeking => _peekComponent != null && _peekComponent.IsPeeking;
        private bool ShowingMiniMap => _peekComponent != null && _peekComponent.ShowingMiniMap;
        private Vector2 _savedMainMapPos = Vector2.zero;
        
        public bool IsShowingMapScreen { get; private set; }
        
        // dynamic map marker providers
        private readonly Dictionary<Type, IDynamicMarkerProvider> _dynamicMarkerProviders = [];
        
        // minimap throttle
        private readonly float _miniMapUpdateInterval = 0.033f;
        private float _miniMapUpdateTimer = 0f;

        // config
        private bool _autoCenterOnPlayerMarker = true;
        private bool _autoSelectLevel = true;
        private bool _resetZoomOnCenter = false;
        private bool _rememberMapPosition = true;
        private bool _transitionAnimations = true;
        
        private float _centeringZoomResetPoint = 0f;
        private KeyboardShortcut _centerPlayerShortcut;
        private KeyboardShortcut _dumpShortcut;
        private KeyboardShortcut _moveMapUpShortcut;
        private KeyboardShortcut _moveMapDownShortcut;
        private KeyboardShortcut _moveMapLeftShortcut;
        private KeyboardShortcut _moveMapRightShortcut;
        private float _moveMapSpeed = 0.25f;
        private KeyboardShortcut _moveMapLevelUpShortcut;
        private KeyboardShortcut _moveMapLevelDownShortcut;

        private bool _zoomMainMapToMouse;
        private KeyboardShortcut _zoomMainMapInShortcut;
        private KeyboardShortcut _zoomMainMapOutShortcut;
        
        private KeyboardShortcut _zoomMiniMapInShortcut;
        private KeyboardShortcut _zoomMiniMapOutShortcut;

        public static DmServerConfig ServerConfig;
        public class DmServerConfig
        {
            public bool AllowShowFriendlyPlayerMarkersInRaid;
            public bool AllowShowEnemyPlayerMarkersInRaid;
            public bool AllowShowBossMarkersInRaid;
            public bool AllowShowScavMarkersInRaid;
        }


        private float _zoomMapHotkeySpeed = 2.5f;
        
        #endregion
        
        internal static ModdedMapScreen Create(GameObject parent)
        {
            var go = UIUtils.CreateUIGameObject(parent, "ModdedMapBlock");
            return go.AddComponent<ModdedMapScreen>();
        }

        #region Unity Methods

        private void Awake()
        {
            // make our game object hierarchy
            var scrollRectGo = UIUtils.CreateUIGameObject(gameObject, "Scroll");
            var scrollMaskGo = UIUtils.CreateUIGameObject(scrollRectGo, "ScrollMask");

            _adjustMiniMapHandler = (_, _) => AdjustForMiniMap(false);
            Settings.MiniMapPosition.SettingChanged += _adjustMiniMapHandler;
            Settings.MiniMapScreenOffsetX.SettingChanged += _adjustMiniMapHandler;
            Settings.MiniMapScreenOffsetY.SettingChanged += _adjustMiniMapHandler;
            Settings.MiniMapSizeX.SettingChanged += _adjustMiniMapHandler;
            Settings.MiniMapSizeY.SettingChanged += _adjustMiniMapHandler;
            
            _mapView = MapView.Create(scrollMaskGo, "MapView");

            // set up mask; size will be set later in Raid/NoRaid
            var scrollMaskImage = scrollMaskGo.AddComponent<Image>();
            scrollMaskImage.color = new Color(0f, 0f, 0f, 0.5f);
            _scrollMask = scrollMaskGo.AddComponent<Mask>();

            // set up scroll rect
            _scrollRect = scrollRectGo.AddComponent<MapScrollRect>();
            _scrollRect.scrollSensitivity = 0;  // don't scroll on mouse wheel
            _scrollRect.movementType = ScrollRect.MovementType.Unrestricted;
            _scrollRect.viewport = _scrollMask.GetRectTransform();
            _scrollRect.content = _mapView.RectTransform;
            _scrollHandler = (_) => _mapView.ClampToMapBounds();
            _scrollRect.onValueChanged.AddListener(
                DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<Vector2>>(_scrollHandler));
            _scrollRect.OnBeginDragCallback = () =>
            {
                if (_mapView == null) 
                    return;
                
                _targetZoom = 0f;
                _mapView.CancelPositionTween();
            };
            // create map controls

            // level select slider
            var sliderPrefab = Singleton<CommonUI>.Instance.transform.Find("Common UI/InventoryScreen/Map Panel/MapBlock/ZoomScroll").gameObject;
            _levelSelectSlider = LevelSelectSlider.Create(sliderPrefab, RectTransform);
            _levelSelectSlider.OnLevelSelectedBySlider += _mapView.SelectTopLevel;
            _mapView.OnLevelSelected += (level) => _levelSelectSlider.SelectedLevel = level;
            _levelSelectSlider.gameObject.SetActive(false);

            // map select dropdown, this will call LoadMap on the first option
            var selectPrefab = Singleton<CommonUI>.Instance.transform.Find("Common UI/InventoryScreen/SkillsAndMasteringPanel/BottomPanel/SkillsPanel/Options/Filter").gameObject;
            _mapSelectDropdown = MapSelectDropdown.Create(selectPrefab, RectTransform);
            _mapSelectDropdown.OnMapSelected += ChangeMap;

            // texts
            _cursorPositionText = CursorPositionText.Create(gameObject, _mapView.RectTransform, PositionTextFontSize);
            _cursorPositionText.RectTransform.anchorMin = TextAnchor;
            _cursorPositionText.RectTransform.anchorMax = TextAnchor;

            _playerPositionText = PlayerPositionText.Create(gameObject, PositionTextFontSize);
            _playerPositionText.RectTransform.anchorMin = TextAnchor;
            _playerPositionText.RectTransform.anchorMax = TextAnchor;
            _playerPositionText.gameObject.SetActive(false);

            // read config before setting up marker providers
            ReadConfig();

            GameWorldOnDestroyPatch.OnRaidEnd += OnRaidEnd;
            
            // load initial maps from path
            _mapSelectDropdown.LoadMapDefsFromPath(MapRelPath);
        }

        private void OnDestroy()
        {
            if (_scrollRect != null)
            {
                _scrollRect.onValueChanged.RemoveListener(
                    DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<Vector2>>(_scrollHandler));
                _scrollRect.OnBeginDragCallback = null;
            }
            
            GameWorldOnDestroyPatch.OnRaidEnd -= OnRaidEnd;
            
            Settings.MiniMapPosition.SettingChanged -= _adjustMiniMapHandler;
            Settings.MiniMapScreenOffsetX.SettingChanged -= _adjustMiniMapHandler;
            Settings.MiniMapScreenOffsetY.SettingChanged -= _adjustMiniMapHandler;
            Settings.MiniMapSizeX.SettingChanged -= _adjustMiniMapHandler;
            Settings.MiniMapSizeY.SettingChanged -= _adjustMiniMapHandler;
        }

        private void Update()
        {
            RenderDiagnostics.ProcessPendingCapture();

            // because we have a scroll rect, it seems to eat OnScroll via IScrollHandler
            var scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                if (!_mapSelectDropdown.isActiveAndEnabled || !_mapSelectDropdown.IsDropdownOpen())
                {
                    OnScroll(scroll);
                }
            }
            
            OnScrollZoomUpdate();

            // change level hotkeys
            if (!ShowingMiniMap)
            {
                if (_moveMapLevelUpShortcut.BetterIsDown())
                {
                    _levelSelectSlider.ChangeLevelBy(1);
                }

                if (_moveMapLevelDownShortcut.BetterIsDown())
                {
                    _levelSelectSlider.ChangeLevelBy(-1);
                }
            }
            
            // shift hotkeys
            var shiftMapX = 0f;
            var shiftMapY = 0f;

            if (!ShowingMiniMap)
            {
                if (_moveMapUpShortcut.BetterIsPressed())
                {
                    shiftMapY += 1f;
                }

                if (_moveMapDownShortcut.BetterIsPressed())
                {
                    shiftMapY -= 1f;
                }

                if (_moveMapLeftShortcut.BetterIsPressed())
                {
                    shiftMapX -= 1f;
                }

                if (_moveMapRightShortcut.BetterIsPressed())
                {
                    shiftMapX += 1f;
                }
            }
            
            if (shiftMapX != 0f || shiftMapY != 0f)
            {
                _targetZoom = 0f;
                _mapView.CancelPositionTween();
                _mapView.ScaledShiftMap(new Vector2(shiftMapX, shiftMapY), _moveMapSpeed * Time.deltaTime, false);
                _mapView.ClampToMapBounds();
            }

            if (ShowingMiniMap)
            {
                OnZoomMini();
            }
            else
            {
                OnZoomMain();
            }
            
            OnCenter();
            
            if (_dumpShortcut.BetterIsDown())
            {
                DumpUtils.DumpExtracts();
                DumpUtils.DumpSwitches();
                DumpUtils.DumpLocks();
            }
        }

        // private void OnDisable()
        // {
        //     OnHide();
        // }

        #endregion

        #region Show And Hide Top Level

        internal void OnMapScreenShow()
        {
            if (_peekComponent is not null)
            {
                _peekComponent.WasMiniMapActive = ShowingMiniMap;
                
                _peekComponent?.EndPeek();
                _peekComponent?.EndMiniMap();
            }

            IsShowingMapScreen = true;
            
            transform.parent.Find("MapBlock").gameObject.SetActive(false);
            transform.parent.Find("EmptyBlock").gameObject.SetActive(false);
            transform.parent.gameObject.SetActive(true);
            transform.SetAsLastSibling();

            Show(false);
            // EFT resets nested canvas sorting while changing inventory panels.
            // Reapply it after the native screen transition, immediately before render.
            var mapCanvas = _mapView.GetComponent<Canvas>();
            mapCanvas.overrideSorting = true;
            mapCanvas.sortingOrder = 32767;
            mapCanvas.enabled = true;
            var visibilityGroup = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            visibilityGroup.alpha = 1f;
            visibilityGroup.ignoreParentGroups = true;
            DumpRenderState("map screen");

            if (GameUtils.IsInRaid())
            {
                Plugin.Instance.EnsurePeekAttached();
            }
        }

        // Temporary port diagnostics: log the whole UI state and take a screenshot, so we can
        // see whether the modded map is rendered and, if not, which part of it is off.
        internal void DumpRenderState(string reason)
        {
            try
            {
                Plugin.Log.LogInfo($"--- DynamicMaps render dump ({reason}) ---");
                Plugin.Log.LogInfo($"screen: {Screen.width}x{Screen.height}");
                Plugin.Log.LogInfo($"root '{gameObject.name}': {RenderDiagnostics.State(gameObject)}, {RenderDiagnostics.RectState(RectTransform)}, groupAlpha={gameObject.GetComponent<CanvasGroup>()?.alpha.ToString() ?? "none"}, map={_mapView.CurrentMapDef?.DisplayName}");
                Plugin.Log.LogInfo($"parent '{transform.parent?.name}': active={transform.parent != null && transform.parent.gameObject.activeInHierarchy}");
                Plugin.Log.LogInfo($"scroll: {RenderDiagnostics.State(_scrollRect.gameObject)}, {RenderDiagnostics.RectState(_scrollRect.GetRectTransform())}");
                Plugin.Log.LogInfo($"mask: {RenderDiagnostics.State(_scrollMask.gameObject)}, {RenderDiagnostics.RectState(_scrollMask.GetRectTransform())}, {RenderDiagnostics.MaskState(_scrollMask)}, image={RenderDiagnostics.ImageState(_scrollMask.GetComponent<Image>())}");
                Plugin.Log.LogInfo($"mapView: {RenderDiagnostics.State(_mapView.gameObject)}, {RenderDiagnostics.RectState(_mapView.RectTransform)}");

                var canvas = _mapView.GetComponent<Canvas>();
                Plugin.Log.LogInfo($"mapView canvas: enabled={canvas?.enabled}, override={canvas?.overrideSorting}, order={canvas?.sortingOrder}, root={canvas?.rootCanvas?.name}, rootOrder={canvas?.rootCanvas?.sortingOrder}, rootOverride={canvas?.rootCanvas?.overrideSorting}");

                var layers = _mapView.MapLayerContainer.GetComponentsInChildren<MapLayer>(true);
                Plugin.Log.LogInfo($"layers: {layers?.Length}");
                if (layers != null)
                {
                    foreach (var layer in layers)
                    {
                        Plugin.Log.LogInfo($"  layer '{layer.name}': level={layer.Level}, status={layer.Status}, {RenderDiagnostics.State(layer.gameObject)}, {RenderDiagnostics.RectState(layer.RectTransform)}, image={RenderDiagnostics.ImageState(layer.GetComponent<Image>())}");
                    }
                }

                var markers = _mapView.MapMarkerContainer.GetComponentsInChildren<MapMarker>(true);
                Plugin.Log.LogInfo($"markers: {markers?.Length}");
                if (markers != null)
                {
                    for (var i = 0; i < markers.Length && i < 12; i++)
                    {
                        var marker = markers[i];
                        var markerImage = marker.Image;
                        var markerSprite = markerImage != null ? markerImage.sprite : null;
                        var markerTexture = markerSprite != null ? markerSprite.texture : null;
                        var spriteName = markerSprite != null ? markerSprite.name : "<null>";
                        var textureSize = markerTexture != null ? $"{markerTexture.width}x{markerTexture.height}" : "<null>";
                        Plugin.Log.LogInfo($"  marker '{marker.Category}' text='{marker.Text}': image={(markerImage != null)}, sprite={spriteName}, texture={textureSize}, color={markerImage?.color}, active={marker.gameObject.activeInHierarchy}");
                    }
                }

                RenderDiagnostics.RequestCapture(reason.Replace(' ', '-'));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Render dump failed: {e.Message}");
            }
        }

        internal void OnMapScreenClose()
        {
            if (_rememberMapPosition)
            {
                _savedMainMapPos = _mapView.MainMapPos;
            }
            
            Hide();
            IsShowingMapScreen = false;
            
            if (_peekComponent is not null && _peekComponent.WasMiniMapActive)
            {
                _mapView.ApplyMiniMapZoom();
                _peekComponent.BeginMiniMap();
            }
        }

        internal void Show(bool playAnimation)
        {
            if (!_initialized)
            {
                //Plugin.Log.LogInfo("Map was not initialized, is resetting size and position");
                AdjustSizeAndPosition();
                _initialized = true;
            }
            
            _isShown = true;
            gameObject.SetActive(GameUtils.ShouldShowMapInRaid());

            // populate map select dropdown
            _mapSelectDropdown.LoadMapDefsFromPath(MapRelPath);

            if (GameUtils.IsInRaid())
            {
                // Plugin.Log.LogInfo("Showing map in raid");
                OnShowInRaid(playAnimation);
            }
            else
            {
                // Plugin.Log.LogInfo("Showing map out-of-raid");
                OnShowOutOfRaid();
            }
        }

        internal void Hide()
        {
            _mapSelectDropdown?.TryCloseDropdown();

            // close isn't called when hidden
            if (GameUtils.IsInRaid())
            {
                // Plugin.Log.LogInfo("Hiding map in raid");
                OnHideInRaid();
            }
            else
            {
                // Plugin.Log.LogInfo("Hiding map out-of-raid");
                OnHideOutOfRaid();
            }

            _isShown = false;
            gameObject.SetActive(false);
        }

        private void OnRaidEnd()
        {
            if (_peekComponent is null && !BattleUIScreenShowPatch.IsAttached) return;
            
            // Reset saved main map position
            _savedMainMapPos = Vector2.zero;
            _mapView.IsMiniMapActive = false;
            
            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnRaidEnd(_mapView);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in OnRaidEnd");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }

            // reset peek and remove reference, it will be destroyed very shortly with parent object
            if (_peekComponent is not null)
            {
                _peekComponent.EndPeek();
                _peekComponent.EndMiniMap();

                Destroy(_peekComponent.gameObject);
                _peekComponent = null;
            }

            // unload map completely when raid ends, since we've removed markers
            _mapView.UnloadMap();
        }
        
        #endregion
        
        #region Size And Positioning

        internal void SaveMainMapPos()
        {
            if (_rememberMapPosition)
            {
                _savedMainMapPos = _mapView.MainMapPos;
            }
        }
        
        private void AdjustSizeAndPosition()
        {
            // set width and height based on inventory screen
            var rect = Singleton<CommonUI>.Instance.InventoryScreen.GetRectTransform().rect;
            RectTransform.sizeDelta = new Vector2(rect.width, rect.height);
            RectTransform.anchoredPosition = Vector2.zero;

            _scrollRect.GetRectTransform().sizeDelta = RectTransform.sizeDelta;

            SetMaskLayout(RectTransform.sizeDelta + MaskSizeModifierOutOfRaid, MaskPositionOutOfRaid, MaskAnchorCenter, MaskAnchorCenter);

            _levelSelectSlider.RectTransform.anchoredPosition = LevelSliderPosition;

            _mapSelectDropdown.RectTransform.sizeDelta = MapSelectDropdownSize;
            _mapSelectDropdown.RectTransform.anchoredPosition = MapSelectDropdownPosition;

            _cursorPositionText.RectTransform.anchoredPosition = CursorPositionTextOffset;
            _playerPositionText.RectTransform.anchoredPosition = PlayerPositionTextOffset;
        }

        private void AdjustForOutOfRaid()
        {
            // adjust mask
            SetMaskLayout(RectTransform.sizeDelta + MaskSizeModifierOutOfRaid, MaskPositionOutOfRaid, MaskAnchorCenter, MaskAnchorCenter);

            // turn on cursor and off player position texts
            _cursorPositionText.gameObject.SetActive(true);
            _levelSelectSlider.gameObject.SetActive(true);
            _playerPositionText.gameObject.SetActive(false);
        }

        private void AdjustForInRaid(bool playAnimation)
        {
            // adjust mask (layout is applied instantly, see SetMaskLayout)
            SetMaskLayout(RectTransform.sizeDelta + MaskSizeModifierInRaid, MaskPositionInRaid, MaskAnchorCenter, MaskAnchorCenter);

            // turn both cursor and player position texts on
            _cursorPositionText.gameObject.SetActive(true);
            _playerPositionText.gameObject.SetActive(true);
            _levelSelectSlider.gameObject.SetActive(!ShowingMiniMap);
        }

        private void AdjustForPeek(bool playAnimation)
        {
            // adjust mask (layout is applied instantly, see SetMaskLayout)
            SetMaskLayout(RectTransform.sizeDelta, Vector2.zero, MaskAnchorCenter, MaskAnchorCenter);

            // turn both cursor and player position texts off
            _cursorPositionText.gameObject.SetActive(false);
            _playerPositionText.gameObject.SetActive(false);
            _levelSelectSlider.gameObject.SetActive(false);
        }

        private void AdjustForMiniMap(bool playAnimation)
        {
            var cornerPosition = ConvertEnumToScreenPos(Settings.MiniMapPosition.Value);

            var offset = new Vector2(Settings.MiniMapScreenOffsetX.Value, Settings.MiniMapScreenOffsetY.Value);
            offset *= ConvertEnumToScenePivot(Settings.MiniMapPosition.Value);

            var size = new Vector2(Settings.MiniMapSizeX.Value, Settings.MiniMapSizeY.Value);

            // adjust mask (layout is applied instantly, see SetMaskLayout)
            SetMaskLayout(size, offset, cornerPosition, cornerPosition);

            _cursorPositionText.gameObject.SetActive(false);
            _playerPositionText.gameObject.SetActive(false);
            _levelSelectSlider.gameObject.SetActive(false);
        }

        private static readonly Vector2 MaskAnchorCenter = new Vector2(0.5f, 0.5f);

        /// <summary>
        /// Applies the mask layout immediately instead of tweening it: on this build the tweened
        /// geometry did not take effect reliably, so the mini-map kept the full screen mask (and
        /// covered the whole view) while the full screen states kept the mini-map anchors.
        /// </summary>
        private void SetMaskLayout(Vector2 size, Vector2 anchoredPosition, Vector2 anchor, Vector2 pivot)
        {
            var maskRect = _scrollMask.GetRectTransform();

            maskRect.anchorMin = anchor;
            maskRect.anchorMax = anchor;
            maskRect.pivot = pivot;
            maskRect.sizeDelta = size;
            maskRect.anchoredPosition = anchoredPosition;
        }

        private Vector2 ConvertEnumToScreenPos(EMiniMapPosition pos)
        {
            // 0,0 Bottom left
            // 0,1 Top left
            // 1,1 Top right
            // 1,0 Bottom right
            
            switch (pos)
            {
                case EMiniMapPosition.TopRight:
                    return new Vector2(1, 1);
                
                case EMiniMapPosition.BottomRight:
                    return new Vector2(1, 0);
                
                case EMiniMapPosition.TopLeft:
                    return new Vector2(0, 1);
                
                case EMiniMapPosition.BottomLeft:
                    return new Vector2(0, 0);
            }

            return Vector2.zero;
        }

        private Vector2 ConvertEnumToScenePivot(EMiniMapPosition pos)
        {
            // Top right = neg neg
            // Bottom right = neg pos
            // Top left = pos neg
            // Bottom left = pos pos
            
            switch (pos)
            {
                case EMiniMapPosition.TopRight:
                    return new Vector2(-1, -1);
                
                case EMiniMapPosition.BottomRight:
                    return new Vector2(-1, 1);
                
                case EMiniMapPosition.TopLeft:
                    return new Vector2(1, -1);
                
                case EMiniMapPosition.BottomLeft:
                    return new Vector2(1, 1);
            }

            return Vector2.zero;
        }
        
        #endregion

        #region Show And Hide Bottom Level

        private void OnShowInRaid(bool playAnimation)
        {
            if (ShowingMiniMap)
            {
                AdjustForMiniMap(playAnimation);
            }
            else if (IsPeeking)
            {
                AdjustForPeek(playAnimation);
            }
            else
            {
                AdjustForInRaid(playAnimation);
            }
            
            // filter dropdown to only maps containing the internal map name
            var mapInternalName = GameUtils.GetCurrentMapInternalName();
            _mapSelectDropdown.FilterByInternalMapName(mapInternalName);
            _mapSelectDropdown.LoadFirstAvailableMap();
            
            if (ShowingMiniMap)
            {
                _levelSelectSlider.gameObject.SetActive(false);
            }

            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnShowInRaid(_mapView);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in OnShowInRaid");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }

            // rest of this function needs player
            var player = GameUtils.GetMainPlayer();
            if (player is null)
            {
                return;
            }

            var mapPosition = MathUtils.ConvertToMapPosition(((IPlayer)player).Position);

            // select layers to show
            if (_autoSelectLevel)
            {
                _mapView.SelectLevelByCoords(mapPosition);
            }

            // Don't set the map position if we're the mini-map, otherwise it can cause artifacting
            if (_rememberMapPosition && !ShowingMiniMap && _mapView.MainMapPos != Vector2.zero)
            {
                _mapView.ApplyMainMapZoom();
    
                var tweenTime = _transitionAnimations && _mapView.MainMapPos != _savedMainMapPos ? 0.35f : 0f;
                _mapView.SetMapPos(_savedMainMapPos, tweenTime);
                return;
            }
            
            if (!_rememberMapPosition && !_autoCenterOnPlayerMarker && !ShowingMiniMap)
            {
                _mapView.ApplyMainMapZoom();
                _mapView.SetMapZoom(_mapView.ZoomMin, 0f);
                return;
            }
            
            // Auto centering while the minimap is active here can cause artifacting
            if (_autoCenterOnPlayerMarker && !ShowingMiniMap)
            {
                // Reset the zoom level
                _mapView.ApplyMainMapZoom();
                if (_resetZoomOnCenter && !ShowingMiniMap)
                {
                    // change zoom to desired level
                    _mapView.SetMapZoom(GetInRaidStartingZoom(), 0);
                }
                // shift map to player position, Vector3 to Vector2 discards z
                _mapView.ShiftMapToPlayer(mapPosition, 0, false);
            }
        }

        private void OnHideInRaid()
        {
            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnHideInRaid(_mapView);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in OnHideInRaid");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }
        }

        private void OnShowOutOfRaid()
        {
            AdjustForOutOfRaid();

            // clear filter on dropdown
            _mapSelectDropdown.ClearFilter();

            // load first available map if no maps loaded
            if (_mapView.CurrentMapDef == null)
            {
                _mapSelectDropdown.LoadFirstAvailableMap();
            }

            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnShowOutOfRaid(_mapView);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in OnShowOutOfRaid");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }
        }

        private void OnHideOutOfRaid()
        {
            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnHideOutOfRaid(_mapView);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in OnHideOutOfRaid");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }
        }

        #endregion
        
        #region Map Manipulation
        
        private void OnScroll(float scrollAmount)
        {
            if (IsPeeking || ShowingMiniMap)
            {
                return;
            }

            if (Input.GetKey(KeyCode.LeftShift))
            {
                if (scrollAmount > 0)
                {
                    _levelSelectSlider.ChangeLevelBy(1);
                }
                else
                {
                    _levelSelectSlider.ChangeLevelBy(-1);
                }
                return;
            }

            // Initialize target zoom if not set
            if (_targetZoom == 0f)
            {
                _targetZoom = _mapView.ZoomMain;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapView.RectTransform, Input.mousePosition, null, out Vector2 mouseRelative);

            _targetZoom = Mathf.Clamp(
                _targetZoom + scrollAmount * _targetZoom * ScrollZoomScaler,
                _mapView.ZoomMin,
                _mapView.ZoomMax);

            _zoomFocusPoint = mouseRelative;
        }
        
        private void OnScrollZoomUpdate()
        {
            // Return early if there's nothing to change, this is in Update
            if (_targetZoom == 0f || IsPeeking || ShowingMiniMap) 
                return;

            var currentZoom = _mapView.ZoomMain;
            if (Mathf.Abs(currentZoom - _targetZoom) < 0.0001f)
            {
                _targetZoom = 0f;
                return;
            }

            var newZoom = Mathf.Lerp(currentZoom, _targetZoom, ScrollZoomLerpSpeed * Time.deltaTime);
            var zoomDelta = newZoom - currentZoom;

            _mapView.IncrementalZoomInto(zoomDelta, _zoomFocusPoint, 0f);
        }

        private void OnZoomMain()
        {
            var zoomAmount = 0f;
            
            if (_zoomMainMapOutShortcut.BetterIsPressed())
            {
                zoomAmount -= 1f;
            }

            if (_zoomMainMapInShortcut.BetterIsPressed())
            {
                zoomAmount += 1f;
            }
            
            if (zoomAmount != 0f)
            {
                // Cancel any existing scroll zoom
                _targetZoom = 0f;
                
                zoomAmount = _mapView.ZoomMain * zoomAmount * (_zoomMapHotkeySpeed * Time.deltaTime);
                if (IsPeeking)
                {
                    var player = GameUtils.GetMainPlayer();
                    var mapPosition = MathUtils.ConvertToMapPosition(((IPlayer)player).Position);
                    _mapView.IncrementalZoomInto(zoomAmount, mapPosition, 0f);
                }
                else
                {
                    if (_zoomMainMapToMouse)
                    {
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            _mapView.RectTransform, Input.mousePosition, null, out Vector2 mouseRelative);
                        _mapView.IncrementalZoomInto(zoomAmount, mouseRelative, 0f);
                    }
                    else
                    {
                        var currentCenter = _mapView.RectTransform.anchoredPosition / _mapView.ZoomMain;
                        _mapView.IncrementalZoomInto(zoomAmount, currentCenter, 0f);
                    }
                }
        
                return;
            }
            
            _mapView.SetMapZoom(_mapView.ZoomMain, 0f);
        }

        private void OnZoomMini()
        {
            var zoomAmount = 0f;
            
            if (_zoomMiniMapOutShortcut.BetterIsPressed())
            {
                zoomAmount -= 1f;
            }
            
            if (_zoomMiniMapInShortcut.BetterIsPressed())
            {
                zoomAmount += 1f;
            }
            
            if (zoomAmount != 0f)
            {
                // Cancel any existing scroll zoom
                _targetZoom = 0f;
                
                var player = GameUtils.GetMainPlayer();
                var mapPosition = MathUtils.ConvertToMapPosition(((IPlayer)player).Position);
                zoomAmount = _mapView.ZoomMini * zoomAmount * (_zoomMapHotkeySpeed * Time.deltaTime);
                    
                _mapView.IncrementalZoomIntoMiniMap(zoomAmount, mapPosition, 0.0f);
                
                return;
            }
            
            _mapView.SetMapZoom(_mapView.ZoomMini, 0f, false, true);
        }

        private void OnCenter()
        {
            if (_centerPlayerShortcut.BetterIsDown())
            {
                CenterOnPlayer(false);
                return;
            }
    
            if (ShowingMiniMap)
            {
                _miniMapUpdateTimer -= Time.deltaTime;
                if (_miniMapUpdateTimer > 0f)
                {
                    return;
                }
                _miniMapUpdateTimer = _miniMapUpdateInterval;
                CenterOnPlayer(true);
            }
        }

        private void CenterOnPlayer(bool isMini)
        {
            var player = GameUtils.GetMainPlayer();
            if (player is null)
            {
                return;
            }
    
            var mapPosition = MathUtils.ConvertToMapPosition(((IPlayer)player).Position);
            _mapView.ShiftMapToCoordinate(mapPosition, isMini ? 0f : PositionTweenTime, isMini);
            _mapView.SelectLevelByCoords(mapPosition);
        }

        #endregion

        #region Config and Marker Providers

        internal void ReadConfig()
        {
            // MainMenuShowOperation.Init loads server settings asynchronously, while the
            // CommonUI (and this screen) may be created before that request completes.
            ServerConfig ??= new DmServerConfig();

            _centerPlayerShortcut = Settings.CenterOnPlayerHotkey.Value;
            _dumpShortcut = Settings.DumpInfoHotkey.Value;

            _moveMapUpShortcut = Settings.MoveMapUpHotkey.Value;
            _moveMapDownShortcut = Settings.MoveMapDownHotkey.Value;
            _moveMapLeftShortcut = Settings.MoveMapLeftHotkey.Value;
            _moveMapRightShortcut = Settings.MoveMapRightHotkey.Value;
            _moveMapSpeed = Settings.MapMoveHotkeySpeed.Value;

            _moveMapLevelUpShortcut = Settings.ChangeMapLevelUpHotkey.Value;
            _moveMapLevelDownShortcut = Settings.ChangeMapLevelDownHotkey.Value;

            _zoomMainMapInShortcut = Settings.ZoomMapInHotkey.Value;
            _zoomMainMapOutShortcut = Settings.ZoomMapOutHotkey.Value;
            
            _zoomMiniMapInShortcut = Settings.ZoomInMiniMapHotkey.Value;
            _zoomMiniMapOutShortcut = Settings.ZoomOutMiniMapHotkey.Value;
            
            _zoomMapHotkeySpeed = Settings.ZoomMapHotkeySpeed.Value;

            _zoomMainMapToMouse = Settings.ZoomMainMapToMouse.Value;

            _autoCenterOnPlayerMarker = Settings.AutoCenterOnPlayerMarker.Value;
            _resetZoomOnCenter = Settings.ResetZoomOnCenter.Value;
            _rememberMapPosition = Settings.RetainMapPosition.Value;
            
            _autoSelectLevel = Settings.AutoSelectLevel.Value;
            _centeringZoomResetPoint = Settings.CenteringZoomResetPoint.Value;

            _transitionAnimations = Settings.MapTransitionEnabled.Value;
            
            if (_peekComponent is not null)
            {
                _peekComponent.PeekShortcut = Settings.PeekShortcut.Value;
                _peekComponent.HoldForPeek = Settings.HoldForPeek.Value;
                _peekComponent.HideMinimapShortcut = Settings.MiniMapShowOrHide.Value;
            }

            AddRemoveMarkerProvider<PlayerMarkerProvider>(Settings.ShowPlayerMarker.Value);
            AddRemoveMarkerProvider<QuestMarkerProvider>(Settings.ShowQuestsInRaid.Value);
            AddRemoveMarkerProvider<LockedDoorMarkerMutator>(Settings.ShowLockedDoorStatus.Value);
            AddRemoveMarkerProvider<BackpackMarkerProvider>(Settings.ShowDroppedBackpackInRaid.Value);
            AddRemoveMarkerProvider<BTRMarkerProvider>(Settings.ShowBTRInRaid.Value);
            AddRemoveMarkerProvider<AirdropMarkerProvider>(Settings.ShowAirdropsInRaid.Value);
            AddRemoveMarkerProvider<LootMarkerProvider>(Settings.ShowWishListItemsInRaid.Value);
            AddRemoveMarkerProvider<HiddenStashMarkerProvider>(Settings.ShowHiddenStashesInRaid.Value);
            AddRemoveMarkerProvider<TransitMarkerProvider>(Settings.ShowTransitPointsInRaid.Value);
            AddRemoveMarkerProvider<SecretMarkerProvider>(Settings.ShowSecretPointsInRaid.Value);
            
            if (Settings.ShowAirdropsInRaid.Value)
            {
                GetMarkerProvider<AirdropMarkerProvider>()
                    .RefreshMarkers();
            }

            if (Settings.ShowWishListItemsInRaid.Value)
            {
                GetMarkerProvider<LootMarkerProvider>()
                    .RefreshMarkers();
            }

            if (Settings.ShowHiddenStashesInRaid.Value)
            {
                GetMarkerProvider<HiddenStashMarkerProvider>()
                    .RefreshMarkers();
            }

            // Transits
            if (Settings.ShowTransitPointsInRaid.Value)
            {
                GetMarkerProvider<TransitMarkerProvider>()
                    .RefreshMarkers(_mapView);
            }

            // Secret Exfils
            AddRemoveMarkerProvider<SecretMarkerProvider>(Settings.ShowSecretPointsInRaid.Value);
            if (Settings.ShowSecretPointsInRaid.Value)
            {
                var provider = GetMarkerProvider<SecretMarkerProvider>();
                provider.ShowExtractStatusInRaid = Settings.ShowExtractStatusInRaid.Value;
            }

            // Exfils
            AddRemoveMarkerProvider<ExtractMarkerProvider>(Settings.ShowExtractsInRaid.Value);
            if (Settings.ShowExtractsInRaid.Value)
            {
                var provider = GetMarkerProvider<ExtractMarkerProvider>();
                provider.ShowExtractStatusInRaid = Settings.ShowExtractStatusInRaid.Value;
            }

            // other player markers
            var needOtherPlayerMarkers = Settings.ShowFriendlyPlayerMarkersInRaid.Value
                                      || Settings.ShowEnemyPlayerMarkersInRaid.Value
                                      || Settings.ShowBossMarkersInRaid.Value
                                      || Settings.ShowScavMarkersInRaid.Value;

            AddRemoveMarkerProvider<OtherPlayersMarkerProvider>(needOtherPlayerMarkers);
            
            if (needOtherPlayerMarkers)
            {
                var provider = GetMarkerProvider<OtherPlayersMarkerProvider>();
                provider.ShowFriendlyPlayers = ServerConfig.AllowShowFriendlyPlayerMarkersInRaid && Settings.ShowFriendlyPlayerMarkersInRaid.Value;
                provider.ShowEnemyPlayers = ServerConfig.AllowShowEnemyPlayerMarkersInRaid && Settings.ShowEnemyPlayerMarkersInRaid.Value;
                provider.ShowScavs = ServerConfig.AllowShowScavMarkersInRaid && Settings.ShowScavMarkersInRaid.Value;
                provider.ShowBosses = ServerConfig.AllowShowBossMarkersInRaid && Settings.ShowBossMarkersInRaid.Value;
                
                provider.RefreshMarkers();
            }

            // corpse markers
            var needCorpseMarkers = Settings.ShowFriendlyCorpsesInRaid.Value
                                 || Settings.ShowKilledCorpsesInRaid.Value
                                 || Settings.ShowFriendlyKilledCorpsesInRaid.Value
                                 || Settings.ShowBossCorpsesInRaid.Value
                                 || Settings.ShowOtherCorpsesInRaid.Value;

            AddRemoveMarkerProvider<CorpseMarkerProvider>(needCorpseMarkers);
            AddRemoveMarkerProvider<ContainerLootMarkerProvider>(Settings.ShowCorpseLootInRaid.Value
                                                            || Settings.ShowContainerLootInRaid.Value);
            if (needCorpseMarkers)
            {
                var provider = GetMarkerProvider<CorpseMarkerProvider>();
                provider.ShowFriendlyCorpses = Settings.ShowFriendlyCorpsesInRaid.Value;
                provider.ShowKilledCorpses = Settings.ShowKilledCorpsesInRaid.Value;
                provider.ShowFriendlyKilledCorpses = Settings.ShowFriendlyKilledCorpsesInRaid.Value;
                provider.ShowBossCorpses = Settings.ShowBossCorpsesInRaid.Value;
                provider.ShowOtherCorpses = Settings.ShowOtherCorpsesInRaid.Value;
                
                provider.RefreshMarkers();
            }
            
            if (ModDetection.HeliCrashLoaded)
            {
                AddRemoveMarkerProvider<HeliCrashMarkerProvider>(Settings.ShowHeliCrashMarker.Value);
            }
        }

        internal void TryAddPeekComponent(EftBattleUIScreen battleUI)
        {
            // Peek component already instantiated, return
            if (_peekComponent is not null)
            {
                return;
            }

            Plugin.Log.LogInfo("Trying to attach peek component to BattleUI");

            _peekComponent = MapPeekComponent.Create(battleUI.gameObject);
            _peekComponent.MapScreen = this;
            _peekComponent.MapScreenTrueParent = ParentTransform;

            ReadConfig();
        }
        
        public void AddRemoveMarkerProvider<T>(bool status) where T : IDynamicMarkerProvider, new()
        {
            if (status && !_dynamicMarkerProviders.ContainsKey(typeof(T)))
            {
                _dynamicMarkerProviders[typeof(T)] = new T();

                // if the map is shown, need to call OnShowXXXX
                if (_isShown && GameUtils.IsInRaid())
                {
                    _dynamicMarkerProviders[typeof(T)].OnShowInRaid(_mapView);
                }
                else if (_isShown && !GameUtils.IsInRaid())
                {
                    _dynamicMarkerProviders[typeof(T)].OnShowOutOfRaid(_mapView);
                }
            }
            else if (!status && _dynamicMarkerProviders.ContainsKey(typeof(T)))
            {
                _dynamicMarkerProviders[typeof(T)].OnDisable(_mapView);
                _dynamicMarkerProviders.Remove(typeof(T));
            }
        }

        private T GetMarkerProvider<T>() where T : IDynamicMarkerProvider
        {
            if (!_dynamicMarkerProviders.ContainsKey(typeof(T)))
            {
                return default;
            }

            return (T)_dynamicMarkerProviders[typeof(T)];
        }

        /// <summary>
        /// 1 Hz refresh for markers that track live game state (corpse loot).
        /// </summary>
        internal void TickDynamicMarkers()
        {
            if (_dynamicMarkerProviders.TryGetValue(typeof(ContainerLootMarkerProvider), out var provider)
                && provider is ContainerLootMarkerProvider containerLootProvider)
            {
                containerLootProvider.Tick();
            }
        }

        #endregion

        #region Utils And Caching

        private float GetInRaidStartingZoom()
        {
            var startingZoom = _mapView.ZoomMin;
            startingZoom += _centeringZoomResetPoint * (_mapView.ZoomMax - _mapView.ZoomMin);

            return startingZoom;
        }

        private void ChangeMap(MapDef mapDef)
        {
            if (mapDef == null || _mapView.CurrentMapDef == mapDef)
            {
                return;
            }

            Plugin.Log.LogInfo($"MapScreen: Loading map {mapDef.DisplayName}");

            // Reset size and position when loading map and in raid
            if (GameUtils.IsInRaid())
            {
                // Plugin.Log.LogInfo($"MapScreen: Resetting Map Size");
                AdjustSizeAndPosition();
            }
            
            _mapView.CancelPositionTween();
            _targetZoom = 0f;
            _savedMainMapPos = Vector2.zero;
            
            _mapView.LoadMap(mapDef, _scrollMask.GetRectTransform());

            // Loading a map resets the mask to the full screen layout (AdjustSizeAndPosition above),
            // which made the first mini-map of a raid cover the whole screen until the next peek
            // cycle. Put the layout of the active mode back after the load.
            if (GameUtils.IsInRaid())
            {
                if (ShowingMiniMap)
                {
                    AdjustForMiniMap(false);
                }
                else if (IsPeeking)
                {
                    AdjustForPeek(false);
                }
            }

            _mapSelectDropdown.OnLoadMap(mapDef);
            _levelSelectSlider.OnLoadMap(mapDef, _mapView.SelectedLevel);

            foreach (var dynamicProvider in _dynamicMarkerProviders.Values)
            {
                try
                {
                    dynamicProvider.OnMapChanged(_mapView, mapDef);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Dynamic marker provider {dynamicProvider} threw exception in ChangeMap");
                    Plugin.Log.LogError($"  Exception given was: {e.Message}");
                    Plugin.Log.LogError($"  {e.StackTrace}");
                }
            }
        }

        #endregion
    }
}
