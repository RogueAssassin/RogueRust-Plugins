using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustRemovalTool", "RogueAssassin", "2.0.0")]
    [Description("RogueRust-native building and deployable removal tool.")]
    public sealed class RogueRustRemovalTool : RogueRustPlugin
    {
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 0, 0);
        private const string UiName = "RogueRustRemovalTool.Hud";
        private const string UiRoot = "RogueRustRemovalTool.UI.Root";
        private const string UiSession = "RogueRustRemovalTool.UI.Session";
        private const string UiCrosshair = "RogueRustRemovalTool.UI.Crosshair";
        private const string UiTarget = "RogueRustRemovalTool.UI.Target";
        private const string UiStatus = "RogueRustRemovalTool.UI.Status";
        private const int TargetLayerMask = ~(1 << 2 | 1 << 3 | 1 << 4 | 1 << 10 | 1 << 18 | 1 << 28 | 1 << 29);

        [RoguePermission] private const string PermNormal = "roguerustremovaltool.normal";
        [RoguePermission] private const string PermAdmin = "roguerustremovaltool.admin";
        [RoguePermission] private const string PermAll = "roguerustremovaltool.all";
        [RoguePermission] private const string PermExternal = "roguerustremovaltool.external";
        [RoguePermission] private const string PermStructure = "roguerustremovaltool.structure";
        [RoguePermission] private const string PermTarget = "roguerustremovaltool.target";
        [RoguePermission] private const string PermOverride = "roguerustremovaltool.override";

        [PluginReference] private Plugin Friends;
        [PluginReference] private Plugin Clans;
        [PluginReference] private Plugin BuildingOwners;
        [PluginReference] private Plugin NoEscape;
        [PluginReference] private Plugin Economics;
        [PluginReference] private Plugin ServerRewards;

        private Configuration _config = new Configuration();
        private readonly Dictionary<ulong, RemovalSession> _sessions = new Dictionary<ulong, RemovalSession>();
        private readonly Dictionary<ulong, BulkConfirmation> _bulkConfirmations =
            new Dictionary<ulong, BulkConfirmation>();
        private readonly Dictionary<ulong, string> _uiState =
            new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, double> _spawnedAt = new Dictionary<ulong, double>();
        private readonly Dictionary<string, ConstructionInfo> _constructionByPrefab =
            new Dictionary<string, ConstructionInfo>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deployablePrefabs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ItemDefinition> _deployableItems =
            new Dictionary<string, ItemDefinition>(StringComparer.OrdinalIgnoreCase);

        #region Configuration

        public sealed class Configuration
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Normal Removal", Order = 20)]
            public NormalSettings Normal = new NormalSettings();

            [JsonProperty("Access & Safety", Order = 30)]
            public AccessSettings Access = new AccessSettings();

            [JsonProperty("Admin Removal", Order = 40)]
            public AdminSettings Admin = new AdminSettings();

            [JsonProperty("Bulk Removal", Order = 45)]
            public BulkSettings Bulk = new BulkSettings();

            [JsonProperty("Removal Costs", Order = 50)]
            public CostSettings Costs = new CostSettings();

            [JsonProperty("Refunds", Order = 60)]
            public RefundSettings Refunds = new RefundSettings();

            [JsonProperty("Integrations", Order = 70)]
            public IntegrationSettings Integrations = new IntegrationSettings();

            [JsonProperty("User Interface", Order = 80)]
            public UiSettings Ui = new UiSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        public sealed class GeneralSettings
        {
            [JsonProperty("Command")]
            public string Command = "remove";

            [JsonProperty("Default Session Seconds")]
            public int DefaultSessionSeconds = 60;

            [JsonProperty("Maximum Session Seconds")]
            public int MaximumSessionSeconds = 300;

            [JsonProperty("Reset Session Timer After Successful Removal")]
            public bool ResetTimerOnRemove = false;

            [JsonProperty("Maximum Removals Per Session (0 = unlimited)")]
            public int MaximumRemovals = 50;

            [JsonProperty("Removal Distance")]
            public float Distance = 3f;

            [JsonProperty("Removal Interval Seconds")]
            public float RemovalInterval = 0.25f;

            [JsonProperty("Cooldown Seconds")]
            public float CooldownSeconds = 60f;
        }

        public sealed class NormalSettings
        {
            [JsonProperty("Allow Building Blocks")]
            public bool AllowBuildingBlocks = true;

            [JsonProperty("Allow Deployables")]
            public bool AllowDeployables = true;

            [JsonProperty("Allow Twig")]
            public bool AllowTwig = true;

            [JsonProperty("Allow Wood")]
            public bool AllowWood = true;

            [JsonProperty("Allow Stone")]
            public bool AllowStone = true;

            [JsonProperty("Allow Metal")]
            public bool AllowMetal = true;

            [JsonProperty("Allow Armored")]
            public bool AllowArmored = true;

            [JsonProperty("Drop Container Contents Before Removal")]
            public bool DropContainerContents = true;

            [JsonProperty("Use Gib Destruction")]
            public bool UseGibs = false;

            [JsonProperty("Allow Doors")]
            public bool AllowDoors = true;

            [JsonProperty("Allow Locked Entities")]
            public bool AllowLockedEntities = true;

            [JsonProperty("Allow Electrical And Industrial Entities")]
            public bool AllowIoEntities = true;

            [JsonProperty("Allow Vehicles")]
            public bool AllowVehicles = false;

            [JsonProperty("Remove Child Locks With Parent")]
            public bool RemoveChildLocks = true;

            [JsonProperty("Log Successful Removals")]
            public bool LogSuccessfulRemovals = true;

            [JsonProperty("Disable Removal On Death")]
            public bool DisableOnDeath = true;

            [JsonProperty("Disable Removal On Disconnect")]
            public bool DisableOnDisconnect = true;

            [JsonProperty("Log Lifecycle Diagnostics")]
            public bool LogDiagnostics = true;

            [JsonProperty("Drop Storage Contents Before Removal")]
            public bool DropStorageContents = true;

            [JsonProperty("Drop Vending Machine Contents Before Removal")]
            public bool DropVendingContents = true;

            [JsonProperty("Drop IO Container Contents Before Removal")]
            public bool DropIoContents = true;

            [JsonProperty("Protect Mounted Or Occupied Entities")]
            public bool ProtectOccupiedEntities = true;
        }

        public sealed class AccessSettings
        {
            [JsonProperty("Require Entity Ownership")]
            public bool RequireOwnership = true;

            [JsonProperty("Require Building Privilege")]
            public bool RequireBuildingPrivilege = true;

            [JsonProperty("Block Damaged Entities")]
            public bool BlockDamagedEntities = true;

            [JsonProperty("Minimum Health Percent")]
            public float MinimumHealthPercent = 100f;

            [JsonProperty("Entity Age Limit Seconds (0 = disabled)")]
            public float EntityAgeLimitSeconds = 0f;

            [JsonProperty("Block Non-Empty Storage")]
            public bool BlockNonEmptyStorage = true;
        }

        public sealed class AdminSettings
        {
            [JsonProperty("Allow Removal Of Damaged Entities")]
            public bool AllowDamagedEntities = true;

            [JsonProperty("Allow Any Supported Entity Type")]
            public bool AllowAnySupportedEntity = true;

            [JsonProperty("Allow Non-Empty Storage")]
            public bool AllowNonEmptyStorage = true;

        }

        public sealed class BulkSettings
        {
            [JsonProperty("Entities Per Batch")]
            public int EntitiesPerBatch = 20;

            [JsonProperty("Batch Interval Seconds")]
            public float BatchIntervalSeconds = 0.05f;

            [JsonProperty("Maximum Entities Per Operation (0 = unlimited)")]
            public int MaximumEntitiesPerOperation = 0;

            [JsonProperty("Structure Mode Includes Building Deployables")]
            public bool StructureIncludesDeployables = false;

            [JsonProperty("All Mode Includes Building Deployables")]
            public bool AllIncludesDeployables = true;

            [JsonProperty("External Mode Radius")]
            public float ExternalRadius = 12f;

            [JsonProperty("External Mode Include Connected External Walls")]
            public bool ExternalIncludeWalls = true;

            [JsonProperty("External Mode Include Connected External Gates")]
            public bool ExternalIncludeGates = true;

            [JsonProperty("Require Confirmation For Structure Mode")]
            public bool ConfirmStructure = true;

            [JsonProperty("Require Confirmation For All Mode")]
            public bool ConfirmAll = true;

            [JsonProperty("Require Confirmation For External Mode")]
            public bool ConfirmExternal = true;

            [JsonProperty("Confirmation Timeout Seconds")]
            public float ConfirmationTimeoutSeconds = 8f;

            [JsonProperty("Show Bulk Entity Count In HUD")]
            public bool ShowEntityCount = true;
        }

        public sealed class CostSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Building Cost Percent")]
            public float BuildingCostPercent = 0f;

            [JsonProperty("Deployable Cost Percent")]
            public float DeployableCostPercent = 0f;

            [JsonProperty("Entity Cost Percent Overrides")]
            public Dictionary<string, float> EntityPercentOverrides =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Excluded Entities")]
            public HashSet<string> ExcludedEntities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Show Cost Preview In HUD")]
            public bool ShowPreview = true;
        }

        public sealed class RefundSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Refund Building Blocks")]
            public bool RefundBuildingBlocks = true;

            [JsonProperty("Building Refund Percent")]
            public float BuildingRefundPercent = 100f;

            [JsonProperty("Refund Deployables")]
            public bool RefundDeployables = true;

            [JsonProperty("Deployable Refund Percent")]
            public float DeployableRefundPercent = 100f;

            [JsonProperty("Entity Refund Percent Overrides")]
            public Dictionary<string, float> EntityPercentOverrides =
                new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Excluded Entities")]
            public HashSet<string> ExcludedEntities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Give Refund Before Entity Is Destroyed")]
            public bool GiveBeforeDestroy = true;

            [JsonProperty("Show Refund Preview In HUD")]
            public bool ShowPreview = true;
        }

        public sealed class IntegrationSettings
        {
            [JsonProperty("Use Rust Teams")]
            public bool UseTeams = true;

            [JsonProperty("Use Friends Plugin When Installed")]
            public bool UseFriends = true;

            [JsonProperty("Use Clans Plugin When Installed")]
            public bool UseClans = true;

            [JsonProperty("Use BuildingOwners Plugin When Installed")]
            public bool UseBuildingOwners = false;

            [JsonProperty("Use NoEscape Raid Block When Installed")]
            public bool UseRaidBlock = true;

            [JsonProperty("Use NoEscape Combat Block When Installed")]
            public bool UseCombatBlock = true;

            [JsonProperty("Use Economics When Installed")]
            public bool UseEconomics = false;

            [JsonProperty("Use ServerRewards When Installed")]
            public bool UseServerRewards = false;
        }

        public sealed class UiSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Show Authorization Result")]
            public bool ShowAuthorization = true;

            [JsonProperty("Show Center Target Reticle")]
            public bool ShowReticle = true;

            [JsonProperty("Show Target Details Below Reticle")]
            public bool ShowReticleTarget = true;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>() ?? new Configuration();
                bool changed = EnsureConfiguration();
                if (!(_config.Version == CurrentVersion))
                {
                    _config.Version = CurrentVersion;
                    changed = true;
                }
                if (changed) SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError("Configuration is invalid; defaults have been loaded: " + ex.Message);
                _config = new Configuration();
                SaveConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private bool EnsureConfiguration()
        {
            bool changed = false;
            if (_config.Admin == null) { _config.Admin = new AdminSettings(); changed = true; }
            if (_config.Costs == null) { _config.Costs = new CostSettings(); changed = true; }

            if (_config.Integrations == null)
            {
                _config.Integrations = new IntegrationSettings();
                changed = true;
            }
            if (string.IsNullOrWhiteSpace(_config.General.Command))
            {
                _config.General.Command = "remove";
                changed = true;
            }
            _config.General.DefaultSessionSeconds = Mathf.Max(5, _config.General.DefaultSessionSeconds);
            _config.General.MaximumSessionSeconds =
                Mathf.Clamp(Mathf.Max(_config.General.DefaultSessionSeconds, _config.General.MaximumSessionSeconds), 1, 86400);
            _config.General.Distance = Mathf.Clamp(_config.General.Distance, 1f, 10f);
            _config.General.RemovalInterval = Mathf.Max(0.1f, _config.General.RemovalInterval);
            _config.General.CooldownSeconds = Mathf.Max(0f, _config.General.CooldownSeconds);
            _config.Access.MinimumHealthPercent = Mathf.Clamp(_config.Access.MinimumHealthPercent, 0f, 100f);
            _config.General.DefaultSessionSeconds = Mathf.Clamp(_config.General.DefaultSessionSeconds, 1, 3600);
            _config.General.MaximumSessionSeconds = Mathf.Max(_config.General.DefaultSessionSeconds, _config.General.MaximumSessionSeconds);
            _config.General.MaximumRemovals = Mathf.Max(0, _config.General.MaximumRemovals);
            _config.General.Distance = Mathf.Clamp(_config.General.Distance, 1f, 10f);
            _config.General.RemovalInterval = Mathf.Clamp(_config.General.RemovalInterval, 0.05f, 5f);
            _config.General.CooldownSeconds = Mathf.Max(0f, _config.General.CooldownSeconds);

            _config.Access.MinimumHealthPercent = Mathf.Clamp(_config.Access.MinimumHealthPercent, 0f, 100f);
            _config.Access.EntityAgeLimitSeconds = Mathf.Max(0f, _config.Access.EntityAgeLimitSeconds);

            _config.Bulk.EntitiesPerBatch = Mathf.Clamp(_config.Bulk.EntitiesPerBatch, 1, 200);
            _config.Bulk.BatchIntervalSeconds = Mathf.Clamp(_config.Bulk.BatchIntervalSeconds, 0.01f, 2f);
            _config.Bulk.MaximumEntitiesPerOperation = Mathf.Max(0, _config.Bulk.MaximumEntitiesPerOperation);
            _config.Bulk.ExternalRadius = Mathf.Clamp(_config.Bulk.ExternalRadius, 2f, 50f);
            _config.Bulk.ConfirmationTimeoutSeconds =
                Mathf.Clamp(_config.Bulk.ConfirmationTimeoutSeconds, 2f, 30f);

            if (_config.Costs.EntityPercentOverrides == null)
            {
                _config.Costs.EntityPercentOverrides =
                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            if (_config.Costs.ExcludedEntities == null)
            {
                _config.Costs.ExcludedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            if (_config.Refunds.EntityPercentOverrides == null)
            {
                _config.Refunds.EntityPercentOverrides =
                    new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            if (_config.Refunds.ExcludedEntities == null)
            {
                _config.Refunds.ExcludedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }

            _config.Costs.BuildingCostPercent = Mathf.Clamp(_config.Costs.BuildingCostPercent, 0f, 100f);
            _config.Costs.DeployableCostPercent = Mathf.Clamp(_config.Costs.DeployableCostPercent, 0f, 100f);
            foreach (string key in _config.Costs.EntityPercentOverrides.Keys.ToList())
                _config.Costs.EntityPercentOverrides[key] =
                    Mathf.Clamp(_config.Costs.EntityPercentOverrides[key], 0f, 100f);
            _config.Refunds.BuildingRefundPercent = Mathf.Clamp(_config.Refunds.BuildingRefundPercent, 0f, 100f);
            _config.Refunds.DeployableRefundPercent = Mathf.Clamp(_config.Refunds.DeployableRefundPercent, 0f, 100f);
            foreach (string key in _config.Refunds.EntityPercentOverrides.Keys.ToList())
                _config.Refunds.EntityPercentOverrides[key] =
                    Mathf.Clamp(_config.Refunds.EntityPercentOverrides[key], 0f, 100f);
            _config.Bulk.EntitiesPerBatch = Mathf.Clamp(_config.Bulk.EntitiesPerBatch, 1, 250);
            _config.Bulk.BatchIntervalSeconds = Mathf.Clamp(_config.Bulk.BatchIntervalSeconds, 0.01f, 2f);
            return changed;
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            LogInformation("Lifecycle", Name + " v" + CurrentVersion + " initialized using RogueRust services.");
        }

        private void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyRemovalUi(player);

            BuildEntityCatalog();
            if (_config.Access.EntityAgeLimitSeconds > 0f)
            {
                foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
                {
                    BaseEntity entity = networkable as BaseEntity;
                    if (entity?.net != null)
                        _spawnedAt[entity.net.ID.Value] = UnixTime;
                }
            }

            LogInformation("Lifecycle",
                "Entity catalog ready. Constructions=" + _constructionByPrefab.Count +
                ", deployables=" + _deployablePrefabs.Count + ".");
            LogIntegrationStatus();
        }

        private void Unload()
        {
            if (_config != null && _config.Normal.LogDiagnostics)
                LogInformation("Lifecycle",
                    "Unloading with sessions=" + _sessions.Count +
                    ", confirmations=" + _bulkConfirmations.Count +
                    ", uiStates=" + _uiState.Count +
                    ", healthy=" + API_IsHealthy() + ".");

            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyRemovalUi(player);

            _bulkConfirmations.Clear();
            _uiState.Clear();
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                DestroyRemovalUi(player);

            _sessions.Clear();
            _spawnedAt.Clear();
        }

        private void OnEntitySpawned(BaseNetworkable networkable)
        {
            if (_config.Access.EntityAgeLimitSeconds <= 0f) return;
            BaseEntity entity = networkable as BaseEntity;
            if (entity?.net != null)
                _spawnedAt[entity.net.ID.Value] = UnixTime;
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            BaseEntity entity = networkable as BaseEntity;
            if (entity?.net != null)
                _spawnedAt.Remove(entity.net.ID.Value);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            DisableSession(player, false);
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            DisableSession(player, false);
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (IsIntegrationPlugin(plugin)) LogIntegrationStatus();
        }

        private void OnPluginUnloaded(Plugin plugin)
        {
            if (IsIntegrationPlugin(plugin)) LogIntegrationStatus();
        }

        private void OnRaidBlock(BasePlayer player)
        {
            if (player != null && _config.Integrations.UseRaidBlock && PluginReady(NoEscape))
                DisableSession(player, false);
        }

        private void OnCombatBlock(BasePlayer player)
        {
            if (player != null && _config.Integrations.UseCombatBlock && PluginReady(NoEscape))
                DisableSession(player, false);
        }

        #endregion

        #region Commands

        [RogueCommand(
            "remove",
            Aliases = new[] { "remover" },
            Description = "Enables or disables the RogueRust removal tool.",
            Usage = "remove [seconds|off|admin|structure|all|external]",
            Category = "Removal",
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult RemoveCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail("This command can only be used by a player.");

            args = args ?? Array.Empty<string>();
            if (args.Length == 0)
            {
                if (_sessions.ContainsKey(player.userID))
                {
                    DisableSession(player, true);
                    return RogueCommandResult.Ok();
                }
                return EnableSession(player, RemovalMode.Normal, _config.General.DefaultSessionSeconds);
            }

            string action = args[0].ToLowerInvariant();
            if (action == "off" || action == "disable" || action == "d")
            {
                DisableSession(player, true);
                return RogueCommandResult.Ok();
            }

            RemovalMode mode = RemovalMode.Normal;
            int seconds = _config.General.DefaultSessionSeconds;

            if (action == "admin" || action == "a") mode = RemovalMode.Admin;
            else if (action == "structure" || action == "s") mode = RemovalMode.Structure;
            else if (action == "all") mode = RemovalMode.All;
            else if (action == "external" || action == "e") mode = RemovalMode.External;
            else if (!int.TryParse(action, out seconds))
                return RogueCommandResult.Fail("Usage: /remove [seconds|off|admin|structure|all|external]");

            if (args.Length > 1)
                int.TryParse(args[1], out seconds);

            return EnableSession(player, mode, seconds);
        }

        private RogueCommandResult EnableSession(BasePlayer player, RemovalMode mode, int seconds)
        {
            string permissionName = PermissionFor(mode);
            if (!HasPermission(player, permissionName) && !HasPermission(player, PermOverride))
                return RogueCommandResult.Fail(Message("Error.NoPermission", player));

            seconds = Mathf.Clamp(seconds <= 0 ? _config.General.DefaultSessionSeconds : seconds,
                5, _config.General.MaximumSessionSeconds);

            _bulkConfirmations.Remove(player.userID);
            _sessions[player.userID] = new RemovalSession
            {
                Mode = mode,
                ExpiresAt = UnixTime + seconds,
                DurationSeconds = seconds,
                LastRemovalAt = 0,
                Removed = 0
            };

            ScheduleSessionExpiry(player.userID, seconds);
            Subscribe(nameof(OnPlayerInput));
            ShowSessionUi(player);
            Interface.CallHook("OnRogueRustRemovalToolActivated", player, mode.ToString());
            player.ChatMessage("RogueRust Removal Tool enabled (" + mode + ") for " + seconds + " seconds.");
            return RogueCommandResult.Ok();
        }

        #endregion

        #region Input / Sessions

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            RemovalSession session;
            if (!_sessions.TryGetValue(player.userID, out session)) return;

            if (!player.IsConnected || UnixTime >= session.ExpiresAt)
            {
                DisableSession(player, true);
                return;
            }

            if (_config.Ui.Enabled)
                Throttle("ui:" + player.userID, TimeSpan.FromMilliseconds(100), () => ShowSessionUi(player));

            if (!input.WasJustPressed(BUTTON.FIRE_PRIMARY)) return;

            double now = UnixTime;
            if (now - session.LastRemovalAt < _config.General.RemovalInterval) return;
            session.LastRemovalAt = now;

            if (session.Mode == RemovalMode.Normal &&
                _config.General.MaximumRemovals > 0 &&
                session.Removed >= _config.General.MaximumRemovals)
            {
                player.ChatMessage("You have reached the removal limit for this session.");
                DisableSession(player, false);
                return;
            }

            BaseEntity target = FindTarget(player, _config.General.Distance);
            if (target == null)
                return;

            if (session.Mode == RemovalMode.Structure ||
                session.Mode == RemovalMode.All ||
                session.Mode == RemovalMode.External)
            {
                BeginBulkRemoval(player, target, session.Mode);
                return;
            }

            string reason;
            if (!TryRemove(player, target, session.Mode, out reason))
                return;

            session.Removed++;
            if (_config.General.ResetTimerOnRemove)
            {
                session.ExpiresAt = UnixTime + session.DurationSeconds;
                ScheduleSessionExpiry(player.userID, session.DurationSeconds);
            }

            ShowSessionUi(player);
        }

        private void ScheduleSessionExpiry(ulong playerId, int seconds)
        {
            timer.Once(seconds + 0.1f, () =>
            {
                RemovalSession session;
                if (!_sessions.TryGetValue(playerId, out session))
                    return;

                // Ignore an older timer if a successful removal extended the session.
                if (UnixTime + 0.05d < session.ExpiresAt)
                    return;

                BasePlayer player = BasePlayer.FindByID(playerId);
                if (player != null)
                {
                    DisableSession(player, true);
                    return;
                }

                _sessions.Remove(playerId);
                if (_sessions.Count == 0)
                    Unsubscribe(nameof(OnPlayerInput));
            });
        }

        private void DisableSession(BasePlayer player, bool message)
        {
            if (player == null) return;
            if (!_sessions.Remove(player.userID)) return;

            DestroyRemovalUi(player);
            Interface.CallHook("OnRogueRustRemovalToolDeactivated", player);

            if (message && player.IsConnected)
                player.ChatMessage("RogueRust Removal Tool disabled.");

            if (_sessions.Count == 0)
                Unsubscribe(nameof(OnPlayerInput));
        }

        #endregion

        #region Removal Engine

        private bool TryRemove(BasePlayer player, BaseEntity entity, RemovalMode mode, out string reason)
        {
            reason = string.Empty;

            if (entity == null || entity.IsDestroyed)
            {
                reason = "That entity is no longer valid.";
                return false;
            }

            object hookResult = Interface.CallHook("CanRogueRustRemove", player, entity, mode.ToString());
            if (hookResult != null)
            {
                reason = hookResult as string ?? "Removal was blocked by another plugin.";
                return false;
            }

            if (mode == RemovalMode.Normal)
            {
                if (!CanRemoveNormal(player, entity, out reason))
                    return false;
            }
            else if (mode == RemovalMode.Admin)
            {
                if (!CanRemoveAdmin(player, entity, out reason))
                    return false;
            }

            List<CostEntry> cost = mode == RemovalMode.Normal ? GetRemovalCost(entity) : null;
            if (mode == RemovalMode.Normal && !CanAfford(player, cost, out reason)) return false;
            if (mode == RemovalMode.Normal) TakeCost(player, cost);

            List<RefundEntry> refund = mode == RemovalMode.Normal ? GetRefund(entity) : null;
            if (_config.Refunds.GiveBeforeDestroy)
                GiveRefund(player, refund);

            DropContentsIfConfigured(entity);
            if (mode == RemovalMode.Normal || mode == RemovalMode.Admin)
                RemoveChildLockIfConfigured(entity);

            BaseNetworkable.DestroyMode destroyMode =
                _config.Normal.UseGibs ? BaseNetworkable.DestroyMode.Gib : BaseNetworkable.DestroyMode.None;

            using (Measure("Removal", mode.ToString()))
                entity.Kill(destroyMode);

            if (!_config.Refunds.GiveBeforeDestroy)
                GiveRefund(player, refund);

            if (_config.Normal.LogSuccessfulRemovals)
                LogInformation("RemovalAudit",
                    player.UserIDString + " removed " +
                    (entity.ShortPrefabName ?? entity.PrefabName ?? "unknown") +
                    " using mode=" + mode + ", owner=" + entity.OwnerID + ".");

            Interface.CallHook("OnRogueRustEntityRemoved", player, entity, mode.ToString());
            if (mode == RemovalMode.Normal)
                Interface.CallHook("OnRogueRustRemovalTransaction",
                    player, entity, cost, refund);
            return true;
        }

        private bool CanRemoveAdmin(BasePlayer player, BaseEntity entity, out string reason)
        {
            reason = string.Empty;

            if (player == null || !HasPermission(player, PermAdmin))
            {
                reason = "You do not have permission to use admin removal.";
                return false;
            }

            // Admin mode deliberately does NOT use the normal-player damaged-entity,
            // ownership, TC, age, storage, NoEscape, grade or payment restrictions.
            // This allows abandoned/raided/damaged bases to be cleaned up safely by staff.
            if (!_config.Admin.AllowAnySupportedEntity && !IsSupportedEntity(entity))
            {
                reason = "That entity type is disabled for admin removal.";
                return false;
            }

            if (!_config.Admin.AllowDamagedEntities)
            {
                BaseCombatEntity combat = entity as BaseCombatEntity;
                if (combat != null && combat.MaxHealth() > 0f &&
                    combat.health + 0.01f < combat.MaxHealth())
                {
                    reason = "Damaged entities are disabled for admin removal.";
                    return false;
                }
            }

            if (!_config.Admin.AllowNonEmptyStorage)
            {
                StorageContainer storage = entity as StorageContainer;
                if (storage?.inventory?.itemList != null && storage.inventory.itemList.Count > 0)
                {
                    reason = "Non-empty storage is disabled for admin removal.";
                    return false;
                }
            }

            return true;
        }

        private bool CanRemoveNormal(BasePlayer player, BaseEntity entity, out string reason)
        {
            if (IsNoEscapeBlocked(player, out reason))
                return false;

            if (!IsSupportedEntity(entity))
            {
                reason = "That entity type cannot be removed.";
                return false;
            }

            if (!_config.Normal.AllowDoors && entity is Door)
            {
                reason = "Door removal is disabled.";
                return false;
            }

            if (!_config.Normal.AllowVehicles && IsVehicleEntity(entity))
            {
                reason = "Vehicle removal is disabled.";
                return false;
            }

            if (!_config.Normal.AllowIoEntities && entity is IOEntity)
            {
                reason = "Electrical and industrial entity removal is disabled.";
                return false;
            }

            if (!_config.Normal.AllowLockedEntities && HasEntityLock(entity))
            {
                reason = "Locked entity removal is disabled.";
                return false;
            }

            if (_config.Normal.ProtectOccupiedEntities && IsOccupiedEntity(entity))
            {
                reason = "That entity is currently occupied.";
                return false;
            }

            BuildingBlock block = entity as BuildingBlock;
            if (block != null && !IsGradeAllowed(block.grade))
            {
                reason = "That building grade cannot be removed.";
                return false;
            }

            if (_config.Access.BlockDamagedEntities)
            {
                BaseCombatEntity combat = entity as BaseCombatEntity;
                if (combat != null && combat.MaxHealth() > 0f)
                {
                    float percent = combat.health / combat.MaxHealth() * 100f;
                    if (percent + 0.01f < _config.Access.MinimumHealthPercent)
                    {
                        reason = "Damaged entities cannot be removed.";
                        return false;
                    }
                }
            }

            if (_config.Access.BlockNonEmptyStorage)
            {
                StorageContainer storage = entity as StorageContainer;
                if (storage?.inventory?.itemList != null && storage.inventory.itemList.Count > 0)
                {
                    reason = "Empty this container before removing it.";
                    return false;
                }
            }

            if (_config.Access.EntityAgeLimitSeconds > 0f && entity.net != null)
            {
                double spawned;
                if (_spawnedAt.TryGetValue(entity.net.ID.Value, out spawned) &&
                    UnixTime - spawned > _config.Access.EntityAgeLimitSeconds)
                {
                    reason = "This entity is too old to remove.";
                    return false;
                }
            }

            if (!HasPermission(player, PermOverride))
            {
                if (_config.Access.RequireOwnership &&
                    entity.OwnerID != 0 &&
                    !HasRemovalRelationship(player, entity))
                {
                    reason = "You do not own or share access to this entity.";
                    return false;
                }

                if (_config.Access.RequireBuildingPrivilege &&
                    player.IsBuildingBlocked(entity.WorldSpaceBounds()))
                {
                    reason = "You need building privilege to remove this entity.";
                    return false;
                }
            }

            object legacyHook = Interface.CallHook("canRemove", player, entity);
            if (legacyHook != null)
            {
                reason = legacyHook as string ?? "Removal was blocked by another plugin.";
                return false;
            }

            reason = "Removal allowed.";
            return true;
        }

        private bool IsSupportedEntity(BaseEntity entity)
        {
            if (entity is BuildingBlock)
                return _config.Normal.AllowBuildingBlocks;

            if (!_config.Normal.AllowDeployables)
                return false;

            return _deployablePrefabs.Contains(entity.ShortPrefabName) ||
                   _deployablePrefabs.Contains(entity.PrefabName);
        }

        private bool IsGradeAllowed(BuildingGrade.Enum grade)
        {
            switch (grade)
            {
                case BuildingGrade.Enum.Twigs: return _config.Normal.AllowTwig;
                case BuildingGrade.Enum.Wood: return _config.Normal.AllowWood;
                case BuildingGrade.Enum.Stone: return _config.Normal.AllowStone;
                case BuildingGrade.Enum.Metal: return _config.Normal.AllowMetal;
                case BuildingGrade.Enum.TopTier: return _config.Normal.AllowArmored;
                default: return false;
            }
        }

        private void DropContentsIfConfigured(BaseEntity entity)
        {
            if (entity == null) return;

            StorageContainer storage = entity as StorageContainer;
            if (storage == null || storage.inventory == null || storage.inventory.itemList == null ||
                storage.inventory.itemList.Count == 0)
                return;

            bool shouldDrop = _config.Normal.DropStorageContents;

            VendingMachine vending = entity as VendingMachine;
            if (vending != null)
                shouldDrop = _config.Normal.DropVendingContents;

            IOEntity io = entity as IOEntity;
            if (io != null)
                shouldDrop = _config.Normal.DropIoContents;

            if (!shouldDrop) return;

            ItemContainer.Drop("assets/prefabs/misc/item drop/item_drop.prefab",
                entity.transform.position + Vector3.up * 0.5f,
                Quaternion.identity,
                storage.inventory);
        }

        private static bool MatchesEntityRule(BaseEntity entity, ICollection<string> rules)
        {
            if (entity == null || rules == null || rules.Count == 0) return false;
            return rules.Contains(entity.PrefabName) || rules.Contains(entity.ShortPrefabName);
        }

        private static bool TryGetEntityPercent(BaseEntity entity, Dictionary<string, float> overrides, out float percent)
        {
            percent = 0f;
            if (entity == null || overrides == null) return false;
            return overrides.TryGetValue(entity.PrefabName, out percent) ||
                   overrides.TryGetValue(entity.ShortPrefabName, out percent);
        }

        private List<CostEntry> GetRemovalCost(BaseEntity entity)
        {
            var result = new List<CostEntry>();
            if (!_config.Costs.Enabled || entity == null) return result;
            if (MatchesEntityRule(entity, _config.Costs.ExcludedEntities)) return result;

            float percent;
            if (!TryGetEntityPercent(entity, _config.Costs.EntityPercentOverrides, out percent))
                percent = entity is BuildingBlock
                    ? _config.Costs.BuildingCostPercent
                    : _config.Costs.DeployableCostPercent;
            percent = Mathf.Clamp(percent, 0f, 100f);
            if (percent <= 0f) return result;

            BuildingBlock block = entity as BuildingBlock;
            if (block != null)
            {
                var grade = block.blockDefinition?.GetGrade(block.grade, block.skinID);
                if (grade != null)
                    foreach (ItemAmount a in grade.CostToBuild()) AddCost(result, a.itemDef, a.amount, percent);
                return result;
            }

            ItemDefinition def;
            if (!_deployableItems.TryGetValue(entity.PrefabName, out def))
                _deployableItems.TryGetValue(entity.ShortPrefabName, out def);
            if (def != null) AddCost(result, def, 1f, percent);
            return result;
        }

        private static void AddCost(List<CostEntry> list, ItemDefinition def, float amount, float percent)
        {
            if (def == null) return;
            int qty = Mathf.CeilToInt(amount * percent / 100f);
            if (qty > 0) list.Add(new CostEntry { Definition = def, Amount = qty });
        }

        private static bool CanAfford(BasePlayer player, List<CostEntry> cost, out string reason)
        {
            reason = null;
            if (cost == null) return true;
            foreach (CostEntry e in cost)
            {
                int available = player.inventory.GetAmount(e.Definition.itemid);
                if (available < e.Amount)
                {
                    reason = "Requires " + e.Amount + "x " + (e.Definition.displayName?.english ?? e.Definition.shortname) +
                             " (" + available + " available).";
                    return false;
                }
            }
            return true;
        }

        private static void TakeCost(BasePlayer player, List<CostEntry> cost)
        {
            if (cost == null) return;
            foreach (CostEntry e in cost) player.inventory.Take(null, e.Definition.itemid, e.Amount);
        }

        private string FormatCost(BaseEntity entity)
        {
            var cost = GetRemovalCost(entity);
            return cost.Count == 0 ? string.Empty : string.Join("  •  ", cost.Take(3).Select(e =>
                e.Amount + "x " + (e.Definition.displayName?.english ?? e.Definition.shortname)));
        }

        private List<RefundEntry> GetRefund(BaseEntity entity)
        {
            var result = new List<RefundEntry>();
            if (!_config.Refunds.Enabled || entity == null) return result;
            if (MatchesEntityRule(entity, _config.Refunds.ExcludedEntities)) return result;

            float refundPercent;
            bool hasOverride = TryGetEntityPercent(entity, _config.Refunds.EntityPercentOverrides, out refundPercent);

            BuildingBlock block = entity as BuildingBlock;
            if (block != null && _config.Refunds.RefundBuildingBlocks)
            {
                if (block.blockDefinition != null && block.currentGrade != null)
                {
                    var grade = block.blockDefinition.GetGrade(block.grade, block.skinID);
                    if (grade != null)
                    {
                        foreach (ItemAmount cost in grade.CostToBuild())
                            AddRefund(result, cost.itemDef, cost.amount,
                                hasOverride ? refundPercent : _config.Refunds.BuildingRefundPercent);
                    }
                }
                return result;
            }

            if (_config.Refunds.RefundDeployables)
            {
                ItemDefinition definition;
                if (!_deployableItems.TryGetValue(entity.PrefabName, out definition))
                    _deployableItems.TryGetValue(entity.ShortPrefabName, out definition);

                if (definition != null)
                    AddRefund(result, definition, 1f,
                        hasOverride ? refundPercent : _config.Refunds.DeployableRefundPercent);
            }
            return result;
        }

        private static void AddRefund(List<RefundEntry> result, ItemDefinition definition, float amount, float percent)
        {
            if (definition == null || amount <= 0f || percent <= 0f) return;
            int quantity = Mathf.FloorToInt(amount * (percent / 100f));
            if (quantity <= 0) return;
            result.Add(new RefundEntry { Definition = definition, Amount = quantity });
        }

        private void GiveRefund(BasePlayer player, List<RefundEntry> refund)
        {
            if (player == null || refund == null || refund.Count == 0) return;

            foreach (RefundEntry entry in refund)
            {
                Item item = ItemManager.Create(entry.Definition, entry.Amount);
                if (item == null) continue;
                player.GiveItem(item);
            }
        }

        private string FormatRefund(BaseEntity entity)
        {
            List<RefundEntry> refund = GetRefund(entity);
            if (refund.Count == 0) return string.Empty;

            return string.Join("  •  ", refund.Take(3).Select(x =>
                x.Amount + "x " + (x.Definition.displayName?.english ?? x.Definition.shortname)));
        }

        #endregion

        #region Bulk Removal

        private void BeginBulkRemoval(BasePlayer player, BaseEntity origin, RemovalMode mode)
        {
            if (player == null || origin == null) return;

            if (mode == RemovalMode.Structure && !HasPermission(player, PermStructure) && !HasPermission(player, PermOverride))
            {
                player.ChatMessage(Message("Error.NoPermission", player));
                return;
            }

            if (mode == RemovalMode.All && !HasPermission(player, PermAll) && !HasPermission(player, PermOverride))
            {
                player.ChatMessage(Message("Error.NoPermission", player));
                return;
            }

            if (mode == RemovalMode.External && !HasPermission(player, PermExternal) && !HasPermission(player, PermOverride))
            {
                player.ChatMessage(Message("Error.NoPermission", player));
                return;
            }

            List<BaseEntity> entities = CollectBulkEntities(origin, mode);
            if (_config.Bulk.MaximumEntitiesPerOperation > 0 &&
                entities.Count > _config.Bulk.MaximumEntitiesPerOperation)
                entities = entities.Take(_config.Bulk.MaximumEntitiesPerOperation).ToList();

            if (entities.Count == 0)
            {
                player.ChatMessage(Message("Bulk.NoneFound", player));
                return;
            }

            bool requiresConfirmation =
                (mode == RemovalMode.Structure && _config.Bulk.ConfirmStructure) ||
                (mode == RemovalMode.All && _config.Bulk.ConfirmAll) ||
                (mode == RemovalMode.External && _config.Bulk.ConfirmExternal);

            if (requiresConfirmation)
            {
                BulkConfirmation pending;
                double now = UnixTime;
                ulong targetId = origin.net != null ? origin.net.ID.Value : 0UL;

                if (!_bulkConfirmations.TryGetValue(player.userID, out pending) ||
                    pending.Mode != mode ||
                    pending.TargetId != targetId ||
                    pending.ExpiresAt < now)
                {
                    _bulkConfirmations[player.userID] = new BulkConfirmation
                    {
                        Mode = mode,
                        TargetId = targetId,
                        Count = entities.Count,
                        ExpiresAt = now + _config.Bulk.ConfirmationTimeoutSeconds
                    };
                    player.ChatMessage(mode.ToString().ToUpperInvariant() + " preview: " +
                        entities.Count + " entities. Click the same target again within " +
                        Mathf.CeilToInt(_config.Bulk.ConfirmationTimeoutSeconds) + "s to confirm.");
                    ShowSessionUi(player);
                    return;
                }

                _bulkConfirmations.Remove(player.userID);
            }

            string key = "bulk:" + player.userID;
            int removed = 0;
            int skippedOccupied = 0;
            int requested = entities.Count;
            player.ChatMessage(string.Format(Message("Bulk.Queued", player), requested, mode));
            LogInformation("BulkRemoval",
                player.UserIDString + " queued " + mode + " removal; target=" +
                (origin.ShortPrefabName ?? "unknown") + ", entities=" + requested + ".");

            Workloads.Batch(
                Name,
                key,
                requested,
                _config.Bulk.EntitiesPerBatch,
                TimeSpan.FromSeconds(_config.Bulk.BatchIntervalSeconds),
                index =>
                {
                    if (player == null || !player.IsConnected) return false;
                    if (index < 0 || index >= entities.Count) return false;

                    BaseEntity entity = entities[index];
                    if (entity == null || entity.IsDestroyed) return true;

                    // Bulk cleanup never removes an entity currently occupied by a player.
                    if (IsOccupiedEntity(entity))
                    {
                        skippedOccupied++;
                        return true;
                    }

                    string ignored;
                    if (TryRemove(player, entity, RemovalMode.Admin, out ignored))
                        removed++;
                    return true;
                },
                () =>
                {
                    if (player != null && player.IsConnected)
                        player.ChatMessage(mode + " removal completed: " + removed + "/" +
                            requested + " entities removed" +
                            (skippedOccupied > 0 ? " (" + skippedOccupied + " occupied skipped)." : "."));

                    LogInformation("BulkRemoval",
                        (player != null ? player.UserIDString : "disconnected") +
                        " completed " + mode + " removal; requested=" + requested +
                        ", removed=" + removed + ", occupiedSkipped=" + skippedOccupied + ".");

                    Interface.CallHook("OnRogueRustBulkRemovalCompleted",
                        player, mode.ToString(), requested, removed);
                    Interface.CallHook("OnRogueRustBulkRemovalDetailed",
                        player, mode.ToString(), requested, removed, skippedOccupied);
                });
        }

        private List<BaseEntity> CollectBulkEntities(BaseEntity origin, RemovalMode mode)
        {
            if (origin == null) return new List<BaseEntity>();

            if (mode == RemovalMode.External)
                return CollectExternalEntities(origin);

            var result = new HashSet<BaseEntity>();
            DecayEntity decay = origin as DecayEntity;
            BuildingManager.Building building = decay?.GetBuilding();

            if (building == null)
            {
                result.Add(origin);
                return result.ToList();
            }

            foreach (DecayEntity entity in building.decayEntities)
            {
                if (entity == null || entity.IsDestroyed) continue;

                if (mode == RemovalMode.Structure)
                {
                    if (entity is BuildingBlock || _config.Bulk.StructureIncludesDeployables)
                        result.Add(entity);
                    continue;
                }

                if (mode == RemovalMode.All)
                {
                    if (entity is BuildingBlock || _config.Bulk.AllIncludesDeployables)
                        result.Add(entity);
                }
            }

            return result.ToList();
        }

        private List<BaseEntity> CollectExternalEntities(BaseEntity origin)
        {
            var result = new HashSet<BaseEntity>();
            float radius = Mathf.Max(2f, _config.Bulk.ExternalRadius);

            List<BaseEntity> nearby = Pool.Get<List<BaseEntity>>();
            try
            {
                Vis.Entities(origin.transform.position, radius, nearby, TargetLayerMask);
                foreach (BaseEntity entity in nearby)
                {
                    if (entity == null || entity.IsDestroyed) continue;
                    string prefab = entity.ShortPrefabName ?? string.Empty;

                    bool wall = prefab.IndexOf("external", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                prefab.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool gate = prefab.IndexOf("external", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                prefab.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0;

                    if ((wall && _config.Bulk.ExternalIncludeWalls) ||
                        (gate && _config.Bulk.ExternalIncludeGates))
                        result.Add(entity);
                }
            }
            finally
            {
                Pool.FreeUnmanaged(ref nearby);
            }

            if (result.Count == 0 && IsExternalEntity(origin))
                result.Add(origin);

            return result.ToList();
        }

        private static bool IsExternalEntity(BaseEntity entity)
        {
            if (entity == null) return false;
            string prefab = entity.ShortPrefabName ?? string.Empty;
            return prefab.IndexOf("external", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (prefab.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf("gate", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        #endregion

        #region Targeting / Catalog

        private BaseEntity FindTarget(BasePlayer player, float distance)
        {
            List<RaycastHit> hits = Pool.Get<List<RaycastHit>>();
            try
            {
                GamePhysics.TraceAll(player.eyes.HeadRay(), 0f, hits, distance, TargetLayerMask);
                BaseEntity first = null;
                foreach (RaycastHit hit in hits)
                {
                    BaseEntity entity = hit.GetEntity();
                    if (entity == null || entity == player) continue;
                    if (first == null) first = entity;
                    if (entity.GetParentEntity() == first) return entity;
                }
                return first;
            }
            finally
            {
                Pool.FreeUnmanaged(ref hits);
            }
        }

        private void BuildEntityCatalog()
        {
            _constructionByPrefab.Clear();
            _deployablePrefabs.Clear();
            _deployableItems.Clear();

            foreach (ItemDefinition definition in ItemManager.GetItemDefinitions())
            {
                string path = definition.GetComponent<ItemModDeployable>()?.entityPrefab?.resourcePath;
                if (string.IsNullOrWhiteSpace(path)) continue;

                _deployablePrefabs.Add(path);
                _deployableItems[path] = definition;
                string shortName = Utility.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    _deployablePrefabs.Add(shortName);
                    _deployableItems[shortName] = definition;
                }
            }

            foreach (var entry in PrefabAttribute.server.prefabs)
            {
                Construction construction = entry.Value.Find<Construction>().FirstOrDefault();
                if (construction == null || construction.deployable != null) continue;
                if (string.IsNullOrWhiteSpace(construction.fullName)) continue;

                string displayName = construction.info.name.english;
                _constructionByPrefab[construction.fullName] = new ConstructionInfo
                {
                    Prefab = construction.fullName,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? construction.fullName : displayName
                };
            }
        }

        #endregion

        #region Entity Safety

        private static bool HasEntityLock(BaseEntity entity)
        {
            if (entity == null) return false;
            BaseLock entityLock = entity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            return entityLock != null && !entityLock.IsDestroyed;
        }

        private static bool IsVehicleEntity(BaseEntity entity)
        {
            if (entity == null) return false;
            if (entity is BaseVehicle) return true;

            string prefab = entity.ShortPrefabName ?? string.Empty;
            return entity is BaseMountable &&
                   (prefab.IndexOf("car", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf("boat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf("heli", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf("horse", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsOccupiedEntity(BaseEntity entity)
        {
            BaseMountable mountable = entity as BaseMountable;
            if (mountable != null && mountable.GetMounted() != null)
                return true;

            BaseVehicle vehicle = entity as BaseVehicle;
            if (vehicle != null)
            {
                BaseMountable[] mounts = vehicle.GetComponentsInChildren<BaseMountable>();
                if (mounts != null)
                {
                    foreach (BaseMountable mount in mounts)
                        if (mount != null && mount.GetMounted() != null)
                            return true;
                }
            }

            return false;
        }

        private void RemoveChildLockIfConfigured(BaseEntity entity)
        {
            if (!_config.Normal.RemoveChildLocks || entity == null) return;
            BaseLock entityLock = entity.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            if (entityLock != null && !entityLock.IsDestroyed)
                entityLock.Kill(BaseNetworkable.DestroyMode.None);
        }

        #endregion

        #region Integrations

        private static bool IsIntegrationPlugin(Plugin plugin)
        {
            if (plugin == null) return false;
            return plugin.Name == "Friends" || plugin.Name == "Clans" ||
                   plugin.Name == "BuildingOwners" || plugin.Name == "NoEscape" ||
                   plugin.Name == "Economics" || plugin.Name == "ServerRewards";
        }

        private void LogIntegrationStatus()
        {
            LogInformation("Integrations",
                "Teams=" + (_config.Integrations.UseTeams ? "enabled" : "disabled") +
                ", Friends=" + IntegrationState(Friends, _config.Integrations.UseFriends) +
                ", Clans=" + IntegrationState(Clans, _config.Integrations.UseClans) +
                ", BuildingOwners=" + IntegrationState(BuildingOwners, _config.Integrations.UseBuildingOwners) +
                ", NoEscape=" + IntegrationState(NoEscape, _config.Integrations.UseRaidBlock || _config.Integrations.UseCombatBlock) + ".");
        }

        private static string IntegrationState(Plugin plugin, bool enabled)
        {
            return !enabled ? "disabled" : (plugin == null ? "not installed" : "active");
        }

        private bool HasRemovalRelationship(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return false;
            ulong ownerId = entity.OwnerID;

            if (_config.Integrations.UseBuildingOwners && PluginReady(BuildingOwners) && entity is BuildingBlock)
            {
                object result = BuildingOwners.Call("FindBlockData", entity);
                ulong buildingOwner;
                if (result != null && ulong.TryParse(result.ToString(), out buildingOwner) && buildingOwner != 0)
                    ownerId = buildingOwner;
            }

            if (ownerId == 0 || ownerId == player.userID) return true;
            if (_config.Integrations.UseTeams && SameTeam(player.userID, ownerId)) return true;

            if (_config.Integrations.UseFriends && PluginReady(Friends))
            {
                object result = Friends.Call("HasFriend", player.userID, ownerId);
                if (result is bool && (bool)result) return true;
            }

            return _config.Integrations.UseClans && PluginReady(Clans) && SameClan(player.userID, ownerId);
        }

        private static bool SameTeam(ulong first, ulong second)
        {
            if (!RelationshipManager.TeamsEnabled()) return false;
            var firstTeam = RelationshipManager.ServerInstance.FindPlayersTeam(first);
            if (firstTeam == null) return false;
            var secondTeam = RelationshipManager.ServerInstance.FindPlayersTeam(second);
            return secondTeam != null && firstTeam == secondTeam;
        }

        private bool SameClan(ulong first, ulong second)
        {
            object direct = Clans.Call("IsClanMember", first.ToString(), second.ToString());
            if (direct is bool) return (bool)direct;

            object firstClan = Clans.Call("GetClanOf", first);
            if (firstClan == null) return false;
            object secondClan = Clans.Call("GetClanOf", second);
            return secondClan != null &&
                   string.Equals(firstClan.ToString(), secondClan.ToString(), StringComparison.Ordinal);
        }

        private bool IsNoEscapeBlocked(BasePlayer player, out string reason)
        {
            reason = null;
            if (player == null || NoEscape == null) return false;

            if (_config.Integrations.UseRaidBlock)
            {
                object raid = NoEscape.Call("IsRaidBlocked", player.UserIDString);
                if (raid is bool && (bool)raid)
                {
                    reason = "Removal is blocked while raid blocked.";
                    return true;
                }
            }

            if (_config.Integrations.UseCombatBlock)
            {
                object combat = NoEscape.Call("IsCombatBlocked", player.UserIDString);
                if (combat is bool && (bool)combat)
                {
                    reason = "Removal is blocked while combat blocked.";
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Optional Economy Adapters

        private double GetEconomicsBalance(BasePlayer player)
        {
            if (player == null || Economics == null || !_config.Integrations.UseEconomics) return 0d;
            try
            {
                object value = Economics.Call("Balance", player.UserIDString);
                double balance;
                return value != null && double.TryParse(value.ToString(), out balance) ? balance : 0d;
            }
            catch (Exception ex)
            {
                LogWarning("Economics adapter failed safely: " + ex.Message);
                return 0d;
            }
        }

        private int GetServerRewardsPoints(BasePlayer player)
        {
            if (player == null || ServerRewards == null || !_config.Integrations.UseServerRewards) return 0;
            try
            {
                object value = ServerRewards.Call("CheckPoints", player.userID);
                int points;
                return value != null && int.TryParse(value.ToString(), out points) ? points : 0;
            }
            catch (Exception ex)
            {
                LogWarning("ServerRewards adapter failed safely: " + ex.Message);
                return 0;
            }
        }

        #endregion

        #region UI

        private void DestroyRemovalUi(BasePlayer player)
        {
            if (player == null) return;
            _uiState.Remove(player.userID);
            CuiHelper.DestroyUi(player, UiStatus);
            CuiHelper.DestroyUi(player, UiTarget);
            CuiHelper.DestroyUi(player, UiCrosshair);
            CuiHelper.DestroyUi(player, UiSession);
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.DestroyUi(player, UiName);
        }

        private void ShowSessionUi(BasePlayer player)
        {
            BulkConfirmation staleConfirmation;
            if (_bulkConfirmations.TryGetValue(player.userID, out staleConfirmation) &&
                (staleConfirmation.ExpiresAt < UnixTime || !_sessions.ContainsKey(player.userID)))
                _bulkConfirmations.Remove(player.userID);

            if (!_config.Ui.Enabled || player == null || !player.IsConnected) return;
            RemovalSession session;
            if (!_sessions.TryGetValue(player.userID, out session)) return;

            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(session.ExpiresAt - UnixTime)));
            BaseEntity target = FindTarget(player, _config.General.Distance);
            string targetText = target == null ? string.Empty : GetDisplayName(target);
            bool allowed = target != null;
            string reason = string.Empty;
            if (target != null && session.Mode == RemovalMode.Normal)
                allowed = CanRemoveNormal(player, target, out reason);
            else if (target != null && session.Mode == RemovalMode.Admin)
                allowed = CanRemoveAdmin(player, target, out reason);
            else if (target != null &&
                     (session.Mode == RemovalMode.Structure || session.Mode == RemovalMode.All || session.Mode == RemovalMode.External))
            {
                allowed = true;
                List<BaseEntity> preview = CollectBulkEntities(target, session.Mode);
                int discoveredCount = preview.Count;
                int count = discoveredCount;
                bool capped = _config.Bulk.MaximumEntitiesPerOperation > 0 &&
                              discoveredCount > _config.Bulk.MaximumEntitiesPerOperation;
                if (capped)
                    count = _config.Bulk.MaximumEntitiesPerOperation;

                BulkConfirmation pending;
                ulong targetId = target.net != null ? target.net.ID.Value : 0UL;
                bool confirming = _bulkConfirmations.TryGetValue(player.userID, out pending) &&
                                  pending.Mode == session.Mode &&
                                  pending.TargetId == targetId &&
                                  pending.ExpiresAt >= UnixTime;

                reason = confirming
                    ? "CLICK AGAIN TO CONFIRM  •  " + count + (capped ? " ENTITIES (CAPPED)" : " ENTITIES")
                    : (_config.Bulk.ShowEntityCount
                        ? "PREVIEW  •  " + count + (capped ? " ENTITIES (CAPPED)" : " ENTITIES")
                        : "CLICK TO PREVIEW BULK REMOVAL");
            }

            string removalCount = _config.General.MaximumRemovals > 0
                ? session.Removed + "/" + _config.General.MaximumRemovals
                : session.Removed + "/UNLIMITED";

            string uiState = session.Mode + "|" + seconds + "|" +
                (target != null && target.net != null ? target.net.ID.Value.ToString() : "0") + "|" +
                allowed + "|" + reason + "|" + removalCount;

            string previousState;
            if (_uiState.TryGetValue(player.userID, out previousState) && previousState == uiState)
                return;
            _uiState[player.userID] = uiState;

            // Keep the proven destroy-before-add lifecycle, but only when visible state changes.
            CuiHelper.DestroyUi(player, UiStatus);
            CuiHelper.DestroyUi(player, UiTarget);
            CuiHelper.DestroyUi(player, UiCrosshair);
            CuiHelper.DestroyUi(player, UiSession);
            CuiHelper.DestroyUi(player, UiRoot);
            CuiHelper.DestroyUi(player, UiName);

            CuiElementContainer c = new CuiElementContainer();

            c.Add(new CuiPanel {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = false
            }, "Hud", UiRoot);

            c.Add(new CuiPanel {
                Image = { Color = "0.025 0.03 0.035 0.88" },
                RectTransform = { AnchorMin = "0.018 0.825", AnchorMax = "0.205 0.925" }
            }, UiRoot, UiSession);

            c.Add(new CuiLabel {
                Text = { Text = "ROGUERUST  |  REMOVE", FontSize = 12, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.055 0.66", AnchorMax = "0.945 0.91" }
            }, UiSession);
            c.Add(new CuiLabel {
                Text = { Text = session.Mode.ToString().ToUpperInvariant() + "   •   " + seconds + "s", FontSize = 10, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.055 0.36", AnchorMax = "0.945 0.65" }
            }, UiSession);
            c.Add(new CuiLabel {
                Text = { Text = "REMOVED  " + removalCount, FontSize = 9, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.055 0.07", AnchorMax = "0.945 0.35" }
            }, UiSession);

            if (_config.Ui.ShowReticle)
            {
                c.Add(new CuiLabel {
                    Text = { Text = target == null ? "·" : (allowed ? "+" : "×"), FontSize = 18, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.492 0.488", AnchorMax = "0.508 0.512" }
                }, UiRoot, UiCrosshair);

                if (target != null && _config.Ui.ShowReticleTarget)
                    c.Add(new CuiLabel {
                        Text = { Text = targetText, FontSize = 10, Align = TextAnchor.MiddleCenter },
                        RectTransform = { AnchorMin = "0.40 0.454", AnchorMax = "0.60 0.477" }
                    }, UiRoot, UiTarget);

                if (target != null && _config.Ui.ShowAuthorization)
                {
                    string status = allowed
                        ? (string.IsNullOrWhiteSpace(reason) ? "READY" : reason)
                        : (string.IsNullOrWhiteSpace(reason) ? "BLOCKED" : "BLOCKED  •  " + reason);
                    c.Add(new CuiLabel {
                        Text = { Text = status, FontSize = 9, Align = TextAnchor.MiddleCenter },
                        RectTransform = { AnchorMin = "0.36 0.427", AnchorMax = "0.64 0.451" }
                    }, UiRoot, UiStatus);

                    if (allowed && _config.Costs.Enabled && _config.Costs.ShowPreview)
                    {
                        string costText = FormatCost(target);
                        if (!string.IsNullOrWhiteSpace(costText))
                            c.Add(new CuiLabel {
                                Text = { Text = "COST  " + costText, FontSize = 9, Align = TextAnchor.MiddleCenter },
                                RectTransform = { AnchorMin = "0.30 0.400", AnchorMax = "0.70 0.424" }
                            }, UiRoot);
                    }

                    if (allowed && _config.Refunds.Enabled && _config.Refunds.ShowPreview)
                    {
                        string refundText = FormatRefund(target);
                        if (!string.IsNullOrWhiteSpace(refundText))
                            c.Add(new CuiLabel {
                                Text = { Text = "REFUND  " + refundText, FontSize = 9, Align = TextAnchor.MiddleCenter },
                                RectTransform = { AnchorMin = "0.30 0.374", AnchorMax = "0.70 0.398" }
                            }, UiRoot);
                    }
                }
            }
            CuiHelper.AddUi(player, c);
        }

        private string GetDisplayName(BaseEntity entity)
        {
            BuildingBlock block = entity as BuildingBlock;
            if (block != null)
            {
                ConstructionInfo info;
                if (_constructionByPrefab.TryGetValue(block.PrefabName, out info))
                    return info.DisplayName + " (" + block.grade + ")";
                return "Building Block (" + block.grade + ")";
            }

            ItemDefinition item = ItemManager.FindItemDefinition(entity.ShortPrefabName);
            if (item != null && item.displayName != null && !string.IsNullOrWhiteSpace(item.displayName.english))
                return item.displayName.english;

            string shortName = entity.ShortPrefabName ?? string.Empty;
            return string.IsNullOrWhiteSpace(shortName)
                ? entity.GetType().Name
                : shortName.Replace("_", " ").Replace(".", " ");
        }

        #endregion

        #region Helpers / API

        private static string PermissionFor(RemovalMode mode)
        {
            // Deliberately avoid a switch here. Some Oxide/RogueRust compilation paths
            // can surface duplicate constant-label diagnostics for plugin enums.
            if (mode == RemovalMode.Admin) return PermAdmin;
            if (mode == RemovalMode.All) return PermAll;
            if (mode == RemovalMode.External) return PermExternal;
            if (mode == RemovalMode.Structure) return PermStructure;
            return PermNormal;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes >= 1)
                return Math.Ceiling(duration.TotalMinutes) + "m";
            return Math.Max(1, Math.Ceiling(duration.TotalSeconds)) + "s";
        }

        [HookMethod("API_IsRemovalActive")]
        private bool ApiIsRemovalActive(ulong playerId) => _sessions.ContainsKey(playerId);

        [HookMethod("API_DisableRemoval")]
        private bool ApiDisableRemoval(BasePlayer player)
        {
            if (player == null || !_sessions.ContainsKey(player.userID)) return false;
            DisableSession(player, false);
            return true;
        }

        protected override IReadOnlyDictionary<string, string> DefaultMessages =>
            new Dictionary<string, string>
            {
                ["Error.NoPermission"] = "You do not have permission to use that removal mode."
            };

        #region RogueRust API

        private bool API_CanRemove(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return false;
            string reason;
            return CanRemoveNormal(player, entity, out reason);
        }

        private string API_CanRemoveReason(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null) return "Invalid player or entity.";
            string reason;
            return CanRemoveNormal(player, entity, out reason) ? null : reason;
        }

        private static bool PluginReady(Plugin plugin)
        {
            return plugin != null && plugin.IsLoaded;
        }

        private bool API_IsIntegrationActive(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.ToLowerInvariant();
            if (name == "friends") return _config.Integrations.UseFriends && PluginReady(Friends);
            if (name == "clans") return _config.Integrations.UseClans && PluginReady(Clans);
            if (name == "buildingowners") return _config.Integrations.UseBuildingOwners && PluginReady(BuildingOwners);
            if (name == "noescape") return (_config.Integrations.UseRaidBlock || _config.Integrations.UseCombatBlock) && PluginReady(NoEscape);
            if (name == "economics") return _config.Integrations.UseEconomics && PluginReady(Economics);
            if (name == "serverrewards") return _config.Integrations.UseServerRewards && PluginReady(ServerRewards);
            return false;
        }



        private Dictionary<string, int> API_GetRemovalCost(BaseEntity entity)
        {
            return GetRemovalCost(entity)
                .GroupBy(x => x.Definition.shortname, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount), StringComparer.OrdinalIgnoreCase);
        }

        private Dictionary<string, int> API_GetRemovalRefund(BaseEntity entity)
        {
            return GetRefund(entity)
                .GroupBy(x => x.Definition.shortname, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.Amount), StringComparer.OrdinalIgnoreCase);
        }

        private int API_GetActiveSessionCount()
        {
            return _sessions.Count;
        }

        private bool API_IsHealthy()
        {
            return _config != null &&
                   _constructionByPrefab != null &&
                   _deployableItems != null;
        }

        private Dictionary<string, object> API_GetDiagnostics()
        {
            int activePlayers = BasePlayer.activePlayerList != null ? BasePlayer.activePlayerList.Count : 0;
            return new Dictionary<string, object>
            {
                ["Version"] = Version.ToString(),
                ["ActivePlayers"] = activePlayers,
                ["ActiveSessions"] = _sessions.Count,
                ["PendingConfirmations"] = _bulkConfirmations.Count,
                ["UiStateEntries"] = _uiState.Count,
                ["EntityCatalogConstructions"] = _constructionByPrefab.Count,
                ["EntityCatalogDeployables"] = _deployableItems.Count,
                ["Healthy"] = API_IsHealthy()
            };
        }

        private Dictionary<string, object> API_GetStatus()
        {
            return new Dictionary<string, object>
            {
                ["Version"] = Version.ToString(),
                ["ActiveSessions"] = _sessions.Count,
                ["PendingBulkConfirmations"] = _bulkConfirmations.Count,
                ["CachedUiStates"] = _uiState.Count,
                ["Friends"] = _config.Integrations.UseFriends && PluginReady(Friends),
                ["Clans"] = _config.Integrations.UseClans && PluginReady(Clans),
                ["BuildingOwners"] = _config.Integrations.UseBuildingOwners && PluginReady(BuildingOwners),
                ["NoEscape"] = (_config.Integrations.UseRaidBlock || _config.Integrations.UseCombatBlock) && PluginReady(NoEscape),
                ["Economics"] = _config.Integrations.UseEconomics && PluginReady(Economics),
                ["ServerRewards"] = _config.Integrations.UseServerRewards && PluginReady(ServerRewards)
            };
        }

        private bool API_IsRemovalActive(ulong playerId)
        {
            return _sessions.ContainsKey(playerId);
        }

        private string API_GetRemovalMode(ulong playerId)
        {
            RemovalSession session;
            return _sessions.TryGetValue(playerId, out session) ? session.Mode.ToString() : null;
        }

        private bool API_DisableRemoval(BasePlayer player)
        {
            if (player == null || !_sessions.ContainsKey(player.userID)) return false;
            DisableSession(player, false);
            return true;
        }

        #endregion

        private enum RemovalMode
        {
            Normal,
            Admin,
            All,
            Structure,
            External
        }

        private sealed class BulkConfirmation
        {
            public RemovalMode Mode;
            public ulong TargetId;
            public int Count;
            public double ExpiresAt;
        }

        private sealed class RemovalSession
        {
            public RemovalMode Mode;
            public double ExpiresAt;
            public int DurationSeconds;
            public double LastRemovalAt;
            public int Removed;
        }

        private sealed class CostEntry { public ItemDefinition Definition; public int Amount; }

        private sealed class RefundEntry
        {
            public ItemDefinition Definition;
            public int Amount;
        }

        private sealed class ConstructionInfo
        {
            public string Prefab;
            public string DisplayName;
        }

        #endregion
    }
}
