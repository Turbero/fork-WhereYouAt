using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using WhereYouAt.Compatibility.WardIsLove;

namespace WhereYouAt
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class WhereYouAtPlugin : BaseUnityPlugin

    {
        internal const string ModName = "WhereYouAt";
        internal const string ModVersion = "1.0.8";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        
        internal static string ConnectionError = "";

        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource WhereYouAtLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);

        private static bool _insideWard;

        private static readonly ConfigSync ConfigSync = new(ModGUID)
            { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

        private void Awake()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                new ConfigDescription("If on, only server admins can change the configuration."));
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            _preventPublicToggle = config("1 - General", "Prevent Public Toggle", Toggle.On,
                new ConfigDescription(
                    "Prevents you and other people on the server to turn off their map sharing option. NOTE: If the admin exempt toggle is on, admins bypass this."));
            _adminExempt = config("1 - General", "Admin Exempt", Toggle.On,
                new ConfigDescription("If on, server admins can bypass the force of position sharing."));

            _offInWards = config("1 - General", "Off In Wards", Toggle.Off,
                new ConfigDescription(
                    "If on, hide position sharing in wards. NOTE: This will force position to toggle off and stay off while inside a ward."));


            _harmony.PatchAll();
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                WhereYouAtLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                WhereYouAtLogger.LogError($"There was an issue loading your {ConfigFileName}");
                WhereYouAtLogger.LogError("Please check your config entries for spelling and format!");
            }
        }
        
        private static void UpdateInsideWard()
        {
            if (Player.m_localPlayer != null)
            {
                _insideWard = WardIsLovePlugin.IsLoaded()
                    ? WardMonoscript.InsideWard(Player.m_localPlayer.transform.position)
                    : PrivateArea.InsideFactionArea(Player.m_localPlayer.transform.position, Character.Faction.Players);
            }
        }

        private static bool CheckOffInWardValue()
        {
            return _offInWards.Value switch
            {
                Toggle.Off => true,
                Toggle.On when _insideWard => false,
                _ => true
            };
        }

        #region HarmonyPatches

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.SetPublicReferencePosition))]
        public static class PreventPublicPositionToggle
        {
            private static void Postfix(ref bool pub, ref bool ___m_publicReferencePosition)
            {
                if (_adminExempt.Value == Toggle.On && ConfigSync.IsAdmin)
                {
                    return;
                }
                
                if (_preventPublicToggle.Value == Toggle.On)
                {
                    ___m_publicReferencePosition = CheckOffInWardValue();
                }
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
        static class Minimap__Patch
        {
            static void Postfix(Minimap __instance)
            {
                if (!__instance) return;
                UpdateInsideWard();

                if (_adminExempt.Value == Toggle.On && ConfigSync.IsAdmin)
                {
                    return;
                }

                if (_preventPublicToggle.Value != Toggle.On) return;
                __instance.m_publicPosition.isOn = CheckOffInWardValue();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        static class PlayerOnSpawnedPatch
        {
            static void Postfix(Player __instance)
            {
                UpdateInsideWard();

                if (_adminExempt.Value == Toggle.On && ConfigSync.IsAdmin)
                {
                    return;
                }

                if (_preventPublicToggle.Value != Toggle.On) return;
                bool shouldSet = CheckOffInWardValue();
                ZNet.instance.SetPublicReferencePosition(shouldSet);
            }
        }

        #endregion


        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        private static ConfigEntry<Toggle> _preventPublicToggle = null!;
        private static ConfigEntry<Toggle> _adminExempt = null!;
        private static ConfigEntry<Toggle> _offInWards = null!;


        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            public bool? Browsable = false;
        }

        class AcceptableShortcuts : AcceptableValueBase // Used for KeyboardShortcut Configs 
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", KeyboardShortcut.AllKeyCodes);
        }

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        #endregion
    }
}