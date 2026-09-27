using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Facepunch;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
namespace Oxide.Plugins
{
    [Info("RogueRustBlueprintManager", "RogueAssassin", "2.1.0")]
    [Description("RogueRust-powered blueprint management with legacy permission and command compatibility.")]

    public sealed class RogueRustBlueprintManager : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);
        private const string LanguageFolder = "RogueRustBlueprintManager";
        private const string LanguageFile = "messages.json";

        #region Configuration

        private Configuration config = new Configuration();
        private readonly Dictionary<int, BlueprintData> defaultsetup = new Dictionary<int, BlueprintData>();

        public class BlueprintData
        {
            [JsonProperty("Default Blueprint")]
            public bool defaultBP;

            [JsonProperty("Can Research")]
            public bool canResearch = true;

            [JsonProperty("Scrap Required")]
            public int scrapRequired = 1;

            [JsonProperty("Unlock Minutes After Wipe (-1 = disabled)")]
            public int unlockMinutesAfterWipe = -1;

            [JsonProperty("Automatically Unlock After Wipe Delay")]
            public bool autoUnlockMinutesAfterWipe;
        }

        public sealed class Configuration
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Permission Settings", Order = 20)]
            public PermissionSettings Permissions = new PermissionSettings();

            [JsonProperty("Blueprint Settings", Order = 30)]
            public BlueprintSettings Blueprints = new BlueprintSettings();

            [JsonProperty("Advanced Blueprint Settings", Order = 40)]
            public AdvancedBlueprintSettings Advanced = new AdvancedBlueprintSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        public sealed class GeneralSettings
        {
            [JsonProperty("Simple Mode (disables advanced blueprint management options)")]
            public bool SimpleMode = true;

            [JsonProperty("Wipe Blueprints With Map Wipe")]
            public bool WipeOnMap;
        }

        public sealed class PermissionSettings
        {
            [JsonProperty("Update Players On Permission Change")]
            public bool UpdateBlueprints = true;

            [JsonProperty("Assign Custom Blueprint Unlocks To Permissions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> BonusBlueprints = new Dictionary<string, List<string>>
            {
                { "customperm1", new List<string> { "rock" } },
                { "customperm2", new List<string> { "torch" } }
            };
        }

        public sealed class BlueprintSettings
        {
            [JsonProperty("Blacklist (items excluded from automatic learning)")]
            public List<string> Blacklist = new List<string>();

            [JsonProperty("Default Blueprints (automatically learned)")]
            public List<string> DefaultBlueprints = new List<string>();
        }

        public sealed class AdvancedBlueprintSettings
        {
            [JsonProperty("Blueprint Management Options")]
            public Dictionary<string, BlueprintData> Options = new Dictionary<string, BlueprintData>();
        }

        private sealed class LegacyConfiguration
        {
            [JsonProperty("Simple Mode (disables advance blueprint management options)")]
            public bool SimpleMode = true;

            [JsonProperty("Update players on permission change (automatically updates a players BPs when their permissions change)")]
            public bool UpdateBlueprints = true;

            [JsonProperty("Wipe BPs with Map Wipe")]
            public bool WipeOnMap;

            [JsonProperty("Blacklist (items from being automatically learnt)")]
            public List<string> Blacklist = new List<string>();

            [JsonProperty("DefaultBPs (Blueprints to be automatically learnt)")]
            public List<string> DefaultBlueprints = new List<string>();

            [JsonProperty("Assign custom BP unlocks to various perms (permission, BP List", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> BonusBlueprints = new Dictionary<string, List<string>>();

            [JsonProperty("Advanced Blueprint Management Options")]
            public Dictionary<string, BlueprintData> BlueprintOptions = new Dictionary<string, BlueprintData>();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
        }

        protected override void LoadConfig()
        {
            MigrateLegacyConfigFile();
            base.LoadConfig();

            try
            {
                JObject raw = Config.ReadObject<JObject>();
                bool legacy = raw["General Settings"] == null &&
                              (raw["Simple Mode (disables advance blueprint management options)"] != null ||
                               raw["Advanced Blueprint Management Options"] != null);

                if (legacy)
                {
                    LegacyConfiguration old = raw.ToObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    config = MigrateLegacyConfiguration(old);
                    LogInformation("Configuration", "Migrated the legacy flat configuration to the RogueRust family configuration layout.");
                }
                else
                {
                    config = raw.ToObject<Configuration>() ?? new Configuration();
                }

                bool changed = EnsureConfigDefaults();
                if (!(config.Version == CurrentVersion))
                {
                    VersionNumber oldVersion = config.Version;
                    config.Version = CurrentVersion;
                    changed = true;
                    LogInformation("Configuration", "Migrated configuration from v" + oldVersion + " to v" + CurrentVersion + ".");
                }

                if (legacy || changed)
                    SaveConfig();
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Configuration file " + Name + ".json is invalid; using defaults: " + exception.Message);
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        private static Configuration MigrateLegacyConfiguration(LegacyConfiguration old)
        {
            return new Configuration
            {
                General = new GeneralSettings
                {
                    SimpleMode = old.SimpleMode,
                    WipeOnMap = old.WipeOnMap
                },
                Permissions = new PermissionSettings
                {
                    UpdateBlueprints = old.UpdateBlueprints,
                    BonusBlueprints = old.BonusBlueprints ?? new Dictionary<string, List<string>>()
                },
                Blueprints = new BlueprintSettings
                {
                    Blacklist = old.Blacklist ?? new List<string>(),
                    DefaultBlueprints = old.DefaultBlueprints ?? new List<string>()
                },
                Advanced = new AdvancedBlueprintSettings
                {
                    Options = old.BlueprintOptions ?? new Dictionary<string, BlueprintData>()
                },
                Version = CurrentVersion
            };
        }

        private bool EnsureConfigDefaults()
        {
            bool changed = false;
            if (config.General == null) { config.General = new GeneralSettings(); changed = true; }
            if (config.Permissions == null) { config.Permissions = new PermissionSettings(); changed = true; }
            if (config.Blueprints == null) { config.Blueprints = new BlueprintSettings(); changed = true; }
            if (config.Advanced == null) { config.Advanced = new AdvancedBlueprintSettings(); changed = true; }
            if (config.Permissions.BonusBlueprints == null) { config.Permissions.BonusBlueprints = new Dictionary<string, List<string>>(); changed = true; }
            if (config.Blueprints.Blacklist == null) { config.Blueprints.Blacklist = new List<string>(); changed = true; }
            if (config.Blueprints.DefaultBlueprints == null) { config.Blueprints.DefaultBlueprints = new List<string>(); changed = true; }
            if (config.Advanced.Options == null) { config.Advanced.Options = new Dictionary<string, BlueprintData>(); changed = true; }
            return changed;
        }

        private void MigrateLegacyConfigFile()
        {
            try
            {
                string current = Path.Combine(Interface.Oxide.ConfigDirectory, Name + ".json");
                if (File.Exists(current)) return;

                string[] candidates =
                {
                    Path.Combine(Interface.Oxide.ConfigDirectory, "BlueprintManager.json"),
                    Path.Combine(Interface.Oxide.ConfigDirectory, "Blueprint Manager.json")
                };

                for (int i = 0; i < candidates.Length; i++)
                {
                    if (!File.Exists(candidates[i])) continue;
                    File.Copy(candidates[i], current, false);
                    LogInformation("Configuration", "Migrated legacy BlueprintManager config to " + Name + ".json.");
                    break;
                }
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Legacy config migration failed: " + exception.Message);
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion Configuration

        #region Init

        [RoguePermission]
        private const string permunlockall = "roguerustblueprintmanager.all";

        [RoguePermission]
        private const string permadmin = "roguerustblueprintmanager.admin";

        private DateTime _lastWipe;
        private readonly Queue<BasePlayer> _playerUpdateQueue = new Queue<BasePlayer>();
        private readonly HashSet<ulong> _queuedPlayerIds = new HashSet<ulong>();
        private bool _playerUpdateScheduled;
        private bool _wipeUnlockSchedulerStarted;
        private readonly HashSet<int> _blacklist = new HashSet<int>();
        private readonly HashSet<int> _defaultBlueprints = new HashSet<int>();
        private readonly Dictionary<string, HashSet<int>> _permissionBPs = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ItemBlueprint, int> _unlockAfterWipe = new Dictionary<ItemBlueprint, int>();
        private void Init()
        {
            LoadLanguageFiles();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustBlueprintManager/; Lang=<language>/RogueRust/RogueRustBlueprintManager/messages.json");
        }


        private void OnServerInitialized()
        {
            defaultsetup.Clear();
            _blacklist.Clear();
            _defaultBlueprints.Clear();
            _permissionBPs.Clear();
            _unlockAfterWipe.Clear();

            //Unsub hooks
            if (!config.Permissions.UpdateBlueprints)
            {
                Unsubscribe("OnUserGroupAdded");
                Unsubscribe("OnUserPermissionGranted");
            }

            //Check blacklist BPs in config
            for (int i = 0; i < config.Blueprints.Blacklist.Count; i++)
            {
                string blacklistBP = config.Blueprints.Blacklist[i];
                ItemBlueprint bp = ItemManager.FindItemDefinition(blacklistBP)?.Blueprint;
                if (bp != null)
                    _blacklist.Add(bp.targetItem.itemid);
                else
                    LogWarning("Configuration", GetLang("CannotFindBPConfig", null, blacklistBP, "Blacklist"));
            }

            //Create Default Permission Sets
            _permissionBPs.Add(permunlockall, new HashSet<int>());

            foreach (ItemBlueprint bp in ItemManager.bpList)
            {
                if (_blacklist.Contains(bp.targetItem.itemid)) continue;

                if (bp.NeedsSteamDLC || bp.NeedsSteamItem)
                {
                    if (!_permissionBPs.TryGetValue($"roguerustblueprintmanager.Dlc", out var dlcList))
                        _permissionBPs.Add($"roguerustblueprintmanager.Dlc", new HashSet<int> { bp.targetItem.itemid });
                    else
                        dlcList.Add(bp.targetItem.itemid);
                    continue;
                }

                //Add BPs to workbench permissions
                HashSet<int> bpList;
                if (!_permissionBPs.TryGetValue($"roguerustblueprintmanager.WorkbenchLvL{bp.workbenchLevelRequired}", out bpList))
                    _permissionBPs.Add($"roguerustblueprintmanager.WorkbenchLvL{bp.workbenchLevelRequired}", new HashSet<int> { bp.targetItem.itemid });
                else
                    bpList.Add(bp.targetItem.itemid);

                //Add BPs to ItemCategory permissions
                if (!_permissionBPs.TryGetValue($"roguerustblueprintmanager.{Enum.GetName(typeof(ItemCategory), bp.targetItem.category)}", out bpList))
                    _permissionBPs.Add($"roguerustblueprintmanager.{Enum.GetName(typeof(ItemCategory), bp.targetItem.category)}", new HashSet<int> { bp.targetItem.itemid });
                else
                    bpList.Add(bp.targetItem.itemid);

                _permissionBPs[permunlockall].Add(bp.targetItem.itemid);
            }

            //Check default BPs in config
            foreach (string defaultBP in config.Blueprints.DefaultBlueprints)
            {
                ItemBlueprint bp = ItemManager.FindItemDefinition(defaultBP)?.Blueprint;
                if (bp == null)
                    LogWarning("Configuration", GetLang("CannotFindBPConfig", null, defaultBP, "DefaultBPs"));
                else if (!_blacklist.Contains(bp.targetItem.itemid))
                    _defaultBlueprints.Add(bp.targetItem.itemid);
            }

            //Check custom perm BPs
            foreach (string key in config.Permissions.BonusBlueprints.Keys)
            {
                string permissionName = "roguerustblueprintmanager." + key;
                if (!_permissionBPs.ContainsKey(permissionName))
                    _permissionBPs.Add(permissionName, new HashSet<int>());
            }

            foreach (KeyValuePair<string, List<string>> bonusBPSet in config.Permissions.BonusBlueprints)
            {
                string permissionName = "roguerustblueprintmanager." + bonusBPSet.Key;
                HashSet<int> permissionBlueprints;
                if (!_permissionBPs.TryGetValue(permissionName, out permissionBlueprints))
                    continue;

                List<string> configuredBlueprints = bonusBPSet.Value;
                if (configuredBlueprints == null)
                    continue;

                for (int i = 0; i < configuredBlueprints.Count; i++)
                {
                    string bonusBP = configuredBlueprints[i];
                    ItemBlueprint bp = ItemManager.FindItemDefinition(bonusBP)?.Blueprint;
                    if (bp == null)
                        LogWarning("Configuration", GetLang("CannotFindBPConfig", null, bonusBP, "Custom BPs set " + bonusBPSet.Key));
                    else if (!_blacklist.Contains(bp.targetItem.itemid))
                        permissionBlueprints.Add(bp.targetItem.itemid);
                }
            }

            //Register Perms
            // Runtime-generated permissions cannot be declared with [RoguePermission].
            foreach (KeyValuePair<string, HashSet<int>> permissionSet in _permissionBPs)
            {
                if (string.Equals(permissionSet.Key, permunlockall, StringComparison.OrdinalIgnoreCase))
                    continue;
                Rogue.Permissions.Register(permissionSet.Key, this);
            }

            if (!config.General.SimpleMode)
            {
                foreach (ItemBlueprint bp in ItemManager.bpList)
                {
                    defaultsetup.Add(bp.targetItem.itemid, new BlueprintData() { defaultBP = bp.defaultBlueprint, scrapRequired = bp.scrapRequired, canResearch = bp.isResearchable });
                    BlueprintData blueprintData;
                    if (!config.Advanced.Options.TryGetValue(bp.targetItem.shortname, out blueprintData))
                        config.Advanced.Options.Add(bp.targetItem.shortname, new BlueprintData() { defaultBP = bp.defaultBlueprint, scrapRequired = bp.scrapRequired, canResearch = bp.isResearchable });
                    else
                    {
                        bp.isResearchable = blueprintData.canResearch;
                        bp.scrapRequired = blueprintData.scrapRequired;
                        bp.defaultBlueprint = blueprintData.defaultBP;

                        if (blueprintData.unlockMinutesAfterWipe > 0)
                        {
                            _unlockAfterWipe[bp] = blueprintData.unlockMinutesAfterWipe;
                        }

                        if (blueprintData.defaultBP)
                        {
                            _defaultBlueprints.Add(bp.targetItem.itemid);
                        }
                    }
                }

                _lastWipe = SaveRestore.SaveCreatedTime;

                if (_unlockAfterWipe.Count > 0)
                    StartUnlockAfterWipeScheduler();
            }
            QueueAllPlayersForUpdate();
            SaveConfig();
        }
        private void QueueAllPlayersForUpdate()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                QueuePlayerUpdate(player);
        }

        private void QueuePlayerUpdate(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !_queuedPlayerIds.Add(player.userID))
                return;

            _playerUpdateQueue.Enqueue(player);
            if (_playerUpdateScheduled)
                return;

            _playerUpdateScheduled = true;
            Delay(TimeSpan.Zero, ProcessPlayerUpdateBatch, "blueprints-player-update");
        }

        private void ProcessPlayerUpdateBatch()
        {
            using (Measure("RogueRustBlueprintManager", "PlayerUpdateBatch"))
            {
                int processed = 0;
                const int batchSize = 8;
                while (_playerUpdateQueue.Count > 0 && processed < batchSize)
                {
                    BasePlayer player = _playerUpdateQueue.Dequeue();
                    if (player != null)
                        _queuedPlayerIds.Remove(player.userID);
                    if (player != null && player.IsConnected)
                        UpdatePlayerBPs(player);
                    processed++;
                }
            }

            if (_playerUpdateQueue.Count > 0)
            {
                Delay(TimeSpan.FromMilliseconds(10), ProcessPlayerUpdateBatch, "blueprints-player-update");
                return;
            }

            _playerUpdateScheduled = false;
        }

        private void StartUnlockAfterWipeScheduler()
        {
            if (_wipeUnlockSchedulerStarted)
                return;

            _wipeUnlockSchedulerStarted = true;
            Repeat(TimeSpan.FromSeconds(30), ProcessUnlockAfterWipe, "blueprints-wipe-unlock");
            ProcessUnlockAfterWipe();
        }

        private void ProcessUnlockAfterWipe()
        {
            if (_unlockAfterWipe.Count == 0)
                return;

            bool changed = false;
            List<ItemBlueprint> unlocked = Pool.Get<List<ItemBlueprint>>();
            try
            {
                double secondsSinceWipe = SecondsSinceWipe();
                foreach (KeyValuePair<ItemBlueprint, int> entry in _unlockAfterWipe)
                {
                    if (secondsSinceWipe < entry.Value * 60d)
                        continue;

                    entry.Key.isResearchable = true;
                    unlocked.Add(entry.Key);

                    BlueprintData blueprintData;
                    if (config.Advanced.Options.TryGetValue(entry.Key.targetItem.shortname, out blueprintData) &&
                        blueprintData.autoUnlockMinutesAfterWipe &&
                        _defaultBlueprints.Add(entry.Key.targetItem.itemid))
                    {
                        changed = true;
                    }
                }

                for (int i = 0; i < unlocked.Count; i++)
                    _unlockAfterWipe.Remove(unlocked[i]);
            }
            finally
            {
                Pool.FreeUnmanaged(ref unlocked);
            }

            if (changed)
                QueueAllPlayersForUpdate();
        }

        private void Unload()
        {
            foreach (ItemBlueprint bp in ItemManager.bpList)
            {
                BlueprintData blueprintData;
                if (!defaultsetup.TryGetValue(bp.targetItem.itemid, out blueprintData)) continue;
                bp.isResearchable = blueprintData.canResearch;
                bp.scrapRequired = blueprintData.scrapRequired;
                bp.defaultBlueprint = blueprintData.defaultBP;
            }

            _playerUpdateQueue.Clear();
            _queuedPlayerIds.Clear();
            _playerUpdateScheduled = false;
            _wipeUnlockSchedulerStarted = false;

        }

        #endregion Init

        #region Localization
        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        protected override void LoadDefaultMessages()
        {
            // RogueRust family language files are managed below so they can live at:
            // lang/<language>/RogueRust/RogueRustBlueprintManager/messages.json
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NoPerms"] = "You don't have permission to use this command.",
                ["CMDBPResetArgs"] = "This command needs one argument in the format /bpreset playerName or playerID",
                ["CMDUnlockAllArgs"] = "This command needs one argument in the format /bpunlockall playerName or playerID",
                ["CMDRemoveArgs"] = "This command needs at least two arguments in the format /bpremove playerName or playerID item.shortname",
                ["PlayerNotFound"] = "Cannot find player by the {0} identifier",
                ["ResetPlayersBps"] = "{0} BPs were reset",
                ["ResetAllBps"] = "All BPs were reset",
                ["UnlockAllPlayersBps"] = "All BPs were unlocked for {0}",
                ["UnlockPlayersBps"] = "{0} was unlocked for {1}",
                ["RemovePlayersBps"] = "{0} was removed for {1}",
                ["CannotFindBPConfig"] = "Cannot find a blueprint for {0} in the {1} config. Use the item shortname or ID",
                ["CannotFindBP"] = "Cannot find a blueprint for {0}. Use the item shortname or ID",
                ["BlueprintLocked"] = "The {0} blueprint is locked for {1}"
            };
        }

        private void LoadLanguageFiles()
        {
            _messages.Clear();
            MigrateLegacyLanguageFiles();

            string langRoot = Interface.Oxide.LangDirectory;
            if (!Directory.Exists(langRoot))
                Directory.CreateDirectory(langRoot);

            string[] languageDirectories = Directory.GetDirectories(langRoot);
            for (int i = 0; i < languageDirectories.Length; i++)
            {
                string language = Path.GetFileName(languageDirectories[i]);
                if (string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                    continue;

                string pluginRoot = Path.Combine(languageDirectories[i], "RogueRust", LanguageFolder);
                string file = Path.Combine(pluginRoot, LanguageFile);
                if (!File.Exists(file))
                    continue;

                TryLoadLanguageCatalog(language, file);
            }

            if (!_messages.ContainsKey("en"))
            {
                string englishRoot = Path.Combine(langRoot, "en", "RogueRust", LanguageFolder);
                Directory.CreateDirectory(englishRoot);
                string englishFile = Path.Combine(englishRoot, LanguageFile);
                Dictionary<string, string> defaults = CreateDefaultMessages();
                File.WriteAllText(englishFile, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                _messages["en"] = defaults;
            }
        }

        private void TryLoadLanguageCatalog(string language, string file)
        {
            try
            {
                Dictionary<string, string> catalog =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
                if (catalog != null)
                    _messages[language] = catalog;
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Could not load '" + file + "': " + exception.Message);
            }
        }

        private void MigrateLegacyLanguageFiles()
        {
            try
            {
                string langRoot = Interface.Oxide.LangDirectory;

                // Migrate the earlier RogueRust layout: lang/RogueRust/PluginName/<language>.json
                string previousRoot = Path.Combine(langRoot, "RogueRust", LanguageFolder);
                if (Directory.Exists(previousRoot))
                {
                    string[] files = Directory.GetFiles(previousRoot, "*.json", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string language = Path.GetFileNameWithoutExtension(files[i]);
                        string targetRoot = Path.Combine(langRoot, language, "RogueRust", LanguageFolder);
                        Directory.CreateDirectory(targetRoot);
                        string target = Path.Combine(targetRoot, LanguageFile);
                        if (!File.Exists(target))
                            File.Copy(files[i], target, false);
                    }
                }

                // Migrate original BlueprintManager English language files.
                string englishRoot = Path.Combine(langRoot, "en", "RogueRust", LanguageFolder);
                Directory.CreateDirectory(englishRoot);
                string englishTarget = Path.Combine(englishRoot, LanguageFile);
                if (!File.Exists(englishTarget))
                {
                    string[] legacyCandidates =
                    {
                        Path.Combine(langRoot, "en", "BlueprintManager.json"),
                        Path.Combine(langRoot, "en", "Blueprint Manager.json")
                    };
                    for (int i = 0; i < legacyCandidates.Length; i++)
                    {
                        if (!File.Exists(legacyCandidates[i])) continue;
                        File.Copy(legacyCandidates[i], englishTarget, false);
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Legacy language migration failed: " + exception.Message);
            }
        }
        #endregion Localization

        #region RogueRust Commands

        [RogueCommand(
            "bpreset",
            Description = "Resets a player's blueprints.",
            Usage = "bpreset <player>",
            Category = "Blueprints",
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandBlueprintReset(RogueCommandContext context)
            => ExecuteBlueprintRogueCommand(context, BlueprintCommand.Reset);

        [RogueCommand(
            "bpunlockall",
            Description = "Unlocks all available blueprints for a player.",
            Usage = "bpunlockall <player>",
            Category = "Blueprints",
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandBlueprintUnlockAll(RogueCommandContext context)
            => ExecuteBlueprintRogueCommand(context, BlueprintCommand.UnlockAll);

        [RogueCommand(
            "bpunlock",
            Description = "Unlocks one or more blueprints for a player.",
            Usage = "bpunlock <player> <item.shortname> [item.shortname...]",
            Category = "Blueprints",
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandBlueprintUnlock(RogueCommandContext context)
            => ExecuteBlueprintRogueCommand(context, BlueprintCommand.Unlock);

        [RogueCommand(
            "bpremove",
            Description = "Removes one or more unlocked blueprints from a player.",
            Usage = "bpremove <player> <item.shortname> [item.shortname...]",
            Category = "Blueprints",
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandBlueprintRemove(RogueCommandContext context)
            => ExecuteBlueprintRogueCommand(context, BlueprintCommand.Remove);

        [RogueCommand(
            "bpwipeall",
            Description = "Resets blueprints for all known players.",
            Usage = "bpwipeall",
            Category = "Blueprints",
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandBlueprintWipeAll(RogueCommandContext context)
            => ExecuteBlueprintRogueCommand(context, BlueprintCommand.WipeAll);

        private enum BlueprintCommand
        {
            Reset,
            UnlockAll,
            Unlock,
            Remove,
            WipeAll
        }

        private RogueCommandResult ExecuteBlueprintRogueCommand(RogueCommandContext context, BlueprintCommand commandType)
        {
            if (!CanAdmin(context))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string message = ExecuteBlueprintAction(context.NativePlayer, context.Arguments ?? Array.Empty<string>(), commandType);
            return RogueCommandResult.Ok(message);
        }

        private string ExecuteBlueprintAction(BasePlayer caller, string[] args, BlueprintCommand commandType)
        {
            if (commandType == BlueprintCommand.WipeAll)
            {
                WipeAllBps();
                return GetLang("ResetAllBps", caller);
            }

            if (args.Length == 0)
            {
                switch (commandType)
                {
                    case BlueprintCommand.Reset:
                        return GetLang("CMDBPResetArgs", caller);
                    case BlueprintCommand.UnlockAll:
                    case BlueprintCommand.Unlock:
                        return GetLang("CMDUnlockAllArgs", caller);
                    case BlueprintCommand.Remove:
                        return GetLang("CMDRemoveArgs", caller);
                }
            }

            if ((commandType == BlueprintCommand.Unlock || commandType == BlueprintCommand.Remove) && args.Length < 2)
                return commandType == BlueprintCommand.Remove
                    ? GetLang("CMDRemoveArgs", caller)
                    : GetLang("CMDUnlockAllArgs", caller);

            BasePlayer targetPlayer = BasePlayer.Find(args[0]);
            if (targetPlayer == null || !targetPlayer.IsConnected)
                return GetLang("PlayerNotFound", caller, args[0]);

            if (commandType == BlueprintCommand.Reset)
            {
                WipeBPs(targetPlayer);
                return GetLang("ResetPlayersBps", caller, targetPlayer.displayName);
            }

            if (commandType == BlueprintCommand.UnlockAll)
            {
                UnlockAllBPs(targetPlayer);
                return GetLang("UnlockAllPlayersBps", caller, targetPlayer.displayName);
            }

            List<int> blueprintIds = Pool.Get<List<int>>();
            List<string> results = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                ItemBlueprint bp = ItemManager.FindItemDefinition(args[i])?.Blueprint;
                if (bp == null)
                {
                    results.Add(GetLang("CannotFindBP", caller, args[i]));
                    continue;
                }

                if (!blueprintIds.Contains(bp.targetItem.itemid))
                    blueprintIds.Add(bp.targetItem.itemid);

                results.Add(GetLang(
                    commandType == BlueprintCommand.Unlock ? "UnlockPlayersBps" : "RemovePlayersBps",
                    caller, bp.name, targetPlayer.displayName));
            }

            if (blueprintIds.Count == 0)
            {
                Pool.FreeUnmanaged(ref blueprintIds);
                return string.Join("\n", results);
            }

            if (commandType == BlueprintCommand.Unlock)
                UnlockBPs(targetPlayer, blueprintIds);
            else
                RemoveBPs(targetPlayer, blueprintIds);

            return string.Join("\n", results);
        }

        #endregion RogueRust Commands

        #region Methods
        private void WipeAllBps()
        {
            foreach (var player in BasePlayer.allPlayerList)
            {
                WipeBPs(player);
            }
            QueueAllPlayersForUpdate();
        }

        private void WipeBPs(BasePlayer player)
        {
            var persistantPlayerInfo = player.PersistantPlayerInfo;
            persistantPlayerInfo.unlockedItems.Clear();
            player.PersistantPlayerInfo = persistantPlayerInfo;
            player.SendNetworkUpdateImmediate();
            player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
        }

        private void RemoveBPs(BasePlayer player, List<int> bps)
        {
            var persistantPlayerInfo = player.PersistantPlayerInfo;
            for (int i = persistantPlayerInfo.unlockedItems.Count - 1; i >= 0; i--)
            {
                if (bps.Contains(persistantPlayerInfo.unlockedItems[i]))
                    persistantPlayerInfo.unlockedItems.RemoveAt(i);
            }
            player.PersistantPlayerInfo = persistantPlayerInfo;
            player.SendNetworkUpdateImmediate();
            player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
            Pool.FreeUnmanaged(ref bps);
        }

        private void UnlockBPs(BasePlayer player, List<int> bps)
        {
            var persistantPlayerInfo = player.PersistantPlayerInfo;
            foreach (var bp in bps)
            {
                if (persistantPlayerInfo.unlockedItems.Contains(bp))
                    continue;
                persistantPlayerInfo.unlockedItems.Add(bp);
            }
            player.PersistantPlayerInfo = persistantPlayerInfo;
            player.SendNetworkUpdateImmediate();
            player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
            Pool.FreeUnmanaged(ref bps);
        }

        private void UnlockAllBPs(BasePlayer player)
        {
            var persistantPlayerInfo = player.PersistantPlayerInfo;
            foreach (var bp in _permissionBPs[permunlockall])
            {
                if (persistantPlayerInfo.unlockedItems.Contains(bp))
                    continue;
                persistantPlayerInfo.unlockedItems.Add(bp);
            }
            player.PersistantPlayerInfo = persistantPlayerInfo;
            player.SendNetworkUpdateImmediate();
            player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
        }

        private void UpdatePlayerBPs(BasePlayer player, bool defaultOnly = false)
        {
            if (player == null) return;

            HashSet<int> bpsToUnlock = Pool.Get<HashSet<int>>();
            foreach (int blueprintId in _defaultBlueprints)
                bpsToUnlock.Add(blueprintId);

            if (!defaultOnly)
            {
                foreach (KeyValuePair<string, HashSet<int>> entry in _permissionBPs)
                {
                    if (!HasPerm(player.UserIDString, entry.Key))
                        continue;

                    foreach (int blueprintId in entry.Value)
                        bpsToUnlock.Add(blueprintId);
                }
            }

            var persistentPlayerInfo = player.PersistantPlayerInfo;
            HashSet<int> existing = Pool.Get<HashSet<int>>();
            foreach (int blueprintId in persistentPlayerInfo.unlockedItems)
                existing.Add(blueprintId);

            bool update = false;
            foreach (int blueprintId in bpsToUnlock)
            {
                if (!existing.Add(blueprintId))
                    continue;

                persistentPlayerInfo.unlockedItems.Add(blueprintId);
                update = true;
            }

            Pool.FreeUnmanaged(ref existing);
            Pool.FreeUnmanaged(ref bpsToUnlock);

            if (!update) return;

            player.PersistantPlayerInfo = persistentPlayerInfo;
            player.SendNetworkUpdateImmediate();
            player.ClientRPC(RpcTarget.Player("UnlockedBlueprint", player), 0);
        }

        #endregion Methods

        #region Hooks
        void OnNewSave(string filename)
        {
            if (!config.General.WipeOnMap)
                return;
            WipeAllBps();
        }

        object CanUnlockTechTreeNode(BasePlayer player, TechTreeData.NodeInstance node, TechTreeData techTree)
        {
            ItemBlueprint itemBlueprint = node.itemDef.Blueprint;
            if (itemBlueprint != null && !itemBlueprint.isResearchable)
            {
                int mins = -1;
                _unlockAfterWipe.TryGetValue(itemBlueprint, out mins);
                player.ChatMessage(GetLang("BlueprintLocked", player, itemBlueprint.name,
                    (mins == -1) ? "forever" : $"{TimeSpan.FromSeconds(mins * 60 - SecondsSinceWipe()):hh\\:mm}"));
                return false;
            }
            return null;
        }
        private void OnUserPermissionGranted(string userId, string perm)
        {
            if (string.IsNullOrEmpty(perm) || perm.IndexOf("blueprintmanager", StringComparison.OrdinalIgnoreCase) < 0) return;
            BasePlayer player = BasePlayer.Find(userId);
            if (player == null) return;
            UpdatePlayerBPs(player);
        }

        private void OnUserGroupAdded(string userId, string groupname)
        {
            foreach (var groupperms in permission.GetGroupPermissions(groupname))
            {
                if (groupperms.IndexOf("blueprintmanager", StringComparison.OrdinalIgnoreCase) < 0) continue;
                BasePlayer player = BasePlayer.Find(userId);
                if (player == null) return;
                UpdatePlayerBPs(player);
                return;
            }
        }

        private void OnPlayerConnected(BasePlayer player) => QueuePlayerUpdate(player);

        #endregion Hooks

        #region Helpers
        private string GetLang(string langKey, BasePlayer player = null, params object[] args)
        {
            string language = player == null ? "en" : lang.GetLanguage(player.UserIDString);
            if (string.IsNullOrWhiteSpace(language)) language = "en";

            Dictionary<string, string> catalog;
            string template;
            if (!_messages.TryGetValue(language, out catalog) || !catalog.TryGetValue(langKey, out template))
            {
                if (!_messages.TryGetValue("en", out catalog) || !catalog.TryGetValue(langKey, out template))
                    template = langKey;
            }

            return args == null || args.Length == 0 ? template : string.Format(template, args);
        }

        private bool HasPerm(string id, string perm)
        {
            if (Rogue.Permissions.Has(id, perm)) return true;
            const string canonicalPrefix = "roguerustblueprintmanager.";
            if (perm != null && perm.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
                return Rogue.Permissions.Has(id, "blueprintmanager." + perm.Substring(canonicalPrefix.Length));
            return false;
        }

        private bool CanAdmin(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player != null)
                return player.IsAdmin || HasPerm(player.UserIDString, permadmin);

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            return arg == null || arg.Connection == null || arg.Connection.authLevel >= 2;
        }

        private double SecondsSinceWipe() => (DateTime.UtcNow - _lastWipe).TotalSeconds;
        #endregion Helpers
    }
}
