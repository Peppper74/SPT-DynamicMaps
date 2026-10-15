using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SPTushonka.Reflection.Utils;
using Comfort.Common;
using DynamicMaps.Config;
using EFT;
using EFT.InventoryLogic;
using EFT.Vehicle;
using HarmonyLib;
using UnityEngine.Profiling;

namespace DynamicMaps.Utils
{
    public static class GameUtils
    {
        // reflection        
        private static Type _profileInterface = typeof(IEftSession).GetInterfaces().First(i =>
            {
                var properties = i.GetProperties();
                return properties.Length == 2 &&
                       properties.Any(p => p.Name == "Profile");
            });
        private static PropertyInfo _sessionProfileProperty = AccessTools.Property(_profileInterface, "Profile");
        public static IEftSession Session => ClientAppUtils.GetMainApp().GetClientBackEndSession();
        public static Profile PlayerProfile => _sessionProfileProperty.GetValue(Session) as Profile;
        //

        private static HashSet<WildSpawnType> _trackedBosses = new HashSet<WildSpawnType>
        {
            WildSpawnType.bossBoar,             // Kaban
            WildSpawnType.bossBully,            // Reshala
            WildSpawnType.bossGluhar,           // Glukhar
            WildSpawnType.bossKilla,
            WildSpawnType.bossKnight,
            WildSpawnType.followerBigPipe,
            WildSpawnType.followerBirdEye,
            WildSpawnType.bossKolontay,
            WildSpawnType.bossKojaniy,          // Shturman
            WildSpawnType.bossSanitar,
            WildSpawnType.bossTagilla,
            WildSpawnType.bossPartisan,
            WildSpawnType.bossZryachiy,
            WildSpawnType.gifter,               // Santa
            WildSpawnType.arenaFighterEvent,    // Blood Hounds
            WildSpawnType.sectantPriest,        // Cultist Priest
            WildSpawnType.bossTagillaAgro,      // Tagilla Labyrinth
            WildSpawnType.bossKillaAgro,        // Killa Labyrinth
            WildSpawnType.tagillaHelperAgro,    // Tagilla Helper Labyrinth
            (WildSpawnType) 199,                // Legion
            (WildSpawnType) 801,                // Punisher
        };

        private static readonly Dictionary<string, string> MapLookUp = new()
        {
            { "factory4_day", "574eb85c245977648157eec3" },
            { "factory4_night", "574eb85c245977648157eec3" },
            { "Woods", "5900b89686f7744e704a8747" },
            { "bigmap", "5798a2832459774b53341029" },
            { "Interchange", "5be4038986f774527d3fae60" },
            { "RezervBase", "6738034a9713b5f42b4a8b78" },
            { "Shoreline", "5a8036fb86f77407252ddc02" },
            { "laboratory", "6738034e9d22459ad7cd1b81" },
            { "Lighthouse", "6738035350b24a4ae4a57997" },
            { "TarkovStreets", "673803448cb3819668d77b1b" },
            { "Sandbox", "6738033eb7305d3bdafe9518"},
            { "Sandbox_high", "6738033eb7305d3bdafe9518"},
            { "Labyrinth", "68f1ad32317cc52f4c0b6fae" }
        };
        
        public static bool IsInRaid()
        {
            var game = Singleton<AbstractGame>.Instance;
            var botGame = Singleton<IBotGame>.Instance;

            return ((game != null) && game.InRaid)
                || ((botGame != null) && botGame.Status != GameStatus.Stopped
                                      && botGame.Status != GameStatus.Stopping
                                      && botGame.Status != GameStatus.SoftStopping);
        }

