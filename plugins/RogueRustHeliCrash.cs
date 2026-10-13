using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;
using UnityEngine.AI;
namespace Oxide.Plugins
{
    [Info("RogueRustHeliCrash", "RogueAssassin", "1.0.3")]
    [Description("A malfunctioning patrol helicopter flies in and crashes. The crash site is marked on the map, guarded, and reported until it is looted or expires.")]
    public sealed class RogueRustHeliCrash : RogueRustPlugin
    {
        [RoguePermission]
        private const string AdminPermission = "roguerusthelicrash.admin";
        private const string PluginVersion = "1.0.3";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 0, 3);

        private const string HelicopterPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";
        private const string HackableCratePrefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
        private const string LabelMarkerPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
        private const string RadiusMarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string ExplosionMarkerPrefab = "assets/prefabs/tools/map/explosionmarker.prefab";
        private const string CrateMarkerPrefab = "assets/prefabs/tools/map/cratemarker.prefab";
        private const string AlertUi = "RogueRustHeliCrash.Alert";
        private const string IconResource = "Oxide.Ext.RogueRust.Icons.RogueRustHeliCrash.png";
        private const string StateDataKey = "RogueRustHeliCrash/state";
        private const string EventHooks = "event";

        // Seconds. The helicopter is re-aimed at the site this often, because its own AI picks a new
        // patrol point whenever it thinks it has arrived somewhere.
        private const float SteerInterval = 3f;
        private const float CrashSpinTimeout = 45f;
        private const float CrateClaimRadius = 150f;
        private const float CrateClaimGrace = 10f;
        private const float ShoreBuffer = 70f;

        private enum Phase { Idle, Flying, Crashing, Site }

        private Configuration _config;
        private StateData _state = new StateData();
        private Phase _phase = Phase.Idle;
        private PatrolHelicopter _helicopter;
        private Vector3 _target;
        private Vector3 _lastHelicopterPosition;
        private Vector3 _site;
        private string _siteGrid = string.Empty;
        private float _phaseStarted;
        private float _nextSteer;
        private bool _firstLootReported;
        private bool _hadCrates;
        private IRogueScheduledJob _tipJob;
        private IRogueScheduledJob _alertJob;
        private string _icon;
        private readonly List<BaseEntity> _crates = new List<BaseEntity>();
        private readonly List<BaseEntity> _guards = new List<BaseEntity>();
        private readonly List<BaseEntity> _markers = new List<BaseEntity>();
        private readonly List<Vector3> _monuments = new List<Vector3>();

        #region Configuration

        private sealed class Configuration
        {
            [JsonProperty("Schedule", Order = 10)] public ScheduleSettings Schedule = new ScheduleSettings();
            [JsonProperty("Flight", Order = 20)] public FlightSettings Flight = new FlightSettings();
            [JsonProperty("Crash Site", Order = 30)] public SiteSettings Site = new SiteSettings();
            [JsonProperty("Loot", Order = 40)] public LootSettings Loot = new LootSettings();
            [JsonProperty("Guards", Order = 50)] public GuardSettings Guards = new GuardSettings();
            [JsonProperty("Map Marker", Order = 60)] public MarkerSettings Marker = new MarkerSettings();
            [JsonProperty("Reporting", Order = 70)] public ReportSettings Reporting = new ReportSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class ScheduleSettings
        {
            [JsonProperty("Start Events Automatically")] public bool Automatic = true;
            [JsonProperty("Minimum Minutes Between Events")] public float MinimumMinutes = 60f;
            [JsonProperty("Maximum Minutes Between Events")] public float MaximumMinutes = 180f;
            [JsonProperty("Minimum Players Online For An Automatic Event")] public int MinimumPlayers = 4;
        }

        private sealed class FlightSettings
        {
            [JsonProperty("Distance From The Crash Site The Helicopter Appears At (Metres)")] public float ApproachDistance = 1000f;
            [JsonProperty("Crash When Within This Distance Of The Site (Metres)")] public float CrashRadius = 40f;
            [JsonProperty("Crash Wherever It Is After This Many Seconds In Flight")] public float TimeoutSeconds = 300f;
            [JsonProperty("Helicopter Cannot Be Damaged Before It Crashes")] public bool Invulnerable = true;
            [JsonProperty("Helicopter Does Not Attack Players")] public bool Passive = true;
        }

        private sealed class SiteSettings
        {
            [JsonProperty("Keep At Least This Far From Player Buildings (Metres, 0 = Off)")] public float AvoidBases = 60f;
            [JsonProperty("Keep At Least This Far From Monument Centres (Metres, 0 = Off)")] public float AvoidMonuments = 150f;
            [JsonProperty("Keep At Least This Far From The Map Edge (Metres)")] public float EdgeBuffer = 250f;
            [JsonProperty("Steepest Ground Allowed (Degrees)")] public float MaximumSlope = 25f;
            [JsonProperty("Only Crash At The Custom Locations Below")] public bool UseCustomLocations = false;
            [JsonProperty("Random Offset Applied To A Custom Location (Metres)")] public float CustomLocationJitter = 25f;
            [JsonProperty("Custom Locations")] public List<Location> CustomLocations = new List<Location>();
            [JsonProperty("Site Stays Active For (Minutes)")] public float DurationMinutes = 20f;
        }

        private sealed class Location
        {
            [JsonProperty("X")] public float X;
            [JsonProperty("Z")] public float Z;
        }

        private sealed class LootSettings
        {
            [JsonProperty("Helicopter Crates Dropped (0 = Game Default)")] public int Crates = 0;
            [JsonProperty("Seconds Until The Crates Stop Burning And Unlock (0 = Game Default)")] public float UnlockSeconds = 0f;
            [JsonProperty("Also Drop A Hackable Locked Crate")] public bool HackableCrate = false;
            [JsonProperty("Remove Unlooted Crates When The Site Expires")] public bool RemoveCratesOnExpiry = false;
        }

