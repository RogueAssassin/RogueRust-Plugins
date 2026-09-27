using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins;

[Info("RogueRustEventDirector", "RogueAssassin", "1.1.0")]
[Description("Performance-focused world event scheduling and CH47 crate direction powered by RogueRust.")]
public sealed class RogueRustEventDirector : RogueRustPlugin
{
    private const string ConfigKey = "RogueRustEventDirector";
    private const string DataKey = "RogueRustEventDirector/schedule";
    private const string LegacyDataKey = "RogueRust/RogueRustEventDirector/schedule";
    [RoguePermission]
    private const string AdminPermission = "roguerusteventdirector.admin";

    private const string PluginVersion = "1.1.0";

    private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 1, 0);

    private const string CargoPlanePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
    private const string PatrolHelicopterPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
    private const string ChinookPrefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
    private const string CargoShipPrefab = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";

    private readonly HashSet<ulong> _scheduledChinooks = new();
    private readonly System.Random _random = new();

    private PluginConfig _config = new();
    private ScheduleState _state = new();

    private void Init()
    {
        LogInformation("Lifecycle",
        $"{Name} {PluginVersion} initialized. " +
        $"Config={Name}.json; Data=RogueRust/RogueRustEventDirector/; Lang=None");
        _config = LoadConfiguration(ConfigKey, () => new PluginConfig()) ?? new PluginConfig();
        NormalizeConfiguration();
        SaveConfiguration(ConfigKey, _config);

        _state = LoadData(DataKey, () => new ScheduleState()) ?? new ScheduleState();
        if (_state.IsEmpty())
        {
            ScheduleState legacy = LoadData(LegacyDataKey, () => new ScheduleState());
            if (legacy != null && !legacy.IsEmpty())
            {
                _state = legacy;
                SaveState();
                LogInformation("Data", "Migrated EventDirector schedule state to RogueRust/RogueRustEventDirector/schedule.json.");
            }
        }
    }

    private void OnServerInitialized()
    {
        EnsureSchedules();

        foreach (RogueWorldEventEntity tracked in WorldEvents.GetActive(RogueWorldEventType.Chinook))
        {
            if (tracked.NativeEntity is CH47HelicopterAIController chinook)
                ScheduleChinookDrop(chinook);
        }

        int tickSeconds = Math.Max(1, _config.Scheduler.TickSeconds);
        RepeatUnique(
            "event-director",
            TimeSpan.FromSeconds(tickSeconds),
            SchedulerTick,
            TimeSpan.FromSeconds(tickSeconds));
    }

    private void Unload()
    {
        SaveState();
        _scheduledChinooks.Clear();
    }

    private void OnEntitySpawned(BaseNetworkable entity)
    {
        if (entity is CH47HelicopterAIController chinook)
            ScheduleChinookDrop(chinook);
    }

    private void OnEntityKill(BaseNetworkable entity)
    {
        if (entity?.net != null)
            _scheduledChinooks.Remove(entity.net.ID.Value);
    }

    private object? OnEventTrigger(TriggeredEventPrefab info)
    {
        string path = info?.targetPrefab?.resourcePath ?? string.Empty;
        if (path.Length == 0)
            return null;

        if (_config.PatrolHelicopter.DisableVanillaSpawns &&
            path.IndexOf("patrol", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (_config.CargoShip.DisableVanillaSpawns &&
            path.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (_config.CargoPlane.DisableVanillaSpawns &&
            path.IndexOf("plane", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        if (_config.Chinook.DisableVanillaSpawns &&
            path.IndexOf("ch47", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return null;
    }

    private object? CanHelicopterDropCrate(CH47HelicopterAIController chinook)
    {
        DropDirectorSettings drop = _config.Chinook.DropDirector;
        if (!_config.Chinook.Enabled || !drop.Enabled || !drop.BlockVanillaDrops)
            return null;

        ScheduleChinookDrop(chinook);
        return false;
    }

    private void SchedulerTick()
    {
        DateTime now = DateTime.UtcNow;

        ProcessEvent(
            RogueWorldEventType.CargoPlane,
            _config.CargoPlane,
            ref _state.CargoPlaneUtc,
            SpawnCargoPlane,
            now);

        ProcessEvent(
            RogueWorldEventType.PatrolHelicopter,
            _config.PatrolHelicopter,
            ref _state.PatrolHelicopterUtc,
            SpawnPatrolHelicopter,
            now);

        ProcessEvent(
            RogueWorldEventType.Bradley,
            _config.Bradley,
            ref _state.BradleyUtc,
            SpawnBradley,
            now);

        ProcessEvent(
            RogueWorldEventType.Chinook,
            _config.Chinook,
            ref _state.ChinookUtc,
            SpawnChinook,
            now);

        ProcessEvent(
            RogueWorldEventType.CargoShip,
            _config.CargoShip,
            ref _state.CargoShipUtc,
            SpawnCargoShip,
            now);
    }

    private void ProcessEvent(
        RogueWorldEventType type,
        EventSettings settings,
        ref DateTime nextUtc,
        Action spawn,
        DateTime now)
    {
        if (!settings.Enabled)
            return;

        if (nextUtc == default)
        {
            nextUtc = NextRun(settings, now);
            SaveStateDebounced();
            return;
        }

        if (now < nextUtc)
            return;

        if (WorldEvents.IsAtOrAboveLimit(type, settings.MaximumConcurrent))
        {
            nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds));
            SaveStateDebounced();
            return;
        }

        int availableSlots = Math.Max(0, settings.MaximumConcurrent - WorldEvents.CountActive(type));
        int requested = NextInt(settings.MinimumSpawnCount, settings.MaximumSpawnCount);
        int amount = Math.Min(requested, availableSlots);

        if (amount > 0)
        {
            using (Measure("events", "spawn-" + type))
            {
                for (int i = 0; i < amount; i++)
                    spawn();
            }
        }

        nextUtc = NextRun(settings, now);
        SaveStateDebounced();
    }

    private void SpawnCargoPlane()
    {
        BaseEntity? entity = GameManager.server.CreateEntity(CargoPlanePrefab, GetAirSpawnPosition());
        entity?.Spawn();
    }

    private void SpawnPatrolHelicopter()
    {
        BaseEntity? entity = GameManager.server.CreateEntity(PatrolHelicopterPrefab, GetAirSpawnPosition());
        entity?.Spawn();
    }

    private void SpawnBradley()
    {
        BradleySpawner.singleton?.SpawnBradley();
    }

    private void SpawnChinook()
    {
        CH47HelicopterAIController? chinook =
            GameManager.server.CreateEntity(ChinookPrefab, GetAirSpawnPosition()) as CH47HelicopterAIController;

        if (chinook == null)
            return;

        chinook.TriggeredEventSpawn();
        chinook.Spawn();
    }

    private void SpawnCargoShip()
    {
        float edge = Math.Max(500f, World.WorldSize);
        double angle;

        lock (_random)
            angle = _random.NextDouble() * Math.PI * 2d;

        Vector3 position = new(
            (float)Math.Cos(angle) * edge,
            0f,
            (float)Math.Sin(angle) * edge);

        if (Rogue.Terrain.TryGetWaterHeight(position, out float waterHeight))
            position.y = waterHeight;

        BaseEntity? entity = GameManager.server.CreateEntity(CargoShipPrefab, position);
        entity?.Spawn();
    }

    private Vector3 GetAirSpawnPosition()
    {
        float edge = Math.Max(500f, World.WorldSize - 50f);
        int side;

        lock (_random)
            side = _random.Next(0, 4);

        return side switch
        {
            0 => new Vector3(-edge, 100f, RandomAxis(edge)),
            1 => new Vector3(edge, 100f, RandomAxis(edge)),
            2 => new Vector3(RandomAxis(edge), 100f, -edge),
            _ => new Vector3(RandomAxis(edge), 100f, edge)
        };
    }

    private float RandomAxis(float edge)
    {
        lock (_random)
            return (float)((_random.NextDouble() * 2d - 1d) * edge);
    }

    private void ScheduleChinookDrop(CH47HelicopterAIController chinook)
    {
        DropDirectorSettings drop = _config.Chinook.DropDirector;

        if (!_config.Chinook.Enabled ||
            !drop.Enabled ||
            chinook == null ||
            chinook.IsDestroyed ||
            chinook.net == null)
            return;

        ulong id = chinook.net.ID.Value;
        if (!_scheduledChinooks.Add(id))
            return;

        Delay(
            TimeSpan.FromSeconds(Math.Max(0d, drop.InitialDelaySeconds)),
            () => TryDirectedDrop(chinook, id),
            "ch47-drop-" + id);
    }

    private void TryDirectedDrop(CH47HelicopterAIController chinook, ulong id)
    {
        if (chinook == null || chinook.IsDestroyed || chinook.net == null || chinook.numCrates <= 0)
        {
            _scheduledChinooks.Remove(id);
            return;
        }

        DropDirectorSettings drop = _config.Chinook.DropDirector;
        Vector3 position = chinook.transform.position;

        bool valid =
            (!drop.AvoidWater || Rogue.Terrain.IsAboveWater(position, drop.WaterClearance)) &&
            (!drop.AvoidMonuments || IsAllowedMonumentPosition(position, drop)) &&
            !IsNearHackableCrate(position, drop.MinimumCrateSpacing);

        if (valid)
        {
            chinook.DropCrate();

            if (chinook.numCrates <= 0)
            {
                _scheduledChinooks.Remove(id);
                return;
            }
        }

        Delay(
            TimeSpan.FromSeconds(NextDouble(drop.MinimumRetrySeconds, drop.MaximumRetrySeconds)),
            () => TryDirectedDrop(chinook, id),
            "ch47-drop-" + id);
    }

    private bool IsAllowedMonumentPosition(Vector3 position, DropDirectorSettings drop)
    {
        RogueMonumentInfo? monument = Rogue.Monuments.FindNearest(position, drop.MonumentRadius);
        if (monument == null)
            return true;

        string name = string.IsNullOrWhiteSpace(monument.DisplayName)
            ? monument.Name
            : monument.DisplayName;

        if (drop.Monuments.TryGetValue(name, out bool enabled))
            return enabled;

        drop.Monuments[name] = false;
        SaveConfigurationDebounced(ConfigKey, _config);
        return false;
    }

    private bool IsNearHackableCrate(Vector3 position, float radius)
    {
        if (radius <= 0f)
            return false;

        float radiusSquared = radius * radius;

        foreach (RogueWorldEventEntity crate in WorldEvents.GetActive(RogueWorldEventType.HackableCrate))
        {
            if ((crate.Position - position).sqrMagnitude <= radiusSquared)
                return true;
        }

        return false;
    }

    private void EnsureSchedules()
    {
        DateTime now = DateTime.UtcNow;

        EnsureSchedule(_config.CargoPlane, ref _state.CargoPlaneUtc, now);
        EnsureSchedule(_config.PatrolHelicopter, ref _state.PatrolHelicopterUtc, now);
        EnsureSchedule(_config.Bradley, ref _state.BradleyUtc, now);
        EnsureSchedule(_config.Chinook, ref _state.ChinookUtc, now);
        EnsureSchedule(_config.CargoShip, ref _state.CargoShipUtc, now);

        SaveState();
    }

    private void EnsureSchedule(EventSettings settings, ref DateTime nextUtc, DateTime now)
    {
        if (!settings.Enabled)
            return;

        if (nextUtc == default || nextUtc < now.AddDays(-1))
            nextUtc = NextRun(settings, now);
    }

    private DateTime NextRun(EventSettings settings, DateTime fromUtc)
    {
        return fromUtc.AddSeconds(
            Math.Max(30d, NextDouble(settings.MinimumIntervalSeconds, settings.MaximumIntervalSeconds)));
    }

    private int NextInt(int minimum, int maximum)
    {
        int min = Math.Max(1, minimum);
        int max = Math.Max(min, maximum);

        lock (_random)
            return _random.Next(min, max + 1);
    }

    private double NextDouble(double minimum, double maximum)
    {
        double min = Math.Max(0d, minimum);
        double max = Math.Max(min, maximum);

        lock (_random)
            return min + (_random.NextDouble() * (max - min));
    }

    private void NormalizeConfiguration()
    {
        _config ??= new PluginConfig();
        _config.Version = CurrentVersion;
        _config.Scheduler ??= new SchedulerSettings();
        _config.CargoPlane ??= EventSettings.Default();
        _config.PatrolHelicopter ??= EventSettings.Default();
        _config.Bradley ??= EventSettings.Default();
        _config.Chinook ??= new ChinookSettings();
        _config.CargoShip ??= EventSettings.Default();

        NormalizeEvent(_config.CargoPlane);
        NormalizeEvent(_config.PatrolHelicopter);
        NormalizeEvent(_config.Bradley);
        NormalizeEvent(_config.Chinook);
        NormalizeEvent(_config.CargoShip);

        _config.Scheduler.TickSeconds = Math.Max(1, _config.Scheduler.TickSeconds);
        _config.Scheduler.ConcurrencyRetrySeconds = Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds);

        _config.Chinook.DropDirector ??= new DropDirectorSettings();
        DropDirectorSettings drop = _config.Chinook.DropDirector;
        drop.InitialDelaySeconds = Math.Max(0d, drop.InitialDelaySeconds);
        drop.MinimumRetrySeconds = Math.Max(1d, drop.MinimumRetrySeconds);
        drop.MaximumRetrySeconds = Math.Max(drop.MinimumRetrySeconds, drop.MaximumRetrySeconds);
        drop.MinimumCrateSpacing = Math.Max(0f, drop.MinimumCrateSpacing);
        drop.WaterClearance = Math.Max(0f, drop.WaterClearance);
        drop.MonumentRadius = Math.Max(0f, drop.MonumentRadius);
        drop.Monuments ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }

    private static void NormalizeEvent(EventSettings settings)
    {
        settings.MinimumIntervalSeconds = Math.Max(30, settings.MinimumIntervalSeconds);
        settings.MaximumIntervalSeconds =
            Math.Max(settings.MinimumIntervalSeconds, settings.MaximumIntervalSeconds);
        settings.MinimumSpawnCount = Math.Max(1, settings.MinimumSpawnCount);
        settings.MaximumSpawnCount = Math.Max(settings.MinimumSpawnCount, settings.MaximumSpawnCount);
        settings.MaximumConcurrent = Math.Max(1, settings.MaximumConcurrent);
    }

    private void SaveState()
    {
        SaveData(DataKey, _state);
    }

    private void SaveStateDebounced()
    {
        SaveDataDebounced(DataKey, _state, TimeSpan.FromSeconds(2));
    }

    [RogueCommand(
        "rred.status",
        Description = "Shows EventDirector event tracking and next-run status.",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult StatusCommand(RogueCommandContext context)
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        RogueWorldEventSnapshot snapshot = WorldEvents.GetSnapshot();

        return RogueCommandResult.Ok(
            $"RogueRustEventDirector v1.1.0 | " +
            $"Tracked={snapshot.TrackedEntities} | " +
            $"Plane={WorldEvents.CountActive(RogueWorldEventType.CargoPlane)} | " +
            $"Patrol={WorldEvents.CountActive(RogueWorldEventType.PatrolHelicopter)} | " +
            $"Bradley={WorldEvents.CountActive(RogueWorldEventType.Bradley)} | " +
            $"CH47={WorldEvents.CountActive(RogueWorldEventType.Chinook)} | " +
            $"Ship={WorldEvents.CountActive(RogueWorldEventType.CargoShip)} | " +
            $"Crates={WorldEvents.CountActive(RogueWorldEventType.HackableCrate)}");
    }

    [RogueCommand(
        "rred.spawn",
        Description = "Immediately spawns a managed world event.",
        Usage = "rred.spawn <plane|patrol|bradley|chinook|ship>",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult SpawnCommand(RogueCommandContext context, string eventName = "")
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        switch ((eventName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "plane":
                SpawnCargoPlane();
                break;
            case "patrol":
            case "heli":
                SpawnPatrolHelicopter();
                break;
            case "bradley":
            case "tank":
                SpawnBradley();
                break;
            case "chinook":
            case "ch47":
                SpawnChinook();
                break;
            case "ship":
            case "cargo":
                SpawnCargoShip();
                break;
            default:
                return RogueCommandResult.Fail(
                    "Usage: rred.spawn <plane|patrol|bradley|chinook|ship>");
        }

        return RogueCommandResult.Ok("Event spawn requested.");
    }

    [RogueCommand(
        "rred.reschedule",
        Description = "Generates fresh timers for all enabled managed events.",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult RescheduleCommand(RogueCommandContext context)
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        DateTime now = DateTime.UtcNow;
        _state.CargoPlaneUtc = NextRun(_config.CargoPlane, now);
        _state.PatrolHelicopterUtc = NextRun(_config.PatrolHelicopter, now);
        _state.BradleyUtc = NextRun(_config.Bradley, now);
        _state.ChinookUtc = NextRun(_config.Chinook, now);
        _state.CargoShipUtc = NextRun(_config.CargoShip, now);
        SaveState();

        return RogueCommandResult.Ok("All EventDirector schedules were regenerated.");
    }

    private bool CanUseAdminCommand(RogueCommandContext context)
    {
        if (context.IsServer)
            return true;

        return context.NativePlayer != null &&
               Rogue.Permissions.Has(context.NativePlayer.UserIDString, AdminPermission);
    }

    public sealed class PluginConfig
    {
        [JsonProperty("Scheduler Settings", Order = 10)]
        public SchedulerSettings Scheduler = new();

        [JsonProperty("Cargo Plane", Order = 20)]
        public EventSettings CargoPlane = EventSettings.Default();

        [JsonProperty("Patrol Helicopter", Order = 30)]
        public EventSettings PatrolHelicopter = EventSettings.Default();

        [JsonProperty("Bradley APC", Order = 40)]
        public EventSettings Bradley = EventSettings.Default();

        [JsonProperty("CH47 Chinook", Order = 50)]
        public ChinookSettings Chinook = new();

        [JsonProperty("Cargo Ship", Order = 60)]
        public EventSettings CargoShip = EventSettings.Default();

        [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
        public VersionNumber Version = CurrentVersion;
    }

    public sealed class SchedulerSettings
    {
        [JsonProperty("Tick Seconds")]
        public int TickSeconds = 1;

        [JsonProperty("Concurrency Retry Seconds")]
        public int ConcurrencyRetrySeconds = 120;
    }

    public class EventSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Disable Vanilla Spawns")]
        public bool DisableVanillaSpawns;

        [JsonProperty("Minimum Interval Seconds")]
        public int MinimumIntervalSeconds = 3600;

        [JsonProperty("Maximum Interval Seconds")]
        public int MaximumIntervalSeconds = 7200;

        [JsonProperty("Minimum Spawn Count")]
        public int MinimumSpawnCount = 1;

        [JsonProperty("Maximum Spawn Count")]
        public int MaximumSpawnCount = 1;

        [JsonProperty("Maximum Concurrent")]
        public int MaximumConcurrent = 1;

        public static EventSettings Default()
        {
            return new EventSettings();
        }
    }

    public sealed class ChinookSettings : EventSettings
    {
        [JsonProperty("Drop Director")]
        public DropDirectorSettings DropDirector = new();
    }

    public sealed class DropDirectorSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Block Vanilla Drops")]
        public bool BlockVanillaDrops = true;

        [JsonProperty("Initial Delay Seconds")]
        public double InitialDelaySeconds = 200d;

        [JsonProperty("Minimum Retry Seconds")]
        public double MinimumRetrySeconds = 40d;

        [JsonProperty("Maximum Retry Seconds")]
        public double MaximumRetrySeconds = 60d;

        [JsonProperty("Minimum Crate Spacing")]
        public float MinimumCrateSpacing = 300f;

        [JsonProperty("Avoid Water")]
        public bool AvoidWater = true;

        [JsonProperty("Water Clearance")]
        public float WaterClearance = 0.25f;

        [JsonProperty("Avoid Monuments")]
        public bool AvoidMonuments;

        [JsonProperty("Monument Radius")]
        public float MonumentRadius = 140f;

        [JsonProperty("Monuments")]
        public Dictionary<string, bool> Monuments =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ScheduleState
    {
        public DateTime CargoPlaneUtc;
        public DateTime PatrolHelicopterUtc;
        public DateTime BradleyUtc;
        public DateTime ChinookUtc;
        public DateTime CargoShipUtc;

        public bool IsEmpty()
        {
            return CargoPlaneUtc == default &&
                   PatrolHelicopterUtc == default &&
                   BradleyUtc == default &&
                   ChinookUtc == default &&
                   CargoShipUtc == default;
        }
    }
}
