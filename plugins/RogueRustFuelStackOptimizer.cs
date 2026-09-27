using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RogueRustFuelStackOptimizer", "RogueAssassin", "2.1.0")]
    [Description("Optimises dedicated Rust fuel containers through cached discovery using native Oxide/Carbon hooks.")]
    public sealed class RogueRustFuelStackOptimizer : RogueRustPlugin
    {
        [RoguePermission]
        private const string AdminPermission = "roguerustfuelstackoptimizer.admin";
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);

        private static readonly string[] DefaultFuelItems =
        {
            "lowgradefuel",
            "diesel_barrel",
            "wood",
            "crude.oil"
        };

        private static readonly string[] FuelHints =
        {
            "fuel", "generator", "quarry", "extractor", "excavator", "furnace", "oven", "refinery",
            "campfire", "bbq", "lantern", "tunalight", "minicopter", "helicopter", "hotairballoon",
            "motorbike", "snowmobile", "boat", "submarine", "car_fuel"
        };

        private ConfigData _config;
        private readonly Dictionary<string, TrackedContainer> _containers =
            new Dictionary<string, TrackedContainer>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Type, DiscoveryPlan> _typePlans = new Dictionary<Type, DiscoveryPlan>();
        private readonly Queue<BaseEntity> _scanQueue = new Queue<BaseEntity>();
        private readonly Queue<TrackedContainer> _applyQueue = new Queue<TrackedContainer>();
        private HashSet<string> _recognisedFuelItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _enabledKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _scanScheduled;
        private bool _applyScheduled;
        private bool _logScanCompletion;
        private bool _unloading;
        private int _skippedEntityTypes;

        private enum FuelContainerKind
        {
            Generator, Quarry, Excavator, Refinery, Furnace, Oven, Light, Vehicle, Other
        }

        private sealed class TrackedContainer
        {
            public BaseEntity Entity;
            public ItemContainer Container;
            public string MemberName;
            public string Identity;
            public FuelContainerKind Kind;
            public bool Dedicated;
        }

        private sealed class DiscoveryPlan
        {
            public MemberInfo[] Members;
            public bool HasDedicatedMember;
            public bool HasAnyContainer;
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
            public bool Equals(T left, T right) { return ReferenceEquals(left, right); }
            public int GetHashCode(T value) { return RuntimeHelpers.GetHashCode(value); }
        }

        private class ConfigData
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Container Settings", Order = 20)]
            public ContainerSettings Containers = new ContainerSettings();

            [JsonProperty("Owner Filter Settings", Order = 30)]
            public OwnerFilterSettings Owners = new OwnerFilterSettings();

            [JsonProperty("Performance Settings", Order = 40)]
            public PerformanceSettings Performance = new PerformanceSettings();

            [JsonProperty("Developer Settings", Order = 50)]
            public DeveloperSettings Developer = new DeveloperSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private class GeneralSettings
        {
            [JsonProperty("Global Max Fuel Stack Size")]
            public int GlobalStackMax = 1000;

            [JsonProperty("Recognised Fuel Item Short Names", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RecognisedFuelItems = new List<string>(DefaultFuelItems);
        }

        private class ContainerSettings
        {
            [JsonProperty("Only Modify Dedicated Fuel Containers")]
            public bool DedicatedFuelContainersOnly = true;

            [JsonProperty("Enabled Container Kinds", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EnabledKinds = DefaultKinds();

            [JsonProperty("Per Entity Overrides by NetID (NetID : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, int> EntityOverrides = new Dictionary<ulong, int>();

            [JsonProperty("Per Entity Overrides by Short Prefab Name (Name : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> NameOverrides =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("Per Entity Overrides by Full Prefab Path (Prefab : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> PrefabOverrides =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private class OwnerFilterSettings
        {
            [JsonProperty("Whitelist Owners by Steam ID or current name (empty = all)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> WhitelistPlayers = new List<string>();

            [JsonProperty("Blacklist Owners by Steam ID or current name", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistPlayers = new List<string>();
        }

        private class PerformanceSettings
        {
            [JsonProperty("Enable Batch Processing")]
            public bool EnableBatchProcessing = true;

            [JsonProperty("Discovery Entities per Tick")]
            public int DiscoveryBatchSize = 40;

            [JsonProperty("Stack Updates per Tick")]
            public int ApplyBatchSize = 25;

            [JsonProperty("Rescan Interval in Seconds (0 = disabled)")]
            public float RescanInterval = 0f;
        }

        private class DeveloperSettings
        {
            [JsonProperty("Log Detailed Audit on Startup")]
            public bool LogDetailedAuditOnStartup = false;
        }

        private class LegacyConfigData
        {
            [JsonProperty("Global Max Fuel Stack Size")]
            public int GlobalStackMax = 1000;
            [JsonProperty("Only Modify Dedicated Fuel Containers")]
            public bool DedicatedFuelContainersOnly = true;
            [JsonProperty("Enabled Container Kinds", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> EnabledKinds = DefaultKinds();
            [JsonProperty("Recognised Fuel Item Short Names", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RecognisedFuelItems = new List<string>(DefaultFuelItems);
            [JsonProperty("Per Entity Overrides by NetID (NetID : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<ulong, int> EntityOverrides = new Dictionary<ulong, int>();
            [JsonProperty("Per Entity Overrides by Short Prefab Name (Name : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> NameOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            [JsonProperty("Per Entity Overrides by Full Prefab Path (Prefab : MaxStack)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> PrefabOverrides = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            [JsonProperty("Whitelist Owners by Steam ID or current name (empty = all)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> WhitelistPlayers = new List<string>();
            [JsonProperty("Blacklist Owners by Steam ID or current name", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> BlacklistPlayers = new List<string>();
            [JsonProperty("Enable Batch Processing")]
            public bool EnableBatchProcessing = true;
            [JsonProperty("Discovery Entities per Tick")]
            public int DiscoveryBatchSize = 40;
            [JsonProperty("Stack Updates per Tick")]
            public int ApplyBatchSize = 25;
            [JsonProperty("Rescan Interval in Seconds (0 = disabled)")]
            public float RescanInterval;
            [JsonProperty("Log Detailed Audit on Startup")]
            public bool LogDetailedAuditOnStartup;
        }

        private static List<string> DefaultKinds()
        {
            return new List<string>
            {
                FuelContainerKind.Generator.ToString(),
                FuelContainerKind.Quarry.ToString(),
                FuelContainerKind.Excavator.ToString(),
                FuelContainerKind.Vehicle.ToString(),
                FuelContainerKind.Light.ToString(),
                FuelContainerKind.Other.ToString()
            };
        }

        protected override void LoadDefaultConfig()
        {
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Newtonsoft.Json.Linq.JObject raw = Config.ReadObject<Newtonsoft.Json.Linq.JObject>();
                bool legacy = raw != null && raw["General Settings"] == null &&
                              raw["Global Max Fuel Stack Size"] != null;

                if (legacy)
                {
                    LegacyConfigData old = raw.ToObject<LegacyConfigData>() ?? new LegacyConfigData();
                    _config = new ConfigData();
                    _config.General.GlobalStackMax = old.GlobalStackMax;
                    _config.General.RecognisedFuelItems = old.RecognisedFuelItems;
                    _config.Containers.DedicatedFuelContainersOnly = old.DedicatedFuelContainersOnly;
                    _config.Containers.EnabledKinds = old.EnabledKinds;
                    _config.Containers.EntityOverrides = old.EntityOverrides;
                    _config.Containers.NameOverrides = old.NameOverrides;
                    _config.Containers.PrefabOverrides = old.PrefabOverrides;
                    _config.Owners.WhitelistPlayers = old.WhitelistPlayers;
                    _config.Owners.BlacklistPlayers = old.BlacklistPlayers;
                    _config.Performance.EnableBatchProcessing = old.EnableBatchProcessing;
                    _config.Performance.DiscoveryBatchSize = old.DiscoveryBatchSize;
                    _config.Performance.ApplyBatchSize = old.ApplyBatchSize;
                    _config.Performance.RescanInterval = old.RescanInterval;
                    _config.Developer.LogDetailedAuditOnStartup = old.LogDetailedAuditOnStartup;
                }
                else
                {
                    _config = raw == null ? new ConfigData() : raw.ToObject<ConfigData>();
                }
            }
            catch (Exception exception)
            {
                PrintError("Invalid configuration: " + exception.Message);
                _config = new ConfigData();
            }

            if (_config == null) _config = new ConfigData();
            if (_config.General == null) _config.General = new GeneralSettings();
            if (_config.Containers == null) _config.Containers = new ContainerSettings();
            if (_config.Owners == null) _config.Owners = new OwnerFilterSettings();
            if (_config.Performance == null) _config.Performance = new PerformanceSettings();
            if (_config.Developer == null) _config.Developer = new DeveloperSettings();

            _config.Version = CurrentVersion;
            _config.General.GlobalStackMax = ClampStack(_config.General.GlobalStackMax);
            _config.Performance.DiscoveryBatchSize = Mathf.Clamp(_config.Performance.DiscoveryBatchSize, 5, 250);
            _config.Performance.ApplyBatchSize = Mathf.Clamp(_config.Performance.ApplyBatchSize, 1, 250);
            _config.Performance.RescanInterval = _config.Performance.RescanInterval <= 0f
                ? 0f
                : Mathf.Clamp(_config.Performance.RescanInterval, 60f, 86400f);

            _config.Containers.EnabledKinds = CleanKinds(_config.Containers.EnabledKinds);
            _config.General.RecognisedFuelItems = CleanStrings(_config.General.RecognisedFuelItems, DefaultFuelItems);
            _config.Owners.WhitelistPlayers = CleanStrings(_config.Owners.WhitelistPlayers, null);
            _config.Owners.BlacklistPlayers = CleanStrings(_config.Owners.BlacklistPlayers, null);
            _config.Containers.EntityOverrides = ValidateOverrides(_config.Containers.EntityOverrides);
            _config.Containers.NameOverrides = ValidateOverrides(_config.Containers.NameOverrides);
            _config.Containers.PrefabOverrides = ValidateOverrides(_config.Containers.PrefabOverrides);

            _recognisedFuelItems = new HashSet<string>(_config.General.RecognisedFuelItems, StringComparer.OrdinalIgnoreCase);
            _enabledKinds = new HashSet<string>(_config.Containers.EnabledKinds, StringComparer.OrdinalIgnoreCase);
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=None");
        }

        private void OnServerInitialized()
        {
            BeginFullScan(true);
            if (_config.Performance.RescanInterval > 0f)
                Repeat(TimeSpan.FromSeconds(_config.Performance.RescanInterval),
                    delegate { BeginFullScan(false); }, "fuelstack-rescan");
        }

        private void Unload()
        {
            _unloading = true;
            _scanScheduled = false;
            _applyScheduled = false;
            _scanQueue.Clear();
            _applyQueue.Clear();
            _containers.Clear();
            _typePlans.Clear();
        }

        private void OnEntitySpawned(BaseNetworkable networkable)
        {
            BaseEntity entity = networkable as BaseEntity;
            if (entity == null) return;
            Delay(TimeSpan.Zero,
                delegate { ExecuteProtected(delegate { DiscoverEntity(entity, true); }, "EntitySpawn"); },
                "fuelstack-spawn-" + (entity.net == null ? "0" : entity.net.ID.Value.ToString()));
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            BaseEntity entity = networkable as BaseEntity;
            if (entity == null) return;
            ExecuteProtected(delegate { RemoveEntity(entity); }, "EntityKill");
        }

        private void BeginFullScan(bool logCompletion)
        {
            if (_scanScheduled) return;

            _scanQueue.Clear();
            _applyQueue.Clear();
            _containers.Clear();
            _skippedEntityTypes = 0;
            _logScanCompletion = logCompletion;

            foreach (BaseNetworkable networkable in BaseNetworkable.serverEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity != null) _scanQueue.Enqueue(entity);
            }

            _scanScheduled = true;
            Delay(TimeSpan.FromMilliseconds(10), ProcessDiscoveryBatch, "fuelstack-discovery");
        }

        private void ProcessDiscoveryBatch()
        {
            if (!_scanScheduled) return;

            ExecuteProtected(() =>
            {
                int processed = 0;
                while (_scanQueue.Count > 0 && processed < _config.Performance.DiscoveryBatchSize)
                {
                    DiscoverEntity(_scanQueue.Dequeue(), false);
                    processed++;
                }

                if (_scanQueue.Count > 0)
                {
                    Delay(TimeSpan.FromMilliseconds(10), ProcessDiscoveryBatch, "fuelstack-discovery");
                    return;
                }

                _scanScheduled = false;
                QueueEligibleContainers();
                CompleteDiscovery();
            });
        }

        private void CompleteDiscovery()
        {
            string summary = CreateSummary();
            if (_logScanCompletion) Puts(summary);
            if (_logScanCompletion && _config.Developer.LogDetailedAuditOnStartup)
                Puts(CreateDetailedAuditReport());
            _logScanCompletion = false;

            if (_config.Performance.EnableBatchProcessing) StartApplyProcessing();
            else ApplyAllEligible();
        }

        private void DiscoverEntity(BaseEntity entity, bool publishImmediately)
        {
            if (entity == null || entity.IsDestroyed) return;

            Type type = entity.GetType();
            DiscoveryPlan plan = GetDiscoveryPlan(type);
            if (!plan.HasAnyContainer)
            {
                _skippedEntityTypes++;
                return;
            }

            string typeName = type.Name ?? string.Empty;
            string prefab = entity.PrefabName ?? string.Empty;
            string shortName = entity.ShortPrefabName ?? string.Empty;
            bool entityHint = ContainsHint(typeName) || ContainsHint(prefab) || ContainsHint(shortName);

            // Once a type has no dedicated fuel member, entity-name hints decide whether its containers need reading.
            // This avoids repeatedly reflecting ordinary entities while preserving generic fuel-storage prefabs.
            if (!plan.HasDedicatedMember && !entityHint)
            {
                _skippedEntityTypes++;
                return;
            }

            Dictionary<ItemContainer, TrackedContainer> discovered =
                new Dictionary<ItemContainer, TrackedContainer>(ReferenceComparer<ItemContainer>.Instance);

            for (int i = 0; i < plan.Members.Length; i++)
            {
                MemberInfo member = plan.Members[i];
                ItemContainer container = ReadContainer(member, entity);
                if (container == null) continue;

                string memberName = member.Name ?? string.Empty;
                bool dedicated = IsDedicatedFuelMember(memberName, shortName, prefab);
                bool containsFuel = ContainsRecognisedFuel(container);
                if (!dedicated && !containsFuel && !entityHint) continue;

                TrackedContainer existing;
                if (discovered.TryGetValue(container, out existing))
                {
                    if (dedicated && !existing.Dedicated)
                    {
                        existing.Dedicated = true;
                        existing.MemberName = memberName;
                    }
                    continue;
                }

                ulong networkId = entity.net == null ? 0UL : entity.net.ID.Value;
                TrackedContainer tracked = new TrackedContainer
                {
                    Entity = entity,
                    Container = container,
                    MemberName = memberName,
                    Identity = networkId + ":" + RuntimeHelpers.GetHashCode(container),
                    Kind = Classify(typeName, shortName, prefab),
                    Dedicated = dedicated
                };
                discovered[container] = tracked;
            }

            foreach (TrackedContainer tracked in discovered.Values)
            {
                _containers[tracked.Identity] = tracked;
                if (!publishImmediately) continue;

                if (IsEligible(tracked)) _applyQueue.Enqueue(tracked);
            }

            if (publishImmediately && _config.Performance.EnableBatchProcessing)
                StartApplyProcessing();
        }

        private DiscoveryPlan GetDiscoveryPlan(Type type)
        {
            DiscoveryPlan cached;
            if (_typePlans.TryGetValue(type, out cached)) return cached;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            List<MemberInfo> members = new List<MemberInfo>();
            members.AddRange(type.GetFields(flags)
                .Where(field => typeof(ItemContainer).IsAssignableFrom(field.FieldType)));
            members.AddRange(type.GetProperties(flags)
                .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead &&
                    typeof(ItemContainer).IsAssignableFrom(property.PropertyType)));

            cached = new DiscoveryPlan
            {
                Members = members.ToArray(),
                HasAnyContainer = members.Count > 0,
                HasDedicatedMember = members.Any(member =>
                    member.Name.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0)
            };
            _typePlans[type] = cached;
            return cached;
        }

        private static ItemContainer ReadContainer(MemberInfo member, BaseEntity entity)
        {
            try
            {
                FieldInfo field = member as FieldInfo;
                if (field != null) return field.GetValue(entity) as ItemContainer;
                PropertyInfo property = member as PropertyInfo;
                return property == null ? null : property.GetValue(entity, null) as ItemContainer;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsDedicatedFuelMember(string memberName, string shortName, string prefab)
        {
            if (memberName.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string value = (shortName + " " + prefab).ToLowerInvariant();
            return value.Contains("fuelstorage") || value.Contains("fuel_storage") ||
                value.Contains("fuel-storage") || value.Contains("fuel tank") || value.Contains("fueltank");
        }

        private bool ContainsRecognisedFuel(ItemContainer container)
        {
            if (container.itemList == null || container.itemList.Count == 0) return false;
            for (int i = 0; i < container.itemList.Count; i++)
            {
                Item item = container.itemList[i];
                if (item != null && item.info != null && _recognisedFuelItems.Contains(item.info.shortname))
                    return true;
            }
            return false;
        }

        private static bool ContainsHint(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < FuelHints.Length; i++)
                if (value.IndexOf(FuelHints[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static FuelContainerKind Classify(string typeName, string shortName, string prefab)
        {
            string value = (typeName + " " + shortName + " " + prefab).ToLowerInvariant();
            if (value.Contains("excavator")) return FuelContainerKind.Excavator;
            if (value.Contains("quarry") || value.Contains("extractor")) return FuelContainerKind.Quarry;
            if (value.Contains("generator")) return FuelContainerKind.Generator;
            if (value.Contains("refinery")) return FuelContainerKind.Refinery;
            if (value.Contains("furnace")) return FuelContainerKind.Furnace;
            if (value.Contains("oven") || value.Contains("campfire") || value.Contains("bbq"))
                return FuelContainerKind.Oven;
            if (value.Contains("lantern") || value.Contains("light")) return FuelContainerKind.Light;
            if (value.Contains("vehicle") || value.Contains("car_") || value.Contains("motorbike") ||
                value.Contains("snowmobile") || value.Contains("boat") || value.Contains("submarine") ||
                value.Contains("copter") || value.Contains("helicopter") || value.Contains("balloon"))
                return FuelContainerKind.Vehicle;
            return FuelContainerKind.Other;
        }

        private bool IsEligible(TrackedContainer tracked)
        {
            if (tracked == null || tracked.Entity == null || tracked.Entity.IsDestroyed || tracked.Container == null)
                return false;
            if (_config.Containers.DedicatedFuelContainersOnly && !tracked.Dedicated) return false;
            if (!_enabledKinds.Contains(tracked.Kind.ToString())) return false;
            return IsOwnerAllowed(tracked.Entity.OwnerID);
        }

        private void QueueEligibleContainers()
        {
            _applyQueue.Clear();
            foreach (TrackedContainer tracked in _containers.Values)
                if (IsEligible(tracked)) _applyQueue.Enqueue(tracked);
        }

        private void StartApplyProcessing()
        {
            if (_applyScheduled || _applyQueue.Count == 0) return;
            _applyScheduled = true;
            Delay(TimeSpan.FromMilliseconds(10), ProcessApplyBatch, "fuelstack-apply");
        }

        private void ProcessApplyBatch()
        {
            _applyScheduled = false;


            ExecuteProtected(() =>
            {
                int processed = 0;
                while (_applyQueue.Count > 0 && processed < _config.Performance.ApplyBatchSize)
                {
                    TrackedContainer tracked = _applyQueue.Dequeue();
                    if (IsEligible(tracked)) ApplyStackSize(tracked);
                    processed++;
                }

                if (_applyQueue.Count > 0) StartApplyProcessing();
    
            });
        }

        private void ApplyAllEligible()
        {
            foreach (TrackedContainer tracked in _containers.Values)
                if (IsEligible(tracked)) ApplyStackSize(tracked);
        }

        private void ApplyStackSize(TrackedContainer tracked)
        {
            tracked.Container.maxStackSize = ResolveMaximum(tracked.Entity);
        }

        private int ResolveMaximum(BaseEntity entity)
        {
            int maximum = _config.General.GlobalStackMax;
            ulong networkId = entity.net == null ? 0UL : entity.net.ID.Value;
            string shortName = entity.ShortPrefabName ?? string.Empty;
            string prefab = entity.PrefabName ?? string.Empty;
            string ownerId = entity.OwnerID.ToString();

            int value;
            if (_config.Containers.EntityOverrides != null && _config.Containers.EntityOverrides.TryGetValue(networkId, out value)) maximum = value;
            else if (_config.Containers.NameOverrides != null && _config.Containers.NameOverrides.TryGetValue(shortName, out value)) maximum = value;
            else if (_config.Containers.PrefabOverrides != null && _config.Containers.PrefabOverrides.TryGetValue(prefab, out value)) maximum = value;

            if (_config.Owners.BlacklistPlayers != null && _config.Owners.BlacklistPlayers.Contains(ownerId)) return 1;
            if (_config.Owners.WhitelistPlayers != null && _config.Owners.WhitelistPlayers.Count > 0 && !_config.Owners.WhitelistPlayers.Contains(ownerId)) return 1;
            return Math.Max(1, maximum);
        }



        private void RemoveEntity(BaseEntity entity)
        {
            if (entity == null) return;
            ulong networkId = entity.net == null ? 0UL : entity.net.ID.Value;
            string prefix = networkId + ":";
            List<string> keys = null;
            foreach (string key in _containers.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (keys == null) keys = new List<string>();
                keys.Add(key);
            }

            if (keys == null) return;
            for (int i = 0; i < keys.Count; i++)
                _containers.Remove(keys[i]);
        }

        private int CountPrefabs()
        {
            HashSet<string> prefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TrackedContainer tracked in _containers.Values)
            {
                if (tracked.Entity == null) continue;
                string prefab = tracked.Entity.PrefabName ?? tracked.Entity.ShortPrefabName;
                if (!string.IsNullOrEmpty(prefab)) prefabs.Add(prefab);
            }
            return prefabs.Count;
        }

        private int CountEligible()
        {
            int count = 0;
            foreach (TrackedContainer tracked in _containers.Values)
                if (IsEligible(tracked)) count++;
            return count;
        }

        private string CreateSummary()
        {
            return "Fuel Stack Optimizer ready: " + _containers.Count + " unique fuel container(s), " +
                CountEligible() + " optimized, " + CountPrefabs() + " prefab(s), " +
                _typePlans.Count + " entity type plan(s), " + _recognisedFuelItems.Count + " recognised fuel item(s).";
        }

        private string CreateDetailedAuditReport()
        {
            StringBuilder output = new StringBuilder();
            output.AppendLine("RogueRustFuelStackOptimizer v2.1.0 audit");
            output.AppendLine(CreateSummary());
            output.AppendLine("Safety mode: dedicated fuel containers only = " +
                _config.Containers.DedicatedFuelContainersOnly + ".");
            output.AppendLine("Recognised fuel short names: " +
                string.Join(", ", _config.General.RecognisedFuelItems.ToArray()) + ".");

            foreach (var group in _containers.Values
                .Where(item => item.Entity != null && item.Container != null)
                .GroupBy(item => item.Kind + "|" + item.Entity.GetType().Name + "|" +
                    item.Entity.ShortPrefabName + "|" + item.Entity.PrefabName)
                .OrderBy(group => group.Key))
            {
                TrackedContainer sample = group.First();
                output.Append("- ").Append(sample.Kind).Append(" | ")
                    .Append(sample.Entity.GetType().Name).Append(" | ")
                    .Append(sample.Entity.ShortPrefabName)
                    .Append(" | containers ").Append(group.Count())
                    .Append(" | dedicated ").Append(group.Count(item => item.Dedicated))
                    .Append(" | eligible ").Append(group.Count(IsEligible))
                    .Append(" | ").Append(sample.Entity.PrefabName).AppendLine();
            }

            return output.ToString().TrimEnd();
        }

        [RogueCommand("fuelstack.status",
            Permission = AdminPermission,
            Description = "Shows Fuel Stack Optimizer runtime status.",
            Usage = "fuelstack.status", Category = "Fuel", AllowConsole = true)]
        private RogueCommandResult StatusCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context)) return RogueCommandResult.Fail("You are not allowed to use this command.");
            return RogueCommandResult.Ok(CreateSummary());
        }

        [RogueCommand("fuelstack.audit",
            Permission = AdminPermission,
            Description = "Creates the grouped fuel-container audit.",
            Usage = "fuelstack.audit", Category = "Fuel", AllowConsole = true)]
        private RogueCommandResult AuditCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context)) return RogueCommandResult.Fail("You are not allowed to use this command.");
            BasePlayer player = context.NativePlayer;
            if (player != null)
            {
                player.ConsoleMessage(CreateDetailedAuditReport());
                return RogueCommandResult.Ok("Grouped fuel audit written to your F1 console.");
            }
            return RogueCommandResult.Ok(CreateDetailedAuditReport());
        }

        [RogueCommand("fuelstack.fuels",
            Permission = AdminPermission,
            Description = "Lists recognized fuel item short names.",
            Usage = "fuelstack.fuels", Category = "Fuel", AllowConsole = true)]
        private RogueCommandResult FuelsCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context)) return RogueCommandResult.Fail("You are not allowed to use this command.");
            return RogueCommandResult.Ok("Recognised fuel item short names: " +
                string.Join(", ", _config.General.RecognisedFuelItems.ToArray()));
        }

        [RogueCommand("fuelstack.rescan",
            Aliases = new[] { "updatefuelstacks" },
            Description = "Starts a background fuel-container rescan.",
            Usage = "fuelstack.rescan", Category = "Fuel", CooldownSeconds = 2, AllowConsole = true)]
        private RogueCommandResult RescanCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context)) return RogueCommandResult.Fail("You are not allowed to use this command.");
            if (_scanScheduled) return RogueCommandResult.Fail("Fuel discovery is already running.");
            BeginFullScan(false);
            return RogueCommandResult.Ok("Fuel discovery rescan started in background batches.");
        }

        [RogueCommand("fuelstackaudit",
            Permission = AdminPermission,
            Description = "Writes the grouped fuel audit to the player's F1 console.",
            Usage = "/fuelstackaudit", Category = "Fuel", CooldownSeconds = 2, AllowConsole = false)]
        private RogueCommandResult AuditChatCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context)) return RogueCommandResult.Fail("You are not allowed to use this command.");
            BasePlayer player = context.NativePlayer;
            if (player == null) return RogueCommandResult.Fail("This command is player-only.");
            player.ConsoleMessage(CreateDetailedAuditReport());
            return RogueCommandResult.Ok("Grouped fuel audit written to your F1 console.");
        }

        private bool CanAdmin(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player != null)
                return player.IsAdmin || Rogue.Permissions.Has(player.UserIDString, AdminPermission);

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            return arg == null || arg.Connection == null || arg.Connection.authLevel >= 2;
        }

        private bool IsOwnerAllowed(ulong ownerId)
        {
            string ownerIdText = ownerId.ToString();
            BasePlayer owner = BasePlayer.FindByID(ownerId) ?? BasePlayer.FindSleeping(ownerId);
            string ownerName = owner == null ? null : owner.displayName;
            if (ContainsOwner(_config.Owners.BlacklistPlayers, ownerIdText, ownerName)) return false;
            return _config.Owners.WhitelistPlayers.Count == 0 ||
                ContainsOwner(_config.Owners.WhitelistPlayers, ownerIdText, ownerName);
        }

        private static bool ContainsOwner(List<string> entries, string ownerId, string ownerName)
        {
            return entries.Any(entry =>
                string.Equals(entry, ownerId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(ownerName) &&
                    string.Equals(entry, ownerName, StringComparison.OrdinalIgnoreCase)));
        }

        private void ExecuteProtected(Action action, string operation = "Runtime")
        {
            if (action == null || _unloading) return;
            try
            {
                using (Measure("FuelStackOptimizer", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private static List<string> CleanKinds(List<string> source)
        {
            List<string> values = source == null ? DefaultKinds() : source;
            HashSet<string> valid = new HashSet<string>(
                Enum.GetNames(typeof(FuelContainerKind)), StringComparer.OrdinalIgnoreCase);
            List<string> result = values
                .Where(value => !string.IsNullOrWhiteSpace(value) && valid.Contains(value.Trim()))
                .Select(value => Enum.GetNames(typeof(FuelContainerKind))
                    .First(name => string.Equals(name, value.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return result.Count == 0 ? DefaultKinds() : result;
        }

        private static List<string> CleanStrings(List<string> source, IEnumerable<string> defaults)
        {
            IEnumerable<string> values = source ?? defaults ?? new string[0];
            List<string> result = values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (result.Count == 0 && defaults != null)
                result = defaults.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        private static int ClampStack(int value)
        {
            return Math.Max(1, Math.Min(value, 1000000));
        }

        private static Dictionary<ulong, int> ValidateOverrides(Dictionary<ulong, int> source)
        {
            Dictionary<ulong, int> result = new Dictionary<ulong, int>();
            if (source == null) return result;
            foreach (KeyValuePair<ulong, int> pair in source)
                result[pair.Key] = ClampStack(pair.Value);
            return result;
        }

        private static Dictionary<string, int> ValidateOverrides(Dictionary<string, int> source)
        {
            Dictionary<string, int> result =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (source == null) return result;
            foreach (KeyValuePair<string, int> pair in source)
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    result[pair.Key.Trim()] = ClampStack(pair.Value);
            return result;
        }
    
}
}
