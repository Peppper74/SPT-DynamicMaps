using System;
using Comfort.Common;
using DynamicMaps.Utils;
using EFT;
using EFT.UI;
using UnityEngine;

namespace DynamicMaps.UI
{
    /// <summary>
    /// Safety net for the case where the game UI hooks no longer fire on a new build:
    /// the modded map screen and the peek/mini-map component then never get attached,
    /// which leaves the whole mod invisible (no map screen, no mini-map, no peek).
    /// The watcher re-checks once per second and attaches whatever is missing.
    /// </summary>
    internal class DynamicMapsWatcher : MonoBehaviour
    {
        private const float CheckIntervalSeconds = 1f;
        private const float FirstCheckDelaySeconds = 5f;
        private const int DiagnosticChecks = 15;

        public DynamicMapsWatcher(IntPtr ptr) : base(ptr) { }

        private float _nextCheckTime;
        private int _errorCount;
        private float _nextErrorLogTime;
        private int _checks;
        private bool _loggedPeekAttached;

        private void Awake()
        {
            _nextCheckTime = Time.realtimeSinceStartup + FirstCheckDelaySeconds;
            Plugin.Log.LogInfo("DynamicMaps watcher awake, first check in " + FirstCheckDelaySeconds + "s");
        }

        private void Update()
        {
            var now = Time.realtimeSinceStartup;
            if (now < _nextCheckTime)
            {
                return;
            }

            _nextCheckTime = now + CheckIntervalSeconds;

            try
            {
                CheckAndAttach();
            }
            catch (Exception e)
            {
                LogThrottled(e);
            }
        }

        private void CheckAndAttach()
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return;
            }

            _checks++;

            // markers that follow live game state (corpse loot) are refreshed once per second
            plugin.Map?.TickDynamicMarkers();

            if (plugin.Map == null)
            {
                var mapScreen = Plugin.FindMapScreen();
                if (mapScreen == null)
                {
                    LogDiagnostics(plugin, null, null);
                    return;
                }

                plugin.TryAttachToMapScreen(mapScreen);
                return;
            }

            if (plugin.Map.HasPeekComponent)
            {
                if (!_loggedPeekAttached)
                {
                    _loggedPeekAttached = true;
                    Plugin.Log.LogInfo("DynamicMaps watcher: peek/mini-map component is attached");
                }

                return;
            }

            var allBattleUis = Resources.FindObjectsOfTypeAll<EftBattleUIScreen>();
            var battleUi = Plugin.FindBattleUIScreen();

            LogDiagnostics(plugin, allBattleUis, battleUi);

            if (battleUi == null)
            {
                return;
            }

            // The presence of the raid HUD is the reliable signal on this build, so the raid flag
            // is only logged next to it instead of gating the attach (see LogDiagnostics).
            plugin.TryAttachToBattleUIScreen(battleUi);
        }

        private void LogDiagnostics(Plugin plugin, EftBattleUIScreen[] allBattleUis, EftBattleUIScreen activeBattleUi)
        {
            if (_checks > DiagnosticChecks)
            {
                return;
            }

            try
            {
                var abstractGame = Singleton<AbstractGame>.Instance;
                var botGame = Singleton<IBotGame>.Instance;
                var gameWorld = Singleton<GameWorld>.Instance;
                var mainPlayer = gameWorld?.MainPlayer;

                var abstractGameState = abstractGame == null ? "null" : $"present, InRaid={abstractGame.InRaid}";
                var botGameState = botGame == null ? "null" : $"present, Status={botGame.Status}";
                var playerState = mainPlayer == null ? "null" : mainPlayer.GetType().Name;
                var battleUiState = allBattleUis == null ? "null" : $"{allBattleUis.Length} found, active={(activeBattleUi != null)}";

                Plugin.Log.LogInfo($"DynamicMaps watcher #{_checks}: inRaid={GameUtils.IsInRaid()}, map={(plugin.Map != null)}, peek={plugin.Map?.HasPeekComponent.ToString() ?? "n/a"}, abstractGame={abstractGameState}, botGame={botGameState}, gameWorld={(gameWorld == null ? "null" : "present")}, mainPlayer={playerState}, battleUi={battleUiState}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"DynamicMaps watcher diagnostics failed: {e.Message}");
            }
        }

        // Attaching can keep failing while the game UI is not ready yet, so keep the log
        // readable: the first attempts are reported in full, after that once per minute.
        private void LogThrottled(Exception e)
        {
            var now = Time.realtimeSinceStartup;
            if (_errorCount < 3 || now >= _nextErrorLogTime)
            {
                _nextErrorLogTime = now + 60f;
                Plugin.Log.LogError($"DynamicMaps watcher failed to attach (attempt {_errorCount + 1})");
                Plugin.Log.LogError($"  Exception given was: {e.Message}");
                Plugin.Log.LogError($"  {e.StackTrace}");
            }

            _errorCount++;
        }

        internal static DynamicMapsWatcher Create()
        {
            var gameObject = new GameObject("DynamicMapsWatcher");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);

            return gameObject.AddComponent<DynamicMapsWatcher>();
        }
    }
}