        private sealed class GuardSettings
        {
            [JsonProperty("Enabled")] public bool Enabled = true;
            [JsonProperty("Number Of Guards")] public int Count = 4;
            [JsonProperty("Guard Prefab")] public string Prefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab";
            [JsonProperty("Guard Health (0 = Prefab Default)")] public float Health = 0f;
            [JsonProperty("Spawn Within This Distance Of The Wreck (Metres)")] public float Radius = 18f;
            [JsonProperty("Remove Surviving Guards When The Site Ends")] public bool RemoveOnEnd = true;
        }

        private sealed class MarkerSettings
        {
            [JsonProperty("Show A Labelled Marker On The Map")] public bool Label = true;
            [JsonProperty("Marker Label")] public string LabelText = "Helicopter Crash";
            [JsonProperty("Native Map Icon At The Wreck (Explosion, Crate or None)")] public string Icon = "Explosion";
            [JsonProperty("Show A Coloured Circle On The Map")] public bool Circle = true;
            [JsonProperty("Circle Size (0.05 - 2.5)")] public float CircleSize = 0.4f;
            [JsonProperty("Circle Opacity (0 - 1)")] public float CircleAlpha = 0.45f;
            [JsonProperty("Circle Colour (Red Green Blue, Each 0-1)")] public string CircleColour = "1 0.3 0.1";
        }

        private sealed class ReportSettings
        {
            [JsonProperty("Announce In Chat")] public bool Chat = true;
            [JsonProperty("Chat Prefix")] public string Prefix = "<color=#E0662B>[Heli Crash]</color> ";
            [JsonProperty("Show A Game Tip When The Helicopter Appears And When It Crashes")] public bool GameTip = true;
            [JsonProperty("Game Tip Seconds")] public float GameTipSeconds = 8f;
            [JsonProperty("Show An On-Screen Alert With The Crash Icon When It Comes Down")] public bool Alert = true;
            [JsonProperty("On-Screen Alert Seconds")] public float AlertSeconds = 10f;
            [JsonProperty("Announce The First Player To Open A Crate")] public bool FirstLooter = true;
            [JsonProperty("Hide The Death Notes Message For The Crash")] public bool HideDeathNote = true;
            [JsonProperty("Discord Webhook URL (Empty = Off)")] public string DiscordWebhook = string.Empty;
        }

        private sealed class StateData
        {
            public double NextEventUnix;
            public int EventsRun;
            public double LastCrashUnix;
            public string LastCrashGrid = string.Empty;
            public string LastOutcome = string.Empty;
            public string LastLooter = string.Empty;
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
            if (_config.Schedule == null) _config.Schedule = new ScheduleSettings();
            if (_config.Flight == null) _config.Flight = new FlightSettings();
            if (_config.Site == null) _config.Site = new SiteSettings();
            if (_config.Loot == null) _config.Loot = new LootSettings();
            if (_config.Guards == null) _config.Guards = new GuardSettings();
            if (_config.Marker == null) _config.Marker = new MarkerSettings();
            if (_config.Reporting == null) _config.Reporting = new ReportSettings();
            if (_config.Site.CustomLocations == null) _config.Site.CustomLocations = new List<Location>();

            ScheduleSettings schedule = _config.Schedule;
            schedule.MinimumMinutes = Mathf.Clamp(schedule.MinimumMinutes, 1f, 10080f);
            schedule.MaximumMinutes = Mathf.Clamp(schedule.MaximumMinutes, schedule.MinimumMinutes, 10080f);
            schedule.MinimumPlayers = Math.Max(0, schedule.MinimumPlayers);

            FlightSettings flight = _config.Flight;
            flight.ApproachDistance = Mathf.Clamp(flight.ApproachDistance, 150f, 4000f);
            flight.CrashRadius = Mathf.Clamp(flight.CrashRadius, 10f, 200f);
            flight.TimeoutSeconds = Mathf.Clamp(flight.TimeoutSeconds, 30f, 1800f);

            SiteSettings site = _config.Site;
            site.AvoidBases = Mathf.Clamp(site.AvoidBases, 0f, 500f);
            site.AvoidMonuments = Mathf.Clamp(site.AvoidMonuments, 0f, 1000f);
            site.EdgeBuffer = Mathf.Clamp(site.EdgeBuffer, 50f, 1500f);
            site.MaximumSlope = Mathf.Clamp(site.MaximumSlope, 5f, 60f);
            site.CustomLocationJitter = Mathf.Clamp(site.CustomLocationJitter, 0f, 300f);
            site.DurationMinutes = Mathf.Clamp(site.DurationMinutes, 1f, 240f);

            _config.Loot.Crates = Mathf.Clamp(_config.Loot.Crates, 0, 12);
            _config.Loot.UnlockSeconds = Mathf.Clamp(_config.Loot.UnlockSeconds, 0f, 1800f);
            _config.Guards.Count = Mathf.Clamp(_config.Guards.Count, 0, 20);
            _config.Guards.Radius = Mathf.Clamp(_config.Guards.Radius, 6f, 60f);
            _config.Guards.Health = Mathf.Clamp(_config.Guards.Health, 0f, 5000f);
            _config.Marker.CircleSize = Mathf.Clamp(_config.Marker.CircleSize, 0.05f, 2.5f);
            _config.Marker.CircleAlpha = Mathf.Clamp01(_config.Marker.CircleAlpha);
            _config.Reporting.GameTipSeconds = Mathf.Clamp(_config.Reporting.GameTipSeconds, 2f, 30f);
            _config.Reporting.AlertSeconds = Mathf.Clamp(_config.Reporting.AlertSeconds, 3f, 60f);
            if (_config.Marker.Icon == null) _config.Marker.Icon = "Explosion";
            if (_config.Reporting.Prefix == null) _config.Reporting.Prefix = string.Empty;
            if (_config.Reporting.DiscordWebhook == null) _config.Reporting.DiscordWebhook = string.Empty;
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
            _state = LoadData(StateDataKey, () => new StateData()) ?? new StateData();
            LogInformation("Lifecycle", Name + " " + PluginVersion + " initialized.");
        }

