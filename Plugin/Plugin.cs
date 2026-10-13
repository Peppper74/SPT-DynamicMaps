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

        public override void Load()
        {
            Instance = this;

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
        /// Attach to the map screen
        /// </summary>
        internal void TryAttachToMapScreen(MapScreen mapScreen)
        {
            if (Map is not null || mapScreen == null) return;
            
            Log.LogInfo("Trying to attach to MapScreen");

            // attach to common UI first to call awake and set things up, then attach to sleeping map screen
            Map = ModdedMapScreen.Create(Singleton<CommonUI>.Instance.gameObject);
            Map.transform.SetParent(mapScreen.transform);
        }

        /// <summary>
        /// Attach the peek component
        /// </summary>
        internal void TryAttachToBattleUIScreen(EftBattleUIScreen battleUI)
        {
            if (Map is null || GameUtils.GetMainPlayer() is HideoutPlayer)
            {
                return;
            }
            
            Map.TryAddPeekComponent(battleUI);
        }

    }
}
