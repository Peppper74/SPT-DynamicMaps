using System;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using DynamicMaps.Config;
using DynamicMaps.Utils;
using SPTushonka.Reflection.Patching;
using EFT;
using EFT.UI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace DynamicMaps.Patches
{
    internal class BattleUIScreenShowPatch : ModulePatch
    {
        public static bool IsAttached = false;
        private static bool _loggedFirstShow;
        
        protected override MethodBase GetTargetMethod()
        {
            var battleScreenType = typeof(EftBattleUIScreen).BaseType;
            return AccessTools.DeclaredMethod(battleScreenType,
                                              nameof(EftBattleUIScreen.Show),
                                              new Type[] { typeof(GamePlayerOwner) });
        }

        [PatchPostfix]
        public static void PatchPostfix(Il2CppObjectBase __instance)
        {
            // IL2CPP returns generic UI instances as their declared proxy/base type;
            // a C# `is` check can miss the actual EFT screen even when it is that type.
            var battleUIScreen = __instance?.TryCast<EftBattleUIScreen>();
            if (!_loggedFirstShow)
            {
                _loggedFirstShow = true;
                Plugin.Log.LogInfo($"BattleUIScreen.Show patch fired; instance={__instance?.GetType().FullName ?? "<null>"}, castSucceeded={battleUIScreen != null}, mapAttached={Plugin.Instance.Map != null}");
            }
            if (battleUIScreen == null) return;

            IsAttached = GameUtils.ShouldShowMapInRaid();
            Plugin.Log.LogInfo($"Battle UI recognized; mapInRaidEnabled={IsAttached}");
            if (!IsAttached) return;
            
            Plugin.Instance.TryAttachToBattleUIScreen(battleUIScreen);
        }
    }
}