        private void OnServerInitialized()
        {
            foreach (RogueMonumentInfo monument in World.GetMonuments())
                _monuments.Add(monument.Position);

            // The alert icon ships inside the RogueRust DLL (4.4.62+). Without it the alert is text only.
            string png;
            if (ImageLibrary.TryGetEmbeddedImage(Name, "alert-icon", IconResource, out png) && !string.IsNullOrWhiteSpace(png))
                _icon = png;

            // A time saved before a restart may already have passed; never start an event the moment the server boots.
            if (_state.NextEventUnix <= UnixTime) ScheduleNext();
            Repeat(TimeSpan.FromSeconds(1), Tick, "helicrash-tick");

            if (_config.Schedule.Automatic)
                LogInformation("Schedule", "Next helicopter crash in " + FormatDuration(_state.NextEventUnix - UnixTime) + ".");
        }

        private void Unload()
        {
            StopEvent("unloaded", announce: false);
            SaveData(StateDataKey, _state);
        }

        private void ScheduleNext()
        {
            float minutes = UnityEngine.Random.Range(_config.Schedule.MinimumMinutes, _config.Schedule.MaximumMinutes);
            _state.NextEventUnix = UnixTime + minutes * 60d;
            SaveDataDebounced(StateDataKey, _state);
        }

        private void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseStarted = Time.realtimeSinceStartup;
        }

        private float PhaseSeconds => Time.realtimeSinceStartup - _phaseStarted;

        // One timer drives the whole event. While nothing is happening it costs a single comparison a second.
        private void Tick()
        {
            switch (_phase)
            {
                case Phase.Idle:
                    if (!_config.Schedule.Automatic || UnixTime < _state.NextEventUnix) return;
                    if (BasePlayer.activePlayerList.Count < _config.Schedule.MinimumPlayers)
                    {
                        ScheduleNext();
                        LogInformation("Schedule", "Skipped a helicopter crash: fewer than " + _config.Schedule.MinimumPlayers +
                            " players online. Next attempt in " + FormatDuration(_state.NextEventUnix - UnixTime) + ".");
                        return;
                    }
                    string failure;
                    if (!StartEvent(null, out failure))
                    {
                        ScheduleNext();
                        LogWarning("Schedule", "Could not start a helicopter crash: " + failure);
                    }
                    return;

                case Phase.Flying:
                    TickFlight();
                    return;

                case Phase.Crashing:
                    if (!IsAlive(_helicopter)) BeginSite();
                    else if (PhaseSeconds > CrashSpinTimeout) _helicopter.Die();
                    else _lastHelicopterPosition = _helicopter.transform.position;
                    return;

                case Phase.Site:
                    TickSite();
                    return;
            }
        }

        #endregion

        #region Event

        private bool StartEvent(Vector3? forcedSite, out string failure)
        {
            failure = string.Empty;
            if (_phase != Phase.Idle)
            {
                failure = "an event is already running.";
                return false;
            }

            Vector3 site;
            if (forcedSite.HasValue) site = Grounded(forcedSite.Value);
            else if (!TryPickSite(out site))
            {
                failure = "no suitable crash site was found.";
                return false;
            }

            Vector2 heading = UnityEngine.Random.insideUnitCircle.normalized;
            if (heading == Vector2.zero) heading = Vector2.up;
            float limit = World.WorldSize * 0.5f + 300f;
            Vector3 start = site + new Vector3(heading.x, 0f, heading.y) * _config.Flight.ApproachDistance;
            start.x = Mathf.Clamp(start.x, -limit, limit);
            start.z = Mathf.Clamp(start.z, -limit, limit);
            start.y = Mathf.Max(GroundHeight(start), 0f) + 90f;

            Vector3 facing = site - start;
            facing.y = 0f;
            PatrolHelicopter helicopter = GameManager.server.CreateEntity(HelicopterPrefab, start,
                facing.sqrMagnitude > 1f ? Quaternion.LookRotation(facing) : Quaternion.identity) as PatrolHelicopter;
            if (helicopter == null)
            {
                failure = "the patrol helicopter prefab could not be created.";
                return false;
            }

            // The event does not survive a restart, so its entities are kept out of the save.
            helicopter.enableSaving = false;
            helicopter.Spawn();
            // The helicopter AI moves itself to a map edge of its own choosing as it starts up; put it back.
            helicopter.transform.SetPositionAndRotation(start, facing.sqrMagnitude > 1f ? Quaternion.LookRotation(facing) : Quaternion.identity);

            _helicopter = helicopter;
            _target = site;
            _lastHelicopterPosition = start;
            _nextSteer = 0f;
            _firstLootReported = false;
            _hadCrates = false;
            _crates.Clear();
            SetPhase(Phase.Flying);
            SetDynamicHooks(EventHooks, true);

            _state.EventsRun++;
            Report("Report.Incoming", true);
            LogInformation("Event", "Helicopter inbound to " + GridReference(site) + " " + FormatPosition(site) + ".");
            Interface.CallHook("OnHeliCrashStarted", helicopter, site);
            return true;
        }

