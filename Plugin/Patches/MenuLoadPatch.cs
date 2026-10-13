using DynamicMaps.UI;
using DynamicMaps.Utils;
using EFT.UI;
using EFT.UI.Map;
using HarmonyLib;
using System.Text.Json;
using SPTushonka.Common.Http;
using SPTushonka.Reflection.Patching;
using SPTushonka.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using EFT.Communications;
using static DynamicMaps.UI.ModdedMapScreen;

namespace DynamicMaps.Patches
{
    internal class MenuLoadPatch : ModulePatch
    {
        private static bool serverConfigLoaded = false;

        protected override MethodBase GetTargetMethod()
        {
            // In SPT 5 the old Taxonomy.Create menu hook no longer exists.
            // MainMenuShowOperation.Init is the current entry point for loading the main menu.
            return AccessTools.Method(typeof(EFT.MainMenuShowOperation), "Init", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(EFT.MainMenuShowOperation).FullName, "Init()");
        }

        [PatchPrefix]
        public static async void PatchPostfix()
        {
            try
            {
                if (serverConfigLoaded == false)
                {
                    serverConfigLoaded = true;
                    ModdedMapScreen.ServerConfig = await LoadFromServer() ?? new DmServerConfig();

                    // The map UI can be created before the request finishes. Refresh it once
                    // the server-side marker permissions are available.
                    Plugin.Instance?.Map?.ReadConfig();

                    Plugin.Log.LogInfo($"Loaded server config");
                }
                else return;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Caught error while trying to load config");
                Plugin.Log.LogError($"{e.Message}");
                Plugin.Log.LogError($"{e.StackTrace}");
            }
        }
        private static async Task<DmServerConfig> LoadFromServer()
        {
            try
            {
                string payload = await RequestHandler.GetJsonAsync("/dynamicmaps/load");
                var options = new JsonSerializerOptions
                {
                    IncludeFields = true,
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize(payload, typeof(DmServerConfig), options) as DmServerConfig;

            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Failed to load: " + ex.ToString());
                NotificationManager.DisplayWarningNotification("Failed to load Dynamic Maps server config - check the server");
                return null;
            }
        }
    }
}
