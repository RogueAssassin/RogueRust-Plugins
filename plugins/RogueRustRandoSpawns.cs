using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustRandoSpawns", "RogueAssassin", "2.1.1")]
    [Description("RogueRust-powered random respawn system with biome weighting, topology/zone protection, cached spawn generation and compatibility hooks.")]
    public sealed class RogueRustRandoSpawns : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.1";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 1);
        [PluginReference]
        private Plugin ZoneManager;

        private const string DataKey = "RogueRustRandoSpawns/spawn-cache";
        private const string LegacyDataKey = "RogueRust/RogueRustRandoSpawns/spawn-cache";
        private const string LanguageRoot = "RogueRust/RogueRustRandoSpawns";
        [RoguePermission]
        private const string AdminPermission = "roguerustrandospawns.admin";
        private const int GroundMask = 1 << 4 | 1 << 8 | 1 << 10 | 1 << 15 | 1 << 16 | 1 << 21 | 1 << 23 | 1 << 27 | 1 << 28 | 1 << 29;

        private Configuration _config = new Configuration();
        private readonly Dictionary<TerrainBiome.Enum, List<Vector3>> _spawnPoints = new Dictionary<TerrainBiome.Enum, List<Vector3>>();
        private readonly Dictionary<string, Dictionary<string, string>> _messages = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Random _random = new System.Random();
        private int _blockedTopologyMask;
        private bool _disabled;
        private bool _generationInProgress;
        private int _rejectedByTopology;
        private int _rejectedByTerrain;
        private int _rejectedBySlope;
        private int _rejectedByZone;

        #region Lifecycle

        private void Init()
        {
            LoadLanguageFiles();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustRandoSpawns/; Lang=<language>/RogueRust/RogueRustRandoSpawns/messages.json");
        }

        private void OnServerInitialized()
        {
            BuildBlockedTopologyMask();

            if (_config.Generation.UsePersistentCache && TryLoadCachedSpawnPoints())
            {
                LogInformation("Generation", "Loaded " + TotalSpawnCount + " cached spawn points for this map.");
                EnsureMinimumSpawnCoverage();
                return;
            }

            GenerateSpawnPoints();
        }

        private void OnNewSave(string filename)
        {
            _spawnPoints.Clear();
            SaveData(DataKey, new SpawnCache());
        }

        private void Unload()
        {
            if (_config.Generation.UsePersistentCache && TotalSpawnCount > 0)
                SaveSpawnCacheImmediate();
        }

        #endregion

        #region Respawn Hooks

        private object OnPlayerRespawn(BasePlayer player)
        {
            if (_disabled || player == null)
                return null;

            Vector3 position;
            if (!TryGetSpawnPoint(false, out position))
            {
                LogWarning("Respawn", "No valid RogueRustRandoSpawns position was available; allowing Rust's default respawn system for " + player.UserIDString + ".");
                RequestRegeneration("respawn-exhausted");
                return null;
            }

            return new BasePlayer.SpawnPoint
            {
                pos = position,
                rot = Quaternion.identity
            };
        }

        #endregion

        #region Spawn Generation

        private void GenerateSpawnPoints()
        {
            if (_generationInProgress)
                return;

            _generationInProgress = true;
            try
            {
                using (Measure("Spawns", "GenerateSpawnPoints"))
                {
                    _spawnPoints.Clear();
                    ResetGenerationCounters();

                    float halfSize = World.WorldSize * 0.5f;
                    float radius = halfSize * Mathf.Clamp(_config.Generation.MapRadiusPercent, 0.1f, 1f);
                    int attempts = Mathf.Max(100, _config.Generation.Attempts);

                    for (int i = 0; i < attempts; i++)
                    {
                        Vector2 sample = UnityEngine.Random.insideUnitCircle * radius;
                        Vector3 position = new Vector3(sample.x, 0f, sample.y);

                        int topology;
                        if (World.TryGetTopology(position, out topology) && (topology & _blockedTopologyMask) != 0)
                        {
                            _rejectedByTopology++;
                            continue;
                        }

                        float terrainHeight;
                        if (!World.TryGetTerrainHeight(position, out terrainHeight))
                        {
                            _rejectedByTerrain++;
                            continue;
                        }

                        position.y = terrainHeight + _config.Generation.VerticalOffset;

                        if (!HasAcceptableGround(position))
                        {
                            _rejectedBySlope++;
                            continue;
                        }

                        if (IsPositionInBlockedZone(position))
                        {
                            _rejectedByZone++;
                            continue;
                        }

                        TerrainBiome.Enum biome = GetBiome(position);
                        if (!IsConfiguredBiomeEnabled(biome))
                            continue;

                        List<Vector3> list;
                        if (!_spawnPoints.TryGetValue(biome, out list))
                        {
                            list = new List<Vector3>();
                            _spawnPoints[biome] = list;
                        }

                        list.Add(position);
                    }

                    if (_config.Generation.UsePersistentCache)
                        SaveSpawnCache();
                }

                LogInformation("Generation",
                    "Generated " + TotalSpawnCount + " spawn points. " +
                    "Rejected topology=" + _rejectedByTopology +
                    ", terrain=" + _rejectedByTerrain +
                    ", slope=" + _rejectedBySlope +
                    ", zone=" + _rejectedByZone + ".");
            }
            catch (Exception exception)
            {
                LogError("Generation", "Spawn generation failed.", exception);
            }
            finally
            {
                _generationInProgress = false;
            }
        }

        private bool HasAcceptableGround(Vector3 position)
        {
            Vector3 origin = position + Vector3.up * Mathf.Max(5f, _config.Generation.GroundProbeHeight);
            RaycastHit hit;
            if (!Physics.Raycast(origin, Vector3.down, out hit, _config.Generation.GroundProbeHeight + 20f, GroundMask, QueryTriggerInteraction.Ignore))
                return false;

            if (!(hit.collider is TerrainCollider))
                return false;

            float slope = Vector3.Angle(hit.normal, Vector3.up);
            return slope <= Mathf.Clamp(_config.Generation.MaximumSlopeDegrees, 0f, 89f);
        }

        private TerrainBiome.Enum GetBiome(Vector3 position)
        {
            try
            {
                return (TerrainBiome.Enum)TerrainMeta.BiomeMap.GetBiomeMaxType(position);
            }
            catch
            {
                return TerrainBiome.Enum.Temperate;
            }
        }

        private bool IsConfiguredBiomeEnabled(TerrainBiome.Enum biome)
        {
            BiomeOptions options;
            return _config.Spawn.Biomes.TryGetValue(biome, out options) && options.Enabled;
        }

        private void EnsureMinimumSpawnCoverage()
        {
            if (TotalSpawnCount >= Mathf.Max(1, _config.Generation.MinimumCachedPoints))
                return;

            LogWarning("Generation", "Cached spawn set is below the configured minimum; regenerating.");
            GenerateSpawnPoints();
        }

        private void RequestRegeneration(string reason)
        {
            CoalesceNextTick("spawn-regeneration:" + reason, () =>
            {
                if (!_generationInProgress)
                    GenerateSpawnPoints();
            });
        }

        #endregion

        #region Spawn Selection

        private bool TryGetSpawnPoint(bool ignorePlayerRestriction, out Vector3 position)
        {
            position = Vector3.zero;
            if (TotalSpawnCount == 0)
                return false;

            List<TerrainBiome.Enum> eligible = GetEligibleBiomes(ignorePlayerRestriction);
            if (eligible.Count == 0)
            {
                foreach (KeyValuePair<TerrainBiome.Enum, List<Vector3>> entry in _spawnPoints)
                {
                    if (entry.Value != null && entry.Value.Count > 0)
                        eligible.Add(entry.Key);
                }
            }

            if (eligible.Count == 0)
                return false;

            int maxAttempts = Mathf.Max(1, _config.Spawn.SelectionAttempts);
            for (int attempt = 0; attempt < maxAttempts && eligible.Count > 0; attempt++)
            {
                TerrainBiome.Enum biome = eligible[_random.Next(eligible.Count)];
                if (TryGetSpawnPoint(biome, out position))
                    return true;

                eligible.Remove(biome);
            }

            return false;
        }

        private bool TryGetSpawnPoint(TerrainBiome.Enum biome, out Vector3 position)
        {
            position = Vector3.zero;
            List<Vector3> points;
            if (!_spawnPoints.TryGetValue(biome, out points) || points == null || points.Count == 0)
                return false;

            int checks = Mathf.Min(Mathf.Max(1, _config.Spawn.CandidateChecksPerBiome), points.Count);
            for (int i = 0; i < checks; i++)
            {
                int index = _random.Next(points.Count);
                Vector3 candidate = points[index];

                if (!IsCandidateStillValid(candidate))
                {
                    points.RemoveAt(index);
                    if (points.Count == 0)
                        break;
                    continue;
                }

                position = candidate;
                return true;
            }

            if (points.Count < _config.Generation.RegenerateWhenBiomeBelow)
                RequestRegeneration("biome-depleted-" + biome);

            return false;
        }

        private bool IsCandidateStillValid(Vector3 position)
        {
            if (IsPositionInBlockedZone(position))
                return false;

            IReadOnlyCollection<RogueWorldEntity> nearby = World.FindEntities(
                position,
                Mathf.Max(0f, _config.Generation.DistanceFromBuildings),
                null,
                Mathf.Max(8, _config.Generation.MaximumNearbyEntityResults));

            if (nearby == null || nearby.Count == 0)
                return true;

            foreach (RogueWorldEntity entity in nearby)
            {
                if (entity == null)
                    continue;

                BaseEntity native = entity.Native as BaseEntity;
                if (native == null || native is BasePlayer)
                    continue;

                // Building blocks and player-owned deployables should invalidate a spawn,
                // while ambient world entities (trees, ores, animals, dropped items, etc.) should not.
                if (native is BuildingBlock || native.OwnerID != 0UL)
                    return false;
            }

            return true;
        }

        private List<TerrainBiome.Enum> GetEligibleBiomes(bool ignorePlayerRestriction)
        {
            int onlinePlayers = BasePlayer.activePlayerList.Count;
            var eligible = new List<TerrainBiome.Enum>();

            foreach (KeyValuePair<TerrainBiome.Enum, BiomeOptions> entry in _config.Spawn.Biomes)
            {
                if (!entry.Value.Enabled)
                    continue;

                List<Vector3> points;
                if (!_spawnPoints.TryGetValue(entry.Key, out points) || points == null || points.Count == 0)
                    continue;

                if (!ignorePlayerRestriction && onlinePlayers < entry.Value.MinimumOnlinePlayers)
                    continue;

                eligible.Add(entry.Key);
            }

            return eligible;
        }

        #endregion

        #region ZoneManager

        private bool IsPositionInBlockedZone(Vector3 position)
        {
            if (ZoneManager == null || _config.Spawn.BlockedZoneIds == null || _config.Spawn.BlockedZoneIds.Length == 0)
                return false;

            for (int i = 0; i < _config.Spawn.BlockedZoneIds.Length; i++)
            {
                string zoneId = _config.Spawn.BlockedZoneIds[i];
                if (string.IsNullOrWhiteSpace(zoneId))
                    continue;

                try
                {
                    object result = ZoneManager.Call("IsPositionInZone", zoneId, position);
                    if (result is bool && (bool)result)
                        return true;
                }
                catch (Exception exception)
                {
                    LogWarning("Integration", "ZoneManager check failed for zone '" + zoneId + "': " + exception.Message);
                }
            }

            return false;
        }

        #endregion

        #region Public API Compatibility

        [HookMethod("GetSpawnPointAtBiome")]
        public object GetSpawnPointAtBiome(string biomeType)
        {
            TerrainBiome.Enum biome;
            if (!Enum.TryParse(biomeType, true, out biome))
                biome = TerrainBiome.Enum.Temperate;

            Vector3 position;
            return TryGetSpawnPoint(biome, out position) ? (object)position : null;
        }

        [HookMethod("DisableSpawnSystem")]
        public void DisableSpawnSystem() => _disabled = true;

        [HookMethod("EnableSpawnSystem")]
        public void EnableSpawnSystem() => _disabled = false;

        [HookMethod("GetSpawnPoint")]
        public object GetSpawnPoint()
        {
            Vector3 position;
            return TryGetSpawnPoint(true, out position) ? (object)position : null;
        }

        [HookMethod("RegenerateSpawnPoints")]
        public int RegenerateSpawnPoints()
        {
            GenerateSpawnPoints();
            return TotalSpawnCount;
        }

        #endregion

        #region Commands

        [RogueCommand("showspawns",
            Aliases = new[] { "roguerust.showspawns", "randospawns.show" },
            Permission = AdminPermission,
            Description = "Displays generated random spawn points for 30 seconds.",
            Usage = "/showspawns",
            Category = "World",
            CooldownSeconds = 2)]
        private RogueCommandResult ShowSpawnsCommand(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(GetMessage("PlayerOnly", null));

            foreach (KeyValuePair<TerrainBiome.Enum, List<Vector3>> entry in _spawnPoints)
            {
                Color color = GetBiomeColor(entry.Key);
                for (int i = 0; i < entry.Value.Count; i++)
                {
                    Vector3 point = entry.Value[i];
                    player.SendConsoleCommand("ddraw.sphere", 30f, color, point, 1f);
                    player.SendConsoleCommand("ddraw.line", 30f, color, point, point + Vector3.up * 100f);
                }
            }

            return RogueCommandResult.Ok(string.Format(GetMessage("SpawnCount", player), TotalSpawnCount));
        }

        [RogueCommand("randospawns.regenerate",
            Aliases = new[] { "roguerust.randospawns.regenerate" },
            Permission = AdminPermission,
            Description = "Regenerates RogueRust random spawn points.",
            Usage = "/randospawns.regenerate",
            Category = "World",
            CooldownSeconds = 10,
            AllowConsole = true)]
        private RogueCommandResult RegenerateCommand(RogueCommandContext context)
        {
            GenerateSpawnPoints();
            return RogueCommandResult.Ok(string.Format(GetMessage("Regenerated", context.NativePlayer), TotalSpawnCount));
        }

        private Color GetBiomeColor(TerrainBiome.Enum biome)
        {
            switch (biome)
            {
                case TerrainBiome.Enum.Arctic: return Color.blue;
                case TerrainBiome.Enum.Arid: return Color.red;
                case TerrainBiome.Enum.Temperate: return Color.green;
                case TerrainBiome.Enum.Jungle: return Color.yellow;
                case TerrainBiome.Enum.Tundra: return Color.cyan;
                default: return Color.white;
            }
        }

        #endregion

        #region Config

        protected override void LoadDefaultConfig()
        {
            _config = Configuration.CreateDefault();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>() ?? Configuration.CreateDefault();
                _config.Normalize();
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Invalid " + Name + ".json; defaults were loaded. " + exception.Message);
                _config = Configuration.CreateDefault();
            }

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void BuildBlockedTopologyMask()
        {
            _blockedTopologyMask = 0;
            string[] values = _config.Spawn.BlockedTopologies ?? new string[0];
            for (int i = 0; i < values.Length; i++)
            {
                TerrainTopology.Enum topology;
                if (Enum.TryParse(values[i], true, out topology))
                    _blockedTopologyMask |= (int)topology;
                else
                    LogWarning("Configuration", "Unknown topology '" + values[i] + "' was ignored.");
            }
        }

        private sealed class Configuration
        {
            [JsonProperty("Generation Options", Order = 10)]
            public GenerationOptions Generation = new GenerationOptions();

            [JsonProperty("Spawn Options", Order = 20)]
            public SpawnOptions Spawn = new SpawnOptions();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            public static Configuration CreateDefault()
            {
                return new Configuration
                {
                    Generation = new GenerationOptions(),
                    Spawn = SpawnOptions.CreateDefault(),
                    Version = CurrentVersion
                };
            }

            public void Normalize()
            {
                if (Generation == null) Generation = new GenerationOptions();
                if (Spawn == null) Spawn = SpawnOptions.CreateDefault();
                if (Spawn.Biomes == null || Spawn.Biomes.Count == 0) Spawn.Biomes = SpawnOptions.CreateDefaultBiomes();
                if (Spawn.BlockedZoneIds == null) Spawn.BlockedZoneIds = new string[0];
                if (Spawn.BlockedTopologies == null) Spawn.BlockedTopologies = SpawnOptions.DefaultTopologies();

                Generation.Attempts = Mathf.Max(100, Generation.Attempts);
                Generation.MaximumSlopeDegrees = Mathf.Clamp(Generation.MaximumSlopeDegrees, 0f, 89f);
                Generation.DistanceFromBuildings = Mathf.Max(0f, Generation.DistanceFromBuildings);
                Generation.MapRadiusPercent = Mathf.Clamp(Generation.MapRadiusPercent, 0.1f, 1f);
                Generation.GroundProbeHeight = Mathf.Max(1f, Generation.GroundProbeHeight);
                Generation.MinimumCachedPoints = Mathf.Max(1, Generation.MinimumCachedPoints);
                Generation.RegenerateWhenBiomeBelow = Mathf.Max(0, Generation.RegenerateWhenBiomeBelow);
                Generation.MaximumNearbyEntityResults = Mathf.Max(8, Generation.MaximumNearbyEntityResults);
                Spawn.SelectionAttempts = Mathf.Max(1, Spawn.SelectionAttempts);
                Spawn.CandidateChecksPerBiome = Mathf.Max(1, Spawn.CandidateChecksPerBiome);

                foreach (KeyValuePair<TerrainBiome.Enum, BiomeOptions> entry in Spawn.Biomes)
                {
                    if (entry.Value != null)
                        entry.Value.MinimumOnlinePlayers = Mathf.Max(0, entry.Value.MinimumOnlinePlayers);
                }

                Version = CurrentVersion;
            }
        }

        private sealed class GenerationOptions
        {
            [JsonProperty("Generation attempts")]
            public int Attempts = 4000;

            [JsonProperty("Maximum slope (degrees)")]
            public float MaximumSlopeDegrees = 45f;

            [JsonProperty("Distance from buildings (metres)")]
            public float DistanceFromBuildings = 18f;

            [JsonProperty("Map radius used for spawn generation (0.1 - 1.0)")]
            public float MapRadiusPercent = 0.95f;

            [JsonProperty("Vertical spawn offset")]
            public float VerticalOffset = 0.15f;

            [JsonProperty("Ground probe height")]
            public float GroundProbeHeight = 8f;

            [JsonProperty("Persist generated spawn cache")]
            public bool UsePersistentCache = true;

            [JsonProperty("Minimum cached points before regeneration")]
            public int MinimumCachedPoints = 250;

            [JsonProperty("Regenerate when a biome falls below this many points")]
            public int RegenerateWhenBiomeBelow = 10;

            [JsonProperty("Maximum nearby entities checked per candidate")]
            public int MaximumNearbyEntityResults = 64;
        }

        private sealed class SpawnOptions
        {
            [JsonProperty("Biome Options")]
            public Dictionary<TerrainBiome.Enum, BiomeOptions> Biomes = CreateDefaultBiomes();

            [JsonProperty("Disable spawn points in these zones (zone IDs)")]
            public string[] BlockedZoneIds = new string[0];

            [JsonProperty("Disable spawn points in these topologies")]
            public string[] BlockedTopologies = DefaultTopologies();

            [JsonProperty("Maximum biome selection attempts per respawn")]
            public int SelectionAttempts = 8;

            [JsonProperty("Candidate checks per biome")]
            public int CandidateChecksPerBiome = 12;

            public static SpawnOptions CreateDefault()
            {
                return new SpawnOptions
                {
                    Biomes = CreateDefaultBiomes(),
                    BlockedZoneIds = new string[0],
                    BlockedTopologies = DefaultTopologies(),
                    SelectionAttempts = 8,
                    CandidateChecksPerBiome = 12
                };
            }

            public static Dictionary<TerrainBiome.Enum, BiomeOptions> CreateDefaultBiomes()
            {
                return new Dictionary<TerrainBiome.Enum, BiomeOptions>
                {
                    [TerrainBiome.Enum.Arctic] = new BiomeOptions { Enabled = true, MinimumOnlinePlayers = 30 },
                    [TerrainBiome.Enum.Tundra] = new BiomeOptions { Enabled = true, MinimumOnlinePlayers = 20 },
                    [TerrainBiome.Enum.Arid] = new BiomeOptions { Enabled = true, MinimumOnlinePlayers = 10 },
                    [TerrainBiome.Enum.Temperate] = new BiomeOptions { Enabled = true, MinimumOnlinePlayers = 1 },
                    [TerrainBiome.Enum.Jungle] = new BiomeOptions { Enabled = true, MinimumOnlinePlayers = 1 }
                };
            }

            public static string[] DefaultTopologies()
            {
                return new[] { "Cliff", "Cliffside", "Lake", "Ocean", "Monument", "Offshore", "River", "Swamp", "Rail" };
            }
        }

        private sealed class BiomeOptions
        {
            [JsonProperty("Enable spawn points to be generated in this biome")]
            public bool Enabled = true;

            [JsonProperty("Minimum required online players before spawns from this biome will be selected")]
            public int MinimumOnlinePlayers = 1;
        }

        #endregion

        #region Data Cache

        private sealed class SpawnCache
        {
            public uint WorldSeed;
            public uint WorldSize;
            public string PluginVersion = "2.1.1";
            public Dictionary<string, List<CachedVector>> Biomes = new Dictionary<string, List<CachedVector>>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class CachedVector
        {
            public float X;
            public float Y;
            public float Z;

            public CachedVector() { }
            public CachedVector(Vector3 value) { X = value.x; Y = value.y; Z = value.z; }
            public Vector3 ToVector3() => new Vector3(X, Y, Z);
        }

        private bool TryLoadCachedSpawnPoints()
        {
            try
            {
                MigrateLegacySpawnCache();
                SpawnCache cache = LoadData(DataKey, () => new SpawnCache());
                if (cache == null || cache.WorldSeed != global::World.Seed || cache.WorldSize != global::World.Size || cache.Biomes == null || cache.Biomes.Count == 0)
                    return false;

                _spawnPoints.Clear();
                foreach (KeyValuePair<string, List<CachedVector>> entry in cache.Biomes)
                {
                    TerrainBiome.Enum biome;
                    if (!Enum.TryParse(entry.Key, true, out biome) || entry.Value == null)
                        continue;

                    List<CachedVector> cachedPoints = entry.Value;
                    var points = new List<Vector3>(cachedPoints.Count);
                    for (int i = 0; i < cachedPoints.Count; i++)
                        points.Add(cachedPoints[i].ToVector3());

                    _spawnPoints[biome] = points;
                }

                return TotalSpawnCount > 0;
            }
            catch (Exception exception)
            {
                LogWarning("Data", "Spawn cache could not be loaded: " + exception.Message);
                return false;
            }
        }

        private void MigrateLegacySpawnCache()
        {
            try
            {
                string currentPath = Rogue.Data.GetPath(DataKey);
                if (File.Exists(currentPath))
                    return;

                string legacyPath = Rogue.Data.GetPath(LegacyDataKey);
                if (!File.Exists(legacyPath))
                    return;

                string directory = Path.GetDirectoryName(currentPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.Copy(legacyPath, currentPath, false);
                LogInformation("Data", "Migrated the legacy spawn cache to " + DataKey + ".");
            }
            catch (Exception exception)
            {
                LogWarning("Data", "Legacy spawn-cache migration failed: " + exception.Message);
            }
        }

        private SpawnCache BuildSpawnCache()
        {
            var cache = new SpawnCache
            {
                WorldSeed = global::World.Seed,
                WorldSize = global::World.Size,
                PluginVersion = "2.1.1"
            };

            foreach (KeyValuePair<TerrainBiome.Enum, List<Vector3>> entry in _spawnPoints)
            {
                List<Vector3> source = entry.Value;
                if (source == null)
                    continue;

                var cached = new List<CachedVector>(source.Count);
                for (int i = 0; i < source.Count; i++)
                    cached.Add(new CachedVector(source[i]));

                cache.Biomes[entry.Key.ToString()] = cached;
            }

            return cache;
        }

        private void SaveSpawnCache()
        {
            try
            {
                SaveDataDebounced(DataKey, BuildSpawnCache(), TimeSpan.FromSeconds(1));
            }
            catch (Exception exception)
            {
                LogWarning("Data", "Spawn cache could not be saved: " + exception.Message);
            }
        }

        private void SaveSpawnCacheImmediate()
        {
            try
            {
                SaveData(DataKey, BuildSpawnCache());
            }
            catch (Exception exception)
            {
                LogWarning("Data", "Spawn cache could not be saved during unload: " + exception.Message);
            }
        }

        #endregion

        #region Localization

        private Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["PlayerOnly"] = "This command must be run by a player.",
                ["SpawnCount"] = "Total spawn count: {0}",
                ["Regenerated"] = "Regenerated {0} RogueRust random spawn points."
            };
        }

        private void LoadLanguageFiles()
        {
            _messages.Clear();

            string langRoot = Interface.Oxide.LangDirectory;
            string[] languages = Directory.Exists(langRoot)
                ? Directory.GetDirectories(langRoot)
                : new string[0];

            bool foundAny = false;
            for (int i = 0; i < languages.Length; i++)
            {
                string language = Path.GetFileName(languages[i]);
                if (string.IsNullOrWhiteSpace(language))
                    continue;

                string familyDirectory = Path.Combine(languages[i], "RogueRust", "RogueRustRandoSpawns");
                string familyPath = Path.Combine(familyDirectory, "messages.json");

                if (!File.Exists(familyPath))
                {
                    string oldFamilyPath = Path.Combine(langRoot, "RogueRust", "RogueRustRandoSpawns", language + ".json");
                    string conventionalPath = Path.Combine(languages[i], "RogueRustRandoSpawns.json");

                    string source = File.Exists(oldFamilyPath) ? oldFamilyPath :
                                    File.Exists(conventionalPath) ? conventionalPath : null;
                    if (!string.IsNullOrEmpty(source))
                    {
                        Directory.CreateDirectory(familyDirectory);
                        File.Copy(source, familyPath, false);
                        LogInformation("Localization", "Migrated " + language + " language messages to the RogueRust family layout.");
                    }
                }

                if (!File.Exists(familyPath))
                    continue;

                try
                {
                    Dictionary<string, string> catalog =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(familyPath));
                    if (catalog != null)
                    {
                        _messages[language] = catalog;
                        foundAny = true;
                    }
                }
                catch (Exception exception)
                {
                    LogWarning("Localization", "Could not load language file '" + familyPath + "': " + exception.Message);
                }
            }

            string englishDirectory = Path.Combine(langRoot, "en", "RogueRust", "RogueRustRandoSpawns");
            string englishPath = Path.Combine(englishDirectory, "messages.json");
            if (!File.Exists(englishPath))
            {
                Directory.CreateDirectory(englishDirectory);

                string oldEnglishPath = Path.Combine(langRoot, "RogueRust", "RogueRustRandoSpawns", "en.json");
                string conventionalEnglishPath = Path.Combine(langRoot, "en", "RogueRustRandoSpawns.json");
                string source = File.Exists(oldEnglishPath) ? oldEnglishPath :
                                File.Exists(conventionalEnglishPath) ? conventionalEnglishPath : null;

                if (!string.IsNullOrEmpty(source))
                    File.Copy(source, englishPath, false);
                else
                    File.WriteAllText(englishPath, JsonConvert.SerializeObject(CreateDefaultMessages(), Formatting.Indented));
            }

            if (!_messages.ContainsKey("en"))
            {
                try
                {
                    Dictionary<string, string> english =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(englishPath));
                    _messages["en"] = english ?? CreateDefaultMessages();
                }
                catch
                {
                    _messages["en"] = CreateDefaultMessages();
                }
            }
        }

        private string GetMessage(string key, BasePlayer player)
        {
            string language = player == null ? "en" : lang.GetLanguage(player.UserIDString);
            Dictionary<string, string> catalog;
            string value;

            if (!string.IsNullOrWhiteSpace(language) && _messages.TryGetValue(language, out catalog) && catalog.TryGetValue(key, out value))
                return value;
            if (_messages.TryGetValue("en", out catalog) && catalog.TryGetValue(key, out value))
                return value;
            return key;
        }

        #endregion

        #region Helpers

        private int TotalSpawnCount
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<TerrainBiome.Enum, List<Vector3>> entry in _spawnPoints)
                {
                    if (entry.Value != null)
                        total += entry.Value.Count;
                }

                return total;
            }
        }

        private void ResetGenerationCounters()
        {
            _rejectedByTopology = 0;
            _rejectedByTerrain = 0;
            _rejectedBySlope = 0;
            _rejectedByZone = 0;
        }

        #endregion
    }
}