        private void TickFlight()
        {
            if (!IsAlive(_helicopter))
            {
                // Shot down, or removed by something else, before it reached the site.
                BeginSite();
                return;
            }

            Vector3 position = _helicopter.transform.position;
            _lastHelicopterPosition = position;

            float dx = position.x - _target.x;
            float dz = position.z - _target.z;
            float radius = _config.Flight.CrashRadius;
            if (dx * dx + dz * dz <= radius * radius || PhaseSeconds >= _config.Flight.TimeoutSeconds)
            {
                Crash();
                return;
            }

            if (Time.realtimeSinceStartup < _nextSteer) return;
            _nextSteer = Time.realtimeSinceStartup + SteerInterval;

            PatrolHelicopterAI ai = _helicopter.myAI;
            if (ai == null)
            {
                Crash();
                return;
            }
            // Leave it alone while it is already heading there; restarting the move makes it slow down.
            Vector3 destination = _target + Vector3.up * 30f;
            if (ai._currentState == PatrolHelicopterAI.aiState.MOVE && (ai.destination - destination).sqrMagnitude < 4f) return;
            ai.ExitCurrentState();
            ai.State_Move_Enter(destination);
        }

        private void Crash()
        {
            SetPhase(Phase.Crashing);
            if (_config.Loot.Crates > 0) _helicopter.maxCratesToSpawn = _config.Loot.Crates;

            // CriticalDamage is the game's own "shot down" sequence: the helicopter spins, falls,
            // explodes and drops its burning crates.
            PatrolHelicopterAI ai = _helicopter.myAI;
            if (ai == null)
            {
                _helicopter.Die();
                return;
            }

            // A dying patrol helicopter normally flies to the nearest monument within 1,200 m before it
            // spins out. That took this one far from its crash site, so the rule is switched off for
            // the moment the death sequence starts, which is when the game reads it.
            bool monumentCrash = PatrolHelicopterAI.monument_crash;
            PatrolHelicopterAI.monument_crash = false;
            try { ai.CriticalDamage(); }
            finally { PatrolHelicopterAI.monument_crash = monumentCrash; }
        }

        private void BeginSite()
        {
            if (_phase == Phase.Site || _phase == Phase.Idle) return;
            _helicopter = null;
            _site = Grounded(_lastHelicopterPosition);
            _siteGrid = GridReference(_site);
            SetPhase(Phase.Site);

            _state.LastCrashUnix = UnixTime;
            _state.LastCrashGrid = _siteGrid;
            _state.LastOutcome = "active";
            _state.LastLooter = string.Empty;
            SaveDataDebounced(StateDataKey, _state);

            SpawnMarkers();
            ShowAlert();
            Report("Report.Crashed", true, _siteGrid);
            SendDiscord(string.Format(CultureInfo.InvariantCulture, Message("Report.Crashed"), _siteGrid));
            LogInformation("Event", "Helicopter crashed at " + _siteGrid + " " + FormatPosition(_site) + ".");

            // Let the wreck and its crates settle before anything is placed around them.
            Delay(TimeSpan.FromSeconds(4), PopulateSite, "helicrash-populate");
            if (_config.Loot.UnlockSeconds > 0f)
                Delay(TimeSpan.FromSeconds(_config.Loot.UnlockSeconds), UnlockCrates, "helicrash-unlock");

            Interface.CallHook("OnHeliCrashLanded", _site, _siteGrid);
        }

        private void PopulateSite()
        {
            if (_phase != Phase.Site) return;

            if (_config.Loot.HackableCrate)
            {
                Vector3 offset = UnityEngine.Random.insideUnitSphere * 4f;
                offset.y = 0f;
                BaseEntity crate = GameManager.server.CreateEntity(HackableCratePrefab, Grounded(_site + offset) + Vector3.up * 1.5f);
                if (crate != null)
                {
                    crate.enableSaving = false;
                    crate.Spawn();
                    _crates.Add(crate);
                }
            }

            if (_config.Guards.Enabled) SpawnGuards();
        }

        private void TickSite()
        {
            for (int i = _crates.Count - 1; i >= 0; i--)
                if (!IsAlive(_crates[i])) _crates.RemoveAt(i);

            if (_crates.Count == 0 && PhaseSeconds > CrateClaimGrace + 5f)
            {
                // A helicopter that was removed rather than destroyed leaves nothing behind.
                StopEvent(_hadCrates ? "looted" : "lost", announce: true);
                return;
            }

            if (PhaseSeconds >= _config.Site.DurationMinutes * 60f)
                StopEvent("expired", announce: true);
        }

        // Ends whatever stage the event is at and returns to idle.
        private void StopEvent(string outcome, bool announce)
        {
            if (_phase == Phase.Idle) return;
            Phase ended = _phase;
            _phase = Phase.Idle;
            SetDynamicHooks(EventHooks, false);

            if (IsAlive(_helicopter)) _helicopter.Kill();
            _helicopter = null;

            KillAll(_markers);
            HideAlert();
            if (_config.Guards.RemoveOnEnd || outcome == "unloaded") KillAll(_guards);
            else _guards.Clear();
            if (outcome == "unloaded" || outcome == "stopped" || (outcome == "expired" && _config.Loot.RemoveCratesOnExpiry)) KillAll(_crates);
            else _crates.Clear();

            _state.LastOutcome = outcome;
            ScheduleNext();

            if (announce && ended == Phase.Site)
            {
                string key = outcome == "looted" ? "Report.Looted" : outcome == "expired" ? "Report.Expired" : "Report.Stopped";
                Report(key, false, _siteGrid);
                SendDiscord(string.Format(CultureInfo.InvariantCulture, Message(key), _siteGrid));
            }
            else if (announce) Report("Report.Stopped", false, string.Empty);

            LogInformation("Event", "Helicopter crash ended (" + outcome + "). Next in " + FormatDuration(_state.NextEventUnix - UnixTime) + ".");
            Interface.CallHook("OnHeliCrashEnded", outcome);
        }

