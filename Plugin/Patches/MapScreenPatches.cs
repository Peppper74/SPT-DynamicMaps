using System;
using System.Reflection;
using SPTushonka.Reflection.Patching;
using DynamicMaps.Config;
using DynamicMaps.Utils;
using EFT.UI.Map;
using HarmonyLib;

namespace DynamicMaps.Patches
{
    internal class MapScreenShowPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MapScreen), nameof(MapScreen.Show));
        }

        [PatchPostfix]
        public static void PatchPostfix(MapScreen __instance)
        {
            try
            {
                Plugin.Instance?.EnsureWatcher();

                Plugin.Log.LogInfo($"MapScreen.Show patch fired; modAttached={Plugin.Instance.Map != null}");

                // Let EFT perform its SPT 5 screen activation first, then replace its map contents.
                if (Plugin.Instance.Map == null && __instance != null)
                {
                    Plugin.Instance.TryAttachToMapScreen(__instance);
                }

                if (!Settings.ReplaceMapScreen.Value || !GameUtils.ShouldShowMapInRaid())
                {
                    Plugin.Instance.Map?.OnMapScreenClose();
                    return;
                }

                Plugin.Instance.Map?.OnMapScreenShow();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Caught error while trying to show map");
                Plugin.Log.LogError($"{e.Message}");
                Plugin.Log.LogError($"{e.StackTrace}");
            }
        }
    }

    internal class MapScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MapScreen), nameof(MapScreen.Close));
        }

        [PatchPostfix]
        public static void PatchPostfix()
        {
            try
            {
                Plugin.Log.LogInfo($"MapScreen.Close patch fired; modAttached={Plugin.Instance.Map != null}");

                if (Settings.ReplaceMapScreen.Value && GameUtils.ShouldShowMapInRaid())
                {
                    Plugin.Instance.Map?.OnMapScreenClose();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Caught error while trying to close map");
                Plugin.Log.LogError($"{e.Message}");
                Plugin.Log.LogError($"{e.StackTrace}");
            }
        }
    }
}
