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

        [PatchPostfix]
        public static void PatchPostfix(InventoryScreen __instance)
        {
            if (__instance == null)
            {
                return;
            }

            var mapScreen = Traverse.Create(__instance).Field("_mapScreen").GetValue<MapScreen>();
            if (mapScreen == null)
            {
                Plugin.Log.LogWarning("InventoryScreen.Awake completed without a MapScreen; skipping DynamicMaps attachment.");
                return;
            }

            Plugin.Instance.TryAttachToMapScreen(mapScreen);
        }
    }
}