        // SPT 4.x hands the client GUID location ids for some raids instead of the classic
        // names ("bigmap", "RezervBase", ...) that the map definitions under Resources/Maps
        // use. Both shapes are mapped onto the classic name here.
        private static readonly Dictionary<string, string> LocationAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "01a445b0-4545-48da-9ce6-6d0190869360", "bigmap" },          // Customs
            { "0246436c-7d69-4036-999d-ebcb956970b5", "factory4_day" },    // Factory (day + night)
            { "004241d4-d939-4a7b-8cad-36924aab6c48", "Woods" },
            { "019d9bd8-411d-49a6-84ca-727ffb38e57b", "Interchange" },
            { "0046beaa-6af4-40d6-b031-b73a4d9229f7", "RezervBase" },
            { "012debaa-649e-4aae-bcd1-c479bf8efbe0", "Shoreline" },
            { "0043f275-0dc2-4c04-87a1-bd90cbaf183a", "laboratory" },
            { "035e94f9-c4b3-4e8a-a1a9-7e1b06b2fb75", "Lighthouse" },
            { "00eb887b-1633-4fd1-ada3-ca02915ec343", "TarkovStreets" },
            { "0043051d-f15a-4e50-8c96-ba69c3bc43f7", "Sandbox" },         // Ground Zero
            { "0344832c-a6f2-4c58-8d89-85e9d451a7ed", "Sandbox_high" },
            { "07c074ca-3ea0-4958-b1a1-aec5e42c0000", "Labyrinth" },
        };

        private static int _locationLogCount;

        public static string GetCurrentMapInternalName()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            var rawLocation = gameWorld?.MainPlayer?.Location;

            var mapInternalName = rawLocation;
            if (!string.IsNullOrEmpty(rawLocation) && LocationAliases.TryGetValue(rawLocation, out var aliased))
            {
                mapInternalName = aliased;
            }

            // Diagnostic: the first few lookups tell us which shape this game build reports.
            if (!string.IsNullOrEmpty(rawLocation) && _locationLogCount < 3)
            {
                _locationLogCount++;
                Plugin.Log.LogInfo($"DynamicMaps: map internal name '{mapInternalName}' (raw location '{rawLocation}')");
            }

            return mapInternalName;
        }
        
        public static Player GetMainPlayer()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            return gameWorld?.MainPlayer;
        }

        public static Profile GetPlayerProfile()
        {
            return PlayerProfile;
        }

        public static BTRView GetBTRView()
        {
            var gameWorld = Singleton<GameWorld>.Instance;
            return gameWorld?.BtrController?.BtrView;
        }

        public static bool IsScavRaid()
        {
            var player = GetMainPlayer();
            return IsInRaid() && (player != null) && player.Side == EPlayerSide.Savage;
        }

        public static string BSGLocalized(this MongoID id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "";
            }

            // TODO: use reflection to get rid of this gclass reference
            return id.ToString().Localized();
        }
        
        public static string BSGLocalized(this string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "";
            }

            // TODO: use reflection to get rid of this gclass reference
            return id.Localized();
        }

        public static bool IsGroupedWithMainPlayer(this IPlayer player)
        {
            var mainPlayerGroupId = GetMainPlayer().GroupId;
            return !string.IsNullOrEmpty(mainPlayerGroupId) && player.GroupId == mainPlayerGroupId;
        }

        public static bool IsTrackedBoss(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Savage && _trackedBosses.Contains(player.Profile.Info.Settings.Role);
        }

        public static bool IsPMC(this IPlayer player)
        {
            if (player == null) return false;

            // SPT can represent AI PMC profiles with Savage as the side while the
            // actual faction is encoded in the WildSpawnType role (pmcBEAR/pmcUSEC).
            var role = player.Profile.Info.Settings.Role.ToString();
            return player.Profile.Side == EPlayerSide.Bear
                || player.Profile.Side == EPlayerSide.Usec
                || role.Contains("pmc", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsScav(this IPlayer player)
        {
            return player != null
                && player.Profile.Side == EPlayerSide.Savage
                && !player.IsPMC();
        }

        public static bool IsBearPMC(this IPlayer player)
        {
            if (player == null) return false;

            var role = player.Profile.Info.Settings.Role.ToString();
            return player.Profile.Side == EPlayerSide.Bear
                || role.Contains("bear", StringComparison.OrdinalIgnoreCase);
        }

        public static bool DidMainPlayerKill(this IPlayer player)
        {
            var aggressor = (player as Player)?.LastAggressor;
            var mainPlayer = GetMainPlayer();
            if (aggressor == null || mainPlayer == null)
            {
                return false;
            }

            if (aggressor.ProfileId == mainPlayer.ProfileId)
            {
                return true;
            }

            return false;
        }

        public static bool DidTeammateKill(this IPlayer player)
        {
            var aggressor = (player as Player)?.LastAggressor;
            var mainPlayer = GetMainPlayer();
            if (aggressor == null || mainPlayer == null || string.IsNullOrEmpty(aggressor.GroupId))
            {
                return false;
            }

            if (aggressor.ProfileId != mainPlayer.ProfileId && aggressor.GroupId == mainPlayer.GroupId)
            {
                return true;
            }

            return false;
        }

        public static bool IsBTRShooter(this IPlayer player)
        {
            return player.Profile.Side == EPlayerSide.Savage
                && player.Profile.Info.Settings.Role == WildSpawnType.shooterBTR;
        }

        public static bool HasCorpse(this Player player)
        {
            return player?.Corpse != null;
        }

        public static bool IsHeadlessClient(this IPlayer player)
        {
            return player.Profile.Info.MemberCategory == EMemberCategory.UnitTest;
        }

        public static int? GetIntelLevel()
        {
            return PlayerProfile.Hideout.Areas
                .SingleOrDefault(a => a.AreaType == EAreaType.IntelligenceCenter)?.Level;
        }

        public static MongoID[] GetWishListItems()
        {
            var wishList = PlayerProfile.WishlistManager.GetWishlist();
            
            return wishList.Keys.ToArray();
        }
        
        public static bool ShouldShowMapInRaid()
        {
            if (!Settings.RequireMapInInventory.Value || !IsInRaid()) return true;
            
            var player = GetMainPlayer();
            var currentLocation = player.Location;

            if (!MapLookUp.TryGetValue(currentLocation, out var id))
            {
                Plugin.Log.LogWarning($"Could not find map id for location {currentLocation} is this a new location?");
                return true;
            }
                    
            var isMapInInventory = player.Inventory.Equipment
                .GetAllItems()
                .Any(m => m.TemplateId == id);
            
            return isMapInInventory;
        }
    }
}
