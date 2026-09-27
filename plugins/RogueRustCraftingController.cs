using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustCraftingController", "RogueAssassin", "2.1.0")]
    [Description("RogueRust-powered crafting control with legacy configuration and permission compatibility.")]

    public sealed class RogueRustCraftingController : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);
        private const string PluginDataPath = "RogueRustCraftingController";
        private const string LanguageFileName = "messages.json";
        #region Config
        private Configuration config;

        private readonly Dictionary<string, CraftingBackupData> defaultsetup = new Dictionary<string, CraftingBackupData>();

        public class CraftingData
        {
            public bool canCraft;
            public bool canResearch;
            public bool useCustomCraftTime;
            public float craftTime;
            public int workbenchLevel;
            public ulong defaultskinid;
        }

        public struct CraftingBackupData
        {
            public bool canCraft;
            public bool canResearch;
            public bool forceCraftTime;
            public float craftTime;
            public float eraTime;
            public int workbenchLevel;
        }

        public sealed class Configuration
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Permission Settings", Order = 20)]
            public PermissionSettings Permissions = new PermissionSettings();

            [JsonProperty("Crafting Settings", Order = 30)]
            public CraftingSettings Crafting = new CraftingSettings();

            [JsonProperty("Advanced Crafting Options", Order = 40, ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, CraftingData> CraftingOptions = new Dictionary<string, CraftingData>();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        public sealed class GeneralSettings
        {
            [JsonProperty("Save commands to config (save config changes via command to the configuration)")]
            public bool SaveCommands = true;

            [JsonProperty("Simple Mode (disables: instant bulk craft, skin options and full inventory checks for better performance)")]
            public bool SimpleMode = false;

            [JsonProperty("Complete crafting on server shut down")]
            public bool CompleteCrafting = false;

            [JsonProperty("Show Crafting Notes")]
            public bool ShowCraftNotes = false;
        }

        public sealed class PermissionSettings
        {
            [JsonProperty("Crafting rate bonus multiplier (permission suffix, multiplier)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> BonusMultiplier = new Dictionary<string, float>
            {
                { "vip1", 1.5f },
                { "vip2", 2f }
            };
        }

        public sealed class CraftingSettings
        {
            [JsonProperty("Default crafting rate multiplier 0 = Instant, 1 = Default, 2 = 2x speed")]
            public float CraftingRateMultiplier = 1f;

            [JsonProperty("Allow crafting when inventory is full")]
            public bool FullInventory = false;

            [JsonProperty("Craft items with random skins if not already skinned")]
            public bool RandomSkins = false;
        }

        // v2.0.0 and legacy CraftingController configuration shape.
        private sealed class LegacyConfiguration
        {
            [JsonProperty("Default crafting rate multiplier 0 = Instant, 1 = Default, 2 = 2x speed")]
            public float CraftingRateMultiplier = 1f;
            [JsonProperty("Save commands to config (save config changes via command to the configuration)")]
            public bool SaveCommands = true;
            [JsonProperty("Simple Mode (disables: instant bulk craft, skin options and full inventory checks for better performance)")]
            public bool SimpleMode;
            [JsonProperty("Allow crafting when inventory is full")]
            public bool FullInventory;
            [JsonProperty("Complete crafting on server shut down")]
            public bool CompleteCrafting;
            [JsonProperty("Craft items with random skins if not already skinned")]
            public bool RandomSkins;
            [JsonProperty("Show Crafting Notes")]
            public bool ShowCraftNotes;
            [JsonProperty("Crafting rate bonus mulitplier (apply oxide perms for additional mulitpliers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, float> BonusMultiplier = new Dictionary<string, float>();
            [JsonProperty("Advanced Crafting Options", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, CraftingData> CraftingOptions = new Dictionary<string, CraftingData>();
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            MigrateLegacyConfig();
            base.LoadConfig();
            try
            {
                Dictionary<string, object> raw = Config.ReadObject<Dictionary<string, object>>();
                bool structured = raw != null &&
                    (raw.ContainsKey("General Settings") || raw.ContainsKey("Crafting Settings") ||
                     raw.ContainsKey("Permission Settings") || raw.ContainsKey("Version (DO NOT CHANGE)"));

                if (structured)
                {
                    config = Config.ReadObject<Configuration>() ?? new Configuration();
                }
                else
                {
                    LegacyConfiguration legacy = Config.ReadObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    config = FromLegacyConfiguration(legacy);
                    LogInformation("Configuration", "Migrated legacy flat configuration to the RogueRust v2.1.0 family layout.");
                }

                NormalizeConfig();
                SaveConfig();
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", $"Configuration file {Name}.json is invalid; using defaults: {exception.Message}");
                config = new Configuration();
                SaveConfig();
            }
        }

        private static Configuration FromLegacyConfiguration(LegacyConfiguration legacy)
        {
            return new Configuration
            {
                General = new GeneralSettings
                {
                    SaveCommands = legacy.SaveCommands,
                    SimpleMode = legacy.SimpleMode,
                    CompleteCrafting = legacy.CompleteCrafting,
                    ShowCraftNotes = legacy.ShowCraftNotes
                },
                Permissions = new PermissionSettings
                {
                    BonusMultiplier = legacy.BonusMultiplier ?? new Dictionary<string, float>()
                },
                Crafting = new CraftingSettings
                {
                    CraftingRateMultiplier = legacy.CraftingRateMultiplier,
                    FullInventory = legacy.FullInventory,
                    RandomSkins = legacy.RandomSkins
                },
                CraftingOptions = legacy.CraftingOptions ?? new Dictionary<string, CraftingData>(),
                Version = CurrentVersion
            };
        }

        private void NormalizeConfig()
        {
            config.General ??= new GeneralSettings();
            config.Permissions ??= new PermissionSettings();
            config.Crafting ??= new CraftingSettings();
            config.Permissions.BonusMultiplier ??= new Dictionary<string, float>();
            config.CraftingOptions ??= new Dictionary<string, CraftingData>();
            config.Crafting.CraftingRateMultiplier = Math.Max(0f, config.Crafting.CraftingRateMultiplier);
            config.Version = CurrentVersion;
        }

        private void MigrateLegacyConfig()
        {
            try
            {
                string current = Path.Combine(Interface.Oxide.ConfigDirectory, Name + ".json");
                if (File.Exists(current)) return;

                string legacy = Path.Combine(Interface.Oxide.ConfigDirectory, "CraftingController.json");
                if (!File.Exists(legacy)) return;

                File.Copy(legacy, current, false);
                LogInformation("Configuration", "Migrated CraftingController.json to " + Name + ".json.");
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Legacy config migration failed: " + exception.Message);
            }
        }

        protected override void SaveConfig()
        {
            LogInformation("Configuration", $"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }

        #endregion Config

        #region Init
        [RoguePermission]
        private const string perminstantbulkcraft = "roguerustcraftingcontroller.instantbulkcraft";
        [RoguePermission]
        private const string permblockitems = "roguerustcraftingcontroller.blockitems";
        [RoguePermission]
        private const string permitemrate = "roguerustcraftingcontroller.itemrate";
        [RoguePermission]
        private const string permcraftingrate = "roguerustcraftingcontroller.craftingrate";
        [RoguePermission]
        private const string permsetbenchlvl = "roguerustcraftingcontroller.setbenchlvl";
        [RoguePermission]
        private const string permsetskins = "roguerustcraftingcontroller.setskins";

        private string[] bonusPermNames = Array.Empty<string>();
        private float[] bonusPermMults = Array.Empty<float>();

        private void OnServerInitialized()
        {
            BackupDefaultBPs();

            bonusPermNames = new string[config.Permissions.BonusMultiplier.Count];
            bonusPermMults = new float[config.Permissions.BonusMultiplier.Count];
            int tier = 0;
            foreach (var bonus in config.Permissions.BonusMultiplier)
            {
                bonusPermNames[tier] = "roguerustcraftingcontroller." + bonus.Key;
                bonusPermMults[tier] = bonus.Value;
                tier++;
                WarnIfPercentage(bonus.Key, bonus.Value);
            }
            WarnIfPercentage("Default crafting rate multiplier", config.Crafting.CraftingRateMultiplier);

            // Static permissions are discovered from [RoguePermission].
            // Config-defined bonus tiers remain dynamic and must be registered at runtime.
            foreach (var perm in bonusPermNames)
                Rogue.Permissions.Register(perm, this);

            LoadLanguageFiles();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustCraftingController/; Lang=<language>/RogueRust/RogueRustCraftingController/messages.json");

            if (config.General.SimpleMode)
            {
                Unsubscribe(nameof(OnItemCraft));
                Unsubscribe(nameof(OnItemCraftFinished));
                Unsubscribe(nameof(OnItemCraftCancelled));
            }

            foreach (var item in ItemManager.bpList)
            {
                if (config.CraftingOptions.ContainsKey(item.targetItem.shortname)) continue;
                config.CraftingOptions.Add(item.targetItem.shortname, new CraftingData()
                {
                    craftTime = item.time,
                    workbenchLevel = item.workbenchLevelRequired,
                    canCraft = item.userCraftable,
                    canResearch = item.isResearchable
                });
            }

            SaveConfig();
            UpdateCraftingRate();
        }

        private void WarnIfPercentage(string key, float value)
        {
            if (value <= 10f) return;
            LogWarning("Configuration", $"'{key}' is {value}, which means {value}x craft speed (effectively instant). " +
                         $"If that came from a pre-3.3.6 config it was a percentage - use {100f / value:0.##} instead.");
        }

        private void BackupDefaultBPs()
        {
            foreach (var bp in ItemManager.GetBlueprints())
            {
                var target = bp.targetItem;
                if (target == null || defaultsetup.ContainsKey(target.shortname)) continue;

                defaultsetup.Add(target.shortname, new CraftingBackupData()
                {
                    canCraft = bp.userCraftable,
                    canResearch = bp.isResearchable,
                    forceCraftTime = bp.ForceThisCraftTime,
                    craftTime = bp.time,
                    eraTime = bp.GetRecipeOverride().craftTime,
                    workbenchLevel = bp.workbenchLevelRequired
                });
            }
        }

        private void Unload()
        {
            ClearBonusBlueprints();
            skinupdate.Clear();
            skinsCache.Clear();

            //Reset to defaults
            foreach (var bp in ItemManager.GetBlueprints())
            {
                var target = bp.targetItem;
                CraftingBackupData craftingData;
                if (target == null || !defaultsetup.TryGetValue(target.shortname, out craftingData))
                    continue;

                SetCraftTime(bp, craftingData.craftTime, craftingData.eraTime);
                bp.ForceThisCraftTime = craftingData.forceCraftTime;
                bp.workbenchLevelRequired = craftingData.workbenchLevel;
                bp.userCraftable = craftingData.canCraft;
                bp.isResearchable = craftingData.canResearch;
            }
        }

        #endregion Init

        #region Localization
        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        protected override void LoadDefaultMessages()
        {
            // RogueRust localization is stored under lang/RogueRust/RogueRustCraftingController.
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NoInvSpace"] = "You don't have enough room to craft this item!",
                ["NoPerms"] = "You don't have permission to use this command.",
                ["CannotFindItem"] = "Cannot find item {0}.",
                ["ItemBlocked"] = "{0} has been blocked from crafting.",
                ["ItemUnblocked"] = "{0} has been unblocked from crafting.",
                ["NeedsAdvancedOptions"] = "You need to enable advanced crafting options in your config to use this.",
                ["WrongNumberInput"] = "Your input needs to be a number.",
                ["ItemCraftTimeSet"] = "{0} craft time set to {1} seconds",
                ["WorkbenchLevelSet"] = "{0} workbench level set to {1}",
                ["CurrentCraftingRate"] = "The current speed crafting multiplier is {0}",
                ["CraftingMultiUpdated"] = "The crafting speed multiplier was updated to {0}",
                ["CraftTime2Args"] = "This command needs two arguments in the format /crafttime item.shortname timetocraft",
                ["BlockItem1Args"] = "This command needs one argument in the format /blockitem item.shortname",
                ["UnblockItem1Args"] = "This command needs one argument in the format /unblockitem item.shortname",
                ["WorckBenchLvl2Args"] = "This command needs two arguments in the format /benchlvl item.shortname workbenchlvl",
                ["BenchLevelInput"] = "The work bench level must be between 0 and 3",
                ["SetSkin2Args"] = "This command needs two arguments in the format /setcraftskin item.shortname skinworkshopid",
                ["SkinSet"] = "The default skin for {0} was set to {1}",
                ["CraftTimeCheck"] = "The craft time of this item is {0}"
            };
        }

        private void LoadLanguageFiles()
        {
            _messages.Clear();

            string langRoot = Interface.Oxide.LangDirectory;
            string legacyRogueRoot = Path.Combine(langRoot, "RogueRust", Name);
            HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };

            try
            {
                if (Directory.Exists(langRoot))
                {
                    foreach (string directory in Directory.GetDirectories(langRoot))
                    {
                        string language = Path.GetFileName(directory);
                        if (!string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                            languages.Add(language);
                    }
                }

                if (Directory.Exists(legacyRogueRoot))
                {
                    foreach (string file in Directory.GetFiles(legacyRogueRoot, "*.json", SearchOption.TopDirectoryOnly))
                        languages.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Could not enumerate language directories: " + exception.Message);
            }

            foreach (string language in languages)
                LoadLanguageCatalog(langRoot, legacyRogueRoot, language);

            if (!_messages.ContainsKey("en"))
                _messages["en"] = CreateDefaultMessages();
        }

        private void LoadLanguageCatalog(string langRoot, string legacyRogueRoot, string language)
        {
            try
            {
                string root = Path.Combine(langRoot, language, "RogueRust", Name);
                Directory.CreateDirectory(root);
                string target = Path.Combine(root, LanguageFileName);

                if (!File.Exists(target))
                {
                    string oldRogue = Path.Combine(legacyRogueRoot, language + ".json");
                    string legacyNamed = Path.Combine(langRoot, language, "CraftingController.json");
                    string legacyCurrent = Path.Combine(langRoot, language, Name + ".json");

                    if (File.Exists(oldRogue))
                        File.Copy(oldRogue, target, false);
                    else if (File.Exists(legacyNamed))
                        File.Copy(legacyNamed, target, false);
                    else if (File.Exists(legacyCurrent))
                        File.Copy(legacyCurrent, target, false);
                    else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(target, JsonConvert.SerializeObject(CreateDefaultMessages(), Formatting.Indented));
                }

                if (!File.Exists(target)) return;

                Dictionary<string, string> catalog =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(target));
                if (catalog != null)
                    _messages[language] = catalog;
            }
            catch (Exception exception)
            {
                LogWarning("Localization", $"Could not load language '{language}': {exception.Message}");
            }
        }

        #endregion Localization

        #region Commands
        [RogueCommand("craftrate", Description = "View or set the global crafting rate.", Usage = "/craftrate [multiplier]", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandCraftingRate(RogueCommandContext context)
        {
            if (!HasPermission(context, permcraftingrate))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length == 0)
                return RogueCommandResult.Ok(GetLang("CurrentCraftingRate", context.NativePlayer, config.Crafting.CraftingRateMultiplier));

            float craftingrate;
            if (!TryParseNumber(args[0], out craftingrate))
                return RogueCommandResult.Fail(GetLang("WrongNumberInput", context.NativePlayer));

            if (craftingrate < 0f) craftingrate = 0f;
            config.Crafting.CraftingRateMultiplier = craftingrate;
            UpdateCraftingRate();
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("CraftingMultiUpdated", context.NativePlayer, config.Crafting.CraftingRateMultiplier));
        }

        [RogueCommand("crafttime", Description = "View or set an item's craft time.", Usage = "/crafttime <item> [seconds|default]", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandCraftTime(RogueCommandContext context)
        {
            if (!HasPermission(context, permitemrate))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length == 1)
            {
                ItemDefinition itemcheck = FindItem(args[0]);
                if (itemcheck)
                    return RogueCommandResult.Ok(GetLang("CraftTimeCheck", context.NativePlayer, itemcheck.Blueprint.GetCraftTime()));
            }

            if (args.Length < 2)
                return RogueCommandResult.Fail(GetLang("CraftTime2Args", context.NativePlayer));

            ItemDefinition setitem = FindItem(args[0]);
            if (!setitem)
                return RogueCommandResult.Fail(GetLang("CannotFindItem", context.NativePlayer, args[0]));

            CraftingData craftingdata;
            if (!config.CraftingOptions.TryGetValue(setitem.shortname, out craftingdata))
                return RogueCommandResult.Fail(GetLang("NeedsAdvancedOptions", context.NativePlayer));

            if (args[1].Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                craftingdata.useCustomCraftTime = false;
                UpdateCraftingRate();
                if (config.General.SaveCommands) SaveConfig();
                return RogueCommandResult.Ok(GetLang("ItemCraftTimeSet", context.NativePlayer,
                    setitem.shortname, setitem.Blueprint.GetCraftTime().ToString(CultureInfo.InvariantCulture)));
            }

            float crafttime;
            if (!TryParseNumber(args[1], out crafttime))
                return RogueCommandResult.Fail(GetLang("WrongNumberInput", context.NativePlayer));

            craftingdata.craftTime = crafttime;
            craftingdata.useCustomCraftTime = true;
            UpdateCraftingRate();
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("ItemCraftTimeSet", context.NativePlayer,
                setitem.shortname, crafttime.ToString(CultureInfo.InvariantCulture)));
        }

        [RogueCommand("blockitem", Description = "Block an item from crafting and research.", Usage = "/blockitem <item>", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandBlockItem(RogueCommandContext context)
        {
            if (!HasPermission(context, permblockitems))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length < 1)
                return RogueCommandResult.Fail(GetLang("BlockItem1Args", context.NativePlayer));

            ItemDefinition item = FindItem(args[0]);
            if (!item)
                return RogueCommandResult.Fail(GetLang("CannotFindItem", context.NativePlayer, args[0]));

            CraftingData data;
            if (!config.CraftingOptions.TryGetValue(item.shortname, out data))
                return RogueCommandResult.Fail(GetLang("NeedsAdvancedOptions", context.NativePlayer));

            data.canCraft = false;
            data.canResearch = false;
            UpdateCraftingRate();
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("ItemBlocked", context.NativePlayer, item.shortname));
        }

        [RogueCommand("unblockitem", Description = "Unblock an item for crafting and research.", Usage = "/unblockitem <item>", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandUnblockItem(RogueCommandContext context)
        {
            if (!HasPermission(context, permblockitems))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length < 1)
                return RogueCommandResult.Fail(GetLang("UnblockItem1Args", context.NativePlayer));

            ItemDefinition item = FindItem(args[0]);
            if (!item)
                return RogueCommandResult.Fail(GetLang("CannotFindItem", context.NativePlayer, args[0]));

            CraftingData data;
            if (!config.CraftingOptions.TryGetValue(item.shortname, out data))
                return RogueCommandResult.Fail(GetLang("NeedsAdvancedOptions", context.NativePlayer));

            data.canCraft = true;
            data.canResearch = true;
            UpdateCraftingRate();
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("ItemUnblocked", context.NativePlayer, item.shortname));
        }

        [RogueCommand("benchlvl", Description = "Set an item's workbench requirement.", Usage = "/benchlvl <item> <0-3>", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandWorkbenchLVL(RogueCommandContext context)
        {
            if (!HasPermission(context, permsetbenchlvl))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length < 2)
                return RogueCommandResult.Fail(GetLang("WorckBenchLvl2Args", context.NativePlayer));

            ItemDefinition item = FindItem(args[0]);
            if (!item)
                return RogueCommandResult.Fail(GetLang("CannotFindItem", context.NativePlayer, args[0]));

            int benchlvl;
            if (!int.TryParse(args[1], out benchlvl))
                return RogueCommandResult.Fail(GetLang("WrongNumberInput", context.NativePlayer));
            if (benchlvl < 0 || benchlvl > 3)
                return RogueCommandResult.Fail(GetLang("BenchLevelInput", context.NativePlayer));

            CraftingData data;
            if (!config.CraftingOptions.TryGetValue(item.shortname, out data))
                return RogueCommandResult.Fail(GetLang("NeedsAdvancedOptions", context.NativePlayer));

            data.workbenchLevel = benchlvl;
            UpdateCraftingRate();
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("WorkbenchLevelSet", context.NativePlayer, item.shortname, benchlvl));
        }

        [RogueCommand("setcraftskin", Description = "Set the default crafting skin for an item.", Usage = "/setcraftskin <item> <skinId>", Category = "Crafting", AllowChat = true, AllowConsole = true)]
        private RogueCommandResult CommandSetDefaultSkin(RogueCommandContext context)
        {
            if (!HasPermission(context, permsetskins))
                return RogueCommandResult.Fail(GetLang("NoPerms", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length < 2)
                return RogueCommandResult.Fail(GetLang("SetSkin2Args", context.NativePlayer));

            ItemDefinition item = FindItem(args[0]);
            if (!item)
                return RogueCommandResult.Fail(GetLang("CannotFindItem", context.NativePlayer, args[0]));

            ulong skinid;
            if (!ulong.TryParse(args[1], out skinid))
                return RogueCommandResult.Fail(GetLang("WrongNumberInput", context.NativePlayer));

            CraftingData data;
            if (!config.CraftingOptions.TryGetValue(item.shortname, out data))
                return RogueCommandResult.Fail(GetLang("NeedsAdvancedOptions", context.NativePlayer));

            data.defaultskinid = skinid;
            if (config.General.SaveCommands) SaveConfig();
            return RogueCommandResult.Ok(GetLang("SkinSet", context.NativePlayer, item.shortname, skinid));
        }
        #endregion Commands

        #region Methods
        private void UpdateCraftingRate()
        {
            using (Measure("RogueRustCraftingController", "UpdateCraftingRate"))
            {
            foreach (var bp in ItemManager.GetBlueprints())
            {
                var target = bp.targetItem;
                if (target == null) continue;

                CraftingData data;
                CraftingBackupData def;
                if (!config.CraftingOptions.TryGetValue(target.shortname, out data)) continue;
                if (!defaultsetup.TryGetValue(target.shortname, out def)) continue;

                bp.userCraftable = data.canCraft;
                bp.isResearchable = data.canResearch;

                float bptime = def.eraTime > 0f ? def.eraTime : def.craftTime;

                if (config.Crafting.CraftingRateMultiplier == 0f)
                    bptime = 0f;
                else
                {
                    if (data.useCustomCraftTime)
                        bptime = data.craftTime;
                    else
                        bptime /= config.Crafting.CraftingRateMultiplier;
                }

                SetCraftTime(bp, bptime, bptime);

                bp.ForceThisCraftTime = true;

                if (data.workbenchLevel > 3) data.workbenchLevel = 3;
                if (data.workbenchLevel >= 0)
                    bp.workbenchLevelRequired = data.workbenchLevel;

                RefreshBonusBlueprints(bp, bptime);
            }
            }
        }

        private void SetCraftTime(ItemBlueprint bp, float time, float eraTime)
        {
            for (int i = 0; i < bp.Overrides.Count; i++)
            {
                if (bp.Overrides[i].TargetEra != ConVar.Server.Era) continue;
                var bpoverride = bp.Overrides[i];
                if (bpoverride.craftTime > 0f)
                {
                    bpoverride.craftTime = eraTime;
                    bp.Overrides[i] = bpoverride;
                }
                break;
            }
            bp.time = time;
        }

        private void InstantBulkCraft(BasePlayer player, ItemCraftTask task, ItemDefinition item, int amount, int craftSkin, ulong skin)
        {
            if (skin == 0uL && craftSkin != 0)
            {
                skin = ItemDefinition.FindSkin(item.itemid, craftSkin);
            }
            int maxStack = item.stackable;
            if (maxStack <= 0) return;

            bool scaleCondition = task.conditionScale != 1f && item.condition.enabled && item.condition.max > 0f;
            bool notes = config.General.ShowCraftNotes;
            var inv = player.inventory;
            var crafter = inv.crafting;

            for (int remaining = amount; remaining > 0; remaining -= maxStack)
            {
                int stack = Mathf.Min(remaining, maxStack);
                var itemtogive = ItemManager.Create(item, stack, skin);

                if (scaleCondition)
                {
                    itemtogive.maxCondition *= task.conditionScale;
                    itemtogive.condition = itemtogive.maxCondition;
                }

                itemtogive.OnVirginSpawn(player);
                itemtogive.SetItemOwnership(player, ItemOwnershipPhrases.CraftedPhrase);

                if (skin != 0uL)
                {
                    var held = itemtogive.GetHeldEntity();
                    if (held != null) held.skinID = skin;
                }

                if (!inv.GiveItem(itemtogive))
                    itemtogive.Drop(inv.containerMain.dropPosition, inv.containerMain.dropVelocity);

                if (notes) player.Command("note.inv", item.itemid, stack);
                Interface.CallHook("OnItemCraftFinished", task, itemtogive, crafter);
            }

            player.ProcessMissionEvent(BaseMission.MissionEventType.CRAFT_ITEM, item.itemid, amount);

            if (!string.IsNullOrEmpty(task.blueprint.UnlockAchievment))
                player.GiveAchievement(task.blueprint.UnlockAchievment);


            if (task.takenItems != null)
            {
                foreach (var taken in task.takenItems) taken.Remove();
                task.takenItems.Clear();
            }
        }

        private static void CompleteCrafting(BasePlayer player)
        {
            if (player.inventory.crafting.queue.Count == 0) return;
            player.inventory.crafting.FinishCrafting(player.inventory.crafting.queue.First.Value);
            player.inventory.crafting.queue.RemoveFirst();
        }

        private static void CancelAllCrafting(BasePlayer player)
        {
            ItemCrafter crafter = player.inventory.crafting;
            crafter.CancelAll();
        }

        #endregion Methods

        #region Bonus Blueprints

        private readonly Dictionary<ItemBlueprint, ItemBlueprint[]> bonusBPs = new Dictionary<ItemBlueprint, ItemBlueprint[]>();
        private GameObject bonusRoot;

        private ItemBlueprint GetBonusBlueprint(ItemBlueprint bp, int tier)
        {
            ItemBlueprint[] tiers;
            if (!bonusBPs.TryGetValue(bp, out tiers))
                bonusBPs[bp] = tiers = new ItemBlueprint[bonusPermMults.Length];

            ItemBlueprint stand = tiers[tier];
            if (stand != null) return stand;

            if (bonusRoot == null)
            {
                bonusRoot = new GameObject("CraftingController.Bonus");
                bonusRoot.SetActive(false);
            }

            stand = bonusRoot.AddComponent<ItemBlueprint>();

            stand._targetItem = bp.targetItem;
            stand.ingredients = bp.ingredients;
            stand.additionalUnlocks = bp.additionalUnlocks;
            stand.defaultBlueprint = bp.defaultBlueprint;
            stand.userCraftable = bp.userCraftable;
            stand.isResearchable = bp.isResearchable;
            stand.forceShowInConveyorFilter = bp.forceShowInConveyorFilter;
            stand.rarity = bp.rarity;
            stand.workbenchLevelRequired = bp.workbenchLevelRequired;
            stand.surplusItems = bp.surplusItems;
            stand.scrapRequired = bp.scrapRequired;
            stand.scrapFromRecycle = bp.scrapFromRecycle;
            stand.NeedsSteamItem = bp.NeedsSteamItem;
            stand.RequireUnlockedItem = bp.RequireUnlockedItem;
            stand.blueprintStackSize = bp.blueprintStackSize;
            stand.amountToCreate = bp.amountToCreate;
            stand.UnlockAchievment = bp.UnlockAchievment;
            stand.RecycleStat = bp.RecycleStat;
            stand.Overrides = new List<ItemBlueprint.BlueprintOverride>(bp.Overrides);
            stand.ForceThisCraftTime = true;

            float mult = bonusPermMults[tier];
            float time = mult == 0f ? 0f : bp.time / mult;
            SetCraftTime(stand, time, time);

            return tiers[tier] = stand;
        }

        private void RefreshBonusBlueprints(ItemBlueprint bp, float bptime)
        {
            ItemBlueprint[] tiers;
            if (!bonusBPs.TryGetValue(bp, out tiers)) return;

            for (int i = 0; i < tiers.Length; i++)
            {
                if (tiers[i] == null) continue;
                tiers[i].workbenchLevelRequired = bp.workbenchLevelRequired;
                float mult = bonusPermMults[i];
                float time = mult == 0f ? 0f : bptime / mult;
                SetCraftTime(tiers[i], time, time);
            }
        }

        private void ClearBonusBlueprints()
        {
            bonusBPs.Clear();
            if (bonusRoot != null) UnityEngine.Object.Destroy(bonusRoot);
            bonusRoot = null;
        }
        #endregion Bonus Blueprints

        #region Hooks

        private readonly Dictionary<ulong, Dictionary<int, ulong>> skinupdate = new Dictionary<ulong, Dictionary<int, ulong>>();

        private object OnItemCraft(ItemCraftTask task, BasePlayer player, Item fromTempBlueprint)
        {
            if (task == null || task.blueprint == null || task.amount == 0) return null;
            var target = task.blueprint.targetItem;
            if (target == null || task.instanceData != null) return null;

            int tocraft = task.amount * task.blueprint.amountToCreate;
            ulong defaultskin = 0uL;
            int freeslots = FreeSlots(player);
            bool trimmed = false;
            if (!config.Crafting.FullInventory && StackCount(target, tocraft) >= freeslots)
            {
                trimmed = true;
                int space = FreeSpace(player, target);
                if (space < 1)
                {
                    ReturnCraft(task, player);
                    return true;
                }
                int taskamt = task.amount * task.blueprint.amountToCreate;
                for (int i = 0; i < 20 && taskamt > space; i++)
                {
                    var oldtaskamt = taskamt;
                    taskamt = space;
                    foreach (var item in task.takenItems)
                    {
                        var itemtogive = item;
                        double fraction = (double)taskamt / (double)oldtaskamt;
                        int amttogive = (int)(item.amount * (1 - fraction));
                        if (amttogive <= 1)
                        {
                            ReturnCraft(task, player);
                            return true;
                        }
                        itemtogive = ItemManager.Create(item.info, amttogive, 0uL);
                        item.amount -= amttogive;

                        player.GiveItem(itemtogive);
                    }
                    space -= (freeslots - FreeSlots(player)) * target.stackable;
                    if (space < 1 || taskamt < 1)
                    {
                        ReturnCraft(task, player);
                        return true;
                    }
                    if (taskamt <= space) break;

                }
                task.amount = (int)(taskamt / task.blueprint.amountToCreate);
            }


            if (task.skinID == 0)
            {
                CraftingData data;
                if (config.CraftingOptions.TryGetValue(target.shortname, out data))
                {
                    defaultskin = data.defaultskinid;
                }

                if (config.Crafting.RandomSkins && defaultskin == 0)
                {
                    List<ulong> skins = GetSkins(target);
                    if (skins.Count > 0) defaultskin = skins.GetRandom();
                }

                if (defaultskin > 999999)
                    SetPendingSkin(player, task, defaultskin);
                else
                    task.skinID = (int)defaultskin;
            }

            int tier = -1;
            for (int i = 0; i < bonusPermNames.Length; i++)
            {
                if (!HasPerm(player.UserIDString, bonusPermNames[i])) continue;
                if (tier < 0 || bonusPermMults[i] == 0f || (bonusPermMults[tier] != 0f && bonusPermMults[i] > bonusPermMults[tier]))
                    tier = i;
            }

            if (tier >= 0 && bonusPermMults[tier] != 1f)
                task.blueprint = GetBonusBlueprint(task.blueprint, tier);

            if (task.blueprint.time == 0f || HasPerm(player.UserIDString, perminstantbulkcraft))
            {
                ClearPendingSkin(player, task);
                if (trimmed)
                    tocraft = task.amount * task.blueprint.amountToCreate;
                InstantBulkCraft(player, task, target, tocraft, task.skinID, defaultskin);
                task.cancelled = true;
                return true;
            }
            return null;
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            var player = crafter?.owner;
            if (player == null) return;

            Dictionary<int, ulong> tasks;
            ulong skinid;
            if (!skinupdate.TryGetValue(player.userID, out tasks)) return;
            if (!tasks.TryGetValue(task.taskUID, out skinid)) return;

            item.skin = skinid;
            var held = item.GetHeldEntity();

            if (held != null)
            {
                held.skinID = skinid;
                held.SendNetworkUpdate();
            }
            if (task.amount <= 0)
                ClearPendingSkin(player, task);
        }

        private void OnItemCraftCancelled(ItemCraftTask task, ItemCrafter crafter)
        {
            if (crafter?.owner != null) ClearPendingSkin(crafter.owner, task);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player != null) skinupdate.Remove(player.userID);
        }

        private void OnServerShutdown()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.inventory.crafting.queue.Count == 0) continue;
                if (config.General.CompleteCrafting)
                    CompleteCrafting(player);
                CancelAllCrafting(player);
            }
        }

        #endregion Hooks

        #region Helpers
        private void SetPendingSkin(BasePlayer player, ItemCraftTask task, ulong skin)
        {
            ulong id = player.userID;
            Dictionary<int, ulong> tasks;
            if (!skinupdate.TryGetValue(id, out tasks))
                skinupdate[id] = tasks = new Dictionary<int, ulong>();
            tasks[task.taskUID] = skin;
        }

        private void ClearPendingSkin(BasePlayer player, ItemCraftTask task)
        {
            if (player == null) return;
            ulong id = player.userID;
            Dictionary<int, ulong> tasks;
            if (!skinupdate.TryGetValue(id, out tasks)) return;
            tasks.Remove(task.taskUID);
            if (tasks.Count == 0) skinupdate.Remove(id);
        }

        private static bool TryParseNumber(string input, out float value)
        {
            return float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void ReturnCraft(ItemCraftTask task, BasePlayer crafter)
        {
            task.cancelled = true;
            crafter.ChatMessage(GetLang("NoInvSpace", crafter));
            foreach (var item in task.takenItems)
            {
                if (item.amount > 0)
                    crafter.GiveItem(item);
            }
        }

        private ItemDefinition FindItem(string itemNameOrId)
        {
            ItemDefinition itemDef;
            if (int.TryParse(itemNameOrId, out int itemId))
            {
                itemDef = ItemManager.FindItemDefinition(itemId);
                return itemDef;
            }
            itemDef = ItemManager.FindItemDefinition(itemNameOrId.ToLower());
            return itemDef;
        }

        private int FreeSpace(BasePlayer player, ItemDefinition item)
        {
            var slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            List<Item> containeritems = Pool.Get<List<Item>>();
            Dictionary<ItemDefinition, int> queueamts = Pool.Get<Dictionary<ItemDefinition, int>>();
            containeritems.AddRange(player.inventory.containerMain.itemList);
            containeritems.AddRange(player.inventory.containerBelt.itemList);

            int value = 0;

            foreach (var queueitem in player.inventory.crafting.queue)
            {
                if (queueitem.blueprint.targetItem == item) continue;
                if (queueamts.TryGetValue(queueitem.blueprint.targetItem, out value))
                {
                    queueamts[queueitem.blueprint.targetItem] += queueitem.amount * queueitem.blueprint.amountToCreate;
                    continue;
                }
                queueamts[queueitem.blueprint.targetItem] = queueitem.amount * queueitem.blueprint.amountToCreate;
            }

            int queuestacks = 0;
            foreach (var i in queueamts)
            {
                queuestacks += StackCount(i.Key, i.Value - Stackroom(containeritems, i.Key.shortname));
            }

            int invstackroom = (slots - containeritems.Count - queuestacks) * item.stackable;
            containeritems.ForEach(x => { if (x.info == item && x.amount < x.MaxStackable()) invstackroom += x.MaxStackable() - x.amount; });
            foreach (var x in player.inventory.crafting.queue)
            {
                if (x.blueprint.targetItem.shortname == item.shortname)
                {
                    invstackroom -= x.amount * x.blueprint.amountToCreate;
                }
            }
            Pool.FreeUnmanaged(ref containeritems);
            Pool.FreeUnmanaged(ref queueamts);
            return invstackroom;
        }

        private int FreeSlots(BasePlayer player)
        {
            var slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            var taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
            return slots - taken;
        }

        private int Stackroom(List<Item> items, string item)
        {
            int stackroom = 0;
            items.ForEach(x => { if (x.info.shortname == item && x.amount < x.MaxStackable()) stackroom += x.MaxStackable() - x.amount; });
            return stackroom;
        }

        private static int StackCount(ItemDefinition item, int amount)
        {
            int maxStack = item.stackable;
            if (maxStack <= 0 || amount <= 0) return 0;
            return amount / maxStack + (amount % maxStack > 0 ? 1 : 0);
        }

        private readonly Dictionary<string, List<ulong>> skinsCache = new Dictionary<string, List<ulong>>();
        private List<ulong> GetSkins(ItemDefinition def)
        {
            List<ulong> skins;
            if (skinsCache.TryGetValue(def.shortname, out skins)) return skins;
            skins = new List<ulong>();
            foreach (var skin in ItemSkinDirectory.ForItem(def))
            {
                skins.Add((ulong)skin.id);
            }
            foreach (var skin in Rust.Workshop.Approved.All.Values)
            {
                if (skin.Skinnable.ItemName == def.shortname)
                    skins.Add(skin.WorkshopdId);
            }
            skinsCache.Add(def.shortname, skins);
            return skins;
        }

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
            const string canonicalPrefix = "roguerustcraftingcontroller.";
            if (perm != null && perm.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
                return Rogue.Permissions.Has(id, "craftingcontroller." + perm.Substring(canonicalPrefix.Length));
            return false;
        }

        private bool HasPermission(RogueCommandContext context, string permissionName)
        {
            BasePlayer player = context.NativePlayer;
            if (player != null)
                return player.IsAdmin || HasPerm(player.UserIDString, permissionName);

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            return arg == null || arg.Connection == null || arg.Connection.authLevel >= 2;
        }

        #endregion Helpers
    }
}
