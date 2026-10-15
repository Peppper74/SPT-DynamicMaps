using BepInEx;
using BepInEx.Unity.IL2CPP;
using Comfort.Common;
using DrakiaXYZ.VersionChecker;
using DynamicMaps.Config;
using DynamicMaps.Patches;
using DynamicMaps.UI;
using DynamicMaps.Utils;
using EFT;
using EFT.UI;
using EFT.UI.Map;
using BepInEx.Logging;
using System;
using System.Reflection;
using DynamicMaps.UI.Components;
using DynamicMaps.UI.Controls;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace DynamicMaps
{
    // the version number here is generated on build and may have a warning if not yet built
    [BepInPlugin("com.mpstark.dynamicmaps", "DynamicMaps", BuildInfo.Version)]
    [BepInDependency("com.sptushonka.custom", "5.0.0")]
    [BepInDependency("com.SamSWAT.HeliCrash.ArysReloaded", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.fika.headless", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BasePlugin
    {
        public const int TarkovVersion = 47965;
        public static Plugin Instance;
        public new static ManualLogSource Log => ((BasePlugin)Instance).Log;
        public static string Path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        public ModdedMapScreen Map;
        private bool _watcherCreated;

        public override void Load()
        {
            Instance = this;

            ClassInjector.RegisterTypeInIl2Cpp<DynamicMapsWatcher>();
            ClassInjector.RegisterTypeInIl2Cpp<ModdedMapScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<MapView>();
            ClassInjector.RegisterTypeInIl2Cpp<MapLayer>();
            ClassInjector.RegisterTypeInIl2Cpp<MapLabel>();
            ClassInjector.RegisterTypeInIl2Cpp<MapMarker>(new RegisterTypeOptions
            {
                Interfaces = new[] { typeof(UnityEngine.EventSystems.IPointerEnterHandler), typeof(UnityEngine.EventSystems.IPointerExitHandler) }
            });
            ClassInjector.RegisterTypeInIl2Cpp<TransformMapMarker>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerMapMarker>();
            ClassInjector.RegisterTypeInIl2Cpp<MapScrollRect>();
            ClassInjector.RegisterTypeInIl2Cpp<MapPeekComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<MapSelectDropdown>();
            ClassInjector.RegisterTypeInIl2Cpp<LevelSelectSlider>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerPositionText>();
            ClassInjector.RegisterTypeInIl2Cpp<CursorPositionText>();

            if (!VersionChecker.CheckEftVersion(Log, Config))
            {
                return;
            }

            ExternalModSupport.ModDetection.CheckforMods();

            Settings.Init(Config);
            Config.SettingChanged += (x, y) => Map?.ReadConfig();

            // Logger.LogWarning("TEST BUILD OF DYNAMIC MAPS, NO SUPPORT OFFERED.");
            
            // patches
            new BattleUIScreenShowPatch().Enable();
            new CommonUIAwakePatch().Enable();
            new MapScreenShowPatch().Enable();
            new MapScreenClosePatch().Enable();
            new GameStartedPatch().Enable();
            new GameWorldUnregisterPlayerPatch().Enable();
            new LootItemInitPatch().Enable();
            new GameWorldDestroyLootPatch().Enable();
            new AirdropBoxOnBoxLandPatch().Enable();
            new PlayerOnDeadPatch().Enable();
            new PlayerInventoryThrowItemPatch().Enable();
            new ShowViewButtonPatch().Enable();
            new MenuLoadPatch().Enable();
        }

        /// <summary>
        /// The watcher has to be created once the game is up and running: an object created
        /// during plugin load does not survive the first scene load on this build.
        /// </summary>
        internal void EnsureWatcher()
        {
            if (_watcherCreated)
            {
                return;
            }

            _watcherCreated = true;

            try
            {
                DynamicMapsWatcher.Create();
                Log.LogInfo("DynamicMaps watcher started");
            }
            catch (Exception e)
            {
                Log.LogError("Could not start the DynamicMaps watcher");
                Log.LogError($"  Exception given was: {e.Message}");
            }
        }

        /// <summary>
        /// Attach the peek / mini-map component as soon as the raid HUD is on screen, no matter
        /// which hook noticed it first (the BattleUI patch does not fire on this game build).
        /// </summary>
        internal void EnsurePeekAttached()
        {
            if (Map is null || Map.HasPeekComponent)
            {
                return;
            }

            if (GameUtils.GetMainPlayer() is HideoutPlayer)
            {
                return;
            }

            var battleUI = FindBattleUIScreen();
            if (battleUI is null)
            {
                return;
            }

            Log.LogInfo("DynamicMaps: raid HUD found, attaching peek/mini-map component");
            TryAttachToBattleUIScreen(battleUI);
        }

        /// <summary>
        /// Attach to the map screen
        /// </summary>
        internal void TryAttachToMapScreen(MapScreen mapScreen)
        {
            if (mapScreen == null) return;

            if (Map is null)
            {
                Log.LogInfo("Trying to attach to MapScreen");

                try
                {
                    // attach to common UI first to call awake and set things up, then attach to sleeping map screen
                    Map = ModdedMapScreen.Create(Singleton<CommonUI>.Instance.gameObject);
                }
                catch
                {
                    // drop a half-built screen so a retry starts from a clean state
                    var leftovers = Singleton<CommonUI>.Instance.GetComponentsInChildren<ModdedMapScreen>(true);
                    if (leftovers is not null)
                    {
                        foreach (var leftover in leftovers)
                        {
                            if (leftover is not null)
                            {
                                UnityEngine.Object.Destroy(leftover.gameObject);
                            }
                        }
                    }

                    throw;
                }
            }

            // keep the modded block under the vanilla map screen unless the peek component owns it right now
            if (!Map.IsPeeked && Map.transform.parent != mapScreen.transform)
            {
                Map.transform.SetParent(mapScreen.transform);
            }
        }

        /// <summary>
        /// Attach the peek component
        /// </summary>
        internal void TryAttachToBattleUIScreen(EftBattleUIScreen battleUI)
        {
            if (battleUI == null || GameUtils.GetMainPlayer() is HideoutPlayer)
            {
                return;
            }

            // The peek component lives on the raid HUD and needs the modded map screen to
            // exist, so create the map screen on demand here as well: it used to be attached
            // when the vanilla map screen first showed, which never happens if that hook misses.
            if (Map is null)
            {
                var mapScreen = FindMapScreen();
                if (mapScreen is null)
                {
                    Log.LogWarning("DynamicMaps: could not find the MapScreen to attach to; peek/mini-map not available yet");
                    return;
                }

                TryAttachToMapScreen(mapScreen);
            }

            Map.TryAddPeekComponent(battleUI);
            BattleUIScreenShowPatch.IsAttached = true;
        }

        /// <summary>
        /// Find the vanilla map screen, including while it is inactive
        /// </summary>
        internal static MapScreen FindMapScreen()
        {
            var commonUI = Singleton<CommonUI>.Instance;
            if (commonUI is not null)
            {
                var inCommonUi = commonUI.GetComponentsInChildren<MapScreen>(true);
                if (inCommonUi is not null && inCommonUi.Length > 0)
                {
                    return inCommonUi[0];
                }
            }

            var all = Resources.FindObjectsOfTypeAll<MapScreen>();
            if (all is not null && all.Length > 0)
            {
                return all[0];
            }

            return null;
        }

        /// <summary>
        /// Find the active raid HUD screen (used when the BattleUI hook did not fire)
        /// </summary>
        internal static EftBattleUIScreen FindBattleUIScreen()
        {
            var all = Resources.FindObjectsOfTypeAll<EftBattleUIScreen>();
            if (all is null)
            {
                return null;
            }

            foreach (var screen in all)
            {
                if (screen is not null && screen.gameObject.activeInHierarchy)
                {
                    return screen;
                }
            }

            return null;
        }

    }
}