        private void UnlockCrates()
        {
            if (_phase != Phase.Site) return;
            foreach (BaseEntity entity in _crates)
            {
                LockedByEntCrate crate = entity as LockedByEntCrate;
                if (!IsAlive(crate) || crate.lockingEnt == null) continue;

                // The crate stays locked for as long as the fire burning on it exists.
                BaseEntity fire = crate.lockingEnt;
                crate.SetLockingEnt(null);
                if (IsAlive(fire)) fire.Kill();
            }
        }

        #endregion

        #region Hooks (subscribed only while an event is running)

        [RogueDynamicHook(EventHooks)]
        private void OnEntitySpawned(LockedByEntCrate crate)
        {
            // The helicopter drops its crates as it dies. Anything of that type appearing next to it
            // belongs to the crash; a Bradley or another helicopter elsewhere on the map does not.
            if (crate == null || _phase == Phase.Idle) return;
            if (_phase == Phase.Site && PhaseSeconds > CrateClaimGrace) return;

            Vector3 origin = IsAlive(_helicopter) ? _helicopter.transform.position : _lastHelicopterPosition;
            if ((crate.transform.position - origin).sqrMagnitude > CrateClaimRadius * CrateClaimRadius) return;

            crate.enableSaving = false;
            _crates.Add(crate);
            _hadCrates = true;
        }

        [RogueDynamicHook(EventHooks)]
        private void OnEntityKill(PatrolHelicopter helicopter)
        {
            if (helicopter == null || helicopter != _helicopter) return;
            _lastHelicopterPosition = helicopter.transform.position;
            // The entity is still being torn down; open the site once that has finished.
            NextTick(BeginSite);
        }

        [RogueDynamicHook(EventHooks)]
        private object OnPatrolHelicopterTakeDamage(PatrolHelicopter helicopter, HitInfo info)
        {
            if (helicopter == null || helicopter != _helicopter) return null;
            return _phase == Phase.Flying && _config.Flight.Invulnerable ? (object)true : null;
        }

        [RogueDynamicHook(EventHooks)]
        private object CanHelicopterTarget(PatrolHelicopterAI ai, BasePlayer player) => IsPassive(ai) ? (object)false : null;

        [RogueDynamicHook(EventHooks)]
        private object CanHelicopterStrafe(PatrolHelicopterAI ai) => IsPassive(ai) ? (object)false : null;

        [RogueDynamicHook(EventHooks)]
        private object CanHelicopterStrafeTarget(PatrolHelicopterAI ai, BasePlayer player) => IsPassive(ai) ? (object)false : null;

        [RogueDynamicHook(EventHooks)]
        private object CanHelicopterUseNapalm(PatrolHelicopterAI ai) => IsPassive(ai) ? (object)false : null;

        // The game retires a patrol helicopter after its lifetime; this one has somewhere to be.
        [RogueDynamicHook(EventHooks)]
        private object OnHelicopterRetire(PatrolHelicopterAI ai) => IsOurs(ai) ? (object)true : null;

