using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins;

[Info("RogueRustEventDirector", "RogueAssassin", "1.2.1")]
[Description("Performance-focused world event scheduling and CH47 crate direction powered by RogueRust.")]
public sealed class RogueRustEventDirector : RogueRustPlugin
{
    private const string ConfigKey = "RogueRustEventDirector";
    private const string DataKey = "RogueRustEventDirector/schedule";
    private const string LegacyDataKey = "RogueRust/RogueRustEventDirector/schedule";
    [RoguePermission]
    private const string AdminPermission = "roguerusteventdirector.admin";

    private const string PluginVersion = "1.2.1";

    private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 2, 1);

    private const string CargoPlanePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
    private const string PatrolHelicopterPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
    private const string ChinookPrefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
    private const string CargoShipPrefab = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";

    private readonly HashSet<ulong> _scheduledChinooks = new();
    private readonly Dictionary<ulong, int> _chinookDropAttempts = new();
    private readonly System.Random _random = new();
    private DateTime _lastManagedSpawnUtc = DateTime.MinValue;
    private readonly Dictionary<RogueWorldEventType, int> _lastGameTipIndexes = new();

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
        _chinookDropAttempts.Clear();
    }

    private void OnEntitySpawned(BaseNetworkable entity)
    {
        if (entity is CH47HelicopterAIController chinook)
            ScheduleChinookDrop(chinook);
    }

    private void OnEntityKill(BaseNetworkable entity)
    {
        if (entity?.net != null)
        {
            ulong id = entity.net.ID.Value;
            _scheduledChinooks.Remove(id);
            _chinookDropAttempts.Remove(id);
        }
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
        Func<bool> spawn,
        DateTime now)
    {
        if (!settings.Enabled)
        {
            if (nextUtc != default)
            {
                nextUtc = default;
                SaveStateDebounced();
            }
            return;
        }

        if (nextUtc == default)
        {
            nextUtc = NextRun(settings, now);
            SaveStateDebounced();
            return;
        }

        if (now < nextUtc)
            return;

        int globalSpacing = Math.Max(0, _config.Scheduler.MinimumSecondsBetweenManagedEvents);
        if (_lastManagedSpawnUtc != DateTime.MinValue &&
            now < _lastManagedSpawnUtc.AddSeconds(globalSpacing))
        {
            nextUtc = _lastManagedSpawnUtc.AddSeconds(globalSpacing);
            SaveStateDebounced();
            return;
        }

        if (WorldEvents.IsAtOrAboveLimit(type, settings.MaximumConcurrent))
        {
            nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds));
            SaveStateDebounced();
            return;
        }

        int availableSlots = Math.Max(0, settings.MaximumConcurrent - WorldEvents.CountActive(type));
        int requested = NextInt(settings.MinimumSpawnCount, settings.MaximumSpawnCount);
        int amount = Math.Min(requested, availableSlots);
        int spawned = 0;

        if (amount > 0)
        {
            using (Measure("events", "spawn-" + type))
            {
                for (int i = 0; i < amount; i++)
                {
                    if (spawn())
                        spawned++;
                }
            }
        }

        if (spawned > 0)
        {
            _lastManagedSpawnUtc = now;
            _state.SetLastSpawn(type, now);
            nextUtc = NextRun(settings, now);
        }
        else
        {
            nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.SpawnFailureRetrySeconds));
            LogInformation("Events", $"Managed event {type} failed to spawn; retry scheduled.");
        }

        SaveStateDebounced();
    }

    private bool SpawnCargoPlane()
    {
        BaseEntity? entity = GameManager.server.CreateEntity(CargoPlanePrefab, GetAirSpawnPosition());
        if (entity == null)
            return false;

        entity.Spawn();
        ShowRandomGameTip(RogueWorldEventType.CargoPlane, _config.GameTips.CargoPlane);
        return true;
    }

    private bool SpawnPatrolHelicopter()
    {
        BaseEntity? entity = GameManager.server.CreateEntity(PatrolHelicopterPrefab, GetAirSpawnPosition());
        if (entity == null)
            return false;

        entity.Spawn();
        ShowRandomGameTip(RogueWorldEventType.PatrolHelicopter, _config.GameTips.PatrolHelicopter);
        return true;
    }

    private bool SpawnBradley()
    {
        if (BradleySpawner.singleton == null)
            return false;

        BradleySpawner.singleton.SpawnBradley();
        return true;
    }

    private bool SpawnChinook()
    {
        CH47HelicopterAIController? chinook =
            GameManager.server.CreateEntity(ChinookPrefab, GetAirSpawnPosition()) as CH47HelicopterAIController;

        if (chinook == null)
            return false;

        chinook.TriggeredEventSpawn();
        chinook.Spawn();
        ShowRandomGameTip(RogueWorldEventType.Chinook, _config.GameTips.Chinook);
        return true;
    }

    private bool SpawnCargoShip()
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
        if (entity == null)
            return false;

        entity.Spawn();
        ShowRandomGameTip(RogueWorldEventType.CargoShip, _config.GameTips.CargoShip);
        return true;
    }

    private void ShowRandomGameTip(RogueWorldEventType type, GameTipSettings settings)
    {
        if (!_config.GameTips.Enabled || settings == null || !settings.Enabled ||
            settings.Messages == null || settings.Messages.Count == 0)
            return;

        int index;
        lock (_random)
        {
            if (settings.Messages.Count == 1)
            {
                index = 0;
            }
            else
            {
                int previous = _lastGameTipIndexes.TryGetValue(type, out int last) ? last : -1;
                do
                {
                    index = _random.Next(settings.Messages.Count);
                }
                while (index == previous);
            }
        }

        _lastGameTipIndexes[type] = index;
        string message = settings.Messages[index];

        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!string.IsNullOrWhiteSpace(_config.GameTips.Prefix))
            message = $"{_config.GameTips.Prefix} {message}";

        foreach (BasePlayer player in BasePlayer.activePlayerList)
            player.SendConsoleCommand("gametip.showgametip", message);

        double duration = Math.Max(1d, _config.GameTips.DisplaySeconds);
        Delay(
            TimeSpan.FromSeconds(duration),
            () =>
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    player.SendConsoleCommand("gametip.hidegametip");
            },
            "event-gametip-hide");
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

        _chinookDropAttempts[id] = 0;

        Delay(
            TimeSpan.FromSeconds(Math.Max(0d, drop.InitialDelaySeconds)),
            () => TryDirectedDrop(chinook, id),
            "ch47-drop-" + id);
    }

    private void TryDirectedDrop(CH47HelicopterAIController chinook, ulong id)
    {
        if (chinook == null || chinook.IsDestroyed || chinook.net == null || chinook.numCrates <= 0)
        {
            ClearChinookDropTracking(id);
            return;
        }

        DropDirectorSettings drop = _config.Chinook.DropDirector;
        int attempts = _chinookDropAttempts.TryGetValue(id, out int current) ? current + 1 : 1;
        _chinookDropAttempts[id] = attempts;

        Vector3 position = chinook.transform.position;
        bool valid =
            (!drop.AvoidWater || Rogue.Terrain.IsAboveWater(position, drop.WaterClearance)) &&
            (!drop.AvoidMonuments || IsAllowedMonumentPosition(position, drop)) &&
            !IsNearHackableCrate(position, drop.MinimumCrateSpacing);

        bool maxAttemptsReached = attempts >= Math.Max(1, drop.MaximumAttempts);
        if (valid || (maxAttemptsReached && drop.FallbackDropAfterMaximumAttempts))
        {
            chinook.DropCrate();

            if (chinook.numCrates <= 0)
            {
                ClearChinookDropTracking(id);
                return;
            }

            _chinookDropAttempts[id] = 0;
        }
        else if (maxAttemptsReached)
        {
            ClearChinookDropTracking(id);
            return;
        }

        Delay(
            TimeSpan.FromSeconds(NextDouble(drop.MinimumRetrySeconds, drop.MaximumRetrySeconds)),
            () => TryDirectedDrop(chinook, id),
            "ch47-drop-" + id);
    }

    private void ClearChinookDropTracking(ulong id)
    {
        _scheduledChinooks.Remove(id);
        _chinookDropAttempts.Remove(id);
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
        int staggerSeconds = Math.Max(0, _config.Scheduler.InitialEventStaggerSeconds);
        int slot = 0;

        EnsureSchedule(_config.CargoPlane, ref _state.CargoPlaneUtc, now, slot++ * staggerSeconds);
        EnsureSchedule(_config.PatrolHelicopter, ref _state.PatrolHelicopterUtc, now, slot++ * staggerSeconds);
        EnsureSchedule(_config.Bradley, ref _state.BradleyUtc, now, slot++ * staggerSeconds);
        EnsureSchedule(_config.Chinook, ref _state.ChinookUtc, now, slot++ * staggerSeconds);
        EnsureSchedule(_config.CargoShip, ref _state.CargoShipUtc, now, slot++ * staggerSeconds);

        SaveState();
    }

    private void EnsureSchedule(EventSettings settings, ref DateTime nextUtc, DateTime now, int staggerSeconds)
    {
        if (!settings.Enabled)
        {
            nextUtc = default;
            return;
        }

        // Fresh installs and overdue schedules use the shorter startup window.
        if (nextUtc == default || nextUtc <= now)
            nextUtc = InitialRun(settings, now.AddSeconds(staggerSeconds));
    }

    private DateTime InitialRun(EventSettings settings, DateTime fromUtc)
    {
        int minimum = settings.InitialMinimumDelaySeconds > 0
            ? settings.InitialMinimumDelaySeconds
            : _config.Scheduler.InitialMinimumSpawnDelaySeconds;
        int maximum = settings.InitialMaximumDelaySeconds > 0
            ? settings.InitialMaximumDelaySeconds
            : _config.Scheduler.InitialMaximumSpawnDelaySeconds;

        minimum = Math.Max(30, minimum);
        maximum = Math.Max(minimum, maximum);

        return fromUtc.AddSeconds(NextDouble(minimum, maximum));
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
        _config.CargoPlane ??= EventSettings.CargoPlaneDefault();
        _config.PatrolHelicopter ??= EventSettings.PatrolDefault();
        _config.Bradley ??= EventSettings.BradleyDefault();
        _config.Chinook ??= new ChinookSettings();
        _config.CargoShip ??= EventSettings.CargoShipDefault();
        _config.GameTips ??= new GameTipConfiguration();

        NormalizeEvent(_config.CargoPlane);
        NormalizeEvent(_config.PatrolHelicopter);
        NormalizeEvent(_config.Bradley);
        NormalizeEvent(_config.Chinook);
        NormalizeEvent(_config.CargoShip);

        _config.Scheduler.TickSeconds = Math.Max(1, _config.Scheduler.TickSeconds);
        _config.Scheduler.ConcurrencyRetrySeconds = Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds);
        _config.Scheduler.SpawnFailureRetrySeconds = Math.Max(30, _config.Scheduler.SpawnFailureRetrySeconds);
        _config.Scheduler.MinimumSecondsBetweenManagedEvents = Math.Max(0, _config.Scheduler.MinimumSecondsBetweenManagedEvents);
        _config.Scheduler.InitialMinimumSpawnDelaySeconds = Math.Max(30, _config.Scheduler.InitialMinimumSpawnDelaySeconds);
        _config.Scheduler.InitialMaximumSpawnDelaySeconds = Math.Max(
            _config.Scheduler.InitialMinimumSpawnDelaySeconds,
            _config.Scheduler.InitialMaximumSpawnDelaySeconds);
        _config.Scheduler.InitialEventStaggerSeconds = Math.Max(0, _config.Scheduler.InitialEventStaggerSeconds);
        _config.GameTips.DisplaySeconds = Math.Max(1d, _config.GameTips.DisplaySeconds);

        _config.Chinook.DropDirector ??= new DropDirectorSettings();
        DropDirectorSettings drop = _config.Chinook.DropDirector;
        drop.InitialDelaySeconds = Math.Max(0d, drop.InitialDelaySeconds);
        drop.MinimumRetrySeconds = Math.Max(1d, drop.MinimumRetrySeconds);
        drop.MaximumRetrySeconds = Math.Max(drop.MinimumRetrySeconds, drop.MaximumRetrySeconds);
        drop.MinimumCrateSpacing = Math.Max(0f, drop.MinimumCrateSpacing);
        drop.WaterClearance = Math.Max(0f, drop.WaterClearance);
        drop.MonumentRadius = Math.Max(0f, drop.MonumentRadius);
        drop.MaximumAttempts = Math.Max(1, drop.MaximumAttempts);
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
        settings.InitialMinimumDelaySeconds = Math.Max(0, settings.InitialMinimumDelaySeconds);
        settings.InitialMaximumDelaySeconds = Math.Max(0, settings.InitialMaximumDelaySeconds);
        if (settings.InitialMaximumDelaySeconds > 0)
            settings.InitialMaximumDelaySeconds = Math.Max(settings.InitialMinimumDelaySeconds, settings.InitialMaximumDelaySeconds);
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

        return RogueCommandResult.Ok(
            $"RogueRustEventDirector v{PluginVersion} | " +
            $"Plane={EventStatus(RogueWorldEventType.CargoPlane, _config.CargoPlane, _state.CargoPlaneUtc)} | " +
            $"Patrol={EventStatus(RogueWorldEventType.PatrolHelicopter, _config.PatrolHelicopter, _state.PatrolHelicopterUtc)} | " +
            $"Bradley={EventStatus(RogueWorldEventType.Bradley, _config.Bradley, _state.BradleyUtc)} | " +
            $"CH47={EventStatus(RogueWorldEventType.Chinook, _config.Chinook, _state.ChinookUtc)} | " +
            $"Ship={EventStatus(RogueWorldEventType.CargoShip, _config.CargoShip, _state.CargoShipUtc)} | " +
            $"Crates={WorldEvents.CountActive(RogueWorldEventType.HackableCrate)}");
    }

    private string EventStatus(RogueWorldEventType type, EventSettings settings, DateTime nextUtc)
    {
        if (!settings.Enabled)
            return "Disabled";

        int active = WorldEvents.CountActive(type);
        return $"{active}/{settings.MaximumConcurrent}, Next={FormatRemaining(nextUtc)}, Last={FormatAgo(_state.GetLastSpawn(type))}";
    }

    private static string FormatRemaining(DateTime nextUtc)
    {
        if (nextUtc == default)
            return "Pending";

        TimeSpan remaining = nextUtc - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
            return "Due";

        if (remaining.TotalHours >= 1d)
            return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";

        if (remaining.TotalMinutes >= 1d)
            return $"{remaining.Minutes}m {remaining.Seconds}s";

        return $"{Math.Max(0, remaining.Seconds)}s";
    }

    private static string FormatAgo(DateTime utc)
    {
        if (utc == default)
            return "Never";

        TimeSpan elapsed = DateTime.UtcNow - utc;
        if (elapsed <= TimeSpan.Zero)
            return "Now";
        if (elapsed.TotalHours >= 1d)
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m ago";
        if (elapsed.TotalMinutes >= 1d)
            return $"{elapsed.Minutes}m {elapsed.Seconds}s ago";
        return $"{Math.Max(0, elapsed.Seconds)}s ago";
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

        bool spawned;
        switch ((eventName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "plane":
                spawned = SpawnCargoPlane();
                break;
            case "patrol":
            case "heli":
                spawned = SpawnPatrolHelicopter();
                break;
            case "bradley":
            case "tank":
                spawned = SpawnBradley();
                break;
            case "chinook":
            case "ch47":
                spawned = SpawnChinook();
                break;
            case "ship":
            case "cargo":
                spawned = SpawnCargoShip();
                break;
            default:
                return RogueCommandResult.Fail("Usage: rred.spawn <plane|patrol|bradley|chinook|ship>");
        }

        if (!spawned)
            return RogueCommandResult.Fail("Event spawn failed. Check the server log for details.");

        _lastManagedSpawnUtc = DateTime.UtcNow;
        return RogueCommandResult.Ok("Event spawned successfully.");
    }

    [RogueCommand(
        "rred.reschedule",
        Description = "Generates a fresh timer for one managed event or all enabled managed events.",
        Usage = "rred.reschedule [plane|patrol|bradley|chinook|ship|all]",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult RescheduleCommand(RogueCommandContext context, string eventName = "all")
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        DateTime now = DateTime.UtcNow;
        string target = (eventName ?? "all").Trim().ToLowerInvariant();

        if (target == "all")
        {
            RescheduleIfEnabled(_config.CargoPlane, ref _state.CargoPlaneUtc, now);
            RescheduleIfEnabled(_config.PatrolHelicopter, ref _state.PatrolHelicopterUtc, now);
            RescheduleIfEnabled(_config.Bradley, ref _state.BradleyUtc, now);
            RescheduleIfEnabled(_config.Chinook, ref _state.ChinookUtc, now);
            RescheduleIfEnabled(_config.CargoShip, ref _state.CargoShipUtc, now);
        }
        else
        {
            switch (target)
            {
                case "plane":
                    RescheduleIfEnabled(_config.CargoPlane, ref _state.CargoPlaneUtc, now);
                    break;
                case "patrol":
                case "heli":
                    RescheduleIfEnabled(_config.PatrolHelicopter, ref _state.PatrolHelicopterUtc, now);
                    break;
                case "bradley":
                case "tank":
                    RescheduleIfEnabled(_config.Bradley, ref _state.BradleyUtc, now);
                    break;
                case "chinook":
                case "ch47":
                    RescheduleIfEnabled(_config.Chinook, ref _state.ChinookUtc, now);
                    break;
                case "ship":
                case "cargo":
                    RescheduleIfEnabled(_config.CargoShip, ref _state.CargoShipUtc, now);
                    break;
                default:
                    return RogueCommandResult.Fail("Usage: rred.reschedule [plane|patrol|bradley|chinook|ship|all]");
            }
        }

        SaveState();
        return RogueCommandResult.Ok(target == "all"
            ? "All enabled EventDirector schedules were regenerated."
            : $"EventDirector schedule regenerated for {target}.");
    }

    private void RescheduleIfEnabled(EventSettings settings, ref DateTime nextUtc, DateTime now)
    {
        nextUtc = settings.Enabled ? NextRun(settings, now) : default;
    }

    [RogueCommand(
        "rred.next",
        Description = "Shows when a managed event is next scheduled.",
        Usage = "rred.next <plane|patrol|bradley|chinook|ship>",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult NextCommand(RogueCommandContext context, string eventName = "")
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        if (!TryGetEventSchedule(eventName, out EventSettings settings, out DateTime nextUtc, out _, out string display))
            return RogueCommandResult.Fail("Usage: rred.next <plane|patrol|bradley|chinook|ship>");

        return RogueCommandResult.Ok(settings.Enabled
            ? $"{display}: next event in {FormatRemaining(nextUtc)}."
            : $"{display}: disabled.");
    }

    [RogueCommand(
        "rred.delay",
        Description = "Delays the next managed event by a number of minutes.",
        Usage = "rred.delay <plane|patrol|bradley|chinook|ship> <minutes>",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult DelayCommand(RogueCommandContext context, string eventName = "", double minutes = 0d)
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        if (minutes <= 0d)
            return RogueCommandResult.Fail("Minutes must be greater than zero.");

        if (!TryDelayEvent(eventName, TimeSpan.FromMinutes(minutes), out string display))
            return RogueCommandResult.Fail("Usage: rred.delay <plane|patrol|bradley|chinook|ship> <minutes>");

        SaveState();
        return RogueCommandResult.Ok($"{display} delayed by {minutes:0.##} minute(s).");
    }

    [RogueCommand(
        "rred.trigger",
        Description = "Immediately triggers a managed world event.",
        Usage = "rred.trigger <plane|patrol|bradley|chinook|ship>",
        Permission = AdminPermission,
        AllowConsole = true,
        AllowChat = true)]
    private RogueCommandResult TriggerCommand(RogueCommandContext context, string eventName = "")
    {
        if (!CanUseAdminCommand(context))
            return RogueCommandResult.Fail("You do not have permission to use this command.");

        RogueWorldEventType type;
        EventSettings settings;
        Func<bool> spawn;
        string display;

        if (!TryGetEvent(eventName, out type, out settings, out spawn, out display))
            return RogueCommandResult.Fail("Usage: rred.trigger <plane|patrol|bradley|chinook|ship>");

        if (!spawn())
            return RogueCommandResult.Fail($"{display} failed to spawn.");

        DateTime now = DateTime.UtcNow;
        _lastManagedSpawnUtc = now;
        _state.SetLastSpawn(type, now);
        RescheduleEvent(type, settings, now);
        SaveState();

        return RogueCommandResult.Ok($"{display} triggered successfully.");
    }

    private bool TryGetEvent(
        string eventName,
        out RogueWorldEventType type,
        out EventSettings settings,
        out Func<bool> spawn,
        out string display)
    {
        switch ((eventName ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "plane":
                type = RogueWorldEventType.CargoPlane; settings = _config.CargoPlane; spawn = SpawnCargoPlane; display = "Cargo Plane"; return true;
            case "patrol":
            case "heli":
                type = RogueWorldEventType.PatrolHelicopter; settings = _config.PatrolHelicopter; spawn = SpawnPatrolHelicopter; display = "Patrol Helicopter"; return true;
            case "bradley":
            case "tank":
                type = RogueWorldEventType.Bradley; settings = _config.Bradley; spawn = SpawnBradley; display = "Bradley APC"; return true;
            case "chinook":
            case "ch47":
                type = RogueWorldEventType.Chinook; settings = _config.Chinook; spawn = SpawnChinook; display = "CH47 Chinook"; return true;
            case "ship":
            case "cargo":
                type = RogueWorldEventType.CargoShip; settings = _config.CargoShip; spawn = SpawnCargoShip; display = "Cargo Ship"; return true;
            default:
                type = default; settings = null!; spawn = null!; display = string.Empty; return false;
        }
    }

    private bool TryGetEventSchedule(
        string eventName,
        out EventSettings settings,
        out DateTime nextUtc,
        out RogueWorldEventType type,
        out string display)
    {
        if (!TryGetEvent(eventName, out type, out settings, out _, out display))
        {
            nextUtc = default;
            return false;
        }

        nextUtc = GetNextSchedule(type);
        return true;
    }

    private DateTime GetNextSchedule(RogueWorldEventType type)
    {
        if (type == RogueWorldEventType.CargoPlane) return _state.CargoPlaneUtc;
        if (type == RogueWorldEventType.PatrolHelicopter) return _state.PatrolHelicopterUtc;
        if (type == RogueWorldEventType.Bradley) return _state.BradleyUtc;
        if (type == RogueWorldEventType.Chinook) return _state.ChinookUtc;
        if (type == RogueWorldEventType.CargoShip) return _state.CargoShipUtc;
        return default;
    }

    private bool TryDelayEvent(string eventName, TimeSpan delay, out string display)
    {
        display = string.Empty;
        string key = (eventName ?? string.Empty).Trim().ToLowerInvariant();
        DateTime now = DateTime.UtcNow;

        switch (key)
        {
            case "plane": display = "Cargo Plane"; _state.CargoPlaneUtc = DelaySchedule(_state.CargoPlaneUtc, now, delay); return true;
            case "patrol":
            case "heli": display = "Patrol Helicopter"; _state.PatrolHelicopterUtc = DelaySchedule(_state.PatrolHelicopterUtc, now, delay); return true;
            case "bradley":
            case "tank": display = "Bradley APC"; _state.BradleyUtc = DelaySchedule(_state.BradleyUtc, now, delay); return true;
            case "chinook":
            case "ch47": display = "CH47 Chinook"; _state.ChinookUtc = DelaySchedule(_state.ChinookUtc, now, delay); return true;
            case "ship":
            case "cargo": display = "Cargo Ship"; _state.CargoShipUtc = DelaySchedule(_state.CargoShipUtc, now, delay); return true;
            default: return false;
        }
    }

    private static DateTime DelaySchedule(DateTime current, DateTime now, TimeSpan delay)
    {
        return (current > now ? current : now).Add(delay);
    }

    private void RescheduleEvent(RogueWorldEventType type, EventSettings settings, DateTime now)
    {
        DateTime next = NextRun(settings, now);
        if (type == RogueWorldEventType.CargoPlane) _state.CargoPlaneUtc = next;
        else if (type == RogueWorldEventType.PatrolHelicopter) _state.PatrolHelicopterUtc = next;
        else if (type == RogueWorldEventType.Bradley) _state.BradleyUtc = next;
        else if (type == RogueWorldEventType.Chinook) _state.ChinookUtc = next;
        else if (type == RogueWorldEventType.CargoShip) _state.CargoShipUtc = next;
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
        public EventSettings CargoPlane = EventSettings.CargoPlaneDefault();

        [JsonProperty("Patrol Helicopter", Order = 30)]
        public EventSettings PatrolHelicopter = EventSettings.PatrolDefault();

        [JsonProperty("Bradley APC", Order = 40)]
        public EventSettings Bradley = EventSettings.BradleyDefault();

        [JsonProperty("CH47 Chinook", Order = 50)]
        public ChinookSettings Chinook = new();

        [JsonProperty("Cargo Ship", Order = 60)]
        public EventSettings CargoShip = EventSettings.CargoShipDefault();

        [JsonProperty("GameTip Announcements", Order = 70)]
        public GameTipConfiguration GameTips = new();

        [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
        public VersionNumber Version = CurrentVersion;
    }

    public sealed class SchedulerSettings
    {
        [JsonProperty("Tick Seconds")]
        public int TickSeconds = 1;

        [JsonProperty("Concurrency Retry Seconds")]
        public int ConcurrencyRetrySeconds = 120;

        [JsonProperty("Spawn Failure Retry Seconds")]
        public int SpawnFailureRetrySeconds = 120;

        [JsonProperty("Minimum Seconds Between Managed Events")]
        public int MinimumSecondsBetweenManagedEvents = 600;

        [JsonProperty("Initial Minimum Spawn Delay Seconds")]
        public int InitialMinimumSpawnDelaySeconds = 600;

        [JsonProperty("Initial Maximum Spawn Delay Seconds")]
        public int InitialMaximumSpawnDelaySeconds = 1800;

        [JsonProperty("Initial Event Stagger Seconds")]
        public int InitialEventStaggerSeconds = 120;
    }

    public class EventSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Disable Vanilla Spawns")]
        public bool DisableVanillaSpawns;

        [JsonProperty("Minimum Interval Seconds")]
        public int MinimumIntervalSeconds = 1800;

        [JsonProperty("Maximum Interval Seconds")]
        public int MaximumIntervalSeconds = 3600;

        [JsonProperty("Minimum Spawn Count")]
        public int MinimumSpawnCount = 1;

        [JsonProperty("Maximum Spawn Count")]
        public int MaximumSpawnCount = 1;

        [JsonProperty("Maximum Concurrent")]
        public int MaximumConcurrent = 1;

        [JsonProperty("Initial Minimum Delay Seconds (0 = Scheduler Default)")]
        public int InitialMinimumDelaySeconds;

        [JsonProperty("Initial Maximum Delay Seconds (0 = Scheduler Default)")]
        public int InitialMaximumDelaySeconds;

        public static EventSettings Default()
        {
            return new EventSettings();
        }

        public static EventSettings CargoPlaneDefault()
        {
            return new EventSettings
            {
                MinimumIntervalSeconds = 1800,
                MaximumIntervalSeconds = 3600
            };
        }

        public static EventSettings PatrolDefault()
        {
            return new EventSettings
            {
                MinimumIntervalSeconds = 2700,
                MaximumIntervalSeconds = 5400
            };
        }

        public static EventSettings BradleyDefault()
        {
            return new EventSettings
            {
                MinimumIntervalSeconds = 3600,
                MaximumIntervalSeconds = 7200
            };
        }

        public static EventSettings CargoShipDefault()
        {
            return new EventSettings
            {
                MinimumIntervalSeconds = 3600,
                MaximumIntervalSeconds = 7200
            };
        }
    }

    public sealed class ChinookSettings : EventSettings
    {
        public ChinookSettings()
        {
            MinimumIntervalSeconds = 2700;
            MaximumIntervalSeconds = 5400;
        }

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

        [JsonProperty("Maximum Attempts")]
        public int MaximumAttempts = 30;

        [JsonProperty("Fallback Drop After Maximum Attempts")]
        public bool FallbackDropAfterMaximumAttempts = true;

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

    public sealed class GameTipConfiguration
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Display Seconds")]
        public double DisplaySeconds = 8d;

        [JsonProperty("Prefix")]
        public string Prefix = "<color=#f2a900>[EVENT]</color>";

        [JsonProperty("Cargo Plane")]
        public GameTipSettings CargoPlane = new()
        {
            Messages = new List<string>
            {
                "Keep your eyes on the sky — a cargo plane is inbound.",
                "A cargo plane has entered the airspace. Suddenly everyone is an aviation expert.",
                "Supplies are on the move — sharpen your rocks and your questionable decisions.",
                "Cargo plane inbound! Somewhere, a naked with a spear just started sprinting.",
                "Free loot is falling from the sky. The bullets are complimentary.",
                "The sky has delivered a loot box. Terms and conditions include possible death.",
                "Cargo plane overhead — time to abandon everything you were doing.",
                "An airdrop is coming. Friendship has been temporarily disabled.",
                "Incoming supplies! May the fastest grub win.",
                "Cargo plane spotted. Prepare for twenty minutes of bushes pretending to be empty.",
                "The loot gods have opened the cargo hatch.",
                "Airdrop inbound — because your inventory wasn't stressful enough already."
            }
        };

        [JsonProperty("Patrol Helicopter")]
        public GameTipSettings PatrolHelicopter = new()
        {
            Messages = new List<string>
            {
                "Take cover — the patrol helicopter is hunting.",
                "Patrol helicopter inbound. That AK suddenly feels a little too visible.",
                "Rotor blades overhead — armed players may wish to reconsider their life choices.",
                "The patrol helicopter has arrived to conduct an unscheduled roof inspection.",
                "Patrol heli inbound! Please keep arms, legs and rocket launchers inside the compound.",
                "Someone woke up the angry flying blender.",
                "The patrol helicopter is looking for trouble. Conveniently, this island has plenty.",
                "Heli inbound — roofs are about to become significantly less relaxing.",
                "Patrol helicopter spotted. Naked players: enjoy your temporary diplomatic immunity.",
                "The sky is angry, armed, and circling your base.",
                "Patrol heli has entered the chat. Roof campers are typing nervously.",
                "Incoming patrol helicopter — now is an excellent time to remember where you left the meds."
            }
        };

        [JsonProperty("CH47 Chinook")]
        public GameTipSettings Chinook = new()
        {
            Messages = new List<string>
            {
                "CH47 Chinook inbound — watch for a crate drop.",
                "Heavy rotors overhead — the Chinook brought loot and absolutely no guarantees.",
                "A Chinook has entered the area. Scientists have once again chosen violence.",
                "CH47 inbound! Somewhere below, a monument is about to get considerably busier.",
                "The big helicopter is here. Try not to stand where the crate lands.",
                "Chinook spotted — valuable cargo, armed scientists, terrible decision-making ahead.",
                "Heavy rotors approaching. The island's loot economy is about to receive a stimulus package.",
                "CH47 inbound — follow the helicopter, then immediately distrust everyone else doing the same.",
                "The Chinook is carrying a crate. Your neighbours are carrying grudges.",
                "Incoming CH47! Nothing brings the server together like loot worth fighting over.",
                "Chinook overhead — scientists are delivering today's community disagreement.",
                "The flying loot bus has arrived. Please form an orderly firefight."
            }
        };

        [JsonProperty("Cargo Ship")]
        public GameTipSettings CargoShip = new()
        {
            Messages = new List<string>
            {
                "Cargo Ship spotted offshore — prepare for a fight.",
                "A Cargo Ship has entered the waters around the island. Boats suddenly have somewhere important to be.",
                "Movement offshore — the Cargo Ship has arrived.",
                "Cargo Ship inbound! Time to discover who remembered low grade fuel.",
                "The ocean has spawned loot with guns attached to it.",
                "Cargo Ship spotted — seasickness is temporary, loot is... also temporary.",
                "All aboard the floating PvP convention.",
                "Cargo Ship has arrived. Bring a boat, bring ammo, and definitely bring poor judgement.",
                "Something valuable is offshore. Naturally, half the server is already on the way.",
                "Cargo Ship inbound — the scientists have reserved the upper deck for violence.",
                "The floating loot fortress is back. Swimming there is technically an option.",
                "Cargo Ship spotted! Your peaceful fishing trip has been cancelled."
            }
        };
    }

    public sealed class GameTipSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Messages")]
        public List<string> Messages = new();
    }

    public sealed class ScheduleState
    {
        public DateTime CargoPlaneUtc;
        public DateTime PatrolHelicopterUtc;
        public DateTime BradleyUtc;
        public DateTime ChinookUtc;
        public DateTime CargoShipUtc;

        public DateTime LastCargoPlaneUtc;
        public DateTime LastPatrolHelicopterUtc;
        public DateTime LastBradleyUtc;
        public DateTime LastChinookUtc;
        public DateTime LastCargoShipUtc;

        public DateTime GetLastSpawn(RogueWorldEventType type)
        {
            if (type == RogueWorldEventType.CargoPlane) return LastCargoPlaneUtc;
            if (type == RogueWorldEventType.PatrolHelicopter) return LastPatrolHelicopterUtc;
            if (type == RogueWorldEventType.Bradley) return LastBradleyUtc;
            if (type == RogueWorldEventType.Chinook) return LastChinookUtc;
            if (type == RogueWorldEventType.CargoShip) return LastCargoShipUtc;
            return default;
        }

        public void SetLastSpawn(RogueWorldEventType type, DateTime utc)
        {
            if (type == RogueWorldEventType.CargoPlane) LastCargoPlaneUtc = utc;
            else if (type == RogueWorldEventType.PatrolHelicopter) LastPatrolHelicopterUtc = utc;
            else if (type == RogueWorldEventType.Bradley) LastBradleyUtc = utc;
            else if (type == RogueWorldEventType.Chinook) LastChinookUtc = utc;
            else if (type == RogueWorldEventType.CargoShip) LastCargoShipUtc = utc;
        }

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
