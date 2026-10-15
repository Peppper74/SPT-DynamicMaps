using System;
using System.Reflection;
using Comfort.Common;
using DynamicMaps.Config;
using DynamicMaps.Utils;
using SPTushonka.Reflection.Patching;
using EFT.UI;
using EFT.UI.Map;
using HarmonyLib;

namespace DynamicMaps.Patches
{
    internal class CommonUIAwakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // CommonUI.Awake runs before InventoryScreen has created its tab screens;
            // at that point InventoryScreen._mapScreen is still null on SPT 5.
            return AccessTools.Method(typeof(InventoryScreen), "Awake");
        }

        private static bool _loggedMissingMapScreen;

        [PatchPostfix]
        public static void PatchPostfix(InventoryScreen __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }

                Plugin.Instance.EnsureWatcher();

                var mapScreen = FindMapScreen(__instance);
                if (mapScreen == null)
                {
                    // Not fatal: on this build the map screen is created lazily, and the mod
                    // attaches as soon as it exists (map screen patch or the watcher).
                    if (!_loggedMissingMapScreen)
                    {
                        _loggedMissingMapScreen = true;
                        Plugin.Log.LogInfo("DynamicMaps: MapScreen does not exist yet during InventoryScreen.Awake; waiting for it to appear.");
                    }

                    return;
                }

                Plugin.Instance.TryAttachToMapScreen(mapScreen);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Caught error while trying to attach to the map screen");
                Plugin.Log.LogError($"{e.Message}");
                Plugin.Log.LogError($"{e.StackTrace}");
            }
        }

        private static MapScreen FindMapScreen(InventoryScreen inventoryScreen)
        {
            // The IL2CPP interop assembly exposes the game field _mapScreen as a property.
            try
            {
                var property = AccessTools.Property(typeof(InventoryScreen), "_mapScreen");
                var fromProperty = property?.GetValue(inventoryScreen) as MapScreen;
                if (fromProperty != null)
                {
                    return fromProperty;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read InventoryScreen._mapScreen property: {e.Message}");
            }

            try
            {
                var field = AccessTools.Field(typeof(InventoryScreen), "_mapScreen");
                var fromField = field?.GetValue(inventoryScreen) as MapScreen;
                if (fromField != null)
                {
                    return fromField;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Could not read InventoryScreen._mapScreen field: {e.Message}");
            }

            return Plugin.FindMapScreen();
        }
    }
}
