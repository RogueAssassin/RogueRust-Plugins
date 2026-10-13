using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins;

[Info("RogueRustEventDirector", "RogueAssassin", "1.4.1")]
[Description("Performance-focused world event scheduling and CH47 crate direction powered by RogueRust.")]
public sealed class RogueRustEventDirector : RogueRustPlugin
{
    private const string ConfigKey = "RogueRustEventDirector";
    private const string DataKey = "RogueRustEventDirector/schedule";
    private const string LegacyDataKey = "RogueRust/RogueRustEventDirector/schedule";
    [RoguePermission]
    private const string AdminPermission = "roguerusteventdirector.admin";

    private const string PluginVersion = "1.4.1";

    private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 4, 1);

    private const string CargoPlanePrefab = "assets/prefabs/npc/cargo plane/cargo_plane.prefab";
    private const string PatrolHelicopterPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
    private const string ChinookPrefab = "assets/prefabs/npc/ch47/ch47scientists.entity.prefab";
    private const string CargoShipPrefab = "assets/content/vehicles/boats/cargoship/cargoshiptest.prefab";

    private readonly HashSet<ulong> _scheduledChinooks = new();
    private readonly Dictionary<ulong, int> _chinookDropAttempts = new();
    private readonly System.Random _random = new();
    private DateTime _lastManagedSpawnUtc = DateTime.MinValue;
    private readonly Dictionary<RogueWorldEventType, int> _lastGameTipIndexes = new();

    private int _blockedVanillaEvents;
    private BradleySpawner? _heldSpawner;
    private static readonly System.Reflection.FieldInfo? PendingRespawnField = typeof(BradleySpawner).GetField(
        "pendingRespawn",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    private PluginConfig _config = new();
    private ScheduleState _state = new();

    private void Init()
    {
        LogInformation("Lifecycle",
        $"{Name} {PluginVersion} initialized. " +
        $"Config={Name}.json; Data=RogueRust/RogueRustEventDirector/; Lang=None");
        // JSON.NET normally populates pre-initialized collections. GameTip message lists have
        // built-in defaults, so Populate would append the saved JSON messages to those defaults on
        // every load. Replace collection instances instead, matching the RogueRust config standard.
        // RogueRust config service handles deserialization; avoid unsafe Init-time Oxide settings access.
        _config = LoadConfiguration(ConfigKey, () => new PluginConfig()) ?? new PluginConfig();
        VersionNumber loadedVersion = _config.Version;
        MigrateConfiguration(loadedVersion);
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
        LogInformation("Events", "Vanilla spawns disabled for: " + DescribeDisabledVanilla() + ".");

        // Mark a Bradley that was already out when the plugin loaded.
        foreach (RogueWorldEventEntity tracked in WorldEvents.GetActive(RogueWorldEventType.Bradley))
            if (tracked.NativeEntity is BradleyAPC bradley)
                AddMarker(bradley, _config.Markers.Bradley);

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
        ReleaseVanillaBradley();
        RemoveAllMarkers();
        SaveState();
        _scheduledChinooks.Clear();
        _chinookDropAttempts.Clear();
    }

    // Typed hook signatures: Oxide only invokes these for Chinooks instead of every entity on the server.
    private void OnEntitySpawned(CH47HelicopterAIController chinook)
    {
        if (chinook != null)
            ScheduleChinookDrop(chinook);
    }

    private void OnEntityKill(CH47HelicopterAIController entity)
    {
        if (entity?.net != null && _scheduledChinooks.Count + _chinookDropAttempts.Count > 0)
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

        // Match the exact prefab each vanilla event spawns. A loose word match ("ship", "plane") could
        // also swallow an unrelated event added by a later game update.
        EventSettings? settings =
            IsPrefab(path, PatrolHelicopterPrefab) ? _config.PatrolHelicopter :
            IsPrefab(path, CargoShipPrefab) ? _config.CargoShip :
            IsPrefab(path, CargoPlanePrefab) ? _config.CargoPlane :
            IsPrefab(path, ChinookPrefab) ? _config.Chinook : null;

        if (settings == null || !settings.DisableVanillaSpawns)
            return null;

        _blockedVanillaEvents++;
        return true;
    }

    private object? CanHelicopterDropCrate(CH47HelicopterAIController chinook)
    {
        DropDirectorSettings drop = _config.Chinook.DropDirector;
        if (!_config.Chinook.Enabled || !drop.Enabled || !drop.BlockVanillaDrops)
            return null;

        ScheduleChinookDrop(chinook);
        return false;
    }

    private string DescribeDisabledVanilla()
    {
        List<string> disabled = new();
        if (_config.CargoPlane.DisableVanillaSpawns) disabled.Add("Cargo Plane");
        if (_config.PatrolHelicopter.DisableVanillaSpawns) disabled.Add("Patrol Helicopter");
        if (_config.Bradley.DisableVanillaSpawns) disabled.Add("Bradley");
        if (_config.Chinook.DisableVanillaSpawns) disabled.Add("CH47");
        if (_config.CargoShip.DisableVanillaSpawns) disabled.Add("Cargo Ship");
        return disabled.Count == 0 ? "none" : string.Join(", ", disabled);
    }

    // Active events of a type as far as scheduling is concerned. A helicopter flying to a
    // RogueRustHeliCrash site is that plugin's event, not a patrol, so it does not use up a patrol slot.
    private int CountManaged(RogueWorldEventType type)
    {
        int active = WorldEvents.CountActive(type);
        if (type != RogueWorldEventType.PatrolHelicopter || active == 0)
            return active;

        foreach (RogueWorldEventEntity tracked in WorldEvents.GetActive(type))
        {
            if (tracked.NativeEntity != null && Interface.CallHook("IsHeliCrashHelicopter", tracked.NativeEntity) != null)
                active--;
        }
        return Math.Max(0, active);
    }

    private static bool IsPrefab(string path, string prefab) =>
        string.Equals(path, prefab, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(System.IO.Path.GetFileName(path), System.IO.Path.GetFileName(prefab), StringComparison.OrdinalIgnoreCase);

    // The Launch Site Bradley is not a triggered event: its spawner re-arms itself whenever the APC
    // is gone. While vanilla spawns are disabled the spawner is kept in its "respawn already pending"
    // state with nothing scheduled, so only this plugin's schedule can bring the Bradley back.
    private void HoldVanillaBradley()
    {
        if (!_config.Bradley.DisableVanillaSpawns || PendingRespawnField == null)
        {
            if (_heldSpawner != null) ReleaseVanillaBradley();
            return;
        }

        BradleySpawner? spawner = BradleySpawner.singleton;
        if (spawner == null)
            return;

        _heldSpawner = spawner;
        if (spawner.IsInvoking(nameof(BradleySpawner.DoRespawn)))
            spawner.CancelInvoke(nameof(BradleySpawner.DoRespawn));
        if (!(bool)PendingRespawnField.GetValue(spawner))
            PendingRespawnField.SetValue(spawner, true);
    }

    private void ReleaseVanillaBradley()
    {
        BradleySpawner? spawner = _heldSpawner;
        _heldSpawner = null;
        if (spawner != null && PendingRespawnField != null)
            PendingRespawnField.SetValue(spawner, false);
    }

    private void SchedulerTick()
    {
        DateTime now = DateTime.UtcNow;
        HoldVanillaBradley();
        UpdateMarkers();

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

        // Bradley is a special case: this plugin controls Rust's native Launch Site BradleySpawner,
        // not arbitrary/custom Bradley APCs. Do not use the generic RogueRust Bradley count to decide
        // whether the Launch Site Bradley may spawn because other plugins/maps can create BradleyAPCs.
        // Instead, ask the live world specifically whether a Bradley is operating at Launch Site.
        if (type == RogueWorldEventType.Bradley)
        {
            if (IsLaunchSiteBradleyActive())
            {
                nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds));
                SaveStateDebounced();
                return;
            }

            // The native spawner is the only supported Bradley spawn path. If Launch Site/its spawner
            // is unavailable (for example on a map without Launch Site), quietly retry instead of
            // reporting a generic event spawn failure every retry interval.
            if (BradleySpawner.singleton == null)
            {
                nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds));
                SaveStateDebounced();
                return;
            }
        }
        else if (CountManaged(type) >= settings.MaximumConcurrent)
        {
            nextUtc = now.AddSeconds(Math.Max(30, _config.Scheduler.ConcurrencyRetrySeconds));
            SaveStateDebounced();
            return;
        }

        // Launch Site only supports one native Bradley. Its availability is determined above from
        // the actual world entity rather than the generic world-event tracker.
        int availableSlots = type == RogueWorldEventType.Bradley
            ? 1
            : Math.Max(0, settings.MaximumConcurrent - CountManaged(type));
        int requested = type == RogueWorldEventType.Bradley
            ? 1
            : NextInt(settings.MinimumSpawnCount, settings.MaximumSpawnCount);
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

        // Left alone, the plane picks its own drop point, which can be on the shoreline or in a lake.
        // Give it a checked spot on dry, reasonably flat land before it works out its flight path.
        if (_config.Placement.DirectAirdropsToLand && entity is CargoPlane plane && TryPickLandPosition(out Vector3 drop))
            plane.InitDropPosition(drop);

        entity.Spawn();
        if (entity.net != null)
            _managedPlanes.Add(entity.net.ID.Value);
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
        BradleySpawner? spawner = BradleySpawner.singleton;
        if (spawner == null)
            return false;

        // A Bradley appearing between the scheduler pre-check and this method is not a failure.
        // The requested state (one Launch Site Bradley alive) is already satisfied.
        if (IsLaunchSiteBradleyActive())
            return true;

        spawner.SpawnBradley();

        // SpawnBradley() does not provide a useful success result. Verify on the next frame that the
        // native Launch Site APC exists instead of blindly treating the request as a successful spawn.
        // Returning true here records the accepted spawn request; subsequent scheduling is still guarded
        // by IsLaunchSiteBradleyActive(), so a second Launch Site Bradley can never be requested while
        // the first one is alive.
        return true;
    }

    private bool IsLaunchSiteBradleyActive()
    {
        BradleySpawner? spawner = BradleySpawner.singleton;
        // RogueRust already tracks every live BradleyAPC by network id; reading that index avoids a
        // full Unity object scan (FindObjectsOfType) each time the Bradley schedule comes due/retries.
        List<BradleyAPC> bradleys = new List<BradleyAPC>();
        foreach (RogueWorldEventEntity tracked in WorldEvents.GetActive(RogueWorldEventType.Bradley))
        {
            if (tracked.NativeEntity is BradleyAPC apc)
                bradleys.Add(apc);
        }
        if (bradleys.Count == 0)
            return false;

        Vector3 spawnerPosition = spawner != null ? spawner.transform.position : Vector3.zero;
        bool hasSpawnerPosition = spawner != null;
        const float launchSiteRadius = 1800f;
        float launchSiteRadiusSquared = launchSiteRadius * launchSiteRadius;

        foreach (BradleyAPC bradley in bradleys)
        {
            if (bradley == null || bradley.IsDestroyed || !bradley.gameObject.activeInHierarchy)
                continue;

            // Primary check: BradleySpawner.singleton is Rust's vanilla Launch Site spawner. A live
            // Bradley on/around that spawner's patrol area is therefore the Bradley this plugin owns.
            // This avoids allowing unrelated road/custom-event Bradleys elsewhere on the map to block it.
            if (hasSpawnerPosition &&
                (bradley.transform.position - spawnerPosition).sqrMagnitude <= launchSiteRadiusSquared)
                return true;

            // Fallback for maps/builds where the spawner transform is not centred on the patrol path.
            // Associate the APC with Launch Site through RogueRust's monument service.
            RogueMonumentInfo? monument = Rogue.Monuments.FindNearest(bradley.transform.position, launchSiteRadius);
            if (monument == null)
                continue;

            string name = string.IsNullOrWhiteSpace(monument.DisplayName)
                ? monument.Name
                : monument.DisplayName;

            if (!string.IsNullOrWhiteSpace(name) &&
                (name.IndexOf("launch site", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("launch_site", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("launchsite", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
        }

        return false;
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

        CargoShip? ship = GameManager.server.CreateEntity(CargoShipPrefab, position) as CargoShip;
        if (ship == null)
            return false;

        // Rust's native Cargo Ship event performs triggered-event initialization before Spawn().
        // This initializes event-specific behaviour, including the normal map/event presentation.
        ship.TriggeredEventSpawn();
        ship.Spawn();
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

        // The crate lands on whatever is under the helicopter, so the checks are made on the ground
        // below it. (Testing the helicopter itself always passed: it flies far above the sea.)
        bool onLand = !drop.AvoidWater || IsDryGround(position, drop.WaterClearance);
        bool valid =
            onLand &&
            IsBuildableGround(position) &&
            (!drop.AvoidMonuments || IsAllowedMonumentPosition(position, drop)) &&
            !IsNearHackableCrate(position, drop.MinimumCrateSpacing);

        // The fallback relaxes the placement preferences, never the water rule: a crate on the sea
        // bed is not a usable drop. If the helicopter never crosses land it leaves with its crate.
        bool maxAttemptsReached = attempts >= Math.Max(1, drop.MaximumAttempts);
        if (valid || (maxAttemptsReached && drop.FallbackDropAfterMaximumAttempts && onLand))
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

    #region Placement

    private const int WaterTopology =
        TerrainTopology.OCEAN | TerrainTopology.OFFSHORE | TerrainTopology.LAKE | TerrainTopology.RIVER;

    // True when the ground at this map position is above the water line and not sea, lake or river bed.
    private bool IsDryGround(Vector3 position, float clearance)
    {
        if (!Rogue.Terrain.TryGetHeight(position, out float ground))
            return false;
        if (Rogue.Terrain.TryGetWaterHeight(position, out float water) && ground < water + Math.Max(0.25f, clearance))
            return false;
        return !World.TryGetTopology(position, out int topology) || (topology & WaterTopology) == 0;
    }

    // Slope and distance from player buildings, as set under Placement Rules.
    private bool IsBuildableGround(Vector3 position)
    {
        PlacementSettings rules = _config.Placement;
        if (rules.MaximumSlope > 0f && TerrainMeta.HeightMap != null &&
            TerrainMeta.HeightMap.GetSlope(position) > rules.MaximumSlope)
            return false;

        if (rules.AvoidBases <= 0f || !Rogue.Terrain.TryGetHeight(position, out float ground))
            return true;

        position.y = ground;
        return !Physics.CheckSphere(position, rules.AvoidBases, Rust.Layers.Mask.Construction, QueryTriggerInteraction.Ignore);
    }

    private bool TryPickLandPosition(out Vector3 position)
    {
        float half = World.WorldSize * 0.5f - Math.Max(50f, _config.Placement.EdgeBuffer);
        for (int attempt = 0; half > 0f && attempt < 200; attempt++)
        {
            position = new Vector3(RandomAxis(half), 0f, RandomAxis(half));
            if (IsDryGround(position, 0.5f) && IsBuildableGround(position))
                return true;
        }

        position = Vector3.zero;
        return false;
    }

    #endregion

    #region Map markers

    private const string LabelMarkerPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
    private const string RadiusMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";

    private sealed class TrackedMarker
    {
        public BaseEntity Target = null!;
        public VendingMachineMapMarker? Label;
        public MapMarkerGenericRadius? Circle;
        public Vector3 LastPosition;
    }

    private readonly List<TrackedMarker> _markers = new();
    private readonly HashSet<ulong> _managedPlanes = new();
    private int _markerTick;

    // Cargo ships, patrol helicopters, CH47s and hackable crates create their own map marker when
    // they spawn. The Bradley and airdrops have none in the game, so they get a labelled marker here.
    private void OnEntitySpawned(BradleyAPC bradley)
    {
        if (bradley != null)
            AddMarker(bradley, _config.Markers.Bradley);
    }

    private void OnSupplyDropDropped(SupplyDrop drop, CargoPlane plane)
    {
        if (drop == null || plane == null || plane.net == null || !_managedPlanes.Contains(plane.net.ID.Value))
            return;
        AddMarker(drop, _config.Markers.Airdrop);
    }

    private void OnEntityKill(CargoPlane plane)
    {
        if (plane?.net != null)
            _managedPlanes.Remove(plane.net.ID.Value);
    }

    private void AddMarker(BaseEntity target, MarkerSettings settings)
    {
        if (settings == null || !settings.Enabled || target == null || target.IsDestroyed)
            return;
        foreach (TrackedMarker existing in _markers)
            if (ReferenceEquals(existing.Target, target))
                return;

        Vector3 position = target.transform.position;
        TrackedMarker marker = new() { Target = target, LastPosition = position };

        if (settings.CircleSize > 0f &&
            GameManager.server.CreateEntity(RadiusMarkerPrefab, position) is MapMarkerGenericRadius circle)
        {
            circle.enableSaving = false;
            circle.alpha = Mathf.Clamp01(settings.CircleAlpha);
            circle.radius = Mathf.Clamp(settings.CircleSize, 0.05f, 2.5f);
            circle.color1 = ParseColour(settings.CircleColour);
            circle.color2 = Color.black;
            circle.Spawn();
            circle.SendUpdate();
            marker.Circle = circle;
        }

        if (GameManager.server.CreateEntity(LabelMarkerPrefab, position) is VendingMachineMapMarker label)
        {
            label.enableSaving = false;
            label.markerShopName = string.IsNullOrWhiteSpace(settings.Label) ? target.ShortPrefabName : settings.Label;
            label.Spawn();
            marker.Label = label;
        }

        _markers.Add(marker);
    }

    // Called from the scheduler tick. Markers follow their target and are removed with it.
    private void UpdateMarkers()
    {
        if (_markers.Count == 0 || ++_markerTick % 3 != 0)
            return;

        for (int i = _markers.Count - 1; i >= 0; i--)
        {
            TrackedMarker marker = _markers[i];
            if (marker.Target == null || marker.Target.IsDestroyed)
            {
                KillMarker(marker);
                _markers.RemoveAt(i);
                continue;
            }

            Vector3 position = marker.Target.transform.position;
            if ((position - marker.LastPosition).sqrMagnitude < 4f)
                continue;
            marker.LastPosition = position;

            if (marker.Label != null && !marker.Label.IsDestroyed)
            {
                marker.Label.transform.position = position;
                marker.Label.SendNetworkUpdate();
            }
            if (marker.Circle != null && !marker.Circle.IsDestroyed)
            {
                marker.Circle.transform.position = position;
                marker.Circle.SendNetworkUpdate();
                marker.Circle.SendUpdate();
            }
        }
    }

    private static void KillMarker(TrackedMarker marker)
    {
        if (marker.Label != null && !marker.Label.IsDestroyed) marker.Label.Kill();
        if (marker.Circle != null && !marker.Circle.IsDestroyed) marker.Circle.Kill();
    }

    private void RemoveAllMarkers()
    {
        foreach (TrackedMarker marker in _markers)
            KillMarker(marker);
        _markers.Clear();
    }

    private static Color ParseColour(string value)
    {
        string[] parts = (value ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 &&
            float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float r) &&
            float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float g) &&
            float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b))
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
        return Color.red;
    }

    #endregion

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

    private void MigrateConfiguration(VersionNumber loadedVersion)
    {
        // The GameTip repair below belongs to the 1.3.0 upgrade. Running it on every later version
        // would put back default messages an administrator has since removed.
        if (loadedVersion >= new VersionNumber(1, 3, 0))
            return;

        // v1.3.0 repairs the old JSON.NET list-population duplication and expands the built-in
        // announcement pools. Existing administrator messages are preserved; new defaults are
        // appended once during migration rather than being forced back on every plugin reload.
        _config.GameTips ??= new GameTipConfiguration();
        MergeGameTipDefaults(_config.GameTips.CargoPlane, GameTipConfiguration.CreateCargoPlaneDefaults());
        MergeGameTipDefaults(_config.GameTips.PatrolHelicopter, GameTipConfiguration.CreatePatrolHelicopterDefaults());
        MergeGameTipDefaults(_config.GameTips.Chinook, GameTipConfiguration.CreateChinookDefaults());
        MergeGameTipDefaults(_config.GameTips.CargoShip, GameTipConfiguration.CreateCargoShipDefaults());

        LogInformation("Configuration",
            "Configuration migrated from v" + loadedVersion + " to v" + CurrentVersion +
            "; administrator values were preserved, duplicate GameTips removed, and new defaults added.");
    }

    private static void MergeGameTipDefaults(GameTipSettings? current, GameTipSettings defaults)
    {
        if (current == null)
            return;

        current.Messages ??= new List<string>();
        NormalizeGameTips(current);

        HashSet<string> seen = new(current.Messages, StringComparer.Ordinal);
        foreach (string message in defaults.Messages)
        {
            if (!string.IsNullOrWhiteSpace(message) && seen.Add(message))
                current.Messages.Add(message);
        }
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
        _config.Placement ??= new PlacementSettings();
        _config.Placement.MaximumSlope = Mathf.Clamp(_config.Placement.MaximumSlope, 0f, 60f);
        _config.Placement.AvoidBases = Mathf.Clamp(_config.Placement.AvoidBases, 0f, 300f);
        _config.Placement.EdgeBuffer = Mathf.Clamp(_config.Placement.EdgeBuffer, 50f, 1500f);
        _config.Markers ??= new MarkerConfiguration();
        _config.Markers.Bradley ??= new MarkerSettings { Label = "Bradley APC" };
        _config.Markers.Airdrop ??= new MarkerSettings { Label = "Airdrop", CircleColour = "0.2 0.7 1" };
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
        _config.GameTips.CargoPlane ??= GameTipConfiguration.CreateCargoPlaneDefaults();
        _config.GameTips.PatrolHelicopter ??= GameTipConfiguration.CreatePatrolHelicopterDefaults();
        _config.GameTips.Chinook ??= GameTipConfiguration.CreateChinookDefaults();
        _config.GameTips.CargoShip ??= GameTipConfiguration.CreateCargoShipDefaults();

        NormalizeGameTips(_config.GameTips.CargoPlane);
        NormalizeGameTips(_config.GameTips.PatrolHelicopter);
        NormalizeGameTips(_config.GameTips.Chinook);
        NormalizeGameTips(_config.GameTips.CargoShip);

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

    private static void NormalizeGameTips(GameTipSettings settings)
    {
        settings.Messages ??= new List<string>();

        // Repair configs produced by older versions and keep administrator-added messages.
        // Ordinal comparison is intentional: differently-capitalized/custom messages remain distinct.
        HashSet<string> seen = new(StringComparer.Ordinal);
        int write = 0;
        for (int read = 0; read < settings.Messages.Count; read++)
        {
            string message = settings.Messages[read]?.Trim() ?? string.Empty;
            if (message.Length == 0 || !seen.Add(message))
                continue;

            settings.Messages[write++] = message;
        }

        if (write < settings.Messages.Count)
            settings.Messages.RemoveRange(write, settings.Messages.Count - write);
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
            $"Crates={WorldEvents.CountActive(RogueWorldEventType.HackableCrate)} | " +
            $"Vanilla disabled: {DescribeDisabledVanilla()} ({_blockedVanillaEvents} blocked since load)");
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

        [JsonProperty("Placement Rules", Order = 62)]
        public PlacementSettings Placement = new();

        [JsonProperty("Map Markers", Order = 64)]
        public MarkerConfiguration Markers = new();

        [JsonProperty("GameTip Announcements", Order = 70)]
        public GameTipConfiguration GameTips = new();

        [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
        public VersionNumber Version = CurrentVersion;
    }

    public sealed class PlacementSettings
    {
        [JsonProperty("Direct Managed Airdrops To Dry Land")]
        public bool DirectAirdropsToLand = true;

        [JsonProperty("Steepest Ground Allowed For Airdrops And CH47 Crates (Degrees, 0 = Any)")]
        public float MaximumSlope = 30f;

        [JsonProperty("Keep Airdrops And CH47 Crates This Far From Player Buildings (Metres, 0 = Off)")]
        public float AvoidBases = 40f;

        [JsonProperty("Keep Airdrops This Far From The Map Edge (Metres)")]
        public float EdgeBuffer = 250f;
    }

    public sealed class MarkerConfiguration
    {
        [JsonProperty("Bradley APC")]
        public MarkerSettings Bradley = new() { Label = "Bradley APC", CircleColour = "1 0.2 0.1" };

        [JsonProperty("Managed Airdrop")]
        public MarkerSettings Airdrop = new() { Label = "Airdrop", CircleColour = "0.2 0.7 1" };
    }

    public sealed class MarkerSettings
    {
        [JsonProperty("Enabled")]
        public bool Enabled = true;

        [JsonProperty("Label")]
        public string Label = string.Empty;

        [JsonProperty("Circle Size (0 = No Circle, Up To 2.5)")]
        public float CircleSize = 0.3f;

        [JsonProperty("Circle Opacity (0 - 1)")]
        public float CircleAlpha = 0.4f;

        [JsonProperty("Circle Colour (Red Green Blue, Each 0-1)")]
        public string CircleColour = "1 0.2 0.1";
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
        public GameTipSettings CargoPlane = CreateCargoPlaneDefaults();

        [JsonProperty("Patrol Helicopter")]
        public GameTipSettings PatrolHelicopter = CreatePatrolHelicopterDefaults();

        [JsonProperty("CH47 Chinook")]
        public GameTipSettings Chinook = CreateChinookDefaults();

        [JsonProperty("Cargo Ship")]
        public GameTipSettings CargoShip = CreateCargoShipDefaults();

        public static GameTipSettings CreateCargoPlaneDefaults() => new()
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
                "Airdrop inbound — because your inventory wasn't stressful enough already.",
                "Cargo plane inbound — every roof camper just looked up at the same time.",
                "Air delivery detected. Customer signature may require an AK.",
                "The island has received express shipping. Delivery insurance was declined.",
                "Incoming care package — care not included.",
                "Cargo plane overhead. Your carefully planned farming route has been cancelled.",
                "Supplies inbound — cue the synchronized sprint toward gunfire.",
                "The sky is dropping loot again. Surely everyone will share.",
                "Cargo plane approaching — inventory space is now a competitive advantage.",
                "Air mail has arrived, addressed to whoever survives.",
                "Incoming airdrop — bushes across the island are accepting new tenants.",
                "Cargo plane spotted. Rock owners are suddenly feeling ambitious.",
                "The server's largest dinner bell is flying overhead.",
                "Supplies inbound. Remember: smoke means loot, loot means company.",
                "Cargo plane incoming — diplomacy expires when the crate lands.",
                "Airborne loot detected. Common sense has left the server.",
                "Cargo plane overhead — someone is already camping the landing zone.",
                "The sky vending machine is dispensing one very contested purchase.",
                "Airdrop inbound — your neighbour has definitely heard it too.",
                "Cargo plane approaching. Prepare your best 'I was here first' argument.",
                "Incoming supplies — brought to you by reckless optimism.",
                "Air delivery on the way. Refunds are unavailable after death.",
                "Cargo plane spotted — the island's cardio session begins now.",
                "A crate is taking the scenic route from 10,000 feet.",
                "Incoming airdrop. The grass is about to become suspiciously full of players.",
                "Cargo plane overhead — everybody suddenly remembers where their binoculars are.",
                "Supplies inbound. Please queue politely behind the man with the M249.",
                "Air freight incoming — contents may include loot, bullets, and regret.",
                "Cargo plane detected. The nearest horse is about to have a very bad afternoon.",
                "The sky has loot. The ground has campers. Choose wisely.",
                "Airdrop inbound — because peaceful resource gathering was getting boring.",
                "Cargo plane approaching. Expect spontaneous alliances lasting up to seven seconds.",
                "Incoming supplies — parachute included, survival not guaranteed.",
                "Air drop detected. Somewhere a clan leader just yelled 'everyone online!'",
                "Cargo plane overhead — your base door can wait.",
                "The island's most dangerous delivery service is back.",
                "Supplies incoming. Please keep your weapon raised until the transaction is complete.",
                "Airdrop inbound — one crate, forty opinions about who owns it.",
                "Cargo plane spotted. The beach nakeds have discovered purpose.",
                "Air delivery incoming — loot first, consequences immediately afterward.",
                "Cargo plane inbound. That distant gunfire is about to become local.",
                "The sky is making poor financial decisions again.",
                "Incoming airdrop — sponsored by bad positioning.",
                "Cargo plane approaching. Your empty inventory is now a tactical plan.",
                "Supplies inbound — may your parachute land closer than your enemies.",
                "Air mail incoming. Recipient: whoever has the better recoil control.",
                "Cargo plane overhead — the island is about to demonstrate supply and demand.",
                "Airdrop inbound. Silence will resume sometime after the last magazine is empty.",
                "Incoming cargo plane — free crate, premium chaos."
            }
        };

        public static GameTipSettings CreatePatrolHelicopterDefaults() => new()
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
                "Incoming patrol helicopter — now is an excellent time to remember where you left the meds.",
                "Patrol heli inbound — armed roof enthusiasts, this message is specifically for you.",
                "Rotor noise detected. The island's airborne tax collector has arrived.",
                "Patrol helicopter approaching — please declare all visible weapons.",
                "The angry ceiling fan is back and it brought rockets.",
                "Heli inbound. Your roof has been selected for a complimentary durability test.",
                "Patrol helicopter spotted — hazmat fashion is suddenly very practical.",
                "Incoming heli. Put the gun away or prepare to negotiate loudly.",
                "The sky police are here. They have rejected your appeal.",
                "Patrol helicopter inbound — windows are now considered optional building parts.",
                "Rotor blades overhead. Somebody is about to donate a kit to the ground.",
                "Heli spotted — the safest weapon is currently the one in your inventory.",
                "Patrol helicopter approaching. Roof access temporarily discouraged.",
                "The island's loudest security guard has started its shift.",
                "Heli inbound — if you can hear the rockets, the inspection has begun.",
                "Patrol helicopter detected. That minicopter trip can probably wait.",
                "Incoming heli — armed players have been upgraded to priority customers.",
                "The flying raid alarm has arrived.",
                "Patrol helicopter overhead. Your compound walls suddenly feel decorative.",
                "Heli inbound — scientists outsourced customer service to missiles.",
                "Rotor blades approaching. Please remain calm and absolutely not on the roof.",
                "Patrol helicopter spotted — every SAM site just became emotionally invested.",
                "Incoming heli. Somewhere, a player is loading HV rockets with confidence.",
                "The sky has chosen violence again.",
                "Patrol helicopter inbound — your shooting floor is now a suggestion.",
                "Heli detected. The island-wide game of 'hide the AK' begins now.",
                "Patrol helicopter approaching — free napalm available while supplies last.",
                "Rotor noise overhead. Insurance claims open after the fire goes out.",
                "Heli inbound — it saw your weapon and took that personally.",
                "Patrol helicopter spotted. Time to discover whether that bunker roof was worth it.",
                "Incoming heli — the airspace has become aggressively unfriendly.",
                "The angry flying blender requests that you stop moving.",
                "Patrol helicopter overhead — tactical crouching intensifies.",
                "Heli inbound. Your neighbour is definitely hoping it chooses you.",
                "Rotor blades detected — rockets may arrive before this message disappears.",
                "Patrol helicopter approaching. The roof-camping department is closing early.",
                "Incoming heli — please store all confidence in a secure container.",
                "The sky boss has spawned. Optional objective: survive.",
                "Patrol helicopter spotted — somebody's compound is about to become a landmark.",
                "Heli inbound. That metal facemask suddenly looks like a beacon.",
                "Rotor blades overhead — the island has entered temporary no-fun airspace.",
                "Patrol helicopter approaching. Your base now has an aviation problem.",
                "Incoming heli — if you're naked, congratulations on your strategic genius.",
                "The patrol helicopter is back to remind everyone that roofs have consequences.",
                "Heli spotted — explosive renovations may begin without notice.",
                "Patrol helicopter inbound. Please extinguish all hopes of a quiet farm run.",
                "Rotor noise detected. The local wildlife has filed a noise complaint.",
                "Incoming heli — one flying machine, several hundred questionable decisions.",
                "Patrol helicopter overhead. Good luck to everyone who forgot to close the garage doors."
            }
        };

        public static GameTipSettings CreateChinookDefaults() => new()
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
                "The flying loot bus has arrived. Please form an orderly firefight.",
                "CH47 inbound — the scientists have scheduled another mandatory PvP meeting.",
                "Heavy rotors detected. Someone is about to call a locked crate 'theirs'.",
                "Chinook approaching — monument parking is expected to be terrible.",
                "CH47 overhead. The crate has not landed and arguments have already begun.",
                "The flying loot bus is back, and nobody bought a return ticket.",
                "Chinook inbound — please keep all betrayal inside the monument.",
                "Heavy rotors approaching. Bring cards, fuses, ammo, and trust issues.",
                "CH47 spotted — one crate will soon employ half the server.",
                "Incoming Chinook. Scientists are delivering premium-grade bad decisions.",
                "The big helicopter has arrived with today's scheduled chaos.",
                "CH47 inbound — somewhere, a DB grub is already choosing a bush.",
                "Heavy rotors overhead. Monument occupancy is about to exceed recommended limits.",
                "Chinook spotted — the crate is locked, but the violence is open immediately.",
                "Incoming CH47. Your peaceful monument run has been rescheduled.",
                "The flying loot bus is making a stop. Exact departure time: after enough gunfire.",
                "CH47 approaching — everyone suddenly remembers they need that monument.",
                "Heavy rotors detected. The scientists have brought a group project nobody asked for.",
                "Chinook inbound — hacking timer sold separately.",
                "The big helicopter is here. Please maintain six bullets of social distance.",
                "CH47 spotted — valuable cargo and absolutely zero queue discipline.",
                "Incoming Chinook. Expect scientists, campers, counters, and one very brave naked.",
                "Heavy rotors overhead — the island's attention span has moved to one location.",
                "CH47 inbound. The crate will be locked; the area will not be.",
                "The flying loot bus approaches — next stop: poor judgement.",
                "Chinook spotted. Your neighbours have suddenly stopped farming.",
                "Incoming CH47 — a monument is about to become a networking event with guns.",
                "Heavy rotors detected. Please prepare your favourite excuse for losing a kit.",
                "CH47 overhead — the scientists are hosting an open house.",
                "Chinook inbound. The crate comes with a complimentary crowd.",
                "The big helicopter is approaching — all roads now lead to trouble.",
                "Incoming CH47. Someone will definitely say 'it's clear' when it isn't.",
                "Heavy rotors overhead — locked loot, unlocked aggression.",
                "Chinook spotted. The island has selected today's busiest grid.",
                "CH47 inbound — bring enough ammo to make new friends from a distance.",
                "The flying loot bus has arrived with a crate full of server-wide motivation.",
                "Incoming Chinook. Camping positions are available on a first-come basis.",
                "Heavy rotors approaching — the monument's property value just collapsed.",
                "CH47 spotted. Scientists recommend against touching their delivery.",
                "Chinook inbound — one crate, several clans, flawless conflict resolution.",
                "The big helicopter is here. Your recycler trip can wait.",
                "Incoming CH47 — please do not feed the scientists.",
                "Heavy rotors overhead. The loot is locked because apparently this wasn't difficult enough.",
                "Chinook approaching. Expect a sudden increase in crouching noises.",
                "CH47 inbound — the monument has enabled hard mode.",
                "The flying loot bus is back. Tickets cost one kit each.",
                "Incoming Chinook. The crate will land wherever your enemies are closest.",
                "Heavy rotors detected — today's community event is competitive waiting.",
                "CH47 overhead. Scientists have delivered a box and approximately thirty problems."
            }
        };

        public static GameTipSettings CreateCargoShipDefaults() => new()
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
                "Cargo Ship spotted! Your peaceful fishing trip has been cancelled.",
                "Cargo Ship inbound — every RHIB on the island just gained a purpose.",
                "The floating loot fortress has entered local waters. Hospitality remains terrible.",
                "Cargo Ship spotted — scientists are offering ocean views and automatic gunfire.",
                "Movement offshore. The server's least relaxing cruise has begun.",
                "Cargo Ship inbound — life jackets optional, ammunition recommended.",
                "The ocean has produced a monument that refuses to stay still.",
                "Cargo Ship spotted. Somewhere, a tugboat captain just became ambitious.",
                "All aboard — today's cruise package includes locked crates and betrayal.",
                "Cargo Ship approaching. Please arrive thirty minutes early for boarding and counter-raids.",
                "The floating PvP convention is accepting walk-ins, swimmers, and poor planners.",
                "Cargo Ship inbound — sunscreen will not protect you from scientists.",
                "Movement offshore. Fishing rods may now be exchanged for assault rifles.",
                "Cargo Ship spotted — the upper deck has a strict no-peace policy.",
                "The ocean's loudest loot box is back.",
                "Cargo Ship inbound. Check fuel, check ammo, forget to check escape plan.",
                "Floating loot detected — sharks are somehow not the main concern.",
                "Cargo Ship approaching. The coast is about to experience rush hour.",
                "All aboard the S.S. Questionable Decision.",
                "Cargo Ship spotted — bring meds for the bullets and snacks for the respawn timer.",
                "Movement offshore. The scientists have launched another hostile cruise.",
                "Cargo Ship inbound — the boarding queue is armed and extremely impatient.",
                "The floating fortress is back. Your boat insurance does not cover this.",
                "Cargo Ship spotted. Expect maritime law to be replaced by roof-camping rules.",
                "Incoming Cargo Ship — low grade fuel prices are about to spike.",
                "The ocean has delivered three decks of reasons to lose your kit.",
                "Cargo Ship approaching. Swimming is free; regretting it is also free.",
                "All aboard — cabins unavailable, counters guaranteed.",
                "Cargo Ship spotted — somebody is already claiming they control it.",
                "Movement offshore. The island's navy consists entirely of players with SARs.",
                "Cargo Ship inbound — bring a boat you are emotionally prepared to lose.",
                "The floating loot fortress has arrived. Quiet hours are cancelled.",
                "Cargo Ship spotted. The nearest beach is about to become a parking lot.",
                "Incoming ship — scientists request that all visitors remain directly in their sights.",
                "The ocean event is live. Expect boats, bullets, and one person swimming from grid Z1.",
                "Cargo Ship approaching — your peaceful base organization can wait.",
                "All aboard the floating argument over who tagged the scientist first.",
                "Cargo Ship spotted — maritime PvP has entered peak season.",
                "Movement offshore. Someone forgot fuel and will discover this halfway there.",
                "Cargo Ship inbound — the captain has turned off the seatbelt sign and common sense.",
                "The floating fortress returns. Boarding passes are printed on respawn screens.",
                "Cargo Ship spotted. Oil rigs are enjoying a rare moment of peace.",
                "Incoming ship — your minicopter pilot suddenly thinks landing on it is easy.",
                "The ocean has spawned a loot piñata guarded by people with rifles.",
                "Cargo Ship approaching. Prepare for nautical crouch-walking.",
                "All aboard — departure is immediate, survival is flexible.",
                "Cargo Ship spotted. The coastline has begun making engine noises.",
                "Movement offshore — one ship, many counters, no refunds.",
                "Cargo Ship inbound. Welcome to the only cruise where everyone brings their own gun."
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