        [RogueDynamicHook(EventHooks)]
        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (_firstLootReported || _phase != Phase.Site || player == null || entity == null || !_crates.Contains(entity)) return;
            _firstLootReported = true;
            _state.LastLooter = player.displayName ?? string.Empty;
            SaveDataDebounced(StateDataKey, _state);
            LogInformation("Event", player.displayName + " (" + player.UserIDString + ") opened the first crate at " + _siteGrid + ".");
            if (_config.Reporting.FirstLooter) Report("Report.FirstLoot", false, StripRichText(player.displayName), _siteGrid);
            Interface.CallHook("OnHeliCrashLooted", player, _site);
        }

        // RogueRustDeathNotes asks before announcing a death; the crash has its own announcement.
        [RogueDynamicHook(EventHooks)]
        private object OnDeathNotice(Dictionary<string, object> data, string message)
        {
            if (!_config.Reporting.HideDeathNote || _helicopter == null || data == null) return null;
            object victim;
            return data.TryGetValue("VictimEntity", out victim) && ReferenceEquals(victim, _helicopter) ? (object)false : null;
        }

        private bool IsOurs(PatrolHelicopterAI ai)
        {
            return ai != null && _helicopter != null && ai.helicopterBase == _helicopter;
        }

        private bool IsPassive(PatrolHelicopterAI ai)
        {
            return _config.Flight.Passive && IsOurs(ai);
        }

        #endregion

        #region API

        // Other plugins can ask whether a helicopter belongs to this event, for example to leave it alone.
        private object IsHeliCrashHelicopter(BaseEntity entity)
        {
            return entity != null && _helicopter != null && entity == _helicopter ? (object)true : null;
        }

        private bool IsHeliCrashActive() => _phase != Phase.Idle;

        private object GetHeliCrashSite() => _phase == Phase.Site ? (object)_site : null;

        #endregion

        #region Site selection

        private float GroundHeight(Vector3 position)
        {
            float height;
            return World.TryGetTerrainHeight(position, out height) ? height : position.y;
        }

        private Vector3 Grounded(Vector3 position)
        {
            position.y = GroundHeight(position);
            return position;
        }

        private bool TryPickSite(out Vector3 site)
        {
            SiteSettings settings = _config.Site;
            if (settings.UseCustomLocations && settings.CustomLocations.Count > 0)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    Location location = settings.CustomLocations[UnityEngine.Random.Range(0, settings.CustomLocations.Count)];
                    Vector2 jitter = UnityEngine.Random.insideUnitCircle * settings.CustomLocationJitter;
                    site = Grounded(new Vector3(location.X + jitter.x, 0f, location.Z + jitter.y));
                    // An owner chose these places on purpose, so only water and player bases rule one out.
                    if (!IsUnderwater(site) && !IsNearBase(site)) return true;
                }
                LogWarning("Site", "None of the custom locations is clear of water and player bases; using a random location instead.");
            }

            float half = World.WorldSize * 0.5f - settings.EdgeBuffer;
            if (half > 0f)
            {
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    site = Grounded(new Vector3(UnityEngine.Random.Range(-half, half), 0f, UnityEngine.Random.Range(-half, half)));
                    if (IsSuitable(site)) return true;
                }
            }

            site = Vector3.zero;
            return false;
        }

        private const int BlockedTopology =
            TerrainTopology.OCEAN | TerrainTopology.OFFSHORE | TerrainTopology.LAKE | TerrainTopology.RIVER |
            TerrainTopology.CLIFF | TerrainTopology.CLIFFSIDE | TerrainTopology.MONUMENT | TerrainTopology.BUILDING;

        private bool IsSuitable(Vector3 site)
        {
            // Cheapest checks first: most random points fail on water or topology.
            if (IsUnderwater(site)) return false;

            int topology;
            if (World.TryGetTopology(site, out topology) && (topology & BlockedTopology) != 0) return false;

            // The helicopter spins for a few seconds before it explodes and its crates are thrown
            // outwards, so the wreck can end up tens of metres from the chosen point. Require dry
            // land all the way round, or a site on the shoreline puts the loot in the sea.
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI * 0.25f;
                Vector3 around = Grounded(site + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ShoreBuffer);
                if (IsUnderwater(around)) return false;
            }
            if (TerrainMeta.HeightMap.GetSlope(site) > _config.Site.MaximumSlope) return false;

            float monumentDistance = _config.Site.AvoidMonuments;
            if (monumentDistance > 0f)
            {
                float limit = monumentDistance * monumentDistance;
                for (int i = 0; i < _monuments.Count; i++)
                {
                    float dx = _monuments[i].x - site.x;
                    float dz = _monuments[i].z - site.z;
                    if (dx * dx + dz * dz < limit) return false;
                }
            }

            return !IsNearBase(site);
        }

        private bool IsUnderwater(Vector3 site)
        {
            float water;
            return World.TryGetWaterHeight(site, out water) && site.y < water + 0.5f;
        }

        private bool IsNearBase(Vector3 site)
        {
            float radius = _config.Site.AvoidBases;
            return radius > 0f && Physics.CheckSphere(site, radius, Rust.Layers.Mask.Construction, QueryTriggerInteraction.Ignore);
        }

        #endregion

        #region Markers and guards

        private void SpawnMarkers()
        {
            MarkerSettings settings = _config.Marker;

            if (settings.Circle)
            {
                MapMarkerGenericRadius circle = GameManager.server.CreateEntity(RadiusMarkerPrefab, _site) as MapMarkerGenericRadius;
                if (circle != null)
                {
                    Color colour = ParseColour(settings.CircleColour);
                    circle.enableSaving = false;
                    circle.alpha = settings.CircleAlpha;
                    circle.radius = settings.CircleSize;
                    circle.color1 = colour;
                    circle.color2 = Color.black;
                    circle.Spawn();
                    circle.SendUpdate();
                    _markers.Add(circle);
                }
            }

            // The in-game map can only draw the marker types built into the client, so the wreck uses
            // the closest native one. A custom image cannot be placed on that map by a server.
            string iconPrefab = string.Equals(settings.Icon, "Explosion", StringComparison.OrdinalIgnoreCase) ? ExplosionMarkerPrefab
                : string.Equals(settings.Icon, "Crate", StringComparison.OrdinalIgnoreCase) ? CrateMarkerPrefab : null;
            if (iconPrefab != null)
            {
                BaseEntity icon = GameManager.server.CreateEntity(iconPrefab, _site);
                if (icon == null) LogWarning("Marker", "Map icon prefab could not be created: " + iconPrefab);
                else
                {
                    icon.enableSaving = false;
                    icon.Spawn();
                    // The explosion marker removes itself after a few minutes unless told how long to stay.
                    MapMarkerExplosion explosion = icon as MapMarkerExplosion;
                    if (explosion != null) explosion.SetDuration(_config.Site.DurationMinutes * 60f);
                    _markers.Add(icon);
                }
            }

            if (settings.Label)
            {
                // The vending marker is the one map marker that carries a text label on hover.
                VendingMachineMapMarker label = GameManager.server.CreateEntity(LabelMarkerPrefab, _site) as VendingMachineMapMarker;
                if (label != null)
                {
                    label.enableSaving = false;
                    label.markerShopName = string.IsNullOrWhiteSpace(settings.LabelText) ? "Helicopter Crash" : settings.LabelText;
                    label.Spawn();
                    _markers.Add(label);
                }
            }
        }

        // Used when the configured prefab does not exist on this version of the game.
        private static readonly string[] FallbackGuardPrefabs =
        {
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab",
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab",
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_patrol.prefab",
            "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_heavy.prefab"
        };

        private string _guardPrefab;

        private BaseEntity CreateGuard(Vector3 position)
        {
            // Once a prefab has worked, keep using it for the rest of the session.
            if (_guardPrefab != null) return GameManager.server.CreateEntity(_guardPrefab, position);

            BaseEntity guard = string.IsNullOrWhiteSpace(_config.Guards.Prefab) ? null : GameManager.server.CreateEntity(_config.Guards.Prefab, position);
            if (guard != null)
            {
                _guardPrefab = _config.Guards.Prefab;
                return guard;
            }

            foreach (string prefab in FallbackGuardPrefabs)
            {
                guard = GameManager.server.CreateEntity(prefab, position);
                if (guard == null) continue;
                _guardPrefab = prefab;
                LogWarning("Guards", "The configured guard prefab was not found; using " + prefab + " instead.");
                return guard;
            }
            return null;
        }

        private void SpawnGuards()
        {
            GuardSettings settings = _config.Guards;
            int placed = 0;
            for (int attempt = 0; attempt < settings.Count * 12 && placed < settings.Count; attempt++)
            {
                Vector2 ring = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(5f, settings.Radius);
                Vector3 point = Grounded(_site + new Vector3(ring.x, 0f, ring.y));

                // Scientists need the navigation mesh to move; skip anywhere it does not reach.
                Vector3 standing;
                if (IsUnderwater(point) || !TryFindNavigablePoint(point, out standing)) continue;

                BaseEntity guard = CreateGuard(standing);
                if (guard == null)
                {
                    LogWarning("Guards", "No guard prefab could be created. Tried the configured one (" + settings.Prefab + ") and the built-in alternatives.");
                    return;
                }

                guard.enableSaving = false;
                guard.Spawn();
                BaseCombatEntity combat = guard as BaseCombatEntity;
                if (combat != null && settings.Health > 0f) combat.InitializeHealth(settings.Health, settings.Health);
                _guards.Add(guard);
                placed++;
            }

            if (placed < settings.Count)
                LogInformation("Guards", "Placed " + placed + " of " + settings.Count + " guards at " + _siteGrid + "; no spot the AI can walk on was found for the rest.");
        }

        // Rust now runs its AI on its own navigation mesh, so Unity's sampler finds nothing on a
        // current server. Ask the game's first, and fall back to Unity's for a server still using it.
        private static bool TryFindNavigablePoint(Vector3 point, out Vector3 standing)
        {
            NavMeshHit hit;
            if (Rust.Ai.Gen2.RustNavMeshHelpers.SamplePosition(point, out hit, 6f, NavMesh.AllAreas, false) ||
                NavMesh.SamplePosition(point, out hit, 6f, NavMesh.AllAreas))
            {
                standing = hit.position;
                return true;
            }

            standing = point;
            return false;
        }

        private static bool IsAlive(BaseNetworkable entity)
        {
            return entity != null && !entity.IsDestroyed;
        }

        private static void KillAll(List<BaseEntity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
                if (IsAlive(entities[i])) entities[i].Kill();
            entities.Clear();
        }

        private static Color ParseColour(string value)
        {
            string[] parts = (value ?? string.Empty).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            float r, g, b;
            if (parts.Length >= 3 &&
                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out r) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out g) &&
                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out b))
                return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b));
            return new Color(1f, 0.3f, 0.1f);
        }

        #endregion

        #region Reporting

        private void Report(string key, bool gameTip, params object[] arguments)
        {
            ReportSettings settings = _config.Reporting;
            bool tip = gameTip && settings.GameTip;
            if (!settings.Chat && !tip) return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                string text = string.Format(CultureInfo.InvariantCulture, Message(key, player), arguments);
                if (settings.Chat) player.ChatMessage(settings.Prefix + text);
                if (tip) player.SendConsoleCommand("gametip.showgametip", text);
            }

            if (!tip) return;
            if (_tipJob != null) _tipJob.Dispose();
            _tipJob = Delay(TimeSpan.FromSeconds(settings.GameTipSeconds), HideGameTips, "helicrash-tip");
        }

        private void HideGameTips()
        {
            _tipJob = null;
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (player != null && player.IsConnected) player.SendConsoleCommand("gametip.hidegametip");
        }

        // A short banner at the top of the screen: the crash icon, the grid, and how far away it is.
        private void ShowAlert()
        {
            ReportSettings settings = _config.Reporting;
            if (!settings.Alert) return;

            bool hasIcon = !string.IsNullOrEmpty(_icon);
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected) continue;
                Vector3 position = player.transform.position;
                string detail = string.Format(CultureInfo.InvariantCulture, Message("Alert.Detail", player), _siteGrid,
                    Mathf.RoundToInt(Vector3.Distance(position, _site)), Bearing(position, _site));

                RogueUiDocument ui = CreateUi(AlertUi, "Hud");
                ui.Panel(AlertUi, "Hud", new RogueUiRect("0.36 0.845", "0.64 0.925"), "0.008 0.020 0.028 0.94");
                ui.Panel("rrhc.alert.rail", AlertUi, new RogueUiRect("0 0", "0.012 1"), "0.95 0.42 0.12 1");
                if (hasIcon) ui.Image("rrhc.alert.icon", AlertUi, new RogueUiRect("0.03 0.10", "0.17 0.90"), _icon);
                ui.Label("rrhc.alert.title", AlertUi, new RogueUiRect(hasIcon ? "0.20 0.50" : "0.05 0.50", "0.98 0.94"),
                    Message("Alert.Title", player), 14, "0.92 0.95 0.96 1", "MiddleLeft");
                ui.Label("rrhc.alert.detail", AlertUi, new RogueUiRect(hasIcon ? "0.20 0.08" : "0.05 0.08", "0.98 0.52"),
                    detail, 11, "0.50 0.78 0.83 1", "MiddleLeft");
                ShowUi(player, ui);
            }

            if (_alertJob != null) _alertJob.Dispose();
            _alertJob = Delay(TimeSpan.FromSeconds(settings.AlertSeconds), HideAlert, "helicrash-alert");
        }

        private void HideAlert()
        {
            if (_alertJob != null) _alertJob.Dispose();
            _alertJob = null;
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (player != null) DestroyUi(player, AlertUi);
        }

        private void SendDiscord(string text)
        {
            string url = _config.Reporting.DiscordWebhook;
            if (string.IsNullOrWhiteSpace(url)) return;
            SendDiscordWebhookAsync(url, StripRichText(text), "Heli Crash").ContinueWith(task =>
            {
                if (task.IsFaulted || (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && !task.Result.Success))
                    MainThread(() => LogWarning("Discord", "The Discord webhook did not accept the crash report."));
            });
        }

        private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);

        private static string StripRichText(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : RichText.Replace(text, string.Empty);
        }

        private static string FormatPosition(Vector3 position)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0}, {1:0}, {2:0})", position.x, position.y, position.z);
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 60d) return Math.Max(0, (int)seconds).ToString(CultureInfo.InvariantCulture) + "s";
            TimeSpan span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1d
                ? ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "h " + span.Minutes.ToString(CultureInfo.InvariantCulture) + "m"
                : span.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
        }

        private static readonly string[] Compass = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

        private static string Bearing(Vector3 from, Vector3 to)
        {
            float degrees = Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
            if (degrees < 0f) degrees += 360f;
            return Compass[Mathf.RoundToInt(degrees / 45f) % 8];
        }

        #endregion

        #region Commands

        [RogueCommand("helicrash",
            Aliases = new[] { "crashsite" },
            Description = "Shows the helicopter crash status. Admins can start, stop and schedule the event.",
            Usage = "/helicrash [start|here|at <x> <z>|stop|next [minutes]]",
            Category = "Events", CooldownSeconds = 1, AllowChat = true, AllowConsole = true)]
        private RogueCommandResult HeliCrashCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            string action = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            if (action == "status") return RogueCommandResult.Ok(Status(player));

            if (!context.IsServer && !HasPermission(player, AdminPermission) &&
                (player.net == null || player.net.connection == null || player.net.connection.authLevel < 2))
                return RogueCommandResult.Fail(Message("Error.NoPermission", player));

            string failure;
            switch (action)
            {
                case "start":
                    return StartEvent(null, out failure)
                        ? RogueCommandResult.Ok("Helicopter crash started; it will come down near " + GridReference(_target) + ".")
                        : RogueCommandResult.Fail("Could not start: " + failure);

                case "here":
                    if (player == null) return RogueCommandResult.Fail("Use this in game, or use: helicrash at <x> <z>");
                    return StartEvent(player.transform.position, out failure)
                        ? RogueCommandResult.Ok("Helicopter crash started; it will come down at your position.")
                        : RogueCommandResult.Fail("Could not start: " + failure);

                case "at":
                    float x, z;
                    if (args.Length < 3 ||
                        !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                        !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                        return RogueCommandResult.Fail("Usage: /helicrash at <x> <z>");
                    return StartEvent(new Vector3(x, 0f, z), out failure)
                        ? RogueCommandResult.Ok("Helicopter crash started; it will come down near " + GridReference(_target) + ".")
                        : RogueCommandResult.Fail("Could not start: " + failure);

                case "stop":
                    if (_phase == Phase.Idle) return RogueCommandResult.Fail("No helicopter crash is running.");
                    StopEvent("stopped", announce: true);
                    return RogueCommandResult.Ok("Helicopter crash stopped and cleaned up.");

                case "next":
                    float minutes;
                    if (args.Length > 1 && float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes))
                    {
                        _state.NextEventUnix = UnixTime + Math.Max(0f, minutes) * 60d;
                        SaveDataDebounced(StateDataKey, _state);
                    }
                    return RogueCommandResult.Ok(_config.Schedule.Automatic
                        ? "Next automatic helicopter crash in " + FormatDuration(_state.NextEventUnix - UnixTime) + "."
                        : "Automatic helicopter crashes are turned off in the config.");

                default:
                    return RogueCommandResult.Fail("Usage: /helicrash [start|here|at <x> <z>|stop|next [minutes]]");
            }
        }

        private string Status(BasePlayer player)
        {
            switch (_phase)
            {
                case Phase.Flying:
                case Phase.Crashing:
                    return Message("Status.Incoming", player);

                case Phase.Site:
                    string remaining = FormatDuration(_config.Site.DurationMinutes * 60f - PhaseSeconds);
                    if (player == null)
                        return string.Format(CultureInfo.InvariantCulture, Message("Status.Site"), _siteGrid, _crates.Count, remaining);
                    Vector3 position = player.transform.position;
                    return string.Format(CultureInfo.InvariantCulture, Message("Status.SiteDistance", player), _siteGrid, _crates.Count, remaining,
                        Mathf.RoundToInt(Vector3.Distance(position, _site)), Bearing(position, _site));

                default:
                    return string.IsNullOrEmpty(_state.LastCrashGrid)
                        ? Message("Status.None", player)
                        : string.Format(CultureInfo.InvariantCulture, Message("Status.Last", player), _state.LastCrashGrid,
                            FormatDuration(UnixTime - _state.LastCrashUnix));
            }
        }

        #endregion

        #region Messages

        protected override IReadOnlyDictionary<string, string> DefaultMessages
        {
            get
            {
                return new Dictionary<string, string>
                {
                    ["Error.NoPermission"] = "You do not have permission to use this command.",
                    ["Report.Incoming"] = "A patrol helicopter is malfunctioning and losing altitude. It is going to come down somewhere on the island.",
                    ["Report.Crashed"] = "The helicopter has crashed at {0}. The wreck is marked on your map.",
                    ["Report.FirstLoot"] = "{0} has reached the wreck at {1} and is opening the crates.",
                    ["Report.Looted"] = "The helicopter wreck at {0} has been stripped.",
                    ["Report.Expired"] = "The helicopter wreck at {0} has burned out.",
                    ["Report.Stopped"] = "The helicopter crash event has been called off.",
                    ["Alert.Title"] = "HELICOPTER DOWN",
                    ["Alert.Detail"] = "Grid {0}  •  {1}m to your {2}",
                    ["Status.None"] = "No helicopter has crashed yet.",
                    ["Status.Last"] = "No active crash site. The last helicopter came down at {0}, {1} ago.",
                    ["Status.Incoming"] = "A malfunctioning helicopter is in the air. Watch the sky and your map.",
                    ["Status.Site"] = "Crash site at {0}: {1} crate(s) left, {2} remaining.",
                    ["Status.SiteDistance"] = "Crash site at {0}: {1} crate(s) left, {2} remaining. It is {3}m to your {4}."
                };
            }
        }

        #endregion
    }
}
