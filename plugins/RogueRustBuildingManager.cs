using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Ext.RogueRust;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using Oxide.Game.Rust.Cui;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RogueRustBuildingManager", "RogueAssassin", "1.2.0")]
    [Description("Upgrade, downgrade, skin and wallpaper a whole base, or grade blocks as you place or hammer them.")]
    public sealed class RogueRustBuildingManager : RogueRustPlugin
    {
        [RoguePermission] private const string UsePermission = "roguerustbuildingmanager.use";
        [RoguePermission] private const string BulkPermission = "roguerustbuildingmanager.bulk";
        [RoguePermission] private const string SkinsPermission = "roguerustbuildingmanager.skins";
        [RoguePermission] private const string WallpaperPermission = "roguerustbuildingmanager.wallpaper";
        [RoguePermission] private const string NoCostPermission = "roguerustbuildingmanager.nocost";
        [RoguePermission] private const string AdminPermission = "roguerustbuildingmanager.admin";

        private const string PluginVersion = "1.2.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 2, 0);
        private static readonly VersionNumber MinimumExtension = new VersionNumber(4, 4, 63);

        private const string DataKey = "RogueRustBuildingManager/players";
        private const string UiRoot = "RogueRustBuildingManager.Main";
        private const string UiCursor = "RogueRustBuildingManager.Cursor";
        private const float LookDistance = 8f;

        [PluginReference] private Plugin NoEscape;

        private Configuration _config;
        private PlayerData _data = new PlayerData();
        private readonly Dictionary<ulong, Operation> _operations = new Dictionary<ulong, Operation>();
        private readonly Dictionary<ulong, UndoRecord> _undo = new Dictionary<ulong, UndoRecord>();
        private readonly Dictionary<ulong, float> _autoGradeUntil = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> _menuOpen = new HashSet<ulong>();
        private readonly List<WallpaperOption>[] _wallpapers = { new List<WallpaperOption>(), new List<WallpaperOption>(), new List<WallpaperOption>() };
        private bool _ready;

        // Wallpaper slots a player can choose for, matched to the game's categories by name.
        private const int SlotWall = 0;
        private const int SlotFloor = 1;
        private const int SlotCeiling = 2;

        private sealed class WallpaperOption
        {
            public ulong Id;
            public string Name = string.Empty;
        }

        #region Configuration

        private sealed class Configuration
        {
            [JsonProperty("General", Order = 10)] public GeneralSettings General = new GeneralSettings();
            [JsonProperty("Access", Order = 20)] public AccessSettings Access = new AccessSettings();
            [JsonProperty("Costs", Order = 30)] public CostSettings Costs = new CostSettings();
            [JsonProperty("Auto Grade (Grade Blocks As They Are Placed)", Order = 40)] public AutoGradeSettings AutoGrade = new AutoGradeSettings();
            [JsonProperty("Hammer Tool (Grade A Block By Hitting It)", Order = 50)] public FeatureSettings Hammer = new FeatureSettings();
            [JsonProperty("Building Skins", Order = 60)] public SkinSettings Skins = new SkinSettings();
            [JsonProperty("Wallpaper", Order = 70)] public WallpaperOptions Wallpaper = new WallpaperOptions();
            [JsonProperty("Undo", Order = 80)] public UndoSettings Undo = new UndoSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class GeneralSettings
        {
            [JsonProperty("Blocks Changed Per Server Tick (Lower = Smoother, Higher = Faster)")] public int BlocksPerTick = 25;
            [JsonProperty("Most Blocks One Operation May Touch")] public int MaximumBlocks = 6000;
            [JsonProperty("Skip Blocks Attacked Within This Many Seconds (0 = Off)")] public float RecentlyDamagedSeconds = 30f;
            [JsonProperty("Play The Upgrade Effect On Each Block")] public bool PlayEffects = true;
            [JsonProperty("Skip The Effect When An Operation Touches More Than This Many Blocks (Keeps Big Bases Smooth)")] public int EffectLimit = 40;
            [JsonProperty("Seconds Between Whole-Base Operations Per Player")] public float CooldownSeconds = 5f;
            [JsonProperty("Chat Prefix")] public string Prefix = "<color=#1AADD1>[Building]</color> ";
        }

        private sealed class AccessSettings
        {
            [JsonProperty("Require Tool Cupboard Authorisation On The Building")] public bool RequireAuthorisation = true;
            [JsonProperty("Block While Raid-Blocked (Needs The NoEscape Plugin)")] public bool BlockWhenRaidBlocked = true;
        }

        private sealed class CostSettings
        {
            [JsonProperty("Charge Resources For Upgrades")] public bool Charge = true;
            [JsonProperty("Refund Resources When Downgrading")] public bool RefundOnDowngrade = false;
            [JsonProperty("Downgrade Refund Percentage (0-100)")] public float RefundPercent = 50f;
        }

        private class FeatureSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
        }

        private sealed class AutoGradeSettings : FeatureSettings
        {
            [JsonProperty("Turn Itself Off After This Many Seconds Without Building (0 = Never)")] public float IdleSeconds = 300f;
        }

        private sealed class WallpaperOptions : FeatureSettings
        {
            [JsonProperty("Charge The Game's Price For Each Face Papered")] public bool Charge = true;
        }

        private sealed class SkinSettings : FeatureSettings
        {
            [JsonProperty("Only Offer Skins The Player Owns")] public bool RequireOwnership = true;
            [JsonProperty("Apply The Player's Chosen Skin When They Upgrade By Hand")] public bool ApplyOnManualUpgrade = true;
        }

        private sealed class UndoSettings : FeatureSettings
        {
            [JsonProperty("Seconds An Operation Can Be Undone For")] public float WindowSeconds = 120f;
        }

        private sealed class PlayerData
        {
            public Dictionary<ulong, PlayerPreferences> Players = new Dictionary<ulong, PlayerPreferences>();
        }

        private sealed class PlayerPreferences
        {
            public int TargetGrade = RogueBuildingGrades.Stone;
            public bool AutoGrade;
            public bool Hammer;
            // Chosen skin per grade (grade number to skin id) and wallpaper per slot (0 = none chosen).
            public Dictionary<int, ulong> Skins = new Dictionary<int, ulong>();
            public ulong[] Wallpapers = new ulong[3];
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            // RogueRust config standard: replace pre-initialized collections instead of appending loaded values.
            Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
            try
            {
                _config = Config.ReadObject<Configuration>();
            }
            catch (Exception exception)
            {
                // Keep the file that could not be read. The defaults are saved over the config below, and
                // without this copy one typo would cost the owner every setting they had made.
                try { System.IO.File.Copy(Config.Filename, Config.Filename + ".invalid", true); } catch { }
                LogError("Configuration", "Invalid configuration: " + exception.Message);
                _config = new Configuration();
            }

            if (_config == null) _config = new Configuration();
            if (_config.General == null) _config.General = new GeneralSettings();
            if (_config.Access == null) _config.Access = new AccessSettings();
            if (_config.Costs == null) _config.Costs = new CostSettings();
            if (_config.AutoGrade == null) _config.AutoGrade = new AutoGradeSettings();
            if (_config.Hammer == null) _config.Hammer = new FeatureSettings();
            if (_config.Skins == null) _config.Skins = new SkinSettings();
            if (_config.Wallpaper == null) _config.Wallpaper = new WallpaperOptions();
            if (_config.Undo == null) _config.Undo = new UndoSettings();

            GeneralSettings general = _config.General;
            general.BlocksPerTick = Mathf.Clamp(general.BlocksPerTick, 1, 200);
            general.MaximumBlocks = Mathf.Clamp(general.MaximumBlocks, 50, 50000);
            general.EffectLimit = Mathf.Clamp(general.EffectLimit, 0, 5000);
            general.RecentlyDamagedSeconds = Mathf.Clamp(general.RecentlyDamagedSeconds, 0f, 600f);
            general.CooldownSeconds = Mathf.Clamp(general.CooldownSeconds, 0f, 600f);
            if (general.Prefix == null) general.Prefix = string.Empty;
            _config.Costs.RefundPercent = Mathf.Clamp(_config.Costs.RefundPercent, 0f, 100f);
            _config.AutoGrade.IdleSeconds = Mathf.Clamp(_config.AutoGrade.IdleSeconds, 0f, 86400f);
            _config.Undo.WindowSeconds = Mathf.Clamp(_config.Undo.WindowSeconds, 10f, 3600f);
            if (_config.Version < CurrentVersion) _config.Version = CurrentVersion;
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        #endregion

        #region Lifecycle

        private void Init()
        {
            _data = LoadData(DataKey, () => new PlayerData()) ?? new PlayerData();
            if (_data.Players == null) _data.Players = new Dictionary<ulong, PlayerPreferences>();
            LogInformation("Lifecycle", Name + " " + PluginVersion + " initialized.");
        }

        private void OnServerInitialized()
        {
            if (RogueRustExtension.CurrentVersion < MinimumExtension)
            {
                LogError("Lifecycle", "RogueRust " + MinimumExtension + " or newer is required; this server has " +
                    RogueRustExtension.CurrentVersion + ". The plugin stays idle until the DLL is updated.");
                return;
            }

            BuildWallpaperCatalogue();
            _ready = true;
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                CloseMenu(player);
            SaveData(DataKey, _data);
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            _menuOpen.Remove(player.userID);
            _pickers.Remove(player.userID);
            _autoGradeUntil.Remove(player.userID);
            _undo.Remove(player.userID);
        }

        private PlayerPreferences Preferences(BasePlayer player)
        {
            PlayerPreferences preferences;
            if (!_data.Players.TryGetValue(player.userID, out preferences))
                _data.Players[player.userID] = preferences = new PlayerPreferences();
            if (preferences.Skins == null) preferences.Skins = new Dictionary<int, ulong>();
            if (preferences.Wallpapers == null || preferences.Wallpapers.Length != 3) preferences.Wallpapers = new ulong[3];
            if (!RogueBuildingGrades.IsValid(preferences.TargetGrade)) preferences.TargetGrade = RogueBuildingGrades.Stone;
            return preferences;
        }

        private void SavePreferences()
        {
            SaveDataDebounced(DataKey, _data, TimeSpan.FromSeconds(3));
        }

        #endregion

        #region Access and costs

        private bool Allowed(BasePlayer player, string permissionName)
        {
            return HasPermission(player, permissionName) || HasPermission(player, AdminPermission);
        }

        private bool PaysNothing(BasePlayer player)
        {
            return !_config.Costs.Charge || HasPermission(player, NoCostPermission);
        }

        private bool IsRaidBlocked(BasePlayer player)
        {
            if (!_config.Access.BlockWhenRaidBlocked || NoEscape == null) return false;
            object blocked = NoEscape.Call("IsRaidBlocked", player);
            return blocked is bool && (bool)blocked;
        }

        private BaseEntity LookedAt(BasePlayer player)
        {
            RaycastHit hit;
            return player.eyes != null &&
                   Physics.Raycast(player.eyes.HeadRay(), out hit, LookDistance, Rust.Layers.Mask.Construction, QueryTriggerInteraction.Ignore)
                ? hit.GetEntity() : null;
        }

        // Finds the building the player means and checks they may change it. Replies with the reason when they may not.
        private bool TryGetBuilding(BasePlayer player, out BuildingManager.Building building)
        {
            if (!Buildings.TryFindBuilding(player, LookedAt(player), out building))
            {
                Reply(player, "Error.NoBuilding");
                return false;
            }
            if (!CanChange(player, building)) return false;
            return true;
        }

        private bool CanChange(BasePlayer player, BuildingManager.Building building)
        {
            if (_config.Access.RequireAuthorisation && !HasPermission(player, AdminPermission) &&
                !Buildings.IsAuthorised(player, building))
            {
                Reply(player, "Error.NotAuthorised");
                return false;
            }
            if (IsRaidBlocked(player))
            {
                Reply(player, "Error.RaidBlocked");
                return false;
            }
            return true;
        }

        private ulong ChosenSkin(BasePlayer player, PlayerPreferences preferences, int grade)
        {
            ulong skin;
            if (!_config.Skins.Enabled || !preferences.Skins.TryGetValue(grade, out skin) || skin == 0UL) return 0UL;
            return Allowed(player, SkinsPermission) ? skin : 0UL;
        }

        private static string DescribeItems(Dictionary<int, int> items)
        {
            var parts = new List<string>(items.Count);
            foreach (KeyValuePair<int, int> entry in items)
            {
                ItemDefinition definition = ItemManager.FindItemDefinition(entry.Key);
                string name = definition != null && definition.displayName != null ? definition.displayName.english : entry.Key.ToString(CultureInfo.InvariantCulture);
                parts.Add(entry.Value.ToString("N0", CultureInfo.InvariantCulture) + " " + name);
            }
            return string.Join(", ", parts);
        }

        #endregion

        #region Whole-base operations

        private struct Change
        {
            public BuildingBlock Block;
            public BuildingGrade.Enum Grade;
            public ulong Skin;
            public BuildingGrade.Enum OldGrade;
            public ulong OldSkin;
            public uint OldColour;
        }

        private sealed class Operation
        {
            public BasePlayer Player;
            public string DoneMessage = string.Empty;
            public readonly List<Change> Changes = new List<Change>();
            public readonly Dictionary<int, float> Paid = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Refund = new Dictionary<int, float>();
            public readonly Dictionary<int, float> Unused = new Dictionary<int, float>();
            public int Applied;
            public int Skipped;
            public bool Recordable = true;
        }

        private sealed class UndoRecord
        {
            public float ExpiresAt;
            public readonly List<Change> Changes = new List<Change>();
            public readonly List<FaceChange> Faces = new List<FaceChange>();
            public Dictionary<int, float> Paid = new Dictionary<int, float>();
            public Dictionary<int, float> Refunded = new Dictionary<int, float>();
        }

        private bool BeginOperation(BasePlayer player, out List<BuildingBlock> blocks)
        {
            blocks = null;
            if (!_ready) return false;
            if (_operations.ContainsKey(player.userID))
            {
                Reply(player, "Error.Busy");
                return false;
            }

            TimeSpan remaining = CooldownRemaining(player, "operation");
            if (remaining > TimeSpan.Zero)
            {
                Reply(player, "Error.Cooldown", Math.Ceiling(remaining.TotalSeconds));
                return false;
            }

            BuildingManager.Building building;
            if (!TryGetBuilding(player, out building)) return false;

            blocks = new List<BuildingBlock>();
            Buildings.CollectBlocks(building, blocks, _config.General.MaximumBlocks);
            if (blocks.Count == 0)
            {
                Reply(player, "Error.NoBuilding");
                return false;
            }
            return true;
        }

        // Takes every block to one grade. A block already past it is left alone on an upgrade, and
        // a block already below it is left alone on a downgrade.
        private void ChangeGrades(BasePlayer player, int gradeNumber, bool upgrade)
        {
            if (!Allowed(player, BulkPermission))
            {
                Reply(player, "Error.NoPermission");
                return;
            }

            List<BuildingBlock> blocks;
            if (!BeginOperation(player, out blocks)) return;

            var grade = (BuildingGrade.Enum)gradeNumber;
            PlayerPreferences preferences = Preferences(player);
            ulong skin = ChosenSkin(player, preferences, gradeNumber);
            var operation = new Operation { Player = player, DoneMessage = upgrade ? "Done.Upgraded" : "Done.Downgraded" };
            var cost = new Dictionary<int, float>();
            float refundFraction = !upgrade && _config.Costs.RefundOnDowngrade && !PaysNothing(player) ? _config.Costs.RefundPercent / 100f : 0f;

            foreach (BuildingBlock block in blocks)
            {
                if (upgrade ? block.grade >= grade : block.grade <= grade) continue;
                ulong blockSkin = SkinFor(block, player, grade, skin);
                if (!Buildings.CanChange(block, grade, blockSkin)) continue;

                if (upgrade) Buildings.AddUpgradeCost(block, grade, blockSkin, cost);
                else if (refundFraction > 0f) Buildings.AddGradeCost(block, grade, block.grade, block.skinID, operation.Refund);
                operation.Changes.Add(NewChange(block, grade, blockSkin));
            }

            if (operation.Changes.Count == 0)
            {
                Reply(player, "Error.NothingToDo");
                return;
            }

            if (upgrade && !PaysNothing(player))
            {
                var missing = new Dictionary<int, int>();
                if (!Buildings.TryTake(player, cost, missing))
                {
                    Reply(player, "Error.CannotAfford", DescribeItems(missing));
                    return;
                }
                foreach (KeyValuePair<int, float> entry in cost) operation.Paid[entry.Key] = entry.Value;
            }

            if (refundFraction > 0f)
            {
                var scaled = new Dictionary<int, float>();
                foreach (KeyValuePair<int, float> entry in operation.Refund) scaled[entry.Key] = Mathf.Floor(entry.Value * refundFraction);
                operation.Refund.Clear();
                foreach (KeyValuePair<int, float> entry in scaled) operation.Refund[entry.Key] = entry.Value;
            }
            else operation.Refund.Clear();

            Run(operation, RogueBuildingGrades.DisplayName(gradeNumber));
        }

        // Re-skins every block at its current grade with the player's chosen skin for that grade.
        // trusted: the caller (another RogueRust plugin) has already checked its own permission.
        private void ApplySkins(BasePlayer player, bool trusted = false)
        {
            if (!_config.Skins.Enabled || (!trusted && !Allowed(player, SkinsPermission)))
            {
                Reply(player, "Error.NoPermission");
                return;
            }

            List<BuildingBlock> blocks;
            if (!BeginOperation(player, out blocks)) return;

            PlayerPreferences preferences = Preferences(player);
            var operation = new Operation { Player = player, DoneMessage = "Done.Skinned" };
            foreach (BuildingBlock block in blocks)
            {
                ulong wanted;
                if (!preferences.Skins.TryGetValue((int)block.grade, out wanted)) continue;
                wanted = SkinFor(block, player, block.grade, wanted);
                if (block.skinID == wanted || !Buildings.CanChange(block, block.grade, wanted)) continue;
                operation.Changes.Add(NewChange(block, block.grade, wanted));
            }

            if (operation.Changes.Count == 0)
            {
                Reply(player, "Error.NothingToDo");
                return;
            }
            Run(operation, string.Empty);
        }

        // The skin to use on one block: the chosen skin where the block has that variant and, when
        // ownership is required, the player owns it; otherwise the default look.
        private ulong SkinFor(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong skin)
        {
            if (skin == 0UL || block.blockDefinition == null || block.blockDefinition.GetGrade(grade, skin) == null) return 0UL;
            if (_config.Skins.RequireOwnership && !block.HasUpgradePrivilege(grade, skin, player)) return 0UL;
            return skin;
        }

        private static Change NewChange(BuildingBlock block, BuildingGrade.Enum grade, ulong skin)
        {
            return new Change
            {
                Block = block,
                Grade = grade,
                Skin = skin,
                OldGrade = block.grade,
                OldSkin = block.skinID,
                OldColour = block.customColour
            };
        }

        // Applies the planned changes a few blocks per tick so a large base never stalls the server.
        private void Run(Operation operation, string detail)
        {
            BasePlayer player = operation.Player;
            ulong userId = player.userID;
            _operations[userId] = operation;
            TryStartCooldown(player, "operation", TimeSpan.FromSeconds(_config.General.CooldownSeconds), out _);
            Reply(player, "Operation.Started", operation.Changes.Count);

            float damagedWindow = _config.General.RecentlyDamagedSeconds;
            // Hundreds of upgrade effects at once is what makes a big operation stutter for nearby players.
            bool effects = _config.General.PlayEffects && operation.Changes.Count <= _config.General.EffectLimit;
            Rogue.Workloads.Batch(Name, "operation:" + userId, operation.Changes.Count, _config.General.BlocksPerTick, TimeSpan.Zero,
                index =>
                {
                    Change change = operation.Changes[index];
                    BuildingBlock block = change.Block;
                    bool applied = block != null && !block.IsDestroyed &&
                                   !Buildings.WasRecentlyDamaged(block, damagedWindow) &&
                                   !(change.Grade != block.grade && block.IsUpgradeBlocked());
                    if (applied)
                    {
                        uint colour = change.Skin != 0UL && player != null ? BuildingBlock.GetShippingContainerBlockColourForPlayer(player) : 0u;
                        applied = Buildings.Apply(block, change.Grade, change.Skin, colour, effects);
                    }

                    if (applied) operation.Applied++;
                    else
                    {
                        operation.Skipped++;
                        // Paid for but not changed: hand that part back when the operation ends.
                        if (block != null && !block.IsDestroyed && operation.Paid.Count != 0 && change.Grade > change.OldGrade)
                            Buildings.AddGradeCost(block, change.OldGrade, change.Grade, change.Skin, operation.Unused);
                        operation.Changes[index] = default(Change);
                    }
                    return true;
                },
                () => Finish(userId, operation, detail));
        }

        private void Finish(ulong userId, Operation operation, string detail)
        {
            _operations.Remove(userId);
            BasePlayer player = operation.Player;
            bool online = player != null && player.IsConnected;

            if (online && operation.Unused.Count != 0)
            {
                Buildings.Give(player, operation.Unused);
                foreach (KeyValuePair<int, float> entry in operation.Unused)
                {
                    float paid;
                    if (operation.Paid.TryGetValue(entry.Key, out paid)) operation.Paid[entry.Key] = Mathf.Max(0f, paid - entry.Value);
                }
            }

            if (online && operation.Refund.Count != 0 && operation.Applied != 0)
            {
                // Scale the promised refund to the share of blocks that actually changed.
                float share = operation.Applied / (float)(operation.Applied + operation.Skipped);
                Buildings.Give(player, operation.Refund, share);
                var given = new Dictionary<int, float>();
                foreach (KeyValuePair<int, float> entry in operation.Refund) given[entry.Key] = Mathf.Floor(entry.Value * share);
                operation.Refund.Clear();
                foreach (KeyValuePair<int, float> entry in given) operation.Refund[entry.Key] = entry.Value;
            }
            else operation.Refund.Clear();

            if (operation.Recordable && _config.Undo.Enabled && operation.Applied != 0)
            {
                var record = new UndoRecord { ExpiresAt = Time.realtimeSinceStartup + _config.Undo.WindowSeconds, Paid = operation.Paid, Refunded = operation.Refund };
                foreach (Change change in operation.Changes)
                    if (change.Block != null) record.Changes.Add(change);
                _undo[userId] = record;
            }

            if (!online) return;
            Reply(player, operation.DoneMessage, operation.Applied, detail);
            if (operation.Skipped != 0) Reply(player, "Operation.Skipped", operation.Skipped);
            if (_menuOpen.Contains(userId)) DrawMenu(player);
        }

        private void Undo(BasePlayer player)
        {
            UndoRecord record;
            if (!_config.Undo.Enabled || !_undo.TryGetValue(player.userID, out record) || record.ExpiresAt < Time.realtimeSinceStartup)
            {
                _undo.Remove(player.userID);
                Reply(player, "Error.NothingToUndo");
                return;
            }
            if (_operations.ContainsKey(player.userID))
            {
                Reply(player, "Error.Busy");
                return;
            }
            if (IsRaidBlocked(player))
            {
                Reply(player, "Error.RaidBlocked");
                return;
            }

            // A downgrade handed resources back; they have to be returned before it is reversed.
            if (record.Refunded.Count != 0)
            {
                var missing = new Dictionary<int, int>();
                if (!Buildings.TryTake(player, record.Refunded, missing))
                {
                    Reply(player, "Error.CannotAfford", DescribeItems(missing));
                    return;
                }
            }

            if (record.Faces.Count != 0)
            {
                _undo.Remove(player.userID);
                var restore = new List<FaceChange>(record.Faces.Count);
                foreach (FaceChange face in record.Faces)
                    restore.Add(new FaceChange { Block = face.Block, Side = face.Side, Wanted = face.Old, Old = face.Wanted });
                Buildings.Give(player, record.Paid);
                RunFaces(player, restore, "Done.Undone", 0, 0f, false);
                return;
            }

            _undo.Remove(player.userID);
            var operation = new Operation { Player = player, DoneMessage = "Done.Undone", Recordable = false };
            foreach (Change change in record.Changes)
            {
                BuildingBlock block = change.Block;
                if (block == null || block.IsDestroyed) continue;
                operation.Changes.Add(new Change { Block = block, Grade = change.OldGrade, Skin = change.OldSkin, OldGrade = block.grade, OldSkin = block.skinID });
            }

            Buildings.Give(player, record.Paid);
            if (operation.Changes.Count == 0)
            {
                Reply(player, "Error.NothingToUndo");
                return;
            }
            Run(operation, string.Empty);
        }

        #endregion

        #region Single blocks: placement, hammer and manual upgrades

        // Grades one block for the player and charges them the normal price. Returns false when nothing changed.
        private bool GradeBlock(BasePlayer player, BuildingBlock block, int gradeNumber, bool quiet)
        {
            var grade = (BuildingGrade.Enum)gradeNumber;
            if (block == null || block.IsDestroyed || block.grade == grade) return false;

            ulong skin = SkinFor(block, player, grade, ChosenSkin(player, Preferences(player), gradeNumber));
            if (!Buildings.CanChange(block, grade, skin)) return false;

            if (grade > block.grade && !PaysNothing(player))
            {
                if (!block.CanAffordUpgrade(grade, skin, player))
                {
                    if (!quiet) Reply(player, "Error.CannotAffordBlock", RogueBuildingGrades.DisplayName(gradeNumber));
                    return false;
                }
                block.PayForUpgrade(block.blockDefinition.GetGrade(grade, skin), player);
            }

            uint colour = skin != 0UL ? BuildingBlock.GetShippingContainerBlockColourForPlayer(player) : 0u;
            return Buildings.Apply(block, grade, skin, colour, _config.General.PlayEffects);
        }

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if (!_ready || !_config.AutoGrade.Enabled || planner == null || gameObject == null) return;
            BasePlayer player = planner.GetOwnerPlayer();
            if (player == null) return;

            PlayerPreferences preferences;
            if (!_data.Players.TryGetValue(player.userID, out preferences) || !preferences.AutoGrade ||
                preferences.TargetGrade <= RogueBuildingGrades.Twig)
                return;

            BuildingBlock block = gameObject.ToBaseEntity() as BuildingBlock;
            if (block == null) return;

            // Auto grade switches itself off after a quiet spell, so it is not still on next session.
            float idle = _config.AutoGrade.IdleSeconds;
            float now = Time.realtimeSinceStartup;
            float until;
            if (idle > 0f && _autoGradeUntil.TryGetValue(player.userID, out until) && now > until)
            {
                preferences.AutoGrade = false;
                _autoGradeUntil.Remove(player.userID);
                SavePreferences();
                Reply(player, "AutoGrade.TimedOut");
                return;
            }
            if (idle > 0f) _autoGradeUntil[player.userID] = now + idle;

            if (!Allowed(player, UsePermission) || IsRaidBlocked(player)) return;

            // The block finishes spawning this tick; grade it on the next one.
            int grade = preferences.TargetGrade;
            NextTick(() =>
            {
                if (block != null && !block.IsDestroyed && player != null && player.IsConnected)
                    GradeBlock(player, block, grade, true);
            });
        }

        private object OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (!_ready || !_config.Hammer.Enabled || player == null || info == null) return null;
            PlayerPreferences preferences;
            if (!_data.Players.TryGetValue(player.userID, out preferences) || !preferences.Hammer) return null;

            BuildingBlock block = info.HitEntity as BuildingBlock;
            // A block already at the target grade gets a normal hammer hit, so it can still be repaired.
            if (block == null || (int)block.grade == preferences.TargetGrade) return null;
            if (!Allowed(player, UsePermission)) return null;

            BuildingManager.Building building = block.GetBuilding();
            if (building != null && !CanChange(player, building)) return true;
            if (Buildings.WasRecentlyDamaged(block, _config.General.RecentlyDamagedSeconds))
            {
                Reply(player, "Error.RecentlyDamaged");
                return true;
            }
            if (block.IsUpgradeBlocked())
            {
                Reply(player, "Error.Obstructed");
                return true;
            }

            GradeBlock(player, block, preferences.TargetGrade, false);
            return true;
        }

        private void OnStructureUpgrade(BuildingBlock block, BasePlayer player, BuildingGrade.Enum grade, ulong skin)
        {
            if (!_ready || !_config.Skins.Enabled || !_config.Skins.ApplyOnManualUpgrade || block == null || player == null || skin != 0UL) return;
            PlayerPreferences preferences;
            if (!_data.Players.TryGetValue(player.userID, out preferences)) return;
            ulong chosen = ChosenSkin(player, preferences, (int)grade);
            if (chosen == 0UL) return;

            NextTick(() =>
            {
                if (block == null || block.IsDestroyed || player == null || block.grade != grade || block.skinID != 0UL) return;
                ulong wanted = SkinFor(block, player, grade, chosen);
                if (wanted != 0UL)
                    Buildings.Apply(block, grade, wanted, BuildingBlock.GetShippingContainerBlockColourForPlayer(player), false);
            });
        }

        #endregion

        #region Wallpaper

        // The game's category enum also has First and Last, which are second names for Wall and
        // Ceiling. Only the three real categories are used, or those two lists would be filled twice.
        private static readonly WallpaperSettings.Category[] WallpaperCategories =
        {
            WallpaperSettings.Category.Wall, WallpaperSettings.Category.Floor, WallpaperSettings.Category.Ceiling
        };

        private static int SlotOf(WallpaperSettings.Category category)
        {
            if (category == WallpaperSettings.Category.Wall) return SlotWall;
            if (category == WallpaperSettings.Category.Floor) return SlotFloor;
            if (category == WallpaperSettings.Category.Ceiling) return SlotCeiling;
            return -1;
        }

        // The wallpapers the game knows about, read from its own item skins so new ones appear without an update.
        private void BuildWallpaperCatalogue()
        {
            foreach (List<WallpaperOption> list in _wallpapers) list.Clear();
            if (!_config.Wallpaper.Enabled) return;

            try
            {
                var seen = new HashSet<ulong>();
                foreach (WallpaperSettings.Category category in WallpaperCategories)
                {
                    int slot = SlotOf(category);
                    seen.Clear();
                    ItemDefinition definition = WallpaperSettings.GetItemDefForCategory(category);
                    if (definition == null || definition.skins == null) continue;

                    List<WallpaperOption> options = _wallpapers[slot];
                    foreach (ItemSkinDirectory.Skin skin in definition.skins)
                    {
                        // Each wallpaper is listed once, whatever the game's skin table repeats.
                        if (skin.id == 0 || !seen.Add((ulong)skin.id)) continue;
                        string name = skin.invItem != null && skin.invItem.displayName != null ? skin.invItem.displayName.english : null;
                        options.Add(new WallpaperOption { Id = (ulong)skin.id, Name = string.IsNullOrEmpty(name) ? "Wallpaper " + skin.id : name });
                    }
                    options.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception exception)
            {
                LogWarning("Wallpaper", "Could not read the game's wallpaper list: " + exception.Message);
            }

            LogInformation("Wallpaper", "Wallpapers available: " + _wallpapers[SlotWall].Count + " wall, " +
                _wallpapers[SlotFloor].Count + " floor, " + _wallpapers[SlotCeiling].Count + " ceiling.");
        }

        private struct FaceChange
        {
            public BuildingBlock Block;
            public int Side;
            public ulong Wanted;
            public ulong Old;
        }

        // Applies the chosen wallpapers to every face that takes one, or strips them all when remove is set.
        private void ApplyWallpaper(BasePlayer player, bool remove)
        {
            if (!_config.Wallpaper.Enabled || !Allowed(player, WallpaperPermission))
            {
                Reply(player, "Error.NoPermission");
                return;
            }

            WallpaperSettings settings = WallpaperPlanner.Settings;
            if (settings == null)
            {
                Reply(player, "Error.NothingToDo");
                return;
            }

            PlayerPreferences preferences = Preferences(player);
            if (!remove && preferences.Wallpapers[SlotWall] == 0UL && preferences.Wallpapers[SlotFloor] == 0UL && preferences.Wallpapers[SlotCeiling] == 0UL)
            {
                Reply(player, "Error.NoWallpaperChosen");
                return;
            }

            List<BuildingBlock> blocks;
            if (!BeginOperation(player, out blocks)) return;

            // Work out every face that would change before anything is charged or touched.
            var faces = new List<FaceChange>();
            foreach (BuildingBlock block in blocks)
            {
                if (!settings.CanUseWallpaper(block)) continue;
                for (int side = 0; side < 2; side++)
                {
                    ulong old = block.HasWallpaper(side) ? block.GetWallpaperSkin(side) : 0UL;
                    if (remove)
                    {
                        if (block.HasWallpaper(side)) faces.Add(new FaceChange { Block = block, Side = side, Old = old });
                        continue;
                    }

                    int slot = SlotOf(settings.GetCategory(block, side));
                    if (slot < 0) continue;
                    ulong wanted = preferences.Wallpapers[slot];
                    if (wanted == 0UL || (block.HasWallpaper(side) && old == wanted)) continue;
                    faces.Add(new FaceChange { Block = block, Side = side, Wanted = wanted, Old = old });
                }
            }

            if (faces.Count == 0)
            {
                Reply(player, "Error.NothingToDo");
                return;
            }

            // The game charges a fixed price for each face papered by hand; charge the same here.
            int priceItem = 0;
            float priceEach = 0f;
            ItemAmount price = settings.PlacementPrice;
            if (!remove && _config.Wallpaper.Charge && !PaysNothing(player) && price != null && price.itemDef != null && price.amount > 0f)
            {
                priceItem = price.itemDef.itemid;
                priceEach = price.amount;
                var cost = new Dictionary<int, float> { [priceItem] = priceEach * faces.Count };
                var missing = new Dictionary<int, int>();
                if (!Buildings.TryTake(player, cost, missing))
                {
                    Reply(player, "Error.CannotAfford", DescribeItems(missing));
                    return;
                }
            }

            RunFaces(player, faces, remove ? "Done.WallpaperRemoved" : "Done.Wallpapered", priceItem, priceEach, true);
        }

        // Papers or strips the listed faces a few per tick, refunds any that did not take, and records the rest for undo.
        private void RunFaces(BasePlayer player, List<FaceChange> faces, string doneMessage, int priceItem, float priceEach, bool recordable)
        {
            ulong userId = player.userID;
            var operation = new Operation { Player = player, Recordable = false };
            _operations[userId] = operation;
            TryStartCooldown(player, "operation", TimeSpan.FromSeconds(_config.General.CooldownSeconds), out _);
            Reply(player, "Operation.Started", faces.Count);

            var done = new List<FaceChange>();
            Rogue.Workloads.Batch(Name, "operation:" + userId, faces.Count, _config.General.BlocksPerTick, TimeSpan.Zero,
                index =>
                {
                    FaceChange face = faces[index];
                    BuildingBlock block = face.Block;
                    if (block == null || block.IsDestroyed)
                    {
                        operation.Skipped++;
                        return true;
                    }

                    if (face.Wanted == 0UL)
                    {
                        if (block.HasWallpaper(face.Side)) block.RemoveWallpaper(face.Side);
                    }
                    else
                    {
                        block.SetWallpaper(face.Wanted, face.Side, 0f);
                        // The second face has to be indoors. This is the game's own check: it takes
                        // the paper back off a face that turns out to be outside.
                        block.CheckWallpaper();
                        if (!block.HasWallpaper(face.Side) || block.GetWallpaperSkin(face.Side) != face.Wanted)
                        {
                            operation.Skipped++;
                            return true;
                        }
                    }

                    block.SendNetworkUpdate();
                    operation.Applied++;
                    done.Add(face);
                    return true;
                },
                () =>
                {
                    _operations.Remove(userId);
                    bool online = player != null && player.IsConnected;

                    var paid = new Dictionary<int, float>();
                    if (priceItem != 0 && priceEach > 0f)
                    {
                        // Faces that did not take were paid for up front; hand that part back.
                        if (online && operation.Skipped != 0)
                            Buildings.Give(player, new Dictionary<int, float> { [priceItem] = priceEach * operation.Skipped });
                        if (operation.Applied != 0) paid[priceItem] = priceEach * operation.Applied;
                    }

                    if (recordable && _config.Undo.Enabled && done.Count != 0)
                    {
                        var record = new UndoRecord { ExpiresAt = Time.realtimeSinceStartup + _config.Undo.WindowSeconds, Paid = paid };
                        record.Faces.AddRange(done);
                        _undo[userId] = record;
                    }

                    if (!online) return;
                    Reply(player, doneMessage, operation.Applied, string.Empty);
                    if (operation.Skipped != 0) Reply(player, "Operation.FacesSkipped", operation.Skipped);
                    DrawMenu(player);
                });
        }

        #endregion

        #region Commands

        [RogueCommand("bm",
            Aliases = new[] { "building", "bgrade" },
            Description = "Opens the building manager, or runs one of its actions directly.",
            Usage = "/bm [up|down <grade>] [grade <grade>] [auto] [hammer] [skins] [wallpaper|wallpaper off] [undo]",
            Category = "Building", Permission = UsePermission, CooldownSeconds = 0.5, AllowChat = true, AllowConsole = false)]
        private RogueCommandResult BuildingCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null) return RogueCommandResult.Fail("This command can only be used in game.");
            if (!_ready) return RogueCommandResult.Fail(Message("Error.NotReady", player));

            if (args == null || args.Length == 0)
            {
                OpenMenu(player);
                return RogueCommandResult.Ok(string.Empty);
            }

            PlayerPreferences preferences = Preferences(player);
            string argument = args.Length > 1 ? args[1] : null;
            switch (args[0].ToLowerInvariant())
            {
                case "up":
                case "upgrade":
                    return RunGradeCommand(player, argument, preferences, true);
                case "down":
                case "downgrade":
                    return RunGradeCommand(player, argument, preferences, false);
                case "grade":
                    int grade;
                    if (!RogueBuildingGrades.TryParse(argument, out grade)) return RogueCommandResult.Fail(Message("Error.UnknownGrade", player));
                    preferences.TargetGrade = grade;
                    SavePreferences();
                    return RogueCommandResult.Ok(string.Format(Message("Set.Grade", player), RogueBuildingGrades.DisplayName(grade)));
                case "auto":
                    return RogueCommandResult.Ok(ToggleAutoGrade(player, preferences));
                case "hammer":
                    return RogueCommandResult.Ok(ToggleHammer(player, preferences));
                case "skin":
                case "skins":
                    ApplySkins(player);
                    return RogueCommandResult.Ok(string.Empty);
                case "wallpaper":
                    ApplyWallpaper(player, argument != null && (argument.Equals("off", StringComparison.OrdinalIgnoreCase) || argument.Equals("remove", StringComparison.OrdinalIgnoreCase)));
                    return RogueCommandResult.Ok(string.Empty);
                case "undo":
                    Undo(player);
                    return RogueCommandResult.Ok(string.Empty);
                default:
                    return RogueCommandResult.Fail("Usage: /bm [up|down <grade>] [grade <grade>] [auto] [hammer] [skins] [wallpaper|wallpaper off] [undo]");
            }
        }

        [RogueCommand("up", Description = "Upgrades the whole building to a grade (your chosen grade when none is given).",
            Usage = "/up [wood|stone|metal|hqm]", Category = "Building", Permission = BulkPermission, CooldownSeconds = 1, AllowChat = true, AllowConsole = false)]
        private RogueCommandResult UpCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null || !_ready) return RogueCommandResult.Fail(Message("Error.NotReady", player));
            return RunGradeCommand(player, args != null && args.Length > 0 ? args[0] : null, Preferences(player), true);
        }

        [RogueCommand("down", Description = "Downgrades the whole building to a grade (one step below your chosen grade when none is given).",
            Usage = "/down [twig|wood|stone|metal]", Category = "Building", Permission = BulkPermission, CooldownSeconds = 1, AllowChat = true, AllowConsole = false)]
        private RogueCommandResult DownCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null || !_ready) return RogueCommandResult.Fail(Message("Error.NotReady", player));
            return RunGradeCommand(player, args != null && args.Length > 0 ? args[0] : null, Preferences(player), false);
        }

        private RogueCommandResult RunGradeCommand(BasePlayer player, string argument, PlayerPreferences preferences, bool upgrade)
        {
            int grade = upgrade ? preferences.TargetGrade : RogueBuildingGrades.Step(preferences.TargetGrade, -1);
            if (argument != null && !RogueBuildingGrades.TryParse(argument, out grade))
                return RogueCommandResult.Fail(Message("Error.UnknownGrade", player));
            if (upgrade && grade <= RogueBuildingGrades.Twig) return RogueCommandResult.Fail(Message("Error.UnknownGrade", player));

            ChangeGrades(player, grade, upgrade);
            return RogueCommandResult.Ok(string.Empty);
        }

        private string ToggleAutoGrade(BasePlayer player, PlayerPreferences preferences)
        {
            if (!_config.AutoGrade.Enabled) return Message("Error.FeatureOff", player);
            preferences.AutoGrade = !preferences.AutoGrade;
            _autoGradeUntil.Remove(player.userID);
            SavePreferences();
            return string.Format(Message(preferences.AutoGrade ? "Set.AutoOn" : "Set.AutoOff", player), RogueBuildingGrades.DisplayName(preferences.TargetGrade));
        }

        private string ToggleHammer(BasePlayer player, PlayerPreferences preferences)
        {
            if (!_config.Hammer.Enabled) return Message("Error.FeatureOff", player);
            preferences.Hammer = !preferences.Hammer;
            SavePreferences();
            return string.Format(Message(preferences.Hammer ? "Set.HammerOn" : "Set.HammerOff", player), RogueBuildingGrades.DisplayName(preferences.TargetGrade));
        }

        private void Reply(BasePlayer player, string key, params object[] arguments)
        {
            if (player == null || !player.IsConnected) return;
            string text = Message(key, player);
            if (arguments != null && arguments.Length != 0) text = string.Format(CultureInfo.InvariantCulture, text, arguments);
            player.ChatMessage(_config.General.Prefix + text);
        }

        #endregion

        #region Menu

        // Colours match the RogueRust administration menu.
        private const string ColourWindow = "0.008 0.020 0.028 0.96";
        private const string ColourHeader = "0.012 0.035 0.045 0.97";
        private const string ColourSurface = "0.018 0.045 0.056 0.96";
        private const string ColourButton = "0.025 0.060 0.072 0.98";
        private const string ColourActive = "0.10 0.68 0.82 1";
        private const string ColourBrand = "0.95 0.42 0.12 1";
        private const string ColourDanger = "0.70 0.15 0.13 1";
        private const string ColourText = "0.92 0.95 0.96 1";
        private const string ColourMuted = "0.58 0.68 0.72 1";
        private const string ColourClear = "0 0 0 0";

        // The menu is a frame that is drawn once, plus one small document per section. A click only
        // resends the sections whose content changed, so the window never blinks and a change of
        // wallpaper does not rebuild the grade buttons. Each section has its own strip of the frame;
        // they must not overlap, because even an invisible panel swallows clicks meant for one beneath.
        private const string UiGrades = UiRoot + ".Grades";
        private const string UiToggles = UiRoot + ".Toggles";
        private const string UiSkins = UiRoot + ".Skins";
        private const string UiWallpaper = UiRoot + ".Wallpaper";
        private const string UiActions = UiRoot + ".Actions";
        private const string UiPicker = UiRoot + ".Picker";
        private const string Frame = "rrbm.frame";
        private const int PickerColumns = 3;
        private const int PickerRows = 12;

        private sealed class PickerState
        {
            public int Slot;
            public int Page;
        }

        private readonly Dictionary<ulong, PickerState> _pickers = new Dictionary<ulong, PickerState>();

        private void OpenMenu(BasePlayer player)
        {
            if (!_menuOpen.Add(player.userID))
            {
                DrawSections(player, false);
                return;
            }

            // The cursor lives on its own element so redrawing a section never drops and re-grabs it.
            var cursor = new CuiElementContainer();
            cursor.Add(new CuiElement
            {
                Name = UiCursor,
                Parent = "Overlay",
                Components = { new CuiNeedsCursorComponent(), new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } }
            });
            DestroyUi(player, UiCursor);
            CuiHelper.AddUi(player, cursor);

            RogueUiDocument shell = CreateUi(UiRoot);
            shell.Panel(UiRoot, "Overlay", RogueUiRect.Full, "0 0 0 0.25");
            shell.Panel(Frame, UiRoot, Rect(0.22f, 0.16f, 0.78f, 0.84f), ColourWindow);
            shell.Panel("rrbm.header", Frame, Rect(0f, 0.91f, 1f, 1f), ColourHeader);
            shell.Label("rrbm.title", "rrbm.header", Rect(0.03f, 0f, 0.8f, 1f), Message("Menu.Title", player), 16, ColourText, "MiddleLeft");
            shell.Button("rrbm.close", "rrbm.header", Rect(0.945f, 0.2f, 0.985f, 0.8f), "×",
                UiActionCallback(player, "close", () => CloseMenu(player)), ColourDanger, 15);
            ShowUi(player, shell);

            // The frame was just (re)created, so every section has to be sent whatever it held before.
            DrawSections(player, true);
        }

        private void CloseMenu(BasePlayer player)
        {
            if (player == null || !_menuOpen.Remove(player.userID)) return;
            _pickers.Remove(player.userID);
            DestroyUi(player, UiPicker);
            DestroyUi(player, UiGrades);
            DestroyUi(player, UiToggles);
            DestroyUi(player, UiSkins);
            DestroyUi(player, UiWallpaper);
            DestroyUi(player, UiActions);
            DestroyUi(player, UiRoot);
            DestroyUi(player, UiCursor);
        }

        private static RogueUiRect Rect(float minX, float minY, float maxX, float maxY)
        {
            return new RogueUiRect(
                minX.ToString("0.####", CultureInfo.InvariantCulture) + " " + minY.ToString("0.####", CultureInfo.InvariantCulture),
                maxX.ToString("0.####", CultureInfo.InvariantCulture) + " " + maxY.ToString("0.####", CultureInfo.InvariantCulture));
        }

        // Kept for callers elsewhere in the plugin: refresh whatever the menu is showing.
        private void DrawMenu(BasePlayer player)
        {
            if (player != null && player.IsConnected && _menuOpen.Contains(player.userID)) DrawSections(player, false);
        }

        private void MenuAction(BasePlayer player, Action action, bool redraw = true)
        {
            if (player == null || !player.IsConnected || !_menuOpen.Contains(player.userID)) return;
            action();
            if (redraw) DrawMenu(player);
        }

        private void Send(BasePlayer player, RogueUiDocument section, bool force)
        {
            if (force) ShowUi(player, section);
            else ShowUiIfChanged(player, section);
        }

        // A section: a see-through strip of the frame that its controls are placed inside.
        private RogueUiDocument Section(string name, float minY, float maxY)
        {
            RogueUiDocument ui = CreateUi(name, Frame);
            ui.Panel(name, Frame, Rect(0f, minY, 1f, maxY), ColourClear);
            return ui;
        }

        private void DrawSections(BasePlayer player, bool force)
        {
            if (player == null || !player.IsConnected) return;
            PlayerPreferences preferences = Preferences(player);
            bool bulk = Allowed(player, BulkPermission);
            bool skins = _config.Skins.Enabled && Allowed(player, SkinsPermission);
            bool wallpaper = _config.Wallpaper.Enabled && Allowed(player, WallpaperPermission);

            Send(player, GradeSection(player, preferences), force);
            Send(player, ToggleSection(player, preferences), force);
            Send(player, SkinSection(player, preferences, skins), force);
            Send(player, WallpaperSection(player, preferences, wallpaper), force);
            Send(player, ActionSection(player, preferences, bulk, skins, wallpaper), force);
        }

        private RogueUiDocument GradeSection(BasePlayer player, PlayerPreferences preferences)
        {
            RogueUiDocument ui = Section(UiGrades, 0.78f, 0.905f);
            ui.Label("rrbm.grade.label", UiGrades, Rect(0.03f, 0.62f, 0.5f, 0.98f), Message("Menu.TargetGrade", player), 11, ColourMuted, "MiddleLeft");
            for (int grade = RogueBuildingGrades.Twig; grade <= RogueBuildingGrades.Armoured; grade++)
            {
                int chosen = grade;
                float x = 0.03f + grade * 0.19f;
                ui.Button("rrbm.grade." + grade, UiGrades, Rect(x, 0.08f, x + 0.18f, 0.58f), RogueBuildingGrades.DisplayName(grade).ToUpperInvariant(),
                    UiActionCallback(player, "grade." + grade, () => MenuAction(player, () => { preferences.TargetGrade = chosen; SavePreferences(); })),
                    preferences.TargetGrade == grade ? ColourActive : ColourButton, 11);
            }
            return ui;
        }

        private RogueUiDocument ToggleSection(BasePlayer player, PlayerPreferences preferences)
        {
            RogueUiDocument ui = Section(UiToggles, 0.70f, 0.78f);
            ToggleButton(ui, player, "auto", 0.03f, Message("Menu.AutoGrade", player), _config.AutoGrade.Enabled && preferences.AutoGrade, _config.AutoGrade.Enabled,
                () => Reply(player, "Plain", ToggleAutoGrade(player, preferences)));
            ToggleButton(ui, player, "hammer", 0.515f, Message("Menu.Hammer", player), _config.Hammer.Enabled && preferences.Hammer, _config.Hammer.Enabled,
                () => Reply(player, "Plain", ToggleHammer(player, preferences)));
            return ui;
        }

        private void ToggleButton(RogueUiDocument ui, BasePlayer player, string key, float x, string label, bool on, bool available, Action toggle)
        {
            string state = !available ? Message("Menu.Unavailable", player) : on ? Message("Menu.On", player) : Message("Menu.Off", player);
            ui.Button("rrbm.toggle." + key, UiToggles, Rect(x, 0.12f, x + 0.455f, 0.88f), label + "   " + state,
                UiActionCallback(player, "toggle." + key, () => MenuAction(player, () => { if (available) toggle(); })),
                on ? ColourActive : ColourButton, 11);
        }

        private RogueUiDocument SkinSection(BasePlayer player, PlayerPreferences preferences, bool enabled)
        {
            RogueUiDocument ui = Section(UiSkins, 0.42f, 0.70f);
            if (!enabled) return ui;

            ui.Label("rrbm.skins.label", UiSkins, Rect(0.03f, 0.84f, 0.5f, 0.98f), Message("Menu.Skins", player), 11, ColourMuted, "MiddleLeft");
            float top = 0.82f;
            for (int grade = RogueBuildingGrades.Wood; grade <= RogueBuildingGrades.Armoured; grade++)
            {
                IReadOnlyList<RogueBuildingSkinEntry> options = Rogue.Skins.GetBuildingSkins(grade);
                if (options == null || options.Count < 2) continue;

                int row = grade;
                ulong current;
                preferences.Skins.TryGetValue(grade, out current);
                ui.Label("rrbm.skins." + grade, UiSkins, Rect(0.03f, top - 0.18f, 0.15f, top - 0.02f), RogueBuildingGrades.DisplayName(grade), 11, ColourText, "MiddleLeft");
                float width = Mathf.Min(0.16f, 0.80f / options.Count);
                for (int i = 0; i < options.Count; i++)
                {
                    RogueBuildingSkinEntry option = options[i];
                    float x = 0.16f + i * width;
                    ui.Button("rrbm.skins." + grade + "." + i, UiSkins, Rect(x, top - 0.18f, x + width - 0.008f, top - 0.02f), option.Name,
                        UiActionCallback(player, "skin." + grade + "." + i, () => MenuAction(player, () => { preferences.Skins[row] = option.SkinId; SavePreferences(); })),
                        current == option.SkinId ? ColourActive : ColourButton, 10);
                }
                top -= 0.20f;
            }
            return ui;
        }

        private RogueUiDocument WallpaperSection(BasePlayer player, PlayerPreferences preferences, bool enabled)
        {
            RogueUiDocument ui = Section(UiWallpaper, 0.17f, 0.42f);
            if (!enabled) return ui;

            ui.Label("rrbm.wall.label", UiWallpaper, Rect(0.03f, 0.80f, 0.5f, 0.98f), Message("Menu.Wallpaper", player), 11, ColourMuted, "MiddleLeft");
            WallpaperRow(ui, player, preferences, SlotWall, Message("Menu.Wall", player), 0.54f);
            WallpaperRow(ui, player, preferences, SlotFloor, Message("Menu.Floor", player), 0.28f);
            WallpaperRow(ui, player, preferences, SlotCeiling, Message("Menu.Ceiling", player), 0.02f);
            return ui;
        }

        // One wallpaper slot: its name and a button showing the current choice, which opens the picker.
        private void WallpaperRow(RogueUiDocument ui, BasePlayer player, PlayerPreferences preferences, int slot, string label, float y)
        {
            List<WallpaperOption> options = _wallpapers[slot];
            int index = options.FindIndex(option => option.Id == preferences.Wallpapers[slot]);
            string name = index >= 0 ? options[index].Name : Message("Menu.None", player);

            ui.Label("rrbm.wall." + slot, UiWallpaper, Rect(0.03f, y, 0.15f, y + 0.22f), label, 11, ColourText, "MiddleLeft");
            ui.Button("rrbm.wall." + slot + ".pick", UiWallpaper, Rect(0.16f, y, 0.66f, y + 0.22f), name + "   ▾",
                UiActionCallback(player, "wall." + slot + ".pick", () => MenuAction(player, () => OpenPicker(player, slot), false)),
                index >= 0 ? ColourSurface : ColourButton, 11);
            ui.Label("rrbm.wall." + slot + ".count", UiWallpaper, Rect(0.68f, y, 0.97f, y + 0.22f),
                string.Format(CultureInfo.InvariantCulture, Message("Menu.Choices", player), options.Count), 10, ColourMuted, "MiddleLeft");
        }

        private RogueUiDocument ActionSection(BasePlayer player, PlayerPreferences preferences, bool bulk, bool skins, bool wallpaper)
        {
            RogueUiDocument ui = CreateUi(UiActions, Frame);
            ui.Panel(UiActions, Frame, Rect(0f, 0f, 1f, 0.15f), ColourSurface);

            string gradeName = RogueBuildingGrades.DisplayName(preferences.TargetGrade);
            string lowerName = RogueBuildingGrades.DisplayName(RogueBuildingGrades.Step(preferences.TargetGrade, -1));
            int slot = 0;
            if (bulk && preferences.TargetGrade > RogueBuildingGrades.Twig)
            {
                ActionButton(ui, player, slot++, "up", string.Format(Message("Menu.Upgrade", player), gradeName), ColourActive,
                    () => ChangeGrades(player, preferences.TargetGrade, true));
                ActionButton(ui, player, slot++, "down", string.Format(Message("Menu.Downgrade", player), lowerName), ColourButton,
                    () => ChangeGrades(player, RogueBuildingGrades.Step(preferences.TargetGrade, -1), false));
            }
            if (skins) ActionButton(ui, player, slot++, "skins", Message("Menu.ApplySkins", player), ColourButton, () => ApplySkins(player));
            if (wallpaper) ActionButton(ui, player, slot++, "wallpaper", Message("Menu.ApplyWallpaper", player), ColourButton, () => ApplyWallpaper(player, false));
            if (wallpaper) ActionButton(ui, player, slot++, "wallpaper.off", Message("Menu.RemoveWallpaper", player), ColourButton, () => ApplyWallpaper(player, true));
            if (_config.Undo.Enabled) ActionButton(ui, player, slot, "undo", Message("Menu.Undo", player), ColourBrand, () => Undo(player));
            return ui;
        }

        private void ActionButton(RogueUiDocument ui, BasePlayer player, int slot, string key, string label, string colour, Action action)
        {
            int column = slot % 3;
            int row = slot / 3;
            float x = 0.03f + column * 0.318f;
            float top = 0.92f - row * 0.46f;
            ui.Button("rrbm.action." + key, UiActions, Rect(x, top - 0.38f, x + 0.305f, top), label,
                UiActionCallback(player, "action." + key, () => MenuAction(player, action, false)), colour, 11);
        }

        #endregion

        #region Wallpaper picker

        // A drop-down list in all but name: a panel listing every wallpaper for one slot, a page at a
        // time, opened from the slot's button. Choosing one closes it and updates only that section.
        private void OpenPicker(BasePlayer player, int slot)
        {
            _pickers[player.userID] = new PickerState { Slot = slot };
            DrawPicker(player);
        }

        private void ClosePicker(BasePlayer player)
        {
            if (player == null) return;
            _pickers.Remove(player.userID);
            DestroyUi(player, UiPicker);
        }

        private void DrawPicker(BasePlayer player)
        {
            PickerState state;
            if (player == null || !player.IsConnected || !_menuOpen.Contains(player.userID) || !_pickers.TryGetValue(player.userID, out state)) return;

            PlayerPreferences preferences = Preferences(player);
            List<WallpaperOption> options = _wallpapers[state.Slot];
            int perPage = PickerColumns * PickerRows;
            // Entry 0 is "None"; the wallpapers follow it.
            int total = options.Count + 1;
            int pages = Math.Max(1, (total + perPage - 1) / perPage);
            state.Page = Mathf.Clamp(state.Page, 0, pages - 1);
            ulong current = preferences.Wallpapers[state.Slot];
            string slotName = Message(state.Slot == SlotWall ? "Menu.Wall" : state.Slot == SlotFloor ? "Menu.Floor" : "Menu.Ceiling", player);

            RogueUiDocument ui = CreateUi(UiPicker);
            // The backdrop covers the menu so a click outside the list closes it, as a drop-down would.
            ui.Button(UiPicker, "Overlay", RogueUiRect.Full, string.Empty, UiActionCallback(player, "picker.dismiss", () => ClosePicker(player)), "0 0 0 0.55", 1);
            ui.Panel("rrbm.picker.frame", UiPicker, Rect(0.25f, 0.14f, 0.75f, 0.86f), ColourWindow);
            ui.Panel("rrbm.picker.header", "rrbm.picker.frame", Rect(0f, 0.925f, 1f, 1f), ColourHeader);
            ui.Label("rrbm.picker.title", "rrbm.picker.header", Rect(0.03f, 0f, 0.8f, 1f),
                string.Format(CultureInfo.InvariantCulture, Message("Picker.Title", player), slotName, options.Count), 14, ColourText, "MiddleLeft");
            ui.Button("rrbm.picker.close", "rrbm.picker.header", Rect(0.94f, 0.18f, 0.985f, 0.82f), "×",
                UiActionCallback(player, "picker.close", () => ClosePicker(player)), ColourDanger, 14);

            const float top = 0.91f;
            const float bottom = 0.09f;
            float rowHeight = (top - bottom) / PickerRows;
            float columnWidth = 0.94f / PickerColumns;
            int first = state.Page * perPage;
            int last = Math.Min(total, first + perPage);
            for (int entry = first; entry < last; entry++)
            {
                int cell = entry - first;
                // Fill down each column in turn, so the list reads top to bottom like a menu.
                int column = cell / PickerRows;
                int row = cell % PickerRows;
                float x = 0.03f + column * columnWidth;
                float y = top - (row + 1) * rowHeight;

                ulong id = entry == 0 ? 0UL : options[entry - 1].Id;
                string name = entry == 0 ? Message("Menu.None", player) : options[entry - 1].Name;
                int slot = state.Slot;
                ui.Button("rrbm.picker." + cell, "rrbm.picker.frame", Rect(x, y + 0.006f, x + columnWidth - 0.012f, y + rowHeight - 0.004f), name,
                    UiActionCallback(player, "picker.pick." + cell, () => PickWallpaper(player, slot, id)),
                    id == current ? ColourActive : ColourButton, 11);
            }

            if (pages > 1)
            {
                ui.Button("rrbm.picker.prev", "rrbm.picker.frame", Rect(0.03f, 0.015f, 0.18f, 0.075f), "‹  " + Message("Picker.Previous", player),
                    UiActionCallback(player, "picker.prev", () => TurnPickerPage(player, -1)), ColourButton, 11);
                ui.Label("rrbm.picker.page", "rrbm.picker.frame", Rect(0.40f, 0.015f, 0.60f, 0.075f),
                    (state.Page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pages.ToString(CultureInfo.InvariantCulture), 11, ColourMuted, "MiddleCenter");
                ui.Button("rrbm.picker.next", "rrbm.picker.frame", Rect(0.82f, 0.015f, 0.97f, 0.075f), Message("Picker.Next", player) + "  ›",
                    UiActionCallback(player, "picker.next", () => TurnPickerPage(player, 1)), ColourButton, 11);
            }

            ShowUiIfChanged(player, ui);
        }

        private void TurnPickerPage(BasePlayer player, int direction)
        {
            PickerState state;
            if (player == null || !_pickers.TryGetValue(player.userID, out state)) return;
            state.Page += direction;
            DrawPicker(player);
        }

        private void PickWallpaper(BasePlayer player, int slot, ulong id)
        {
            if (player == null || !_menuOpen.Contains(player.userID)) return;
            Preferences(player).Wallpapers[slot] = id;
            SavePreferences();
            ClosePicker(player);
            DrawMenu(player);
        }

        #endregion

        #region API

        // Lets other plugins hand building work to this one (RogueRustSkins does for structure skins).
        private void OpenBuildingManager(BasePlayer player)
        {
            if (player != null && _ready && Allowed(player, UsePermission)) OpenMenu(player);
        }

        // skins: grade number to skin id, as chosen in another plugin's menu. Null keeps the choices made here.
        private void ApplyBuildingSkins(BasePlayer player, Dictionary<int, ulong> skins)
        {
            if (player == null || !_ready) return;
            if (skins != null)
            {
                PlayerPreferences preferences = Preferences(player);
                foreach (KeyValuePair<int, ulong> entry in skins)
                    if (RogueBuildingGrades.IsValid(entry.Key)) preferences.Skins[entry.Key] = entry.Value;
                SavePreferences();
            }
            ApplySkins(player, skins != null);
        }

        #endregion

        #region Messages

        protected override IReadOnlyDictionary<string, string> DefaultMessages
        {
            get
            {
                return new Dictionary<string, string>
                {
                    ["Plain"] = "{0}",
                    ["Error.NoPermission"] = "You do not have permission to do that.",
                    ["Error.NotReady"] = "The building manager is not available right now.",
                    ["Error.FeatureOff"] = "That feature is turned off on this server.",
                    ["Error.NoBuilding"] = "Look at a building block, or stand inside the range of a tool cupboard, and try again.",
                    ["Error.NotAuthorised"] = "You need to be authorised on this building's tool cupboard.",
                    ["Error.RaidBlocked"] = "You cannot change a building while raid-blocked.",
                    ["Error.Busy"] = "Your last operation is still running.",
                    ["Error.Cooldown"] = "Wait {0} more second(s) before starting another operation.",
                    ["Error.NothingToDo"] = "Nothing in this building needs changing.",
                    ["Error.NothingToUndo"] = "There is nothing to undo.",
                    ["Error.CannotAfford"] = "You are short of: {0}.",
                    ["Error.CannotAffordBlock"] = "You cannot afford to take that block to {0}.",
                    ["Error.UnknownGrade"] = "Choose a grade: twig, wood, stone, metal or hqm.",
                    ["Error.RecentlyDamaged"] = "That block was attacked too recently.",
                    ["Error.Obstructed"] = "Something is in the way of that block.",
                    ["Error.NoWallpaperChosen"] = "Choose a wallpaper in /bm first.",
                    ["Operation.Started"] = "Working through {0} block(s)...",
                    ["Operation.Skipped"] = "{0} block(s) were skipped: attacked recently, obstructed or removed. Anything you paid for them has been returned.",
                    ["Operation.FacesSkipped"] = "{0} face(s) were skipped: outdoors or removed. Anything you paid for them has been returned.",
                    ["Done.Upgraded"] = "Upgraded {0} block(s) to {1}.",
                    ["Done.Downgraded"] = "Downgraded {0} block(s) to {1}.",
                    ["Done.Skinned"] = "Re-skinned {0} block(s).",
                    ["Done.Wallpapered"] = "Wallpapered {0} block(s).",
                    ["Done.WallpaperRemoved"] = "Removed wallpaper from {0} block(s).",
                    ["Done.Undone"] = "Put {0} block(s) back as they were.",
                    ["Set.Grade"] = "Target grade set to {0}.",
                    ["Set.AutoOn"] = "Auto grade is on: blocks you place become {0}.",
                    ["Set.AutoOff"] = "Auto grade is off.",
                    ["Set.HammerOn"] = "Hammer grading is on: hit a block to make it {0}.",
                    ["Set.HammerOff"] = "Hammer grading is off.",
                    ["AutoGrade.TimedOut"] = "Auto grade switched itself off after a quiet spell.",
                    ["Menu.Title"] = "BUILDING MANAGER",
                    ["Menu.TargetGrade"] = "TARGET GRADE",
                    ["Menu.AutoGrade"] = "Auto grade placed blocks",
                    ["Menu.Hammer"] = "Grade with the hammer",
                    ["Menu.On"] = "ON",
                    ["Menu.Off"] = "OFF",
                    ["Menu.Unavailable"] = "UNAVAILABLE",
                    ["Menu.Skins"] = "SKIN FOR EACH GRADE",
                    ["Menu.Wallpaper"] = "WALLPAPER",
                    ["Menu.Wall"] = "Walls",
                    ["Menu.Floor"] = "Floors",
                    ["Menu.Ceiling"] = "Ceilings",
                    ["Menu.None"] = "None",
                    ["Menu.Choices"] = "{0} to choose from",
                    ["Picker.Title"] = "{0}: choose a wallpaper ({1} available)",
                    ["Picker.Previous"] = "Previous",
                    ["Picker.Next"] = "Next",
                    ["Menu.Upgrade"] = "Upgrade base to {0}",
                    ["Menu.Downgrade"] = "Downgrade base to {0}",
                    ["Menu.ApplySkins"] = "Apply skins to base",
                    ["Menu.ApplyWallpaper"] = "Wallpaper the base",
                    ["Menu.RemoveWallpaper"] = "Remove wallpaper",
                    ["Menu.Undo"] = "Undo last operation"
                };
            }
        }

        #endregion
    }
}
