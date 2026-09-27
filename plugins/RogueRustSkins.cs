using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustSkins", "RogueRust", "2.1.1")]
    [Description("Performance-first Skinner-style native Rust skin browser powered by RogueRust services.")]
    public sealed class RogueRustSkins : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.1";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 1);
        [RoguePermission]
        private const string PermissionUse = "roguerustskins.use";
        [RoguePermission]
        private const string PermissionItems = "roguerustskins.items";
        [RoguePermission]
        private const string PermissionCraft = "roguerustskins.craft";
        [RoguePermission]
        private const string PermissionInventory = "roguerustskins.inventory";
        [RoguePermission]
        private const string PermissionContainer = "roguerustskins.container";
        [RoguePermission]
        private const string PermissionBase = "roguerustskins.base";
        [RoguePermission]
        private const string PermissionAll = "roguerustskins.all";
        [RoguePermission]
        private const string PermissionAuto = "roguerustskins.auto";
        [RoguePermission]
        private const string PermissionTeam = "roguerustskins.team";
        [RoguePermission]
        private const string PermissionRequest = "roguerustskins.request";
        [RoguePermission]
        private const string PermissionImport = "roguerustskins.import";
        [RoguePermission]
        private const string PermissionAdmin = "roguerustskins.admin";
        [RoguePermission]
        private const string PermissionBypassAuth = "roguerustskins.bypassauth";
        [RoguePermission]
        private const string PermissionCollection = "roguerustskins.collection";
        private const string CacheConnection = "roguerustskins-cache";
        private const string CacheOwner = "RogueRustSkins";
        private const int PageSize = 48;
        private const string LootPanel = "generic_resizable";
        private const string UiRoot = "RRS.NativePager";
        private const string UiHeader = "RRS.SkinnerHeader";
        private const string UiOptions = "RRS.SkinnerOptions";
        private const int SprayCanItemId = -596876839;

        private readonly Dictionary<int, SkinBucket> _skinsByItem = new Dictionary<int, SkinBucket>();
        private readonly Dictionary<ulong, SkinEntry> _skinById = new Dictionary<ulong, SkinEntry>();
        private readonly Dictionary<string, string> _cacheFingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, BrowserState> _browsers = new Dictionary<ulong, BrowserState>();
        private readonly Dictionary<ulong, PlayerSkinData> _playerData = new Dictionary<ulong, PlayerSkinData>();
        private readonly HashSet<ulong> _sprayHeld = new HashSet<ulong>();
        private readonly Dictionary<ulong, Dictionary<string, float>> _cooldowns = new Dictionary<ulong, Dictionary<string, float>>();
        private readonly Dictionary<ulong, SkinRequestData> _skinRequests = new Dictionary<ulong, SkinRequestData>();
        private readonly Dictionary<ulong, ImportedWorkshopSkin> _importedWorkshop = new Dictionary<ulong, ImportedWorkshopSkin>();
        private readonly HashSet<ulong> _workshopJobs = new HashSet<ulong>();

        private PluginConfig _config;
        private IRogueServices _rogue;
        private BaseEntity _limitedEntity;
        private bool _cacheReady;
        private bool _steamReady;
        private bool _refreshing;
        private Timer _playerSaveTimer;
        private bool _playerDataDirty;
        private int _lastPublishedCatalogueSignature = int.MinValue;


        private sealed class PluginConfig
        {
            [JsonProperty("General Settings")]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Skin Access Settings")]
            public SkinAccessSettings SkinAccess = new SkinAccessSettings();

            [JsonProperty("Automation Settings")]
            public AutomationSettings Automation = new AutomationSettings();

            [JsonProperty("Skin Set Settings")]
            public SkinSetSettings SkinSets = new SkinSetSettings();

            [JsonProperty("UI Settings")]
            public UiSettings UI = new UiSettings();

            [JsonProperty("Cooldown Settings")]
            public CooldownSettings Cooldowns = new CooldownSettings();

            [JsonProperty("Player History Settings")]
            public HistorySettings History = new HistorySettings();

            [JsonProperty("Workshop Request Settings")]
            public RequestSettings Requests = new RequestSettings();

            [JsonProperty("Performance Settings")]
            public PerformanceSettings Performance = new PerformanceSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class GeneralSettings
        {
            [JsonProperty("Apply workshop skin names to items")]
            public bool ApplySkinNames = false;

            [JsonProperty("Require building authorization for deployable/container skinning")]
            public bool RequireBuildingAuthorization = true;

            [JsonProperty("Blacklisted skin IDs")]
            public List<ulong> BlacklistedSkins = new List<ulong>();

            [JsonProperty("Blacklisted item IDs")]
            public List<int> BlacklistedItems = new List<int>();
        }

        private sealed class SkinAccessSettings
        {
            [JsonProperty("Allow approved paid/DLC skins the player does not own")]
            public bool AllowUnownedPaidDlcSkins = true;

            [JsonProperty("Allow skins the player owns")]
            public bool AllowOwnedSkins = true;

            [JsonProperty("Include redirected/DLC item variants")]
            public bool IncludeRedirectedSkins = true;
        }

        private sealed class AutomationSettings
        {
            [JsonProperty("Enable crafted item skinning")]
            public bool EnableCraftSkinning = true;

            [JsonProperty("Enable automatic inventory skinning")]
            public bool EnableAutoSkinning = true;

            [JsonProperty("Enable spray can skin browser override")]
            public bool EnableSprayCanOverride = true;

            [JsonProperty("Spray can range")]
            public float SprayCanRange = 5f;

            [JsonProperty("Spray can input cooldown seconds")]
            public float SprayCanCooldownSeconds = 0.5f;
        }

        private sealed class SkinSetSettings
        {
            [JsonProperty("Number of player skin sets (3-10)")]
            public int SetCount = 3;
        }

        private sealed class UiSettings
        {
            [JsonProperty("Page UI Anchor Min")]
            public string PageAnchorMin = "0.5 0.0";
            [JsonProperty("Page UI Anchor Max")]
            public string PageAnchorMax = "0.5 0.0";
            [JsonProperty("Page UI Offset Min")]
            public string PageOffsetMin = "198 60";
            [JsonProperty("Page UI Offset Max")]
            public string PageOffsetMax = "400 97";

            [JsonProperty("Browser Header Anchor Min")]
            public string HeaderAnchorMin = "0.650 0.850";
            [JsonProperty("Browser Header Anchor Max")]
            public string HeaderAnchorMax = "0.948 0.920";
            [JsonProperty("Options Panel Anchor Min")]
            public string OptionsAnchorMin = "0.650 0.680";
            [JsonProperty("Options Panel Anchor Max")]
            public string OptionsAnchorMax = "0.948 0.847";
        }

        private sealed class CooldownSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Skin seconds")] public float Skin = 1f;
            [JsonProperty("Skin item seconds")] public float SkinItem = 2f;
            [JsonProperty("Inventory seconds")] public float Inventory = 15f;
            [JsonProperty("Container seconds")] public float Container = 15f;
            [JsonProperty("Team seconds")] public float Team = 30f;
            [JsonProperty("Base seconds")] public float Base = 45f;
            [JsonProperty("All seconds")] public float All = 60f;
        }

        private sealed class HistorySettings
        {
            [JsonProperty("Recent skins per item")] public int RecentPerItem = 6;
            [JsonProperty("Enable favourites")] public bool EnableFavourites = true;
            [JsonProperty("Remove player data after inactivity days (0 disables)")] public int RemoveInactiveDays = 30;
        }

        private sealed class RequestSettings
        {
            [JsonProperty("Enable player skin requests")] public bool Enabled = true;
            [JsonProperty("Maximum pending requests per player")] public int MaxPendingPerPlayer = 10;
            [JsonProperty("Enable direct Workshop imports")] public bool EnableWorkshopImports = true;
            [JsonProperty("Enable Workshop collection imports")] public bool EnableCollectionImports = true;
            [JsonProperty("Workshop request timeout seconds")] public int TimeoutSeconds = 15;
            [JsonProperty("Workshop import batch size (1-100)")] public int BatchSize = 50;
            [JsonProperty("Configured Workshop collection IDs")] public List<ulong> CollectionIds = new List<ulong>();
        }

        private sealed class PerformanceSettings
        {
            [JsonProperty("Maximum bulk entities/items processed per command")]
            public int MaxBulkOperations = 2000;

            [JsonProperty("Bulk operations per server tick")]
            public int BulkBatchSize = 32;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        private void MigrateConfigSectionNames()
        {
            JObject root = Config.ReadObject<JObject>();
            if (root == null || root["General Settings"] != null) return;

            bool changed = false;
            changed |= RenameConfigSection(root, "01 - General", "General Settings");
            changed |= RenameConfigSection(root, "02 - Skin Access", "Skin Access Settings");
            changed |= RenameConfigSection(root, "03 - Automation", "Automation Settings");
            changed |= RenameConfigSection(root, "04 - Skin Sets", "Skin Set Settings");
            changed |= RenameConfigSection(root, "05 - UI", "UI Settings");
            changed |= RenameConfigSection(root, "06 - Cooldowns", "Cooldown Settings");
            changed |= RenameConfigSection(root, "07 - Player History", "Player History Settings");
            changed |= RenameConfigSection(root, "08 - Workshop Requests", "Workshop Request Settings");
            changed |= RenameConfigSection(root, "09 - Performance", "Performance Settings");

            if (changed) Config.WriteObject(root, true);
        }

        private static bool RenameConfigSection(JObject root, string oldName, string newName)
        {
            JToken value = root[oldName];
            if (value == null || root[newName] != null) return false;
            root[newName] = value.DeepClone();
            root.Remove(oldName);
            return true;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                MigrateConfigSectionNames();
                _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig();
                if (_config.General == null) _config.General = new GeneralSettings();
                if (_config.SkinAccess == null) _config.SkinAccess = new SkinAccessSettings();
                if (_config.Automation == null) _config.Automation = new AutomationSettings();
                if (_config.SkinSets == null) _config.SkinSets = new SkinSetSettings();
                if (_config.UI == null) _config.UI = new UiSettings();
                if (_config.Cooldowns == null) _config.Cooldowns = new CooldownSettings();
                if (_config.History == null) _config.History = new HistorySettings();
                if (_config.Requests == null) _config.Requests = new RequestSettings();
                if (_config.Performance == null) _config.Performance = new PerformanceSettings();

                if (_config.General.BlacklistedSkins == null) _config.General.BlacklistedSkins = new List<ulong>();
                if (_config.General.BlacklistedItems == null) _config.General.BlacklistedItems = new List<int>();
                if (_config.Requests.CollectionIds == null) _config.Requests.CollectionIds = new List<ulong>();

                _config.SkinSets.SetCount = Mathf.Clamp(_config.SkinSets.SetCount, 3, 10);
                _config.Automation.SprayCanRange = Mathf.Clamp(_config.Automation.SprayCanRange, 1f, 8f);
                _config.Automation.SprayCanCooldownSeconds = Mathf.Clamp(_config.Automation.SprayCanCooldownSeconds, 0.1f, 5f);
                _config.Cooldowns.Skin = Mathf.Max(0f, _config.Cooldowns.Skin);
                _config.Cooldowns.SkinItem = Mathf.Max(0f, _config.Cooldowns.SkinItem);
                _config.Cooldowns.Inventory = Mathf.Max(0f, _config.Cooldowns.Inventory);
                _config.Cooldowns.Container = Mathf.Max(0f, _config.Cooldowns.Container);
                _config.Cooldowns.Team = Mathf.Max(0f, _config.Cooldowns.Team);
                _config.Cooldowns.Base = Mathf.Max(0f, _config.Cooldowns.Base);
                _config.Cooldowns.All = Mathf.Max(0f, _config.Cooldowns.All);
                _config.History.RecentPerItem = Mathf.Clamp(_config.History.RecentPerItem, 0, 24);
                _config.History.RemoveInactiveDays = Mathf.Max(0, _config.History.RemoveInactiveDays);
                _config.Requests.MaxPendingPerPlayer = Mathf.Clamp(_config.Requests.MaxPendingPerPlayer, 1, 100);
                _config.Requests.TimeoutSeconds = Mathf.Clamp(_config.Requests.TimeoutSeconds, 5, 120);
                _config.Requests.BatchSize = Mathf.Clamp(_config.Requests.BatchSize, 1, 100);
                _config.Performance.MaxBulkOperations = Mathf.Clamp(_config.Performance.MaxBulkOperations, 100, 10000);
                _config.Performance.BulkBatchSize = Mathf.Clamp(_config.Performance.BulkBatchSize, 4, 128);
                _config.Version = CurrentVersion;
            }
            catch { PrintWarning("Configuration was invalid; loading RogueRustSkins defaults."); _config = new PluginConfig(); }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private sealed class SkinBucket
        {
            public int ItemId;
            public string Shortname = string.Empty;
            public string DisplayName = string.Empty;
            public readonly List<SkinEntry> Skins = new List<SkinEntry>();
        }

        private sealed class SkinEntry
        {
            public ulong SkinId;
            public int ItemId;
            public int RedirectItemId;
            public string Name = string.Empty;
            public int InventoryDefinitionId;
            public bool Approved;
            public string Source = string.Empty;
            public long LastVerifiedUtc;
        }

        private sealed class PlayerSkinData
        {
            public int SelectedSet = 1;
            public bool AutoSkin;
            public bool CraftSkin = true;
            public bool BlockTeamSkins;
            public string[] SetNames;
            public Dictionary<int, List<ulong>> Recent = new Dictionary<int, List<ulong>>();
            public Dictionary<int, HashSet<ulong>> Favourites = new Dictionary<int, HashSet<ulong>>();
            public long LastOnlineUtc;
            public Dictionary<int, ulong>[] Sets =
            {
                new Dictionary<int, ulong>(), new Dictionary<int, ulong>(), new Dictionary<int, ulong>()
            };
        }

        private sealed class ImportedWorkshopSkin
        {
            public ulong SkinId;
            public int ItemId;
            public string Shortname = string.Empty;
            public string Name = string.Empty;
            public long ImportedUtc;
        }

        private sealed class SteamPublishedResponse { public SteamPublishedInner response; }
        private sealed class SteamPublishedInner { public List<SteamPublishedFile> publishedfiledetails; }
        private sealed class SteamPublishedFile
        {
            public int result;
            public string publishedfileid;
            public string title;
            public List<SteamTag> tags;
        }
        private sealed class SteamTag { public string tag; }

        private sealed class SteamCollectionResponse { public SteamCollectionInner response; }
        private sealed class SteamCollectionInner { public List<SteamCollectionDetail> collectiondetails; }
        private sealed class SteamCollectionDetail { public string publishedfileid; public List<SteamCollectionChild> children; }
        private sealed class SteamCollectionChild { public string publishedfileid; public int sortorder; public int filetype; }

        private sealed class SkinRequestData
        {
            public ulong SkinId;
            public ulong RequestedBy;
            public string RequestedByName = string.Empty;
            public long RequestedUtc;
            public string Status = "Pending";
        }

        private enum BrowserView
        {
            MainSkins,
            Favourites,
            Options,
            SetEditor,
            Requests
        }

        private sealed class BrowserState
        {
            public BasePlayer Player;
            public Item Target;
            public BaseCombatEntity TargetEntity;
            public ItemContainer Container;
            public int ItemId;
            public bool Closing;
            public float LastPageChange;
            public string SearchText = string.Empty;
            public bool FavouritesOnly;
            public BrowserView View = BrowserView.MainSkins;
            public int MainPage;
            public List<ulong> Catalogue = new List<ulong>();
        }

        private void Init()
        {
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustSkins/; Lang=None");
            cmd.AddChatCommand("skin", this, nameof(CommandSkin));
            cmd.AddChatCommand("s", this, nameof(CommandSkin));
            cmd.AddChatCommand("skinitem", this, nameof(CommandSkinItem));
            cmd.AddChatCommand("si", this, nameof(CommandSkinItem));
            cmd.AddChatCommand("skincraft", this, nameof(CommandSkinCraft));
            cmd.AddChatCommand("sc", this, nameof(CommandSkinCraft));
            cmd.AddChatCommand("skininv", this, nameof(CommandSkinInventory));
            cmd.AddChatCommand("sinv", this, nameof(CommandSkinInventory));
            cmd.AddChatCommand("skincon", this, nameof(CommandSkinContainer));
            cmd.AddChatCommand("scon", this, nameof(CommandSkinContainer));
            cmd.AddChatCommand("skinset", this, nameof(CommandSkinSet));
            cmd.AddChatCommand("ss", this, nameof(CommandSkinSet));
            cmd.AddChatCommand("skinauto", this, nameof(CommandSkinAuto));
            cmd.AddChatCommand("skinteam", this, nameof(CommandSkinTeam));
            cmd.AddChatCommand("skinbase", this, nameof(CommandSkinBase));
            cmd.AddChatCommand("skinall", this, nameof(CommandSkinAll));
            cmd.AddChatCommand("skinsearch", this, nameof(CommandSkinSearch));
            cmd.AddChatCommand("skinrecent", this, nameof(CommandSkinRecent));
            cmd.AddChatCommand("skinfav", this, nameof(CommandSkinFavourite));
            cmd.AddChatCommand("skinsetname", this, nameof(CommandSkinSetName));
            cmd.AddChatCommand("skinrequest", this, nameof(CommandSkinRequest));
            cmd.AddChatCommand("skinrequests", this, nameof(CommandSkinRequests));
            cmd.AddChatCommand("skinimport", this, nameof(CommandSkinImport));
            cmd.AddChatCommand("skinremove", this, nameof(CommandSkinRemove));
            cmd.AddConsoleCommand("rrs.search", this, nameof(CommandSearchInput));
            cmd.AddConsoleCommand("rrs.view", this, nameof(CommandView));
            cmd.AddConsoleCommand("rrs.set", this, nameof(CommandSetUi));
            cmd.AddConsoleCommand("rrs.option", this, nameof(CommandOptionUi));
            cmd.AddChatCommand("skincollection", this, nameof(CommandSkinCollection));
            cmd.AddChatCommand("skinsets", this, nameof(CommandSkinSets));
        }

        private async void OnServerInitialized()
        {
            try
            {
                _rogue = RogueServices.Instance;
                _rogue.Workloads.RepeatUnique(
                    CacheOwner,
                    "cache-prune",
                    TimeSpan.FromMinutes(10),
                    () =>
                    {
                        _rogue.PlayerSkins.Prune(TimeSpan.FromMinutes(30));
                        _rogue.SkinSessions.Prune(TimeSpan.FromMinutes(30));
                    },
                    TimeSpan.FromMinutes(10));
                LoadPlayerData();
                LoadRequestData();
                LoadImportedWorkshopData();
                CleanupInactivePlayerData();
                _limitedEntity = new BaseEntity { _limitedNetworking = true };

                _cacheReady = await InitializeAndLoadCacheAsync();
                if (_cacheReady)
                {
                    PublishCatalogueToRogueRust();
                    Puts($"RogueRustSkins cache ready: {_skinById.Count} skins across {_skinsByItem.Count} items.");
                }

                if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) > 0)
                {
                    RefreshLiveCatalogue("server-start");
                }
                else
                {
                    Puts("Steam inventory definitions are not ready yet. Cached skins remain available while RogueRustSkins waits for Steam.");
                    Steamworks.SteamInventory.OnDefinitionsUpdated -= OnSteamDefinitionsUpdated;
                    Steamworks.SteamInventory.OnDefinitionsUpdated += OnSteamDefinitionsUpdated;
                    timer.Once(15f, CheckSteamDefinitions);
                }
            }
            catch (Exception ex)
            {
                PrintError("RogueRustSkins initialization failed: " + ex);
            }
        }

        private void CheckSteamDefinitions()
        {
            if (_steamReady || _refreshing) return;
            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) > 0)
            {
                RefreshLiveCatalogue("steam-ready-poll");
                return;
            }
            timer.Once(15f, CheckSteamDefinitions);
        }

        private void OnSteamDefinitionsUpdated()
        {
            RefreshLiveCatalogue("steam-definitions-updated");
        }

        private void RefreshLiveCatalogue(string reason)
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                RebuildSkinRegistry();
                _steamReady = (Steamworks.SteamInventory.Definitions?.Length ?? 0) > 0;
                PublishCatalogueToRogueRust();
                _ = PersistCacheAsync(reason);
                Puts($"RogueRustSkins live catalogue: {_skinById.Count} skins across {_skinsByItem.Count} items. Steam ready: {_steamReady}.");
            }
            catch (Exception ex)
            {
                PrintError("RogueRustSkins live catalogue refresh failed: " + ex);
            }
            finally
            {
                _refreshing = false;
            }
        }

        private async Task<bool> InitializeAndLoadCacheAsync()
        {
            string directory = Path.Combine(Interface.Oxide.DataDirectory, "RogueRust", "RogueRustSkins");
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "SkinCache.db");

            _rogue.Database.Register(new RogueDatabaseConnectionOptions
            {
                Name = CacheConnection,
                Provider = RogueDatabaseProvider.SQLite,
                ConnectionString = databasePath,
                CommandTimeout = TimeSpan.FromSeconds(15),
                MaxRetries = 2
            }, true);

            await _rogue.Database.MigrateAsync(CacheConnection, CacheOwner, new[]
            {
                new RogueDatabaseMigration(1, "Create RogueRustSkins catalogue cache",
                    "CREATE TABLE IF NOT EXISTS skin_cache (skin_id TEXT NOT NULL, item_id INTEGER NOT NULL, redirect_item_id INTEGER NOT NULL DEFAULT 0, shortname TEXT NOT NULL, item_name TEXT NOT NULL, skin_name TEXT NOT NULL, approved INTEGER NOT NULL DEFAULT 0, inventory_definition_id INTEGER NOT NULL DEFAULT 0, source TEXT NOT NULL DEFAULT 'runtime', last_verified_utc INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (skin_id, item_id))",
                    "CREATE INDEX IF NOT EXISTS idx_rrskins_item ON skin_cache(item_id)",
                    "CREATE INDEX IF NOT EXISTS idx_rrskins_shortname ON skin_cache(shortname)",
                    "CREATE INDEX IF NOT EXISTS idx_rrskins_source ON skin_cache(source)",
                    "CREATE TABLE IF NOT EXISTS skin_cache_meta (meta_key TEXT PRIMARY KEY NOT NULL, meta_value TEXT NOT NULL)")
            });

            IReadOnlyList<RogueDataRow> rows = await _rogue.Database.QueryAsync(CacheConnection,
                "SELECT skin_id,item_id,redirect_item_id,shortname,item_name,skin_name,approved,inventory_definition_id,source,last_verified_utc FROM skin_cache ORDER BY item_id,skin_name");
            if (rows.Count == 0) return false;

            _skinsByItem.Clear();
            _skinById.Clear();
            foreach (RogueDataRow row in rows)
            {
                ulong skinId;
                if (!ulong.TryParse(row.Get<string>("skin_id", "0") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out skinId)) continue;
                int itemId = row.Get<int>("item_id", 0);
                if (itemId == 0) continue;

                SkinBucket bucket = EnsureBucket(itemId,
                    row.Get<string>("shortname", string.Empty) ?? string.Empty,
                    row.Get<string>("item_name", string.Empty) ?? string.Empty);
                AddSkinEntry(bucket, new SkinEntry
                {
                    SkinId = skinId,
                    ItemId = itemId,
                    RedirectItemId = row.Get<int>("redirect_item_id", 0),
                    Name = row.Get<string>("skin_name", skinId == 0 ? "Default" : skinId.ToString()) ?? skinId.ToString(),
                    Approved = row.Get<int>("approved", 0) != 0,
                    InventoryDefinitionId = row.Get<int>("inventory_definition_id", 0),
                    Source = row.Get<string>("source", "cache") ?? "cache",
                    LastVerifiedUtc = row.Get<long>("last_verified_utc", 0)
                });
            }

            SortRegistry();
            RefreshFingerprints();
            return _skinById.Count > 0;
        }

        private void RebuildSkinRegistry()
        {
            _skinsByItem.Clear();
            _skinById.Clear();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            foreach (ItemDefinition raw in ItemManager.GetItemDefinitions())
            {
                if (raw == null || raw.isRedirectOf != null) continue;
                List<ItemSkinDirectory.Skin> directorySkins = ItemSkinDirectory.ForItem(raw).Where(x => x.id != 0).ToList();
                if (directorySkins.Count == 0) continue;

                SkinBucket bucket = EnsureBucket(raw.itemid, raw.shortname, raw.displayName?.english ?? raw.shortname);
                AddSkinEntry(bucket, new SkinEntry { SkinId = 0, ItemId = raw.itemid, Name = "Default", Source = "default", LastVerifiedUtc = now });

                foreach (ItemSkinDirectory.Skin directorySkin in directorySkins)
                {
                    ItemSkin itemSkin = directorySkin.invItem as ItemSkin;
                    ulong skinId = itemSkin != null && itemSkin.workshopID != 0 ? itemSkin.workshopID : (ulong)directorySkin.id;
                    int redirect = itemSkin?.Redirect?.itemid ?? 0;
                    string name = directorySkin.invItem?.displayName?.english ?? directorySkin.name ?? skinId.ToString(CultureInfo.InvariantCulture);
                    AddSkinEntry(bucket, new SkinEntry
                    {
                        SkinId = skinId,
                        ItemId = raw.itemid,
                        RedirectItemId = redirect,
                        Name = name,
                        InventoryDefinitionId = directorySkin.id,
                        Source = "item-directory",
                        LastVerifiedUtc = now
                    });
                }
            }

            Steamworks.InventoryDef[] definitions = Steamworks.SteamInventory.Definitions;
            if (definitions != null && definitions.Length > 0)
            {
                Dictionary<int, ItemSkinDirectory.Skin> directoryById = new Dictionary<int, ItemSkinDirectory.Skin>();
                foreach (ItemSkinDirectory.Skin skin in ItemSkinDirectory.Instance.skins)
                    directoryById[skin.id] = skin;

                foreach (Steamworks.InventoryDef inventoryDef in definitions)
                {
                    string shortname = inventoryDef.GetProperty("itemshortname");
                    if (string.Equals(shortname, "lr300.item", StringComparison.OrdinalIgnoreCase)) shortname = "rifle.lr300";
                    if (string.IsNullOrWhiteSpace(shortname) || inventoryDef.Id < 100) continue;

                    ItemDefinition rawDef = ItemManager.FindItemDefinition(shortname);
                    if (rawDef == null) continue;
                    ItemDefinition canonical = rawDef.isRedirectOf ?? rawDef;

                    ulong skinId;
                    ItemSkinDirectory.Skin directorySkin;
                    bool hasDirectorySkin = directoryById.TryGetValue(inventoryDef.Id, out directorySkin);
                    if (hasDirectorySkin)
                        skinId = (ulong)inventoryDef.Id;
                    else if (!ulong.TryParse(inventoryDef.GetProperty("workshopid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out skinId) || skinId == 0)
                        continue;

                    ItemSkin itemSkin = hasDirectorySkin ? directorySkin.invItem as ItemSkin : null;
                    int redirect = itemSkin?.Redirect?.itemid ?? (rawDef.isRedirectOf != null ? rawDef.itemid : 0);
                    SkinBucket bucket = EnsureBucket(canonical.itemid, canonical.shortname, canonical.displayName?.english ?? canonical.shortname);
                    AddSkinEntry(bucket, new SkinEntry
                    {
                        SkinId = skinId,
                        ItemId = canonical.itemid,
                        RedirectItemId = redirect,
                        Name = string.IsNullOrWhiteSpace(inventoryDef.Name) ? skinId.ToString(CultureInfo.InvariantCulture) : inventoryDef.Name,
                        Approved = true,
                        InventoryDefinitionId = inventoryDef.Id,
                        Source = "steam-approved",
                        LastVerifiedUtc = now
                    });
                }
            }

            MergeImportedWorkshopSkins(now);
            SortRegistry();
        }

        private void MergeImportedWorkshopSkins(long now)
        {
            foreach (ImportedWorkshopSkin imported in _importedWorkshop.Values)
            {
                if (imported == null || imported.SkinId == 0 || imported.ItemId == 0 || IsBlacklisted(imported.ItemId, imported.SkinId)) continue;
                ItemDefinition def = ItemManager.FindItemDefinition(imported.ItemId);
                if (def == null) continue;
                ItemDefinition canonical = def.isRedirectOf ?? def;
                SkinBucket bucket = EnsureBucket(canonical.itemid, canonical.shortname, canonical.displayName?.english ?? canonical.shortname);
                AddSkinEntry(bucket, new SkinEntry
                {
                    SkinId = imported.SkinId,
                    ItemId = canonical.itemid,
                    Name = string.IsNullOrWhiteSpace(imported.Name) ? imported.SkinId.ToString(CultureInfo.InvariantCulture) : imported.Name,
                    Source = "workshop-import",
                    LastVerifiedUtc = now
                });
            }
        }

        private SkinBucket EnsureBucket(int itemId, string shortname, string displayName)
        {
            SkinBucket bucket;
            if (_skinsByItem.TryGetValue(itemId, out bucket)) return bucket;
            bucket = new SkinBucket { ItemId = itemId, Shortname = shortname ?? string.Empty, DisplayName = displayName ?? shortname ?? string.Empty };
            _skinsByItem[itemId] = bucket;
            return bucket;
        }

        private void AddSkinEntry(SkinBucket bucket, SkinEntry entry)
        {
            SkinEntry existing = bucket.Skins.FirstOrDefault(x => x.SkinId == entry.SkinId);
            if (existing != null)
            {
                if (entry.Approved || string.Equals(entry.Source, "steam-approved", StringComparison.Ordinal))
                {
                    existing.Name = entry.Name;
                    existing.Approved = entry.Approved;
                    existing.InventoryDefinitionId = entry.InventoryDefinitionId;
                    existing.RedirectItemId = entry.RedirectItemId;
                    existing.Source = entry.Source;
                    existing.LastVerifiedUtc = entry.LastVerifiedUtc;
                }
                _skinById[entry.SkinId] = existing;
                return;
            }
            bucket.Skins.Add(entry);
            _skinById[entry.SkinId] = entry;
        }

        private void SortRegistry()
        {
            foreach (SkinBucket bucket in _skinsByItem.Values)
                bucket.Skins.Sort((a, b) => a.SkinId == 0 ? -1 : b.SkinId == 0 ? 1 : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        }

        private void PublishCatalogueToRogueRust()
        {
            if (_rogue == null) return;

            int signature = ComputeCatalogueSignature();
            if (signature == _lastPublishedCatalogueSignature) return;

            List<RogueSkinRegistration> registrations = new List<RogueSkinRegistration>(_skinById.Count);
            foreach (SkinBucket bucket in _skinsByItem.Values)
            {
                foreach (SkinEntry skin in bucket.Skins)
                {
                    if (skin.SkinId == 0) continue;
                    registrations.Add(new RogueSkinRegistration
                    {
                        SkinId = skin.SkinId,
                        ItemId = bucket.ItemId,
                        RedirectItemId = skin.RedirectItemId,
                        ContentId = skin.InventoryDefinitionId,
                        Name = skin.Name,
                        Owner = CacheOwner,
                        Source = skin.Approved ? RogueSkinSource.Workshop : RogueSkinSource.Rust
                    });
                }
            }

            _rogue.Skins.RemoveOwner(CacheOwner);
            _rogue.Skins.RegisterRange(registrations);
            _rogue.PlayerSkins.InvalidateAll();
            _lastPublishedCatalogueSignature = signature;
        }

        private int ComputeCatalogueSignature()
        {
            unchecked
            {
                int hash = 17;
                foreach (SkinBucket bucket in _skinsByItem.Values.OrderBy(x => x.ItemId))
                {
                    hash = hash * 31 + bucket.ItemId;
                    foreach (SkinEntry skin in bucket.Skins.OrderBy(x => x.SkinId))
                    {
                        hash = hash * 31 + skin.SkinId.GetHashCode();
                        hash = hash * 31 + skin.RedirectItemId;
                        hash = hash * 31 + skin.InventoryDefinitionId;
                        hash = hash * 31 + (skin.Approved ? 1 : 0);
                        hash = hash * 31 + (skin.Name == null ? 0 : StringComparer.Ordinal.GetHashCode(skin.Name));
                    }
                }
                return hash;
            }
        }

        private async Task PersistCacheAsync(string reason)
        {
            if (_rogue == null || !_rogue.Database.IsRegistered(CacheConnection)) return;
            try
            {
                Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);
                List<RogueDatabaseStatement> statements = new List<RogueDatabaseStatement>();

                foreach (SkinBucket bucket in _skinsByItem.Values)
                {
                    foreach (SkinEntry skin in bucket.Skins)
                    {
                        string key = FingerprintKey(bucket.ItemId, skin.SkinId);
                        string fingerprint = Fingerprint(bucket, skin);
                        current[key] = fingerprint;
                        string previous;
                        if (_cacheFingerprints.TryGetValue(key, out previous) && string.Equals(previous, fingerprint, StringComparison.Ordinal)) continue;

                        statements.Add(new RogueDatabaseStatement(
                            "INSERT OR REPLACE INTO skin_cache (skin_id,item_id,redirect_item_id,shortname,item_name,skin_name,approved,inventory_definition_id,source,last_verified_utc) VALUES (@skin,@item,@redirect,@short,@itemname,@skinname,@approved,@inventory,@source,@verified)",
                            new[]
                            {
                                new RogueDatabaseParameter("skin", skin.SkinId.ToString(CultureInfo.InvariantCulture)),
                                new RogueDatabaseParameter("item", bucket.ItemId),
                                new RogueDatabaseParameter("redirect", skin.RedirectItemId),
                                new RogueDatabaseParameter("short", bucket.Shortname),
                                new RogueDatabaseParameter("itemname", bucket.DisplayName),
                                new RogueDatabaseParameter("skinname", skin.Name),
                                new RogueDatabaseParameter("approved", skin.Approved ? 1 : 0),
                                new RogueDatabaseParameter("inventory", skin.InventoryDefinitionId),
                                new RogueDatabaseParameter("source", skin.Source),
                                new RogueDatabaseParameter("verified", skin.LastVerifiedUtc)
                            }));
                    }
                }

                foreach (string stale in _cacheFingerprints.Keys.Where(x => !current.ContainsKey(x)).ToArray())
                {
                    int separator = stale.IndexOf(':');
                    if (separator <= 0) continue;
                    int itemId;
                    if (!int.TryParse(stale.Substring(0, separator), out itemId)) continue;
                    statements.Add(new RogueDatabaseStatement("DELETE FROM skin_cache WHERE item_id=@item AND skin_id=@skin",
                        new[] { new RogueDatabaseParameter("item", itemId), new RogueDatabaseParameter("skin", stale.Substring(separator + 1)) }));
                }

                statements.Add(new RogueDatabaseStatement("INSERT OR REPLACE INTO skin_cache_meta (meta_key,meta_value) VALUES ('last_refresh_reason',@value)", new[] { new RogueDatabaseParameter("value", reason ?? string.Empty) }));
                statements.Add(new RogueDatabaseStatement("INSERT OR REPLACE INTO skin_cache_meta (meta_key,meta_value) VALUES ('last_refresh_utc',@value)", new[] { new RogueDatabaseParameter("value", DateTime.UtcNow.ToString("O")) }));
                statements.Add(new RogueDatabaseStatement("INSERT OR REPLACE INTO skin_cache_meta (meta_key,meta_value) VALUES ('skin_count',@value)", new[] { new RogueDatabaseParameter("value", _skinById.Count.ToString(CultureInfo.InvariantCulture)) }));
                statements.Add(new RogueDatabaseStatement("INSERT OR REPLACE INTO skin_cache_meta (meta_key,meta_value) VALUES ('item_count',@value)", new[] { new RogueDatabaseParameter("value", _skinsByItem.Count.ToString(CultureInfo.InvariantCulture)) }));

                if (statements.Count > 0) await _rogue.Database.ExecuteBatchAsync(CacheConnection, statements, true);
                _cacheFingerprints.Clear();
                foreach (KeyValuePair<string, string> pair in current) _cacheFingerprints[pair.Key] = pair.Value;
                _cacheReady = true;
            }
            catch (Exception ex)
            {
                PrintWarning("Failed to persist RogueRustSkins cache: " + ex.Message);
            }
        }

        private static string FingerprintKey(int itemId, ulong skinId) => itemId.ToString(CultureInfo.InvariantCulture) + ":" + skinId.ToString(CultureInfo.InvariantCulture);
        private static string Fingerprint(SkinBucket bucket, SkinEntry skin) => string.Join("|", bucket.Shortname, bucket.DisplayName, skin.RedirectItemId, skin.Name, skin.Approved ? "1" : "0", skin.InventoryDefinitionId, skin.Source);
        private void RefreshFingerprints()
        {
            _cacheFingerprints.Clear();
            foreach (SkinBucket bucket in _skinsByItem.Values)
                foreach (SkinEntry skin in bucket.Skins)
                    _cacheFingerprints[FingerprintKey(bucket.ItemId, skin.SkinId)] = Fingerprint(bucket, skin);
        }

        private bool IsBlacklisted(int itemId, ulong skinId)
        {
            return (_config.General.BlacklistedItems?.Contains(itemId) ?? false) ||
                   (skinId != 0 && (_config.General.BlacklistedSkins?.Contains(skinId) ?? false));
        }

        private ItemDefinition GetEntityItemDefinition(BaseCombatEntity entity)
        {
            if (entity == null) return null;
            if (entity.pickup.itemTarget != null) return entity.pickup.itemTarget;
            if (entity.repair.itemTarget != null) return entity.repair.itemTarget;
            return null;
        }

        private void CommandSkinItem(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionItems)) return;
            if (_config.General.RequireBuildingAuthorization && !player.CanBuild() && !permission.UserHasPermission(player.UserIDString, PermissionBypassAuth))
            {
                SendReply(player, "You need building authorization to skin this deployable.");
                return;
            }
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, _config.Automation.SprayCanRange))
            {
                SendReply(player, "Look at a skinnable deployable.");
                return;
            }
            BaseCombatEntity entity = hit.GetEntity() as BaseCombatEntity;
            ItemDefinition def = GetEntityItemDefinition(entity);
            if (entity == null || def == null)
            {
                SendReply(player, "No skinnable deployable found.");
                return;
            }
            OpenDeployableBrowser(player, entity, def);
        }

        private void OpenDeployableBrowser(BasePlayer player, BaseCombatEntity entity, ItemDefinition def)
        {
            if (_rogue == null || player == null || entity == null || def == null) return;
            if (_browsers.ContainsKey(player.userID)) { CloseBrowser(player.userID, true); return; }
            ItemDefinition canonical = def.isRedirectOf ?? def;
            SkinBucket bucket;
            if (!_skinsByItem.TryGetValue(canonical.itemid, out bucket))
            {
                SendReply(player, "No cached skins are available for this deployable.");
                return;
            }
            List<ulong> skinIds = bucket.Skins
                .Where(x => !IsBlacklisted(canonical.itemid, x.SkinId) && CanPlayerUseSkin(player, x))
                .Select(x => x.SkinId).Distinct().ToList();
            if (skinIds.Count == 0) { SendReply(player, "No skins are available for this deployable."); return; }

            List<ulong> ordered = BuildMainCatalogue(player, canonical.itemid, skinIds);
            _rogue.SkinSessions.Open(player.userID, canonical.itemid, ordered, PageSize);
            BrowserState state = new BrowserState { Player = player, TargetEntity = entity, ItemId = canonical.itemid, Catalogue = skinIds };
            if (!CreateContainer(state)) { _rogue.SkinSessions.Close(player.userID); return; }
            _browsers[player.userID] = state;
            PopulatePage(state);
            player.Invoke(() => { if (_browsers.ContainsKey(player.userID)) StartLooting(state); }, 0.2f);
        }

        private void CommandSkin(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse)) { SendReply(player, "You do not have permission to use RogueRustSkins."); return; }
            if (_rogue == null) { SendReply(player, "RogueRust services are not available."); return; }
            if (_browsers.ContainsKey(player.userID)) { CloseBrowser(player.userID, true); return; }

            Item target = player.GetActiveItem();
            if (target?.info == null) { SendReply(player, "Hold the item you want to skin, then use /skin."); return; }
            ItemDefinition canonical = target.info.isRedirectOf ?? target.info;
            SkinBucket bucket;
            if (!_skinsByItem.TryGetValue(canonical.itemid, out bucket) || bucket.Skins.Count <= 1)
            {
                SendReply(player, $"No cached skins are available for {canonical.displayName?.english ?? canonical.shortname}. Cache: {(_cacheReady ? "ready" : "building")}, Steam: {(_steamReady ? "ready" : "waiting")}.");
                return;
            }

            List<ulong> skinIds = bucket.Skins
                .Where(x => !IsBlacklisted(canonical.itemid, x.SkinId))
                .Where(x => _config.SkinAccess.IncludeRedirectedSkins || x.RedirectItemId == 0)
                .Where(x => CanPlayerUseSkin(player, x))
                .Select(x => x.SkinId).Distinct().ToList();
            List<ulong> ordered = BuildMainCatalogue(player, canonical.itemid, skinIds);
            RogueSkinSession session = _rogue.SkinSessions.Open(player.userID, canonical.itemid, ordered, PageSize);
            BrowserState state = new BrowserState { Player = player, Target = target, ItemId = canonical.itemid, Catalogue = skinIds };
            if (!CreateContainer(state)) { _rogue.SkinSessions.Close(player.userID); SendReply(player, "Unable to create the skin browser container."); return; }
            _browsers[player.userID] = state;
            PopulatePage(state);
            player.Invoke(() => { if (_browsers.ContainsKey(player.userID)) StartLooting(state); }, 0.3f);
        }

        private bool CreateContainer(BrowserState state)
        {
            ItemContainer container = new ItemContainer { entityOwner = _limitedEntity, allowedContents = ItemContainer.ContentsType.Generic };
            container.maxStackSize = 0;
            container.ServerInitialize(null, PageSize);
            container.GiveUID();
            if (container.uid == default(ItemContainerId)) { container.Kill(); return false; }
            state.Container = container;
            return true;
        }

        private void PopulatePage(BrowserState state)
        {
            if (state.Container == null) return;
            ClearPreviewItems(state.Container);
            RogueSkinSession session;
            if (!_rogue.SkinSessions.TryGet(state.Player.userID, out session)) return;
            IReadOnlyList<ulong> page = session.GetPage();
            for (int i = 0; i < page.Count; i++)
            {
                ulong skinId = page[i];
                SkinEntry skinEntry;
                _skinById.TryGetValue(skinId, out skinEntry);
                int previewItemId = skinEntry != null && skinEntry.RedirectItemId != 0 ? skinEntry.RedirectItemId : state.ItemId;
                ulong previewSkinId = skinEntry != null && skinEntry.RedirectItemId != 0 ? 0UL : skinId;
                Item preview = ItemManager.CreateByItemID(previewItemId, 1, previewSkinId);
                if (preview == null) continue;
                preview.name = GetSkinName(skinId);
                preview.text = "RogueRustSkinsPreview:" + skinId.ToString(CultureInfo.InvariantCulture);
                if (!preview.MoveToContainer(state.Container, i, false)) preview.Remove();
            }
            state.Container.MarkDirty();
            DrawPager(state.Player, session);
            DrawBrowserHeader(state);
        }

        private void DrawPager(BasePlayer player, RogueSkinSession session)
        {
            CuiHelper.DestroyUi(player, UiRoot);
            if (session.PageCount <= 1) return;

            CuiElementContainer ui = new CuiElementContainer();
            string root = ui.Add(new CuiPanel
            {
                Image = { Color = "0.22 0.22 0.22 0.94" },
                RectTransform =
                {
                    AnchorMin = _config.UI.PageAnchorMin,
                    AnchorMax = _config.UI.PageAnchorMax,
                    OffsetMin = _config.UI.PageOffsetMin,
                    OffsetMax = _config.UI.PageOffsetMax
                },
                CursorEnabled = false
            }, "Inventory", UiRoot);

            ui.Add(new CuiLabel
            {
                Text = { Text = $"{session.Page + 1} of {session.PageCount}", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.86 0.86 0.86 1" },
                RectTransform = { AnchorMin = "0.30 0", AnchorMax = "0.70 1" }
            }, root);
            ui.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "rrs.page prev" },
                Text = { Text = "←", FontSize = 25, Align = TextAnchor.MiddleCenter, Color = "0.82 0.82 0.82 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.30 1" }
            }, root);
            ui.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "rrs.page next" },
                Text = { Text = "→", FontSize = 25, Align = TextAnchor.MiddleCenter, Color = "0.82 0.82 0.82 1" },
                RectTransform = { AnchorMin = "0.70 0", AnchorMax = "1 1" }
            }, root);
            CuiHelper.AddUi(player, ui);
        }

        [ConsoleCommand("rrs.page")]
        private void CommandPage(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            BrowserState state;
            RogueSkinSession session;
            if (player == null || !_browsers.TryGetValue(player.userID, out state) || !_rogue.SkinSessions.TryGet(player.userID, out session) || arg.Args == null || arg.Args.Length == 0) return;
            // Rust can submit the same CUI command twice during a rapid inventory redraw.
            // Debounce the navigation so one click always means exactly one page.
            if (Time.realtimeSinceStartup - state.LastPageChange < 0.20f) return;
            state.LastPageChange = Time.realtimeSinceStartup;

            int page = session.Page;
            string pageArg = arg.Args[0].ToString();
            if (string.Equals(pageArg, "next", StringComparison.OrdinalIgnoreCase)) page++;
            else if (string.Equals(pageArg, "prev", StringComparison.OrdinalIgnoreCase)) page--;
            else if (!int.TryParse(pageArg, out page)) return;

            if (page < 0) page = session.PageCount - 1;
            else if (page >= session.PageCount) page = 0;

            if (!_rogue.SkinSessions.TrySetPage(player.userID, page)) return;
            PopulatePage(state);
        }

        private List<ulong> BuildMainCatalogue(BasePlayer player, int itemId, IEnumerable<ulong> source)
        {
            List<ulong> all = source == null ? new List<ulong>() : source.Distinct().ToList();
            PlayerSkinData data = GetPlayerData(player);
            List<ulong> recent;
            if (data.Recent != null && data.Recent.TryGetValue(itemId, out recent) && recent != null)
            {
                int take = Mathf.Clamp(_config.History.RecentPerItem, 0, 6);
                List<ulong> first = recent.AsEnumerable().Reverse().Where(all.Contains).Distinct().Take(take).ToList();
                if (first.Count > 0)
                {
                    HashSet<ulong> used = new HashSet<ulong>(first);
                    first.AddRange(all.Where(x => !used.Contains(x)));
                    return first;
                }
            }
            return all;
        }

        private void SetBrowserCatalogue(BrowserState state, IEnumerable<ulong> ids, bool restoreMainPage = false)
        {
            if (state == null || state.Player == null) return;
            List<ulong> filtered = ids == null ? new List<ulong>() : ids.Distinct().ToList();
            _rogue.SkinSessions.Close(state.Player.userID);
            RogueSkinSession session = _rogue.SkinSessions.Open(state.Player.userID, state.ItemId, filtered, PageSize);
            if (restoreMainPage && state.MainPage > 0)
                _rogue.SkinSessions.TrySetPage(state.Player.userID, Math.Min(state.MainPage, Math.Max(0, session.PageCount - 1)));
            PopulatePage(state);
        }

        private void ShowMainSkins(BrowserState state, bool restorePage)
        {
            state.View = BrowserView.MainSkins;
            state.FavouritesOnly = false;
            IEnumerable<ulong> ids = BuildMainCatalogue(state.Player, state.ItemId, state.Catalogue);
            if (!string.IsNullOrWhiteSpace(state.SearchText))
            {
                string q = state.SearchText;
                ids = ids.Where(id => id.ToString(CultureInfo.InvariantCulture).Contains(q) ||
                    GetSkinName(id).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            SetBrowserCatalogue(state, ids, restorePage);
        }

        private void ShowFavourites(BrowserState state)
        {
            RogueSkinSession current;
            if (_rogue.SkinSessions.TryGet(state.Player.userID, out current)) state.MainPage = current.Page;
            state.View = BrowserView.Favourites;
            state.FavouritesOnly = true;
            HashSet<ulong> fav;
            PlayerSkinData data = GetPlayerData(state.Player);
            IEnumerable<ulong> ids = data.Favourites != null && data.Favourites.TryGetValue(state.ItemId, out fav)
                ? state.Catalogue.Where(fav.Contains)
                : Enumerable.Empty<ulong>();
            SetBrowserCatalogue(state, ids);
        }

        private void DrawBrowserHeader(BrowserState state)
        {
            BasePlayer player = state?.Player;
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiHeader);
            CuiHelper.DestroyUi(player, UiOptions);

            CuiElementContainer ui = new CuiElementContainer();
            // Positioned over Rust's right-side loot panel, matching Skinner's visual flow.
            string root = ui.Add(new CuiPanel
            {
                Image = { Color = "0.20 0.20 0.20 0.96" },
                RectTransform = { AnchorMin = _config.UI.HeaderAnchorMin, AnchorMax = _config.UI.HeaderAnchorMax },
                CursorEnabled = true
            }, "Overlay", UiHeader);

            string active = "0.20 0.52 0.25 1";
            string normal = "0.34 0.34 0.34 1";
            ui.Add(new CuiButton
            {
                Button = { Color = state.View == BrowserView.SetEditor ? active : normal, Command = "rrs.view sets" },
                Text = { Text = "🔧  Set Skins", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "0.92 0.92 0.92 1" },
                RectTransform = { AnchorMin = "0 0.52", AnchorMax = "0.30 1" }
            }, root);
            ui.Add(new CuiButton
            {
                Button = { Color = state.View == BrowserView.Options ? active : normal, Command = "rrs.view options" },
                Text = { Text = "⚙  Options", FontSize = 15, Align = TextAnchor.MiddleCenter, Color = "0.88 0.88 0.88 1" },
                RectTransform = { AnchorMin = "0.305 0.52", AnchorMax = "0.54 1" }
            }, root);

            string searchRoot = ui.Add(new CuiPanel
            {
                Image = { Color = "0.38 0.38 0.38 1" },
                RectTransform = { AnchorMin = "0.545 0.52", AnchorMax = "0.94 1" }
            }, root);
            ui.Add(new CuiElement
            {
                Parent = searchRoot,
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Text = string.IsNullOrWhiteSpace(state.SearchText) ? "" : state.SearchText,
                        FontSize = 14,
                        Align = TextAnchor.MiddleLeft,
                        Color = "0.88 0.88 0.88 1",
                        Command = "rrs.search",
                        CharsLimit = 32,
                        NeedsKeyboard = true
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.05 0", AnchorMax = "0.90 1" }
                }
            });
            ui.Add(new CuiButton
            {
                Button = { Color = "0 0 0 0", Command = "rrs.search" },
                Text = { Text = "×", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "0.62 0.28 0.28 1" },
                RectTransform = { AnchorMin = "0.94 0.52", AnchorMax = "1 1" }
            }, root);

            PlayerSkinData data = GetPlayerData(player);
            int count = data.SetNames.Length;
            float start = state.View == BrowserView.MainSkins ? 0.0f : 0.0f;
            // Back replaces Loot while inside a sub-view.
            ui.Add(new CuiButton
            {
                Button = { Color = state.View == BrowserView.MainSkins ? normal : "0.28 0.28 0.28 1", Command = "rrs.view back" },
                Text = { Text = state.View == BrowserView.MainSkins ? "Loot" : "← Back", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.14 0.48" }
            }, root);

            int visible = Math.Min(count, 10);
            float width = 0.86f / Math.Max(1, visible);
            for (int i = 0; i < visible; i++)
            {
                float min = 0.14f + width * i;
                float max = min + width - 0.004f;
                bool selected = data.SelectedSet == i + 1;
                ui.Add(new CuiButton
                {
                    Button = { Color = selected ? "0.35 0.67 0.32 1" : normal, Command = "rrs.set " + (i + 1) },
                    Text = { Text = data.SetNames[i], FontSize = visible > 6 ? 11 : 14, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = min.ToString("0.###", CultureInfo.InvariantCulture) + " 0", AnchorMax = max.ToString("0.###", CultureInfo.InvariantCulture) + " 0.48" }
                }, root);
            }
            CuiHelper.AddUi(player, ui);

            if (state.View == BrowserView.Options) DrawOptionsPanel(state);
        }

        private void DrawOptionsPanel(BrowserState state)
        {
            BasePlayer player = state.Player;
            PlayerSkinData data = GetPlayerData(player);
            CuiElementContainer ui = new CuiElementContainer();
            string root = ui.Add(new CuiPanel
            {
                Image = { Color = "0.18 0.18 0.18 0.97" },
                RectTransform = { AnchorMin = _config.UI.OptionsAnchorMin, AnchorMax = _config.UI.OptionsAnchorMax },
                CursorEnabled = true
            }, "Overlay", UiOptions);

            AddOptionButton(ui, root, "Auto Skins", data.AutoSkin, "rrs.option auto", "0.02 0.68", "0.49 0.96");
            AddOptionButton(ui, root, "Craft Skins", data.CraftSkin, "rrs.option craft", "0.51 0.68", "0.98 0.96");
            AddOptionButton(ui, root, "Team Skins", !data.BlockTeamSkins, "rrs.option team", "0.02 0.36", "0.49 0.64");
            if (_config.History.EnableFavourites)
                AddOptionButton(ui, root, "Favourites", state.View == BrowserView.Favourites, "rrs.view fav", "0.51 0.36", "0.98 0.64");
            if (permission.UserHasPermission(player.UserIDString, PermissionRequest))
                AddOptionButton(ui, root, "Skin Requests", false, "rrs.view requests", "0.02 0.04", "0.49 0.32");
            AddOptionButton(ui, root, "Main Skins", false, "rrs.view back", "0.51 0.04", "0.98 0.32");
            CuiHelper.AddUi(player, ui);
        }

        private static void AddOptionButton(CuiElementContainer ui, string parent, string text, bool enabled, string command, string amin, string amax)
        {
            ui.Add(new CuiButton
            {
                Button = { Color = enabled ? "0.28 0.58 0.31 1" : "0.34 0.34 0.34 1", Command = command },
                Text = { Text = text + (enabled ? "  ON" : ""), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                RectTransform = { AnchorMin = amin, AnchorMax = amax }
            }, parent);
        }

        private void CommandSearchInput(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state)) return;
            state.SearchText = arg.Args == null || arg.Args.Length == 0 ? string.Empty : string.Join(" ", arg.Args).Trim();
            state.View = BrowserView.MainSkins;
            state.MainPage = 0;
            ShowMainSkins(state, false);
        }

        private void CommandView(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state) || arg.Args == null || arg.Args.Length == 0) return;
            string view = arg.Args[0].ToString().ToLowerInvariant();
            if (view == "back" || view == "main")
            {
                CuiHelper.DestroyUi(player, UiOptions);
                state.SearchText = string.Empty;
                ShowMainSkins(state, true);
            }
            else if (view == "fav")
            {
                CuiHelper.DestroyUi(player, UiOptions);
                ShowFavourites(state);
            }
            else if (view == "options")
            {
                RogueSkinSession session;
                if (_rogue.SkinSessions.TryGet(player.userID, out session)) state.MainPage = session.Page;
                state.View = BrowserView.Options;
                DrawBrowserHeader(state);
            }
            else if (view == "sets")
            {
                state.View = BrowserView.SetEditor;
                DrawBrowserHeader(state);
                SendReply(player, "Select a Set tab, then skin items normally. Each selected skin is saved to that set.");
            }
            else if (view == "requests")
            {
                state.View = BrowserView.Requests;
                DrawBrowserHeader(state);
                ShowRequestBrowser(state);
            }
        }

        private void ShowRequestBrowser(BrowserState state)
        {
            BasePlayer player = state?.Player;
            if (player == null) return;
            if (!permission.UserHasPermission(player.UserIDString, PermissionAdmin))
            {
                SendReply(player, "Use /skinrequest <workshopID> to submit a skin request.");
                return;
            }

            List<SkinRequestData> pending = _skinRequests.Values
                .Where(x => x != null && string.Equals(x.Status, "Pending", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.RequestedUtc)
                .Take(PageSize)
                .ToList();

            if (pending.Count == 0)
            {
                SendReply(player, "No pending skin requests.");
                return;
            }

            ClearPreviewItems(state.Container);
            int slot = 0;
            foreach (SkinRequestData request in pending)
            {
                ImportedWorkshopSkin imported;
                SkinEntry entry;
                int itemId = 0;
                ulong previewSkin = request.SkinId;
                if (_skinById.TryGetValue(request.SkinId, out entry))
                {
                    itemId = entry.RedirectItemId != 0 ? entry.RedirectItemId : entry.ItemId;
                    if (entry.RedirectItemId != 0) previewSkin = 0;
                }
                else if (_importedWorkshop.TryGetValue(request.SkinId, out imported))
                    itemId = imported.ItemId;

                if (itemId == 0) continue;
                Item preview = ItemManager.CreateByItemID(itemId, 1, previewSkin);
                if (preview == null) continue;
                preview.name = "REQUEST " + request.SkinId.ToString(CultureInfo.InvariantCulture);
                preview.text = "RogueRustSkinRequest:" + request.SkinId.ToString(CultureInfo.InvariantCulture);
                if (!preview.MoveToContainer(state.Container, slot++, false)) preview.Remove();
                if (slot >= PageSize) break;
            }
            state.Container.MarkDirty();
            SendReply(player, "Request review: drag a request item to TRY it. Use /skinrequests approve <id> or deny <id> to decide it.");
        }

        private void CommandSetUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state) || arg.Args == null || arg.Args.Length == 0) return;
            int set;
            PlayerSkinData data = GetPlayerData(player);
            if (!int.TryParse(arg.Args[0], out set) || set < 1 || set > data.Sets.Length) return;
            data.SelectedSet = set;
            MarkPlayerDataDirty();
            state.View = BrowserView.SetEditor;
            DrawBrowserHeader(state);
        }

        private void CommandOptionUi(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Connection?.player as BasePlayer;
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state) || arg.Args == null || arg.Args.Length == 0) return;
            PlayerSkinData data = GetPlayerData(player);
            string option = arg.Args[0].ToString().ToLowerInvariant();
            if (option == "auto" && permission.UserHasPermission(player.UserIDString, PermissionAuto)) data.AutoSkin = !data.AutoSkin;
            else if (option == "craft" && permission.UserHasPermission(player.UserIDString, PermissionCraft)) data.CraftSkin = !data.CraftSkin;
            else if (option == "team" && permission.UserHasPermission(player.UserIDString, PermissionTeam)) data.BlockTeamSkins = !data.BlockTeamSkins;
            MarkPlayerDataDirty();
            state.View = BrowserView.Options;
            DrawBrowserHeader(state);
        }

        private void StartLooting(BrowserState state)
        {
            BasePlayer player = state.Player;
            if (player == null || state.Container == null || !player.IsConnected || !player.IsAlive()) return;
            player.inventory.loot.Clear();
            player.inventory.loot.AddContainer(state.Container);
            player.inventory.loot.entitySource = RelationshipManager.ServerInstance;
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.MarkDirty();
            player.SendNetworkUpdateImmediate();
            OpenLootPanel(player);
        }

        private static readonly uint OpenLootRpc = StringPool.Get("RPC_OpenLootPanel");
        private static readonly byte[] LootPanelBytes = Encoding.UTF8.GetBytes(LootPanel);
        private static void OpenLootPanel(BasePlayer player)
        {
            RpcTarget target = default(RpcTarget);
            target.Function = "RPC_OpenLootPanel";
            target.Connections = new SendInfo(player.net.connection);
            NetWrite write = Net.sv.StartWrite();
            write.PacketID(Network.Message.Type.RPCMessage);
            write.EntityID(player.net.ID);
            write.UInt32(OpenLootRpc);
            write.BytesWithSize(LootPanelBytes);
            write.Send(target.Connections);
            if (target.UsingPooledConnections) Pool.FreeUnmanaged<Connection>(ref target.Connections.connections);
        }

        private object CanMoveItem(Item item, PlayerInventory inventory, ItemContainerId targetContainerId, int targetSlot, int amount)
        {
            if (item == null || item.parent == null || string.IsNullOrEmpty(item.text)) return null;
            BasePlayer player = inventory?.baseEntity;
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state) || item.parent != state.Container) return null;

            if (item.text.StartsWith("RogueRustSkinRequest:", StringComparison.Ordinal))
            {
                ulong requestId;
                if (!ulong.TryParse(item.text.Substring("RogueRustSkinRequest:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out requestId)) return false;
                NextTick(() =>
                {
                    if (state.Closing) return;
                    SkinEntry entry;
                    int previewItemId = 0;
                    ulong previewSkin = requestId;
                    if (_skinById.TryGetValue(requestId, out entry) && entry != null)
                    {
                        previewItemId = entry.RedirectItemId != 0 ? entry.RedirectItemId : entry.ItemId;
                        if (entry.RedirectItemId != 0) previewSkin = 0UL;
                    }
                    else
                    {
                        ImportedWorkshopSkin imported;
                        if (_importedWorkshop.TryGetValue(requestId, out imported) && imported != null)
                            previewItemId = imported.ItemId;
                    }

                    if (previewItemId == 0) return;
                    Item tryItem = ItemManager.CreateByItemID(previewItemId, 1, previewSkin);
                    if (tryItem != null) state.Player.GiveItem(tryItem);
                });
                return false;
            }

            if (!item.text.StartsWith("RogueRustSkinsPreview:", StringComparison.Ordinal)) return null;
            ulong skinId;
            if (!ulong.TryParse(item.text.Substring("RogueRustSkinsPreview:".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out skinId)) skinId = item.skin;
            NextTick(() => { if (!state.Closing) ApplySkin(state, skinId); });
            return false;
        }

        private bool TryApplyItemSkin(BasePlayer targetPlayer, Item item, int canonicalItemId, ulong skinId, BasePlayer skinOwner, out Item resultingItem)
        {
            resultingItem = item;
            if (item?.info == null || item.parent == null) return false;

            SkinEntry entry = null;
            if (skinId != 0)
            {
                RogueSkinEntry sharedEntry;
                if (_rogue == null || !_rogue.Skins.TryGetSkin(canonicalItemId, skinId, out sharedEntry))
                    return false;
                if (!_skinById.TryGetValue(skinId, out entry) || entry == null || entry.ItemId != canonicalItemId)
                    return false;
                if (IsBlacklisted(canonicalItemId, skinId)) return false;
                if (!CanPlayerUseSkin(skinOwner ?? targetPlayer, entry)) return false;
            }

            // Redirect -> default/normal must recreate the canonical item. Redirect ->
            // redirect also uses replacement, while canonical -> canonical stays in-place.
            int desiredItemId = entry != null && entry.RedirectItemId != 0 ? entry.RedirectItemId : canonicalItemId;
            bool requiresReplacement = item.info.itemid != desiredItemId;

            if (requiresReplacement)
            {
                Item replacement = ReplaceItemPreservingState(targetPlayer, item, desiredItemId,
                    entry != null && entry.RedirectItemId != 0 ? 0UL : skinId,
                    skinId);
                if (replacement == null) return false;
                resultingItem = replacement;
                ForceItemSkinNetworkUpdate(targetPlayer, replacement);
                return true;
            }

            if (item.skin == skinId) return false;
            if (skinId == 0)
            {
                item.skin = 0UL;
                if (_config.General.ApplySkinNames) item.name = null;
                ForceItemSkinNetworkUpdate(targetPlayer, item);
                return true;
            }

            RogueSkinApplyResult result = _rogue.Skinning.Apply(item, skinId);
            if (!result.Success) return false;
            if (_config.General.ApplySkinNames) item.name = GetSkinName(skinId);
            ForceItemSkinNetworkUpdate(targetPlayer, item);
            return true;
        }

        private void ApplySkin(BrowserState state, ulong skinId)
        {
            if (state.TargetEntity != null)
            {
                ApplySkinToDeployable(state, skinId);
                return;
            }

            Item target = state.Target;
            if (target?.info == null || target.parent == null)
            {
                CloseBrowser(state.Player.userID, true);
                return;
            }

            Item resultingItem;
            if (!TryApplyItemSkin(state.Player, target, state.ItemId, skinId, state.Player, out resultingItem))
            {
                if (target.skin == skinId && target.info.itemid == state.ItemId)
                    return;
                SendReply(state.Player, "Skin could not be applied safely.");
                return;
            }

            state.Target = resultingItem;
            RememberSelectedSkin(state.Player, state.ItemId, skinId);
            SendReply(state.Player, "Applied " + GetSkinName(skinId) + ".");
        }

        private void ForceItemSkinNetworkUpdate(BasePlayer player, Item item)
        {
            if (item == null) return;

            item.MarkDirty();
            BaseEntity held = item.GetHeldEntity();
            if (held != null)
            {
                held.skinID = item.skin;
                held.SendNetworkUpdateImmediate();

                if (player != null && player.svActiveItemID == item.uid)
                {
                    NextTick(() =>
                    {
                        if (player == null || !player.IsConnected || item == null) return;
                        HeldEntity activeHeld = player.GetActiveItem()?.GetHeldEntity() as HeldEntity;
                        if (activeHeld != null) activeHeld.SetHeld(true);
                        player.SendNetworkUpdateImmediate();
                    });
                }
            }

            item.parent?.MarkDirty();
            player?.SendNetworkUpdateImmediate();
        }

        private bool CanPlayerUseSkin(BasePlayer player, SkinEntry skin)
        {
            if (skin == null || skin.SkinId == 0) return true;
            if (!skin.Approved) return true;
            if (_config.SkinAccess.AllowUnownedPaidDlcSkins) return true;
            return _config.SkinAccess.AllowOwnedSkins && PlayerOwnsSkin(player, skin);
        }

        private static bool PlayerOwnsSkin(BasePlayer player, SkinEntry skin)
        {
            if (skin == null || skin.SkinId == 0) return true;
            if (player?.blueprints?.steamInventory == null) return false;
            try
            {
                foreach (IPlayerItem owned in player.blueprints.steamInventory.Items)
                {
                    if (owned.Id == skin.SkinId || (ulong)owned.DefinitionId == skin.SkinId) return true;
                    if (skin.InventoryDefinitionId > 0 && owned.DefinitionId == skin.InventoryDefinitionId) return true;
                }
            }
            catch { }
            return false;
        }

        private Item ReplaceItemPreservingState(BasePlayer player, Item original, int targetItemId, ulong targetSkin, ulong displaySkinId)
        {
            ItemContainer parent = original.parent;
            if (parent == null || targetItemId == 0) return null;

            int position = original.position;
            bool wasActive = player != null && player.svActiveItemID == original.uid;
            Item replacement = ItemManager.CreateByItemID(targetItemId, original.amount, targetSkin);
            if (replacement?.info == null)
            {
                replacement?.Remove();
                return null;
            }

            replacement.maxCondition = original.maxCondition;
            replacement.condition = original.condition;
            replacement.name = _config.General.ApplySkinNames
                ? (displaySkinId == 0 ? null : GetSkinName(displaySkinId))
                : original.name;
            replacement.ownershipShares = original.ownershipShares;

            BaseProjectile oldProjectile = original.GetHeldEntity() as BaseProjectile;
            BaseProjectile newProjectile = replacement.GetHeldEntity() as BaseProjectile;
            if (oldProjectile != null && newProjectile != null)
            {
                newProjectile.canUnloadAmmo = oldProjectile.canUnloadAmmo;
                newProjectile.primaryMagazine.contents = oldProjectile.primaryMagazine.contents;
                newProjectile.primaryMagazine.ammoType = oldProjectile.primaryMagazine.ammoType;
            }

            if (original.contents != null && original.contents.itemList.Count > 0)
            {
                if (replacement.contents == null)
                {
                    replacement.contents = Pool.Get<ItemContainer>();
                    replacement.contents.ServerInitialize(replacement, original.contents.capacity);
                }

                foreach (Item child in original.contents.itemList.ToArray())
                    if (!child.MoveToContainer(replacement.contents))
                    {
                        replacement.Remove();
                        return null;
                    }
            }

            // Do not destroy the source until the replacement has successfully entered
            // the original container. Temporarily remove the source to free its slot.
            original.RemoveFromContainer();
            if (!replacement.MoveToContainer(parent, position, false))
            {
                original.MoveToContainer(parent, position, false);
                replacement.Remove();
                return null;
            }

            original.Remove(0f);
            replacement.MarkDirty();
            parent.MarkDirty();

            if (wasActive && player != null)
            {
                player.UpdateActiveItem(replacement.uid);
                NextTick(() =>
                {
                    if (player == null || !player.IsConnected) return;
                    HeldEntity held = player.GetActiveItem()?.GetHeldEntity() as HeldEntity;
                    if (held != null) held.SetHeld(true);
                    player.SendNetworkUpdateImmediate();
                });
            }

            return replacement;
        }

        private string GetSkinName(ulong skinId)
        {
            if (skinId == 0) return "Default";

            SkinEntry entry;
            if (_skinById.TryGetValue(skinId, out entry) && entry != null)
            {
                RogueSkinEntry shared;
                if (_rogue != null && _rogue.Skins.TryGetSkin(entry.ItemId, skinId, out shared) &&
                    shared != null && !string.IsNullOrWhiteSpace(shared.Name))
                    return shared.Name;
                if (!string.IsNullOrWhiteSpace(entry.Name)) return entry.Name;
            }

            return "Skin " + skinId;
        }


        private void ApplySkinToDeployable(BrowserState state, ulong skinId)
        {
            BaseCombatEntity entity = state.TargetEntity;
            if (entity == null || entity.IsDestroyed) { CloseBrowser(state.Player.userID, true); return; }
            SkinEntry entry = null;
            _skinById.TryGetValue(skinId, out entry);
            if (IsBlacklisted(state.ItemId, skinId) || (entry != null && !CanPlayerUseSkin(state.Player, entry))) return;

            ItemDefinition currentDef = GetEntityItemDefinition(entity);
            if (currentDef == null) return;

            if (entry == null || entry.RedirectItemId == 0)
            {
                entity.skinID = skinId;
                entity.SendNetworkUpdateImmediate();
                RememberSelectedSkin(state.Player, state.ItemId, skinId);
                SendReply(state.Player, "Applied " + GetSkinName(skinId) + ".");
                return;
            }

            ItemDefinition redirectDef = ItemManager.FindItemDefinition(entry.RedirectItemId);
            string path;
            if (redirectDef == null || !TryGetEntityPrefabPath(redirectDef, out path))
            {
                SendReply(state.Player, "This redirected deployable skin cannot be recreated safely.");
                return;
            }

            Vector3 pos = entity.transform.position;
            Quaternion rot = entity.transform.rotation;
            SprayCan.ReskinPreserveInfo preserve = new SprayCan.ReskinPreserveInfo();
            entity.Reskin_Preserve(ref preserve);
            BaseEntity replacement = GameManager.server.CreateEntity(path, pos, rot, true);
            if (replacement == null) return;
            entity.Kill(BaseNetworkable.DestroyMode.None, true);
            replacement.Spawn();
            replacement.Reskin_Restore(ref preserve);
            replacement.skinID = 0UL;
            replacement.SendNetworkUpdateImmediate();
            state.TargetEntity = replacement as BaseCombatEntity;
            RememberSelectedSkin(state.Player, state.ItemId, skinId);
            SendReply(state.Player, "Applied " + GetSkinName(skinId) + ".");
        }

        private static bool TryGetEntityPrefabPath(ItemDefinition def, out string path)
        {
            path = string.Empty;
            ItemModDeployable deployable;
            if (def.TryGetComponent<ItemModDeployable>(out deployable))
            {
                path = deployable.entityPrefab.resourcePath;
                return !string.IsNullOrEmpty(path);
            }
            ItemModEntity entity;
            if (def.TryGetComponent<ItemModEntity>(out entity))
            {
                path = entity.entityPrefab.resourcePath;
                return !string.IsNullOrEmpty(path);
            }
            ItemModEntityReference reference;
            if (def.TryGetComponent<ItemModEntityReference>(out reference))
            {
                path = reference.entityPrefab.resourcePath;
                return !string.IsNullOrEmpty(path);
            }
            return false;
        }

        private bool HasFeature(BasePlayer player, string perm)
        {
            if (player == null) return false;
            if (permission.UserHasPermission(player.UserIDString, perm)) return true;
            SendReply(player, "You do not have permission to use this RogueRustSkins feature.");
            return false;
        }

        private PlayerSkinData GetPlayerData(BasePlayer player)
        {
            PlayerSkinData data;
            if (!_playerData.TryGetValue(player.userID, out data))
            {
                data = new PlayerSkinData();
                _playerData[player.userID] = data;
            }
            int setCount = Mathf.Clamp(_config.SkinSets.SetCount, 3, 10);
            if (data.Sets == null || data.Sets.Length != setCount)
            {
                Dictionary<int, ulong>[] old = data.Sets ?? new Dictionary<int, ulong>[0];
                data.Sets = new Dictionary<int, ulong>[setCount];
                for (int i = 0; i < setCount; i++)
                    data.Sets[i] = i < old.Length && old[i] != null ? old[i] : new Dictionary<int, ulong>();
            }
            for (int i = 0; i < setCount; i++) if (data.Sets[i] == null) data.Sets[i] = new Dictionary<int, ulong>();
            if (data.SelectedSet < 1 || data.SelectedSet > setCount) data.SelectedSet = 1;
            if (data.SetNames == null || data.SetNames.Length != setCount)
            {
                string[] oldNames = data.SetNames ?? new string[0];
                data.SetNames = new string[setCount];
                for (int i = 0; i < setCount; i++) data.SetNames[i] = i < oldNames.Length && !string.IsNullOrWhiteSpace(oldNames[i]) ? oldNames[i] : "Set " + (i + 1);
            }
            if (data.Recent == null) data.Recent = new Dictionary<int, List<ulong>>();
            if (data.Favourites == null) data.Favourites = new Dictionary<int, HashSet<ulong>>();
            data.LastOnlineUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return data;
        }

        private void LoadPlayerData()
        {
            try
            {
                Dictionary<ulong, PlayerSkinData> loaded = _rogue.Data.Load<Dictionary<ulong, PlayerSkinData>>("RogueRustSkins/players", () => new Dictionary<ulong, PlayerSkinData>());
                _playerData.Clear();
                if (loaded != null) foreach (KeyValuePair<ulong, PlayerSkinData> pair in loaded) _playerData[pair.Key] = pair.Value;
            }
            catch (Exception ex) { PrintWarning("Could not load player skin sets: " + ex.Message); }
        }

        private void CleanupInactivePlayerData()
        {
            int days = _config.History.RemoveInactiveDays;
            if (days <= 0 || _playerData.Count == 0) return;
            long cutoff = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
            int removed = 0;
            foreach (ulong id in _playerData.Where(x => x.Value != null && x.Value.LastOnlineUtc > 0 && x.Value.LastOnlineUtc < cutoff).Select(x => x.Key).ToArray())
            { _playerData.Remove(id); removed++; }
            if (removed > 0) { MarkPlayerDataDirty(); Puts("Cleaned " + removed + " inactive RogueRustSkins player record(s)."); }
        }

        private void MarkPlayerDataDirty()
        {
            _playerDataDirty = true;
            if (_playerSaveTimer != null && !_playerSaveTimer.Destroyed) return;
            _playerSaveTimer = timer.Once(2f, () =>
            {
                _playerSaveTimer = null;
                if (!_playerDataDirty) return;
                _playerDataDirty = false;
                SavePlayerData();
            });
        }

        private void SavePlayerData()
        {
            try { if (_rogue != null) _rogue.Data.Save("RogueRustSkins/players", _playerData); }
            catch (Exception ex) { PrintWarning("Could not save player skin sets: " + ex.Message); }
        }

        private void RememberSelectedSkin(BasePlayer player, int itemId, ulong skinId)
        {
            if (player == null) return;
            PlayerSkinData data = GetPlayerData(player);
            data.Sets[data.SelectedSet - 1][itemId] = skinId;
            if (skinId != 0)
            {
                List<ulong> recent;
                if (!data.Recent.TryGetValue(itemId, out recent)) data.Recent[itemId] = recent = new List<ulong>();
                recent.Remove(skinId);
                recent.Add(skinId);
                int maxRecent = Mathf.Clamp(_config.History.RecentPerItem, 1, 24);
                while (recent.Count > maxRecent) recent.RemoveAt(0);
            }
            MarkPlayerDataDirty();
        }

        private Dictionary<int, ulong> GetSelectedSet(BasePlayer player, int explicitSet = 0)
        {
            PlayerSkinData data = GetPlayerData(player);
            int set = explicitSet >= 1 && explicitSet <= data.Sets.Length ? explicitSet : data.SelectedSet;
            return data.Sets[set - 1];
        }

        private void CommandSkinSet(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionUse)) return;
            PlayerSkinData data = GetPlayerData(player);
            if (args == null || args.Length == 0)
            {
                SendReply(player, $"Active skin set: {data.SelectedSet}. Use /skinset 1-{data.Sets.Length}.");
                return;
            }
            int set;
            if (!int.TryParse(args[0], out set) || set < 1 || set > data.Sets.Length)
            { SendReply(player, "Skin set must be between 1 and " + data.Sets.Length + "."); return; }
            data.SelectedSet = set;
            MarkPlayerDataDirty();
            SendReply(player, $"Skin set {set} selected. Skins chosen with /skin are now saved to this set.");
        }

        private void CommandSkinAuto(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionAuto)) return;
            PlayerSkinData data = GetPlayerData(player);
            if (args != null && args.Length > 0)
            {
                int requestedSet;
                if (int.TryParse(args[0], out requestedSet) && requestedSet >= 1 && requestedSet <= data.Sets.Length)
                    data.SelectedSet = requestedSet;
            }
            data.AutoSkin = !data.AutoSkin;
            MarkPlayerDataDirty();
            SendReply(player, "Auto skin " + (data.AutoSkin ? "enabled." : "disabled."));
        }

        private void CommandSkinCraft(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionCraft)) return;
            PlayerSkinData data = GetPlayerData(player);
            data.CraftSkin = !data.CraftSkin;
            MarkPlayerDataDirty();
            SendReply(player, "Craft skinning " + (data.CraftSkin ? "enabled." : "disabled."));
        }

        private void CommandSkinInventory(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionInventory) || !CheckCooldown(player, "inventory", _config.Cooldowns.Inventory)) return;
            ApplyInventorySet(player, true);
        }

        private bool CheckCooldown(BasePlayer player, string key, float seconds)
        {
            if (!_config.Cooldowns.Enabled || seconds <= 0f || player == null) return true;
            Dictionary<string, float> map;
            if (!_cooldowns.TryGetValue(player.userID, out map)) _cooldowns[player.userID] = map = new Dictionary<string, float>();
            float until;
            float now = Time.realtimeSinceStartup;
            if (map.TryGetValue(key, out until) && until > now)
            {
                SendReply(player, "Please wait " + Mathf.CeilToInt(until - now) + " second(s) before using that again.");
                return false;
            }
            map[key] = now + seconds;
            return true;
        }

        private void CommandSkinSearch(BasePlayer player, string command, string[] args)
        {
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state))
            { SendReply(player, "Open /skin or /skinitem first."); return; }
            string query = args == null ? string.Empty : string.Join(" ", args);
            state.SearchText = query;
            state.MainPage = 0;
            ShowMainSkins(state, false);
            SendReply(player, string.IsNullOrWhiteSpace(query) ? "Skin search cleared." : "Skin search: " + query);
        }

        private void CommandSkinRecent(BasePlayer player, string command, string[] args)
        {
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state))
            { SendReply(player, "Open /skin or /skinitem first."); return; }
            SendReply(player, "Recent skins are now shown first on the main skin page.");
        }

        private void CommandSkinFavourite(BasePlayer player, string command, string[] args)
        {
            if (!_config.History.EnableFavourites) { SendReply(player, "Skin favourites are disabled."); return; }
            BrowserState state;
            if (player == null || !_browsers.TryGetValue(player.userID, out state))
            { SendReply(player, "Open /skin or /skinitem first."); return; }
            if (args == null || args.Length == 0)
            {
                ShowFavourites(state);
                return;
            }
            ulong skinId;
            if (!ulong.TryParse(args[0], out skinId) || !state.Catalogue.Contains(skinId))
            { SendReply(player, "Use /skinfav <skinID> while the skin browser is open."); return; }
            PlayerSkinData data = GetPlayerData(player);
            HashSet<ulong> fav;
            if (!data.Favourites.TryGetValue(state.ItemId, out fav)) data.Favourites[state.ItemId] = fav = new HashSet<ulong>();
            bool added = fav.Add(skinId);
            if (!added) fav.Remove(skinId);
            MarkPlayerDataDirty();
            SendReply(player, (added ? "Added " : "Removed ") + GetSkinName(skinId) + (added ? " to" : " from") + " favourites.");
        }

        private void CommandSkinSetName(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionUse)) return;
            PlayerSkinData data = GetPlayerData(player);
            if (args == null || args.Length < 2) { SendReply(player, "Usage: /skinsetname <set> <name>"); return; }
            int set;
            if (!int.TryParse(args[0], out set) || set < 1 || set > data.SetNames.Length) { SendReply(player, "Invalid skin set."); return; }
            string name = string.Join(" ", args.Skip(1).ToArray()).Trim();
            if (name.Length > 24) name = name.Substring(0, 24);
            if (string.IsNullOrWhiteSpace(name)) return;
            data.SetNames[set - 1] = name;
            MarkPlayerDataDirty();
            SendReply(player, "Skin set " + set + " renamed to " + name + ".");
        }

        private void CommandSkinRequest(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionRequest) || !_config.Requests.Enabled) return;
            ulong id;
            if (args == null || args.Length != 1 || !ulong.TryParse(args[0], out id) || id == 0)
            { SendReply(player, "Usage: /skinrequest <workshopID>"); return; }
            int pending = _skinRequests.Values.Count(x => x.RequestedBy == player.userID && x.Status == "Pending");
            if (pending >= _config.Requests.MaxPendingPerPlayer) { SendReply(player, "You have reached the pending skin request limit."); return; }
            if (_skinRequests.ContainsKey(id)) { SendReply(player, "That skin is already in the request queue."); return; }
            _skinRequests[id] = new SkinRequestData { SkinId = id, RequestedBy = player.userID, RequestedByName = player.displayName, RequestedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
            SaveRequestData();
            SendReply(player, "Skin " + id + " has been queued for review.");
        }

        private void CommandSkinRequests(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionAdmin)) return;
            if (args != null && args.Length >= 2)
            {
                ulong id;
                if (!ulong.TryParse(args[1], out id) || !_skinRequests.ContainsKey(id)) { SendReply(player, "Request not found."); return; }
                if (args[0].Equals("approve", StringComparison.OrdinalIgnoreCase))
                {
                    _skinRequests[id].Status = "Approved";
                    SaveRequestData();
                    SendReply(player, "Approved request " + id + ". Importing from Steam Workshop...");
                    QueueWorkshopImport(player, new List<ulong> { id }, false);
                    return;
                }
                if (args[0].Equals("deny", StringComparison.OrdinalIgnoreCase))
                {
                    _skinRequests[id].Status = "Denied";
                    SaveRequestData();
                    SendReply(player, "Denied request " + id + ".");
                    return;
                }
            }
            List<SkinRequestData> pending = _skinRequests.Values.Where(x => x.Status == "Pending").Take(20).ToList();
            if (pending.Count == 0) { SendReply(player, "No pending skin requests."); return; }
            SendReply(player, "Pending: " + string.Join(", ", pending.Select(x => x.SkinId + " (" + x.RequestedByName + ")").ToArray()));
        }

        private void CommandSkinImport(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionImport) || !_config.Requests.EnableWorkshopImports) return;
            ulong id;
            if (args == null || args.Length != 1 || !ulong.TryParse(args[0], out id) || id == 0)
            { SendReply(player, "Usage: /skinimport <workshopID>"); return; }

            if (_skinById.ContainsKey(id))
            {
                _config.General.BlacklistedSkins.Remove(id);
                SaveConfig();
                SendReply(player, "Skin " + id + " is already known and has been enabled.");
                return;
            }
            QueueWorkshopImport(player, new List<ulong> { id }, false);
        }

        private void CommandSkinRemove(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionImport)) return;
            ulong id;
            if (args == null || args.Length != 1 || !ulong.TryParse(args[0], out id)) { SendReply(player, "Usage: /skinremove <skinID>"); return; }
            if (!_config.General.BlacklistedSkins.Contains(id)) _config.General.BlacklistedSkins.Add(id);
            _importedWorkshop.Remove(id);
            foreach (PlayerSkinData data in _playerData.Values)
            {
                if (data.Sets != null) foreach (Dictionary<int, ulong> set in data.Sets) if (set != null)
                    foreach (int key in set.Where(x => x.Value == id).Select(x => x.Key).ToArray()) set.Remove(key);
                if (data.Recent != null) foreach (List<ulong> recent in data.Recent.Values) recent.Remove(id);
                if (data.Favourites != null) foreach (HashSet<ulong> fav in data.Favourites.Values) fav.Remove(id);
            }
            SaveConfig();
            MarkPlayerDataDirty();
            SaveImportedWorkshopData();
            RefreshLiveCatalogue("skin-remove");
            SendReply(player, "Skin " + id + " has been disabled and removed from saved player selections.");
        }

        private void CommandSkinSets(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionUse)) return;
            PlayerSkinData data = GetPlayerData(player);
            string[] parts = new string[data.SetNames.Length];
            for (int i = 0; i < parts.Length; i++)
                parts[i] = (data.SelectedSet == i + 1 ? "[ACTIVE] " : string.Empty) + (i + 1) + ": " + data.SetNames[i] + " (" + data.Sets[i].Count + " skins)";
            SendReply(player, string.Join(" | ", parts));
        }

        private void CommandSkinCollection(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionCollection) || !_config.Requests.EnableCollectionImports) return;
            ulong collectionId;
            if (args == null || args.Length != 1 || !ulong.TryParse(args[0], out collectionId) || collectionId == 0)
            { SendReply(player, "Usage: /skincollection <collectionID>"); return; }
            if (_workshopJobs.Contains(collectionId)) { SendReply(player, "That collection is already being processed."); return; }
            _workshopJobs.Add(collectionId);
            string body = "collectioncount=1&publishedfileids[0]=" + collectionId.ToString(CultureInfo.InvariantCulture);
            webrequest.Enqueue("https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", body, (code, response) =>
            {
                _workshopJobs.Remove(collectionId);
                if (code != 200 || string.IsNullOrWhiteSpace(response))
                { if (player != null && player.IsConnected) SendReply(player, "Steam collection request failed (HTTP " + code + ")."); return; }
                try
                {
                    SteamCollectionResponse parsed = JsonConvert.DeserializeObject<SteamCollectionResponse>(response);
                    List<ulong> ids = new List<ulong>();
                    if (parsed?.response?.collectiondetails != null)
                        foreach (SteamCollectionDetail detail in parsed.response.collectiondetails)
                            if (detail?.children != null)
                                foreach (SteamCollectionChild child in detail.children)
                                {
                                    ulong id;
                                    if (child != null && ulong.TryParse(child.publishedfileid, out id) && id != 0) ids.Add(id);
                                }
                    ids = ids.Distinct().ToList();
                    if (ids.Count == 0) { if (player != null && player.IsConnected) SendReply(player, "No Workshop items were found in that collection."); return; }
                    if (!_config.Requests.CollectionIds.Contains(collectionId)) { _config.Requests.CollectionIds.Add(collectionId); SaveConfig(); }
                    QueueWorkshopImport(player, ids, true);
                }
                catch (Exception ex) { PrintError("Collection import parse failed: " + ex.Message); }
            }, this, Oxide.Core.Libraries.RequestMethod.POST, new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" }, _config.Requests.TimeoutSeconds);
        }

        private void QueueWorkshopImport(BasePlayer requester, List<ulong> ids, bool collection)
        {
            if (ids == null || ids.Count == 0) return;
            List<ulong> unique = ids.Where(x => x != 0 && !_workshopJobs.Contains(x)).Distinct().ToList();
            if (unique.Count == 0) return;
            int batchSize = Mathf.Clamp(_config.Requests.BatchSize, 1, 100);
            int pending = unique.Count;
            int imported = 0;
            int failed = 0;

            for (int start = 0; start < unique.Count; start += batchSize)
            {
                List<ulong> batch = unique.Skip(start).Take(batchSize).ToList();
                foreach (ulong id in batch) _workshopJobs.Add(id);
                StringBuilder form = new StringBuilder("itemcount=").Append(batch.Count);
                for (int i = 0; i < batch.Count; i++) form.Append("&publishedfileids[").Append(i).Append("]=").Append(batch[i]);
                webrequest.Enqueue("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", form.ToString(), (code, response) =>
                {
                    foreach (ulong id in batch) _workshopJobs.Remove(id);
                    if (code != 200 || string.IsNullOrWhiteSpace(response))
                    {
                        failed += batch.Count; pending -= batch.Count;
                        FinishWorkshopImport(requester, pending, imported, failed, collection);
                        return;
                    }
                    try
                    {
                        SteamPublishedResponse parsed = JsonConvert.DeserializeObject<SteamPublishedResponse>(response);
                        int handled = 0;
                        if (parsed?.response?.publishedfiledetails != null)
                        {
                            foreach (SteamPublishedFile file in parsed.response.publishedfiledetails)
                            {
                                handled++;
                                ImportedWorkshopSkin skin;
                                if (TryMapWorkshopFile(file, out skin))
                                {
                                    _importedWorkshop[skin.SkinId] = skin;
                                    _config.General.BlacklistedSkins.Remove(skin.SkinId);
                                    imported++;
                                }
                                else failed++;
                            }
                        }
                        failed += Math.Max(0, batch.Count - handled);
                    }
                    catch (Exception ex)
                    {
                        failed += batch.Count;
                        PrintError("Workshop import parse failed: " + ex.Message);
                    }
                    pending -= batch.Count;
                    FinishWorkshopImport(requester, pending, imported, failed, collection);
                }, this, Oxide.Core.Libraries.RequestMethod.POST, new Dictionary<string, string> { ["Content-Type"] = "application/x-www-form-urlencoded" }, _config.Requests.TimeoutSeconds);
            }
        }

        private void FinishWorkshopImport(BasePlayer requester, int pending, int imported, int failed, bool collection)
        {
            if (pending > 0) return;
            SaveImportedWorkshopData();
            SaveConfig();
            RefreshLiveCatalogue(collection ? "workshop-collection-import" : "workshop-import");
            if (requester != null && requester.IsConnected)
                SendReply(requester, "Workshop import complete: " + imported + " imported, " + failed + " skipped/failed.");
        }

        private bool TryMapWorkshopFile(SteamPublishedFile file, out ImportedWorkshopSkin imported)
        {
            imported = null;
            ulong id;
            if (file == null || file.result != 1 || !ulong.TryParse(file.publishedfileid, out id) || id == 0) return false;
            ItemDefinition def = ResolveWorkshopItem(file.tags);
            if (def == null) return false;
            ItemDefinition canonical = def.isRedirectOf ?? def;
            imported = new ImportedWorkshopSkin
            {
                SkinId = id,
                ItemId = canonical.itemid,
                Shortname = canonical.shortname,
                Name = string.IsNullOrWhiteSpace(file.title) ? id.ToString(CultureInfo.InvariantCulture) : file.title,
                ImportedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            return true;
        }

        private ItemDefinition ResolveWorkshopItem(List<SteamTag> tags)
        {
            if (tags == null) return null;
            foreach (SteamTag tag in tags)
            {
                if (tag == null || string.IsNullOrWhiteSpace(tag.tag)) continue;
                string normalized = new string(tag.tag.Where(char.IsLetterOrDigit).Select(c => char.ToLowerInvariant(c)).ToArray());
                foreach (ItemDefinition def in ItemManager.GetItemDefinitions())
                {
                    if (def == null || def.isRedirectOf != null) continue;
                    string display = new string((def.displayName?.english ?? string.Empty).Where(char.IsLetterOrDigit).Select(c => char.ToLowerInvariant(c)).ToArray());
                    string shortname = new string((def.shortname ?? string.Empty).Where(char.IsLetterOrDigit).Select(c => char.ToLowerInvariant(c)).ToArray());
                    if (normalized == display || normalized == shortname) return def;
                }
            }
            return null;
        }

        private void LoadImportedWorkshopData()
        {
            try
            {
                Dictionary<ulong, ImportedWorkshopSkin> loaded = _rogue.Data.Load<Dictionary<ulong, ImportedWorkshopSkin>>("RogueRustSkins/imported-workshop", () => new Dictionary<ulong, ImportedWorkshopSkin>());
                _importedWorkshop.Clear();
                if (loaded != null) foreach (var pair in loaded) _importedWorkshop[pair.Key] = pair.Value;
            }
            catch (Exception ex) { PrintWarning("Could not load imported Workshop skins: " + ex.Message); }
        }

        private void SaveImportedWorkshopData()
        {
            try { if (_rogue != null) _rogue.Data.Save("RogueRustSkins/imported-workshop", _importedWorkshop); }
            catch (Exception ex) { PrintWarning("Could not save imported Workshop skins: " + ex.Message); }
        }

        private void LoadRequestData()
        {
            try
            {
                Dictionary<ulong, SkinRequestData> loaded = _rogue.Data.Load<Dictionary<ulong, SkinRequestData>>("RogueRustSkins/requests", () => new Dictionary<ulong, SkinRequestData>());
                _skinRequests.Clear();
                if (loaded != null) foreach (var pair in loaded) _skinRequests[pair.Key] = pair.Value;
            }
            catch (Exception ex) { PrintWarning("Could not load skin requests: " + ex.Message); }
        }

        private void SaveRequestData()
        {
            try { if (_rogue != null) _rogue.Data.Save("RogueRustSkins/requests", _skinRequests); }
            catch (Exception ex) { PrintWarning("Could not save skin requests: " + ex.Message); }
        }

        private void ApplyInventorySet(BasePlayer player, bool sendMessage)
        {
            if (player?.inventory == null || _rogue == null) return;

            List<Item> pooled = Pool.Get<List<Item>>();
            player.inventory.GetAllItems(pooled);

            // Snapshot references before returning the pooled list. The batch scheduler
            // owns only this small array while work is spread across server ticks.
            Item[] items = pooled.Take(_config.Performance.MaxBulkOperations).ToArray();
            Pool.FreeUnmanaged(ref pooled);

            Dictionary<int, ulong> selected = GetSelectedSet(player);
            int changed = 0;
            _rogue.Workloads.Batch(
                CacheOwner,
                "inventory:" + player.userID,
                items.Length,
                _config.Performance.BulkBatchSize,
                TimeSpan.Zero,
                index =>
                {
                    if (player == null || !player.IsConnected) return false;
                    Item item = items[index];
                    if (item != null && ApplySetSkin(player, item, selected)) changed++;
                    return true;
                },
                () =>
                {
                    if (player == null || !player.IsConnected) return;
                    player.SendNetworkUpdateImmediate();
                    if (sendMessage)
                        SendReply(player, $"Applied skin set {GetPlayerData(player).SelectedSet} to {changed} inventory item(s).");
                });
        }

        private void CommandSkinContainer(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionContainer) || !CheckCooldown(player, "container", _config.Cooldowns.Container)) return;
            if (_config.General.RequireBuildingAuthorization && !player.CanBuild() && !permission.UserHasPermission(player.UserIDString, PermissionBypassAuth)) { SendReply(player, "You need building authorization to skin this container."); return; }
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f)) { SendReply(player, "Look at a storage container within 5 metres."); return; }
            StorageContainer storage = hit.GetEntity() as StorageContainer;
            if (storage?.inventory == null) { SendReply(player, "No storage container found."); return; }
            Item[] items = storage.inventory.itemList.Take(_config.Performance.MaxBulkOperations).ToArray();
            Dictionary<int, ulong> selected = GetSelectedSet(player);
            int changed = 0;
            _rogue.Workloads.Batch(
                CacheOwner,
                "container:" + player.userID,
                items.Length,
                _config.Performance.BulkBatchSize,
                TimeSpan.Zero,
                index =>
                {
                    if (player == null || !player.IsConnected || storage == null || storage.IsDestroyed) return false;
                    Item item = items[index];
                    if (item != null && ApplySetSkin(player, item, selected)) changed++;
                    return true;
                },
                () =>
                {
                    if (storage != null && !storage.IsDestroyed) storage.inventory?.MarkDirty();
                    if (player != null && player.IsConnected)
                        SendReply(player, $"Applied skin set to {changed} item(s) in {storage.ShortPrefabName}.");
                });
        }

        private void CommandSkinTeam(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionTeam)) return;
            if (args != null && args.Length > 0 && args[0].Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                PlayerSkinData own = GetPlayerData(player);
                own.BlockTeamSkins = !own.BlockTeamSkins;
                MarkPlayerDataDirty();
                SendReply(player, "Team skinning " + (own.BlockTeamSkins ? "blocked." : "allowed."));
                return;
            }
            if (!CheckCooldown(player, "team", _config.Cooldowns.Team)) return;
            if (player.currentTeam == 0) { SendReply(player, "You are not in a team."); return; }

            RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
            if (team == null) { SendReply(player, "Your team could not be found."); return; }

            int set = GetPlayerData(player).SelectedSet;
            if (args != null && args.Length > 0) int.TryParse(args[0], out set);
            if (set < 1 || set > GetPlayerData(player).Sets.Length) set = GetPlayerData(player).SelectedSet;

            Dictionary<int, ulong> skins = GetSelectedSet(player, set);
            List<KeyValuePair<BasePlayer, Item>> work = new List<KeyValuePair<BasePlayer, Item>>();
            foreach (ulong memberId in team.members)
            {
                BasePlayer member = BasePlayer.FindByID(memberId);
                if (member?.inventory == null || GetPlayerData(member).BlockTeamSkins) continue;

                List<Item> pooled = Pool.Get<List<Item>>();
                try
                {
                    member.inventory.GetAllItems(pooled);
                    foreach (Item item in pooled)
                    {
                        if (work.Count >= _config.Performance.MaxBulkOperations) break;
                        work.Add(new KeyValuePair<BasePlayer, Item>(member, item));
                    }
                }
                finally { Pool.FreeUnmanaged(ref pooled); }

                if (work.Count >= _config.Performance.MaxBulkOperations) break;
            }

            int changed = 0;
            int selectedSet = set;
            HashSet<ulong> touchedPlayers = new HashSet<ulong>();
            _rogue.Workloads.Batch(
                CacheOwner,
                "team:" + player.userID,
                work.Count,
                _config.Performance.BulkBatchSize,
                TimeSpan.Zero,
                index =>
                {
                    if (player == null || !player.IsConnected) return false;
                    KeyValuePair<BasePlayer, Item> pair = work[index];
                    BasePlayer member = pair.Key;
                    if (member == null || !member.IsConnected || pair.Value == null) return true;
                    if (ApplySetSkin(member, pair.Value, skins, player))
                    {
                        changed++;
                        touchedPlayers.Add(member.userID);
                    }
                    return true;
                },
                () =>
                {
                    foreach (ulong memberId in touchedPlayers)
                    {
                        BasePlayer member = BasePlayer.FindByID(memberId);
                        if (member != null && member.IsConnected) member.SendNetworkUpdateImmediate();
                    }
                    if (player != null && player.IsConnected)
                        SendReply(player, $"Applied skin set {selectedSet} to {changed} team inventory item(s).");
                });
        }

        private void CommandSkinBase(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionBase) || !CheckCooldown(player, "base", _config.Cooldowns.Base)) return;
            ApplyBuildingSkins(player, false);
        }

        private void CommandSkinAll(BasePlayer player, string command, string[] args)
        {
            if (!HasFeature(player, PermissionAll) || !CheckCooldown(player, "all", _config.Cooldowns.All)) return;
            ApplyInventorySet(player, false);
            ApplyBuildingSkins(player, true);
        }

        private void ApplyBuildingSkins(BasePlayer player, bool includeInventoryMessage)
        {
            if (!player.IsBuildingAuthed() && !permission.UserHasPermission(player.UserIDString, PermissionBypassAuth))
            { SendReply(player, "You must have building authorization to use this command."); return; }
            BuildingPrivlidge privilege = player.GetBuildingPrivilege();
            BuildingManager.Building building = privilege?.GetBuilding();
            if (building == null) { SendReply(player, "No authorized building was found."); return; }
            Dictionary<int, ulong> skins = GetSelectedSet(player);
            DecayEntity[] entities = building.decayEntities.Take(_config.Performance.MaxBulkOperations).ToArray();
            int changed = 0;
            _rogue.Workloads.Batch(
                CacheOwner,
                "building:" + player.userID,
                entities.Length,
                _config.Performance.BulkBatchSize,
                TimeSpan.Zero,
                index =>
                {
                    if (player == null || !player.IsConnected) return false;
                    BaseCombatEntity entity = entities[index] as BaseCombatEntity;
                    ItemDefinition def = entity != null && !entity.IsDestroyed ? entity.pickup.itemTarget : null;
                    if (entity == null || def == null) return true;
                    int itemId = (def.isRedirectOf ?? def).itemid;
                    ulong skinId;
                    if (!skins.TryGetValue(itemId, out skinId)) return true;
                    SkinEntry entry = null;
                    if (skinId != 0 && _skinById.TryGetValue(skinId, out entry) && !CanPlayerUseSkin(player, entry)) return true;
                    if (entity.skinID == skinId) return true;
                    entity.skinID = skinId;
                    entity.SendNetworkUpdateImmediate();
                    changed++;
                    return true;
                },
                () =>
                {
                    if (player != null && player.IsConnected)
                        SendReply(player, $"Applied skin set {GetPlayerData(player).SelectedSet} to {changed} deployable(s) in the authorized building.");
                });
        }

        private bool ApplySetSkin(BasePlayer targetPlayer, Item item, Dictionary<int, ulong> set, BasePlayer skinOwner = null)
        {
            if (item?.info == null || item.parent == null) return false;
            int itemId = (item.info.isRedirectOf ?? item.info).itemid;
            ulong skinId;
            if (!set.TryGetValue(itemId, out skinId)) return false;

            Item resultingItem;
            return TryApplyItemSkin(targetPlayer, item, itemId, skinId, skinOwner ?? targetPlayer, out resultingItem);
        }

        private void OnItemCraftFinished(ItemCraftTask task, Item item, ItemCrafter crafter)
        {
            if (!_config.Automation.EnableCraftSkinning || item?.info == null || crafter?.owner == null) return;
            BasePlayer player = crafter.owner;
            PlayerSkinData data = GetPlayerData(player);
            if (!data.CraftSkin) return;
            ApplySetSkin(player, item, GetSelectedSet(player));
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            BasePlayer player = container?.playerOwner;
            if (player == null || item?.info == null || !_config.Automation.EnableAutoSkinning) return;
            PlayerSkinData data = GetPlayerData(player);
            if (!data.AutoSkin) return;
            NextTick(() => { if (item != null && item.parent != null) ApplySetSkin(player, item, GetSelectedSet(player)); });
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null || !_config.Automation.EnableSprayCanOverride) return;
            if (newItem?.info?.itemid == SprayCanItemId) _sprayHeld.Add(player.userID);
            else _sprayHeld.Remove(player.userID);
        }

        private readonly Dictionary<ulong, float> _sprayInputTimes = new Dictionary<ulong, float>();

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!_config.Automation.EnableSprayCanOverride || player == null || input == null || !_sprayHeld.Contains(player.userID)) return;
            if (!input.WasJustPressed(BUTTON.FIRE_SECONDARY)) return;
            float now = Time.realtimeSinceStartup;
            float last;
            if (_sprayInputTimes.TryGetValue(player.userID, out last) && now - last < _config.Automation.SprayCanCooldownSeconds) return;
            _sprayInputTimes[player.userID] = now;
            if (!permission.UserHasPermission(player.UserIDString, PermissionItems)) return;
            CommandSkinItem(player, "spraycan", Array.Empty<string>());
        }

        private void OnPlayerLootEnd(PlayerLoot loot)
        {
            BasePlayer player = loot?.baseEntity;
            if (player == null) return;
            DestroyBrowserUi(player);
            CloseBrowser(player.userID, false);
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            if (player == null) return;
            DestroyBrowserUi(player);
            CloseBrowser(player.userID, false);
        }

        private void DestroyBrowserUi(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.DestroyUi(player, UiHeader);
            CuiHelper.DestroyUi(player, UiOptions);
        }
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            _sprayHeld.Remove(player.userID);
            _sprayInputTimes.Remove(player.userID);
            CloseBrowser(player.userID, false);
        }

        private void CloseBrowser(ulong playerId, bool clearLoot)
        {
            BrowserState state;
            if (!_browsers.TryGetValue(playerId, out state) || state.Closing) return;
            state.Closing = true;
            _browsers.Remove(playerId);
            _rogue?.SkinSessions.Close(playerId);
            if (state.Player != null)
            {
                CuiHelper.DestroyUi(state.Player, UiRoot);
                CuiHelper.DestroyUi(state.Player, UiHeader);
                CuiHelper.DestroyUi(state.Player, UiOptions);
            }
            if (clearLoot && state.Player?.inventory != null) state.Player.inventory.loot.Clear();
            if (state.Container != null) { ClearPreviewItems(state.Container); state.Container.Kill(); state.Container = null; }
        }

        private static void ClearPreviewItems(ItemContainer container)
        {
            if (container?.itemList == null || container.itemList.Count == 0) return;
            Item[] items = container.itemList.ToArray();
            for (int i = 0; i < items.Length; i++)
            {
                Item item = items[i];
                if (item == null) continue;
                item.RemoveFromContainer();
                item.Remove();
            }
            container.MarkDirty();
        }

        private void Unload()
        {
            if (_playerSaveTimer != null && !_playerSaveTimer.Destroyed)
            {
                _playerSaveTimer.Destroy();
                _playerSaveTimer = null;
            }
            if (_playerDataDirty)
            {
                _playerDataDirty = false;
                SavePlayerData();
            }
            SaveRequestData();
            SaveImportedWorkshopData();
            Steamworks.SteamInventory.OnDefinitionsUpdated -= OnSteamDefinitionsUpdated;
            foreach (BrowserState state in _browsers.Values.ToArray()) CloseBrowser(state.Player.userID, false);
            if (_rogue != null)
            {
                _rogue.Workloads.CancelOwner(CacheOwner);
                _rogue.Skins.RemoveOwner(CacheOwner);
                _lastPublishedCatalogueSignature = int.MinValue;
                _rogue.SkinSessions.CloseAll();
                _rogue.PlayerSkins.InvalidateAll();
                _rogue.Database.Unregister(CacheConnection);
            }
        }
    }
}
