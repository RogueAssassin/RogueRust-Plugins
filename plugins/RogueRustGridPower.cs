using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Oxide.Plugins
{
    [Info("RogueRustGridPower", "RogueAssassin", "1.9.2")]
    [Description("RogueRust-native GridPower controller for automatic world streetlights, deterministic density, diagnostics, and player-facing grid events.")]
    public sealed class RogueRustGridPower : RogueRustPlugin
    {
        #region Constants

        private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 9, 2);

        [RoguePermission]
        private const string PermissionAdmin = "roguerustgridpower.admin";

        #endregion

        #region Configuration

        private PluginConfig _config;

        private sealed class PluginConfig
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Street Lights", Order = 20)]
            public StreetLightSettings StreetLights = new StreetLightSettings();

            [JsonProperty("Player Grid Power", Order = 30)]
            public PlayerGridSettings PlayerGrid = new PlayerGridSettings();

            [JsonProperty("Player Notifications", Order = 40)]
            public NotificationSettings Notifications = new NotificationSettings();

            [JsonProperty("Performance Settings", Order = 50)]
            public PerformanceSettings Performance = new PerformanceSettings();

            [JsonProperty("Developer Settings", Order = 60)]
            public DeveloperSettings Developer = new DeveloperSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class GeneralSettings
        {
            [JsonProperty("Enable Plugin")]
            public bool Enabled = true;
        }

        private sealed class StreetLightSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Turn On Hour (0-23)")]
            public float TurnOnHour = 20f;

            [JsonProperty("Turn Off Hour (0-23)")]
            public float TurnOffHour = 8f;

            [JsonProperty("Street Light Density Percent (0-100)")]
            public int DensityPercent = 100;

            [JsonProperty("Selection Seed")]
            public int SelectionSeed = 0;
        }

        private sealed class PlayerGridSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Discover Power Poles")]
            public bool DiscoverPowerPoles = true;

            [JsonProperty("Power Poles")]
            public PowerPoleSettings PowerPoles = new PowerPoleSettings();

            [JsonProperty("Require Vanilla Power Grid")]
            public bool RequireVanillaPowerGrid = false;

        }

        private sealed class PowerPoleSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Density Percent (0-100)")]
            public int DensityPercent = 100;

            [JsonProperty("Selection Seed")]
            public int SelectionSeed = 0;

            [JsonProperty("Power Output")]
            public int PowerOutput = 600;

            [JsonProperty("Limit Transformer Output")]
            public bool LimitTransformerOutput = true;

            [JsonProperty("Transformer Maximum Output")]
            public int TransformerMaximumOutput = 100;

            [JsonProperty("Spawn Batch Size")]
            public int SpawnBatchSize = 2;

            [JsonProperty("Batch Interval Seconds")]
            public float BatchIntervalSeconds = 0.25f;

            [JsonProperty("Allow Wooden Ladders On Grid Power Poles")]
            public bool AllowLaddersOnPoles = true;

            [JsonProperty("Ladder Pole Detection Radius")]
            public float LadderPoleDetectionRadius = 4f;

            [JsonProperty("Show Ladder Placement GameTip")]
            public bool ShowLadderPlacementGameTip = true;

            [JsonProperty("Ladder Placement GameTip Duration Seconds")]
            public float LadderPlacementGameTipDurationSeconds = 2.5f;

            [JsonProperty("Ladder Placement Success Message")]
            public string LadderPlacementSuccessMessage = "Ladder attached to RogueRust GridPower pole.";
        }


        private sealed class NotificationSettings
        {
            [JsonProperty("Show GameTips")]
            public bool ShowGameTips = true;

            [JsonProperty("GameTip Duration Seconds")]
            public float DurationSeconds = 8f;

            [JsonProperty("Night Message")]
            public string NightMessage =
                "As night settles in, the grid generators rumble to life and the roadside lights begin to glow.";

            [JsonProperty("Dawn Message")]
            public string DawnMessage =
                "As daylight returns, the grid generators wind down and the streetlights fade out.";
        }

        private sealed class PerformanceSettings
        {
            [JsonProperty("Street Light Check Interval Seconds")]
            public float CheckIntervalSeconds = 30f;

            [JsonProperty("Refresh Street Light Cache Minutes")]
            public float RefreshCacheMinutes = 10f;
        }

        private sealed class DeveloperSettings
        {
            [JsonProperty("Log Diagnostics")]
            public bool LogDiagnostics = false;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            PrintWarning("Creating a new RogueRustGridPower default configuration.");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                // Read raw JSON first. Typed deserialization populates field defaults in
                // memory, which otherwise hides sections missing from an older config.
                Newtonsoft.Json.Linq.JObject raw = Config.ReadObject<Newtonsoft.Json.Linq.JObject>();
                bool missingSections =
                    raw == null ||
                    raw["General Settings"] == null ||
                    raw["Street Lights"] == null ||
                    raw["Player Grid Power"] == null ||
                    raw["Player Notifications"] == null ||
                    raw["Performance Settings"] == null ||
                    raw["Developer Settings"] == null ||
                    raw["Version (DO NOT CHANGE)"] == null;

                _config = raw != null ? raw.ToObject<PluginConfig>() : new PluginConfig();
                if (_config == null)
                    throw new JsonException("Configuration deserialized to null.");

                VersionNumber loadedVersion = _config.Version;
                bool changed = missingSections;
                changed |= EnsureConfigDefaults();
                changed |= MigrateConfig(loadedVersion);

                if (_config.Version != CurrentVersion)
                {
                    _config.Version = CurrentVersion;
                    changed = true;
                }

                if (changed)
                {
                    SaveConfig();
                    LogInformation("Configuration",
                        "Configuration updated to v" + CurrentVersion +
                        "; existing values were preserved and new settings were added.");
                }
            }
            catch (Exception ex)
            {
                PrintError("Configuration is invalid: " + ex.Message);
                PrintWarning("Loading RogueRustGridPower default configuration.");
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private bool EnsureConfigDefaults()
        {
            bool changed = false;

            if (_config.General == null) { _config.General = new GeneralSettings(); changed = true; }
            if (_config.StreetLights == null) { _config.StreetLights = new StreetLightSettings(); changed = true; }
            if (_config.PlayerGrid == null) { _config.PlayerGrid = new PlayerGridSettings(); changed = true; }
            if (_config.PlayerGrid.PowerPoles == null) { _config.PlayerGrid.PowerPoles = new PowerPoleSettings(); changed = true; }
            int poleDensity = Math.Max(0, Math.Min(100, _config.PlayerGrid.PowerPoles.DensityPercent));
            if (poleDensity != _config.PlayerGrid.PowerPoles.DensityPercent) { _config.PlayerGrid.PowerPoles.DensityPercent = poleDensity; changed = true; }
            int poleOutput = Math.Max(0, _config.PlayerGrid.PowerPoles.PowerOutput);
            if (poleOutput != _config.PlayerGrid.PowerPoles.PowerOutput) { _config.PlayerGrid.PowerPoles.PowerOutput = poleOutput; changed = true; }
            int transformerMaximumOutput = Math.Max(0, _config.PlayerGrid.PowerPoles.TransformerMaximumOutput);
            if (transformerMaximumOutput != _config.PlayerGrid.PowerPoles.TransformerMaximumOutput) { _config.PlayerGrid.PowerPoles.TransformerMaximumOutput = transformerMaximumOutput; changed = true; }
            int batchSize = Math.Max(1, Math.Min(2, _config.PlayerGrid.PowerPoles.SpawnBatchSize));
            if (batchSize != _config.PlayerGrid.PowerPoles.SpawnBatchSize) { _config.PlayerGrid.PowerPoles.SpawnBatchSize = batchSize; changed = true; }
            float ladderRadius = Math.Max(1f, Math.Min(8f, _config.PlayerGrid.PowerPoles.LadderPoleDetectionRadius));
            if (Math.Abs(ladderRadius - _config.PlayerGrid.PowerPoles.LadderPoleDetectionRadius) > 0.001f) { _config.PlayerGrid.PowerPoles.LadderPoleDetectionRadius = ladderRadius; changed = true; }
            float ladderTipDuration = Math.Max(1f, Math.Min(10f, _config.PlayerGrid.PowerPoles.LadderPlacementGameTipDurationSeconds));
            if (Math.Abs(ladderTipDuration - _config.PlayerGrid.PowerPoles.LadderPlacementGameTipDurationSeconds) > 0.001f) { _config.PlayerGrid.PowerPoles.LadderPlacementGameTipDurationSeconds = ladderTipDuration; changed = true; }
            float batchInterval = Math.Max(0.25f, _config.PlayerGrid.PowerPoles.BatchIntervalSeconds);
            if (Math.Abs(batchInterval - _config.PlayerGrid.PowerPoles.BatchIntervalSeconds) > 0.001f) { _config.PlayerGrid.PowerPoles.BatchIntervalSeconds = batchInterval; changed = true; }

            if (_config.Notifications == null) { _config.Notifications = new NotificationSettings(); changed = true; }
            if (_config.Performance == null) { _config.Performance = new PerformanceSettings(); changed = true; }
            if (_config.Developer == null) { _config.Developer = new DeveloperSettings(); changed = true; }

            float onHour = ClampHour(_config.StreetLights.TurnOnHour);
            if (Math.Abs(onHour - _config.StreetLights.TurnOnHour) > 0.001f)
            {
                _config.StreetLights.TurnOnHour = onHour;
                changed = true;
            }

            float offHour = ClampHour(_config.StreetLights.TurnOffHour);
            if (Math.Abs(offHour - _config.StreetLights.TurnOffHour) > 0.001f)
            {
                _config.StreetLights.TurnOffHour = offHour;
                changed = true;
            }

            int density = Math.Max(0, Math.Min(100, _config.StreetLights.DensityPercent));
            if (density != _config.StreetLights.DensityPercent)
            {
                _config.StreetLights.DensityPercent = density;
                changed = true;
            }

            float checkInterval = Math.Max(5f, _config.Performance.CheckIntervalSeconds);
            if (Math.Abs(checkInterval - _config.Performance.CheckIntervalSeconds) > 0.001f)
            {
                _config.Performance.CheckIntervalSeconds = checkInterval;
                changed = true;
            }

            float refreshMinutes = Math.Max(1f, _config.Performance.RefreshCacheMinutes);
            if (Math.Abs(refreshMinutes - _config.Performance.RefreshCacheMinutes) > 0.001f)
            {
                _config.Performance.RefreshCacheMinutes = refreshMinutes;
                changed = true;
            }

            float gameTipDuration = Math.Max(1f, Math.Min(30f, _config.Notifications.DurationSeconds));
            if (Math.Abs(gameTipDuration - _config.Notifications.DurationSeconds) > 0.001f)
            {
                _config.Notifications.DurationSeconds = gameTipDuration;
                changed = true;
            }

            return changed;
        }

        private bool MigrateConfig(VersionNumber loadedVersion)
        {
            if (loadedVersion == CurrentVersion)
                return false;

            // Value-preserving migration: missing v1.6.0 sections receive defaults while
            // existing administrator values remain untouched.
            _config.Version = CurrentVersion;
            LogInformation("Configuration",
                "Configuration migrated from v" + loadedVersion + " to v" + CurrentVersion + ".");
            return true;
        }

        #endregion

        #region Logging Policy

        // Routine operational logging stays intentionally light. Detailed discovery,
        // per-entity, inspection and recovery traces are emitted only when the
        // Developer Settings -> Log Diagnostics switch is enabled.
        private bool DiagnosticsEnabled =>
            _config != null &&
            _config.Developer != null &&
            _config.Developer.LogDiagnostics;

        private void DiagnosticLog(string area, string message)
        {
            if (!DiagnosticsEnabled)
                return;

            LogInformation(area, message);
        }

        private void DiagnosticPuts(string message)
        {
            if (!DiagnosticsEnabled)
                return;

            Puts(message);
        }

        #endregion

        #region Localization

        protected override IReadOnlyDictionary<string, string> DefaultMessages =>
            new Dictionary<string, string>
            {
                ["Error.PluginDisabled"] = "RogueRustGridPower is currently disabled.",
                ["Error.StreetlightsDisabled"] = "Streetlight control is currently disabled.",
                ["Error.BackendUnavailable"] = "RogueRust GridPower backend is unavailable.",
                ["Error.PlayerOnly"] = "This command must be run by an in-game player.",
                ["Command.Status"] = "RogueRustGridPower v{0} | Backend=RogueRust 4.1.27 | WorldHour={1:0.00} | Desired={2} | {3}",
                ["Command.Refresh"] = "GridPower refresh complete. Discovered={0}. {1}",
                ["Command.Apply"] = "GridPower configuration reapplied. {0}",
                ["Command.Grid"] = "Public Grid | State={0} | Cell={1} | PoweredHere={2} | GridStateChanges={3}",
                ["Command.Infrastructure"] = "{0}",
                ["Command.Scan"] = "Player infrastructure scan complete. {0}",
                ["Command.Debug"] = "GridPower debug markers drawn for 30 seconds. Poles={0} | Streetlights={1} | Substations={2} | Conflicts={3}",
                ["Command.DebugUnavailable"] = "GridPower reference files are not available yet. Run rrgrid.scan first.",
                ["Command.PowerProbe"] = "Nearest GRID pole probe | Distance={0:0.0}m | NearbyEntities={1} | IOEntities={2}. Details written to server console.",
                ["Command.PowerProbeNone"] = "No discovered GRID pole was found within {0:0}m.",
                ["Command.Inspect"] = "Infrastructure inspector complete | PoleDistance={0:0.0}m | NearbyTransforms={1} | RelevantTransforms={2} | IOEntities={3}. See server console.",
                ["Command.InspectNone"] = "No discovered GRID pole was found within {0:0}m.",
                ["Command.PowerTestDisabled"] = "Player Grid Power is disabled in configuration.",
                ["Command.PowerInspect"] = "{0}",
                ["Command.PowerTest"] = "{0}",
                ["Command.PowerRemove"] = "{0}",
                ["Command.ClimbInspect"] = "{0}",
                ["Command.ClimbInspectNone"] = "No cached GRID pole was found within {0:0}m.",
                ["Grid.StateChanged"] = "Public grid state changed to {0}."
            };

        private string Localize(string key, BasePlayer player = null, params object[] args)
        {
            string message = Message(key, player);
            return args != null && args.Length > 0 ? string.Format(message, args) : message;
        }

        #endregion

        #region Fields

        private IRogueGridPowerService _gridPower;
        private Timer _startupTimer;
        private Timer _streetlightTimer;
        private Timer _refreshTimer;
        private Timer _playerGridTimer;
        private Timer _gameTipTimer;
        private Timer _commandQueueTimer;
        private bool? _lastDesiredState;
        private bool _serverReady;
        private bool _gameTipVisible;
        private readonly List<UnityEngine.Vector3> _ladderPoleCache = new List<UnityEngine.Vector3>();
        private DateTime _ladderPoleCacheUtc = DateTime.MinValue;

        #endregion

        #region Initialization

        private void Init()
        {
            LogInformation("Lifecycle",
                Name + " v" + CurrentVersion + " initialized using RogueRust services.");

            try
            {
                _gridPower = Rogue.GridPower;
                if (_gridPower != null)
                    _gridPower.GridStateChanged += OnGridStateChanged;
            }
            catch (Exception ex)
            {
                LogError("GridPower",
                    "RogueRust 4.1.27 GridPower backend could not be acquired: " + ex.Message);
            }
        }

        private void OnServerInitialized()
        {
            _serverReady = true;

            if (!_config.General.Enabled)
            {
                LogWarning("Lifecycle", "Plugin is disabled in configuration.");
                return;
            }

            if (_gridPower == null)
            {
                LogError("GridPower",
                    "RogueRust GridPower backend is unavailable. Install RogueRust DLL or newer and restart the server.");
                return;
            }

            ApplyBackendConfiguration();

            // Pump RogueRust's shared cooperative command queue at a conservative rate.
            // Heavy admin commands enqueue work and return immediately instead of creating
            // entity/network bursts in the player's command frame.
            _commandQueueTimer?.Destroy();
            _commandQueueTimer = timer.Every(0.35f, () =>
            {
                if (_serverReady)
                    RogueCommandQueueService.Pump(1);
            });

            if (_config.PlayerGrid.Enabled && _config.PlayerGrid.DiscoverPowerPoles)
                _gridPower.RefreshPlayerInfrastructure(false);

            // Keep OnServerInitialized lightweight. Streetlights and player-grid
            // population begin on the next scheduler slice and remain independent.
            _startupTimer?.Destroy();
            _startupTimer = timer.Once(0.1f, () =>
            {
                _startupTimer = null;
                if (!_serverReady || !_config.General.Enabled || _gridPower == null)
                    return;

                if (_config.StreetLights.Enabled)
                    StartStreetlightController();
                else
                    LogWarning("GridPower", "Streetlight control is disabled in configuration.");

                StartPlayerGridController();
            });
        }


        private void StartPlayerGridController()
        {
            _playerGridTimer?.Destroy();
            _playerGridTimer = null;

            if (_config.PlayerGrid == null || !_config.PlayerGrid.Enabled ||
                _config.PlayerGrid.PowerPoles == null || !_config.PlayerGrid.PowerPoles.Enabled)
            {
                RogueGridPowerPlayerService.Configure(false, 0, 0, 0, false);
                return;
            }

            PowerPoleSettings poles = _config.PlayerGrid.PowerPoles;

            // Saving RogueRust transformers use Rust's native PowergridIOAccessPoint prefab.
            // A forced infrastructure scan can therefore see our own persisted transformer
            // as native IO.  The authoritative vanilla marker is the native access spawn
            // point (powergridAccess), so normalise that self-contamination before the
            // player-grid service consumes schema-4 references.
            RepairSelfContaminatedPlayerReferences();

            string configured = RogueGridPowerPlayerService.Configure(
                true,
                poles.DensityPercent,
                poles.SelectionSeed,
                poles.PowerOutput,
                _config.PlayerGrid.RequireVanillaPowerGrid,
                poles.LimitTransformerOutput,
                poles.TransformerMaximumOutput);

            DiagnosticLog("PowerPoles", configured);

            string started = RogueGridPowerRecoveryService.Reconcile(false);
            DiagnosticLog("PowerPoles", started);

            _playerGridTimer = timer.Every(poles.BatchIntervalSeconds, () =>
            {
                if (!_serverReady || !_config.General.Enabled)
                    return;

                bool more = RogueGridPowerRecoveryService.ProcessBatch(poles.SpawnBatchSize);
                if (more)
                    return;

                _playerGridTimer?.Destroy();
                _playerGridTimer = null;
                DiagnosticLog("PowerPoles", "Ready :: " + RogueGridPowerPlayerService.Status());
            });
        }

        #endregion

        #region RogueRust Commands

        [RogueCommand(
            "rrgrid.status",
            Aliases = new[] { "gridpower.status" },
            Description = "Shows RogueRust GridPower streetlight status.",
            Usage = "rrgrid.status",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandStatus(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckCommandAvailability();
            if (unavailable != null)
                return unavailable;

            float hour = GetWorldHour();
            bool desired = IsNightWindow(
                hour,
                _config.StreetLights.TurnOnHour,
                _config.StreetLights.TurnOffHour);

            return RogueCommandResult.Ok(Localize(
                "Command.Status",
                context.NativePlayer,
                CurrentVersion,
                hour,
                desired ? "ON" : "OFF",
                _gridPower.GetStreetlightStatus()));
        }

        [RogueCommand(
            "rrgrid.refresh",
            Aliases = new[] { "gridpower.refresh" },
            Description = "Refreshes discovered streetlights and reapplies the current GridPower state.",
            Usage = "rrgrid.refresh",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandRefresh(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckCommandAvailability();
            if (unavailable != null)
                return unavailable;

            ApplyBackendConfiguration();
            int discovered = _gridPower.RefreshStreetlights(true);
            _lastDesiredState = null;
            EvaluateStreetlights(true, false);

            return RogueCommandResult.Ok(Localize(
                "Command.Refresh",
                context.NativePlayer,
                discovered,
                _gridPower.GetStreetlightStatus()));
        }

        [RogueCommand(
            "rrgrid.apply",
            Aliases = new[] { "gridpower.apply" },
            Description = "Reapplies GridPower configuration and the current world-time streetlight state.",
            Usage = "rrgrid.apply",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandApply(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckCommandAvailability();
            if (unavailable != null)
                return unavailable;

            ApplyBackendConfiguration();
            _lastDesiredState = null;
            EvaluateStreetlights(true, false);

            return RogueCommandResult.Ok(Localize(
                "Command.Apply",
                context.NativePlayer,
                _gridPower.GetStreetlightStatus()));
        }

        [RogueCommand(
            "rrgrid.grid",
            Aliases = new[] { "gridpower.grid" },
            Description = "Shows the authoritative public-grid state and the caller's backend grid cell.",
            Usage = "rrgrid.grid",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandGrid(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckCommandAvailability();
            if (unavailable != null)
                return unavailable;

            BasePlayer player = context.NativePlayer;
            UnityEngine.Vector3 position = player != null ? player.transform.position : UnityEngine.Vector3.zero;
            string cell = _gridPower.GetGridCell(position);
            bool powered = _gridPower.IsPowered(position);

            return RogueCommandResult.Ok(Localize(
                "Command.Grid",
                player,
                _gridPower.GridOnline ? "ONLINE" : "OFFLINE",
                cell,
                powered ? "YES" : "NO",
                _gridPower.GridStateChanges));
        }

        [RogueCommand(
            "rrgrid.infrastructure",
            Aliases = new[] { "gridpower.infrastructure" },
            Description = "Shows cached player GridPower infrastructure discovery status.",
            Usage = "rrgrid.infrastructure",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandInfrastructure(RogueCommandContext context)
        {
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            return RogueCommandResult.Ok(Localize(
                "Command.Infrastructure",
                context.NativePlayer,
                _gridPower.GetPlayerInfrastructureStatus()));
        }

        [RogueCommand(
            "rrgrid.scan",
            Aliases = new[] { "gridpower.scan" },
            Description = "Rebuilds the read-only player GridPower infrastructure reference cache.",
            Usage = "rrgrid.scan",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandScan(RogueCommandContext context)
        {
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            _gridPower.RefreshPlayerInfrastructure(true);
            return RogueCommandResult.Ok(Localize(
                "Command.Scan",
                context.NativePlayer,
                _gridPower.GetPlayerInfrastructureStatus()));
        }

        [RogueCommand(
            "rrgrid.debug",
            Aliases = new[] { "gridpower.debug" },
            Description = "Draws temporary admin-only GridPower discovery markers within 200m.",
            Usage = "rrgrid.debug",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandDebug(RogueCommandContext context)
        {
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string playerPath = _gridPower.PlayerReferenceFilePath;
            string streetlightPath = _gridPower.ReferenceFilePath;

            if (!File.Exists(playerPath) || !File.Exists(streetlightPath))
                return RogueCommandResult.Fail(Localize("Command.DebugUnavailable", player));

            try
            {
                JObject playerDoc = JObject.Parse(File.ReadAllText(playerPath));
                JObject streetlightDoc = JObject.Parse(File.ReadAllText(streetlightPath));

                List<UnityEngine.Vector3> streetlights = ReadDebugPositions(streetlightDoc["streetlights"]);
                List<UnityEngine.Vector3> poles = ReadDebugPositions(playerDoc["playerPoles"]);
                List<UnityEngine.Vector3> substations = ReadDebugPositions(playerDoc["substations"]);

                const float radius = 200f;
                const float duration = 30f;
                float radiusSqr = radius * radius;
                int drawnPoles = 0;
                int drawnStreetlights = 0;
                int drawnSubstations = 0;
                int conflicts = 0;

                UnityEngine.Vector3 origin = player.transform.position;

                for (int i = 0; i < poles.Count; i++)
                {
                    UnityEngine.Vector3 pos = poles[i];
                    if ((pos - origin).sqrMagnitude > radiusSqr)
                        continue;

                    bool conflict = IsNearAny(pos, streetlights, 4f);
                    if (conflict)
                        conflicts++;

                    DrawDebugMarker(
                        player,
                        pos,
                        conflict ? UnityEngine.Color.red : UnityEngine.Color.cyan,
                        conflict ? "GRID POLE - CONFLICT" : "GRID POLE");
                    drawnPoles++;
                }

                for (int i = 0; i < streetlights.Count; i++)
                {
                    UnityEngine.Vector3 pos = streetlights[i];
                    if ((pos - origin).sqrMagnitude > radiusSqr)
                        continue;

                    DrawDebugMarker(player, pos, UnityEngine.Color.yellow, "STREETLIGHT - EXCLUDED");
                    drawnStreetlights++;
                }

                for (int i = 0; i < substations.Count; i++)
                {
                    UnityEngine.Vector3 pos = substations[i];
                    if ((pos - origin).sqrMagnitude > radiusSqr)
                        continue;

                    DrawDebugMarker(player, pos, UnityEngine.Color.green, "SUBSTATION");
                    drawnSubstations++;
                }

                return RogueCommandResult.Ok(Localize(
                    "Command.Debug",
                    player,
                    drawnPoles,
                    drawnStreetlights,
                    drawnSubstations,
                    conflicts));
            }
            catch (Exception ex)
            {
                PrintError("GridPower debug visualization failed: " + ex.Message);
                return RogueCommandResult.Fail("GridPower debug visualization failed. Check the server console.");
            }
        }

        private static List<UnityEngine.Vector3> ReadDebugPositions(JToken token)
        {
            List<UnityEngine.Vector3> result = new List<UnityEngine.Vector3>();
            JArray array = token as JArray;
            if (array == null)
                return result;

            foreach (JToken entry in array)
            {
                float x = entry.Value<float?>("x") ?? 0f;
                float y = entry.Value<float?>("y") ?? 0f;
                float z = entry.Value<float?>("z") ?? 0f;
                result.Add(new UnityEngine.Vector3(x, y, z));
            }

            return result;
        }

        private static bool IsNearAny(
            UnityEngine.Vector3 position,
            List<UnityEngine.Vector3> references,
            float radius)
        {
            float radiusSqr = radius * radius;

            for (int i = 0; i < references.Count; i++)
            {
                UnityEngine.Vector3 delta = references[i] - position;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radiusSqr)
                    return true;
            }

            return false;
        }

        private static void DrawDebugMarker(
            BasePlayer player,
            UnityEngine.Vector3 position,
            UnityEngine.Color color,
            string label)
        {
            UnityEngine.Vector3 marker = position + UnityEngine.Vector3.up * 2f;
            player.SendConsoleCommand("ddraw.sphere", 30f, color, marker, 0.45f);
            player.SendConsoleCommand("ddraw.text", 30f, color, marker + UnityEngine.Vector3.up * 0.75f, label);
        }

        [RogueCommand(
            "rrgrid.powerprobe",
            Aliases = new[] { "gridpower.powerprobe" },
            Description = "Inspects the nearest discovered GRID pole for existing vanilla/networked electrical entities without changing anything.",
            Usage = "rrgrid.powerprobe",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandPowerProbe(RogueCommandContext context)
        {
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string playerPath = _gridPower.PlayerReferenceFilePath;
            if (!File.Exists(playerPath))
                return RogueCommandResult.Fail(Localize("Command.DebugUnavailable", player));

            try
            {
                JObject playerDoc = JObject.Parse(File.ReadAllText(playerPath));
                List<UnityEngine.Vector3> poles = ReadDebugPositions(playerDoc["playerPoles"]);
                UnityEngine.Vector3 origin = player.transform.position;

                const float maxPoleDistance = 75f;
                float bestSqr = maxPoleDistance * maxPoleDistance;
                UnityEngine.Vector3 nearest = UnityEngine.Vector3.zero;
                bool found = false;

                for (int i = 0; i < poles.Count; i++)
                {
                    float sqr = (poles[i] - origin).sqrMagnitude;
                    if (sqr >= bestSqr)
                        continue;

                    bestSqr = sqr;
                    nearest = poles[i];
                    found = true;
                }

                if (!found)
                    return RogueCommandResult.Fail(Localize("Command.PowerProbeNone", player, maxPoleDistance));

                const float probeRadius = 18f;
                UnityEngine.Collider[] colliders = UnityEngine.Physics.OverlapSphere(
                    nearest,
                    probeRadius,
                    UnityEngine.Physics.AllLayers,
                    UnityEngine.QueryTriggerInteraction.Collide);

                HashSet<BaseEntity> entities = new HashSet<BaseEntity>();
                int ioCount = 0;

                for (int i = 0; i < colliders.Length; i++)
                {
                    UnityEngine.Collider collider = colliders[i];
                    if (collider == null)
                        continue;

                    BaseEntity entity = collider.GetComponentInParent<BaseEntity>();
                    if (entity == null || entity.IsDestroyed || !entities.Add(entity))
                        continue;

                    IOEntity io = entity as IOEntity;
                    if (io != null)
                        ioCount++;

                    string prefab = entity.PrefabName ?? string.Empty;
                    string type = entity.GetType().Name;
                    float distance = UnityEngine.Vector3.Distance(nearest, entity.transform.position);

                    DiagnosticPuts(
                        "[RogueRust/GridPower Probe] " +
                        "Pole=" + nearest +
                        " | Entity=" + type +
                        " | Prefab=" + prefab +
                        " | Distance=" + distance.ToString("0.00") + "m" +
                        " | IO=" + (io != null ? "YES" : "NO"));

                    player.SendConsoleCommand(
                        "ddraw.text",
                        20f,
                        io != null ? UnityEngine.Color.green : UnityEngine.Color.white,
                        entity.transform.position + UnityEngine.Vector3.up * 1.0f,
                        io != null ? "IO: " + type : type);
                }

                player.SendConsoleCommand(
                    "ddraw.sphere",
                    20f,
                    UnityEngine.Color.magenta,
                    nearest,
                    1.25f);

                player.SendConsoleCommand(
                    "ddraw.text",
                    20f,
                    UnityEngine.Color.magenta,
                    nearest + UnityEngine.Vector3.up * 2.5f,
                    "GRID POWER PROBE");

                return RogueCommandResult.Ok(Localize(
                    "Command.PowerProbe",
                    player,
                    UnityEngine.Mathf.Sqrt(bestSqr),
                    entities.Count,
                    ioCount));
            }
            catch (Exception ex)
            {
                PrintError("GridPower pole probe failed: " + ex);
                return RogueCommandResult.Fail("GridPower pole probe failed. Check the server console.");
            }
        }

        [RogueCommand(
            "rrgrid.inspect",
            Aliases = new[] { "gridpower.inspect" },
            Description = "Inspects the nearest cached GRID pole's static transform hierarchy and relevant electrical components.",
            Usage = "rrgrid.inspect",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandInspect(RogueCommandContext context)
        {
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string playerPath = _gridPower.PlayerReferenceFilePath;
            if (!File.Exists(playerPath))
                return RogueCommandResult.Fail(Localize("Command.DebugUnavailable", player));

            try
            {
                JObject playerDoc = JObject.Parse(File.ReadAllText(playerPath));
                List<UnityEngine.Vector3> poles = ReadDebugPositions(playerDoc["playerPoles"]);
                UnityEngine.Vector3 origin = player.transform.position;

                const float maxPoleDistance = 75f;
                float bestSqr = maxPoleDistance * maxPoleDistance;
                UnityEngine.Vector3 nearest = UnityEngine.Vector3.zero;
                bool found = false;

                for (int i = 0; i < poles.Count; i++)
                {
                    float sqr = (poles[i] - origin).sqrMagnitude;
                    if (sqr >= bestSqr)
                        continue;

                    bestSqr = sqr;
                    nearest = poles[i];
                    found = true;
                }

                if (!found)
                    return RogueCommandResult.Fail(Localize("Command.InspectNone", player, maxPoleDistance));

                const float transformRadius = 14f;
                float transformRadiusSqr = transformRadius * transformRadius;
                UnityEngine.Transform[] transforms = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>();
                int nearbyTransforms = 0;
                int relevantTransforms = 0;

                DiagnosticPuts("[RogueRust/GridPower Inspect] ===== BEGIN =====");
                DiagnosticPuts("[RogueRust/GridPower Inspect] Pole=" + nearest +
                     " | PlayerDistance=" + UnityEngine.Mathf.Sqrt(bestSqr).ToString("0.00") + "m");

                for (int i = 0; i < transforms.Length; i++)
                {
                    UnityEngine.Transform t = transforms[i];
                    if (t == null || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
                        continue;

                    UnityEngine.Vector3 delta = t.position - nearest;
                    if (delta.sqrMagnitude > transformRadiusSqr)
                        continue;

                    nearbyTransforms++;

                    string name = t.name ?? string.Empty;
                    string parentName = t.parent != null ? (t.parent.name ?? string.Empty) : "<none>";
                    if (!IsRelevantInfrastructureName(name) && !IsRelevantInfrastructureName(parentName))
                        continue;

                    relevantTransforms++;
                    string rootName = t.root != null ? (t.root.name ?? string.Empty) : "<none>";
                    string path = BuildTransformPath(t);
                    string components = GetComponentTypeList(t.gameObject);

                    DiagnosticPuts("[RogueRust/GridPower Inspect] Transform" +
                         " | Distance=" + delta.magnitude.ToString("0.00") + "m" +
                         " | Name=" + name +
                         " | Parent=" + parentName +
                         " | Root=" + rootName +
                         " | Path=" + path +
                         " | Components=" + components);

                    player.SendConsoleCommand(
                        "ddraw.text",
                        25f,
                        UnityEngine.Color.yellow,
                        t.position + UnityEngine.Vector3.up * 0.35f,
                        name);
                }

                // Secondary networked-entity check: only report IO or infrastructure-related
                // entities, avoiding the tree/junk/loot noise from the original probe.
                UnityEngine.Collider[] colliders = UnityEngine.Physics.OverlapSphere(
                    nearest,
                    18f,
                    UnityEngine.Physics.AllLayers,
                    UnityEngine.QueryTriggerInteraction.Collide);

                HashSet<BaseEntity> entities = new HashSet<BaseEntity>();
                int ioCount = 0;

                for (int i = 0; i < colliders.Length; i++)
                {
                    UnityEngine.Collider collider = colliders[i];
                    if (collider == null)
                        continue;

                    BaseEntity entity = collider.GetComponentInParent<BaseEntity>();
                    if (entity == null || entity.IsDestroyed || !entities.Add(entity))
                        continue;

                    IOEntity io = entity as IOEntity;
                    string prefab = entity.PrefabName ?? string.Empty;
                    string type = entity.GetType().Name;

                    if (io == null &&
                        !IsRelevantInfrastructureName(prefab) &&
                        !IsRelevantInfrastructureName(type))
                        continue;

                    if (io != null)
                        ioCount++;

                    float distance = UnityEngine.Vector3.Distance(nearest, entity.transform.position);
                    DiagnosticPuts("[RogueRust/GridPower Inspect] Entity" +
                         " | Distance=" + distance.ToString("0.00") + "m" +
                         " | Type=" + type +
                         " | Prefab=" + prefab +
                         " | IO=" + (io != null ? "YES" : "NO"));
                }

                player.SendConsoleCommand(
                    "ddraw.sphere",
                    25f,
                    UnityEngine.Color.magenta,
                    nearest,
                    1.25f);
                player.SendConsoleCommand(
                    "ddraw.text",
                    25f,
                    UnityEngine.Color.magenta,
                    nearest + UnityEngine.Vector3.up * 2.5f,
                    "GRID INFRASTRUCTURE INSPECT");

                DiagnosticPuts("[RogueRust/GridPower Inspect] Summary" +
                     " | NearbyTransforms=" + nearbyTransforms +
                     " | RelevantTransforms=" + relevantTransforms +
                     " | IOEntities=" + ioCount);
                DiagnosticPuts("[RogueRust/GridPower Inspect] ===== END =====");

                return RogueCommandResult.Ok(Localize(
                    "Command.Inspect",
                    player,
                    UnityEngine.Mathf.Sqrt(bestSqr),
                    nearbyTransforms,
                    relevantTransforms,
                    ioCount));
            }
            catch (Exception ex)
            {
                PrintError("GridPower infrastructure inspector failed: " + ex);
                return RogueCommandResult.Fail("GridPower infrastructure inspector failed. Check the server console.");
            }
        }

        private static bool IsRelevantInfrastructureName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string n = value.ToLowerInvariant();
            return n.Contains("power") ||
                   n.Contains("pole") ||
                   n.Contains("transform") ||
                   n.Contains("electric") ||
                   n.Contains("socket") ||
                   n.Contains("output") ||
                   n.Contains("root") ||
                   n.Contains("switch") ||
                   n.Contains("substation") ||
                   n.Contains("fuse");
        }

        private static string BuildTransformPath(UnityEngine.Transform transform)
        {
            if (transform == null)
                return "<null>";

            List<string> parts = new List<string>();
            UnityEngine.Transform current = transform;
            int guard = 0;

            while (current != null && guard++ < 16)
            {
                parts.Add(current.name ?? "<unnamed>");
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static string GetComponentTypeList(UnityEngine.GameObject gameObject)
        {
            if (gameObject == null)
                return "<none>";

            UnityEngine.Component[] components = gameObject.GetComponents<UnityEngine.Component>();
            List<string> names = new List<string>();

            for (int i = 0; i < components.Length; i++)
            {
                UnityEngine.Component component = components[i];
                if (component == null)
                    continue;

                string name = component.GetType().Name;
                if (!names.Contains(name))
                    names.Add(name);
            }

            return names.Count > 0 ? string.Join(",", names.ToArray()) : "<none>";
        }

        [RogueCommand(
            "rrgrid.climb.inspect",
            Aliases = new[] { "gridpower.climb.inspect" },
            Description = "Inspects the nearest cached GRID pole for Rust native climb-ladder components without changing the world.",
            Usage = "rrgrid.climb.inspect",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandClimbInspect(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string playerPath = _gridPower.PlayerReferenceFilePath;
            if (!File.Exists(playerPath))
                return RogueCommandResult.Fail(Localize("Command.DebugUnavailable", player));

            try
            {
                JObject playerDoc = JObject.Parse(File.ReadAllText(playerPath));
                List<UnityEngine.Vector3> poles = ReadDebugPositions(playerDoc["playerPoles"]);
                UnityEngine.Vector3 origin = player.transform.position;

                const float maxPoleDistance = 75f;
                float bestSqr = maxPoleDistance * maxPoleDistance;
                UnityEngine.Vector3 nearest = UnityEngine.Vector3.zero;
                bool found = false;

                for (int i = 0; i < poles.Count; i++)
                {
                    float sqr = (poles[i] - origin).sqrMagnitude;
                    if (sqr >= bestSqr)
                        continue;

                    bestSqr = sqr;
                    nearest = poles[i];
                    found = true;
                }

                if (!found)
                    return RogueCommandResult.Fail(Localize("Command.ClimbInspectNone", player, maxPoleDistance));

                const float transformRadius = 8f;
                float transformRadiusSqr = transformRadius * transformRadius;
                UnityEngine.Transform[] transforms = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Transform>();
                int ladderObjects = 0;
                int triggerLadders = 0;
                int colliders = 0;

                DiagnosticPuts("[RogueRust/GridPower Climb] ===== BEGIN =====");
                DiagnosticPuts("[RogueRust/GridPower Climb] Pole=" + nearest +
                     " | PlayerDistance=" + UnityEngine.Mathf.Sqrt(bestSqr).ToString("0.00") + "m");

                for (int i = 0; i < transforms.Length; i++)
                {
                    UnityEngine.Transform t = transforms[i];
                    if (t == null || !t.gameObject.scene.IsValid() || !t.gameObject.scene.isLoaded)
                        continue;

                    UnityEngine.Vector3 delta = t.position - nearest;
                    if (delta.sqrMagnitude > transformRadiusSqr)
                        continue;

                    string name = t.name ?? string.Empty;
                    string parentName = t.parent != null ? (t.parent.name ?? string.Empty) : "<none>";
                    string components = GetComponentTypeList(t.gameObject);
                    bool ladderNamed =
                        name.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        parentName.IndexOf("ladder", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasTriggerLadder = components.IndexOf("TriggerLadder", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool hasCollider = components.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!ladderNamed && !hasTriggerLadder)
                        continue;

                    ladderObjects++;
                    if (hasTriggerLadder) triggerLadders++;
                    if (hasCollider) colliders++;

                    DiagnosticPuts("[RogueRust/GridPower Climb] Transform" +
                         " | Name=" + name +
                         " | Parent=" + parentName +
                         " | Root=" + (t.root != null ? (t.root.name ?? "<none>") : "<none>") +
                         " | ActiveSelf=" + t.gameObject.activeSelf +
                         " | ActiveHierarchy=" + t.gameObject.activeInHierarchy +
                         " | Components=" + components +
                         " | Path=" + BuildTransformPath(t));
                }

                DiagnosticPuts("[RogueRust/GridPower Climb] Summary" +
                     " | LadderObjects=" + ladderObjects +
                     " | TriggerLadders=" + triggerLadders +
                     " | Colliders=" + colliders);
                DiagnosticPuts("[RogueRust/GridPower Climb] ===== END =====");

                string summary =
                    "Climb inspect | PoleDistance=" + UnityEngine.Mathf.Sqrt(bestSqr).ToString("0.0") + "m" +
                    " | LadderObjects=" + ladderObjects +
                    " | TriggerLadders=" + triggerLadders +
                    " | Colliders=" + colliders +
                    ". Details written to server console.";

                return RogueCommandResult.Ok(Localize("Command.ClimbInspect", player, summary));
            }
            catch (Exception ex)
            {
                PrintError("GridPower climb inspector failed: " + ex);
                return RogueCommandResult.Fail("GridPower climb inspector failed. Check the server console.");
            }
        }

        [RogueCommand(
            "rrgrid.climb.test",
            Aliases = new[] { "gridpower.climb.test" },
            Description = "Creates one temporary non-networked native TriggerLadder test on the nearest power access point.",
            Usage = "rrgrid.climb.test",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandClimbTest(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string result = RogueGridPowerClimbService.CreateTest(player.transform.position);
            DiagnosticPuts("[RogueRust/GridPower Climb] " + result);
            return result.IndexOf("FAILED", StringComparison.OrdinalIgnoreCase) >= 0
                ? RogueCommandResult.Fail(result)
                : RogueCommandResult.Ok(result);
        }

        [RogueCommand(
            "rrgrid.climb.teststatus",
            Aliases = new[] { "gridpower.climb.teststatus" },
            Description = "Shows the temporary native climbing test state on the nearest power access point.",
            Usage = "rrgrid.climb.teststatus",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandClimbTestStatus(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));
            string result = RogueGridPowerClimbService.Inspect(player.transform.position);
            DiagnosticPuts("[RogueRust/GridPower Climb] " + result);
            return RogueCommandResult.Ok(result);
        }

        [RogueCommand(
            "rrgrid.climb.remove",
            Aliases = new[] { "gridpower.climb.remove" },
            Description = "Removes the temporary native climbing test from the nearest power access point.",
            Usage = "rrgrid.climb.remove",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandClimbRemove(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));
            string result = RogueGridPowerClimbService.RemoveNearest(player.transform.position);
            DiagnosticPuts("[RogueRust/GridPower Climb] " + result);
            return RogueCommandResult.Ok(result);
        }

        [RogueCommand(
            "rrgrid.playergrid",
            Aliases = new[] { "gridpower.playergrid" },
            Description = "Shows the RogueRust player power-pole backend status.",
            Usage = "rrgrid.playergrid",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandPlayerGridStatus(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));
            string status = RogueGridPowerPlayerService.Status();
            DiagnosticPuts("[RogueRust/GridPower] Player grid status :: " + status);
            return RogueCommandResult.Ok(status);
        }

        [RogueCommand(
            "rrgrid.help",
            Aliases = new[] { "gridpower.help" },
            Description = "Shows RogueRustGridPower administration and diagnostic commands.",
            Usage = "rrgrid.help",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandGridHelp(RogueCommandContext context)
        {
            string help =
                "RogueRustGridPower v1.9.2 commands\n" +
                "rrgrid.apply\nrrgrid.climb.inspect\nrrgrid.climb.remove\nrrgrid.climb.test\nrrgrid.climb.teststatus\nrrgrid.debug\nrrgrid.grid\nrrgrid.help\nrrgrid.infrastructure\nrrgrid.inspect\nrrgrid.playergrid\nrrgrid.power.inspect\nrrgrid.power.remove\nrrgrid.power.test\nrrgrid.powerprobe\nrrgrid.rebuild\nrrgrid.rebuild.status\nrrgrid.refresh\nrrgrid.scan\nrrgrid.status\n" +
                "Chat: /rrgrid ... | F1/server/RCON: rrgrid....\n" +
                "Nearest/inspect commands require an in-game player position.";
            DiagnosticPuts("[RogueRust/GridPower Help]\n" + help);
            return RogueCommandResult.Ok(help);
        }


        [RogueCommand(
            "rrgrid.rebuild",
            Aliases = new[] { "gridpower.rebuild" },
            Description = "Safely rebuilds player GridPower references and reconciles saved transformers using the DLL recovery pipeline.",
            Usage = "rrgrid.rebuild",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandRebuild(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));
            if (_config.PlayerGrid == null || !_config.PlayerGrid.Enabled ||
                _config.PlayerGrid.PowerPoles == null || !_config.PlayerGrid.PowerPoles.Enabled)
                return RogueCommandResult.Fail("Player Grid Power poles are disabled in configuration.");
            if (RogueGridPowerRecoveryService.RecoveryActive)
                return RogueCommandResult.Fail("A GridPower recovery cycle is already active.");

            _playerGridTimer?.Destroy();
            _playerGridTimer = null;

            ApplyBackendConfiguration();

            PowerPoleSettings poles = _config.PlayerGrid.PowerPoles;
            string configured = RogueGridPowerPlayerService.Configure(
                true,
                poles.DensityPercent,
                poles.SelectionSeed,
                poles.PowerOutput,
                _config.PlayerGrid.RequireVanillaPowerGrid,
                poles.LimitTransformerOutput,
                poles.TransformerMaximumOutput);

            DiagnosticPuts("[RogueRust/GridPower Rebuild] Configuration :: " + configured);

            // DLL 4.1.10 owns cache validation/rebuild policy. Healthy reference and
            // population caches are reused; missing/invalid/empty references are rebuilt,
            // self-owned transformer IO contamination is repaired, and saved Rust wiring
            // remains untouched throughout recovery.
            string result = RogueGridPowerRecoveryService.Reconcile(true);
            DiagnosticPuts("[RogueRust/GridPower Rebuild] " + result);

            _playerGridTimer = timer.Every(poles.BatchIntervalSeconds, () =>
            {
                if (!_serverReady || !_config.General.Enabled)
                    return;

                bool more = RogueGridPowerRecoveryService.ProcessBatch(poles.SpawnBatchSize);
                if (more)
                    return;

                _playerGridTimer?.Destroy();
                _playerGridTimer = null;
                DiagnosticPuts("[RogueRust/GridPower Rebuild] COMPLETE :: " + RogueGridPowerRecoveryService.Status());
            });

            return RogueCommandResult.Ok(result);
        }

        [RogueCommand(
            "rrgrid.rebuild.status",
            Aliases = new[] { "gridpower.rebuild.status" },
            Description = "Shows the current/last GridPower recovery stage and player-grid reconciliation status.",
            Usage = "rrgrid.rebuild.status",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandRebuildStatus(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));
            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            string status = RogueGridPowerRecoveryService.Status();
            DiagnosticPuts("[RogueRust/GridPower Rebuild] " + status);
            return RogueCommandResult.Ok(status);
        }

        [RogueCommand(
            "rrgrid.power.inspect",
            Aliases = new[] { "gridpower.power.inspect" },
            Description = "Inspects the nearest schema-4 GridPower pole and the current controlled power test state.",
            Usage = "rrgrid.power.inspect",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandPowerInspect(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckPowerTestAvailability(context);
            if (unavailable != null)
                return unavailable;

            string result = RogueGridPowerControlledTest.Inspect(context.NativePlayer.transform.position);
            return RogueCommandResult.Ok(Localize("Command.PowerInspect", context.NativePlayer, result));
        }

        [RogueCommand(
            "rrgrid.power.test",
            Aliases = new[] { "gridpower.power.test" },
            Description = "Creates one non-saving native Rust power-grid access entity on the nearest safe Rogue candidate pole.",
            Usage = "rrgrid.power.test",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandPowerTest(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckPowerTestAvailability(context);
            if (unavailable != null)
                return unavailable;

            string result = RogueGridPowerControlledTest.TestNearest(context.NativePlayer.transform.position);
            if (result.IndexOf("=FAILED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.IndexOf("=BLOCKED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.IndexOf("=NO-CANDIDATE", StringComparison.OrdinalIgnoreCase) >= 0)
                return RogueCommandResult.Fail(result);

            return RogueCommandResult.Ok(Localize("Command.PowerTest", context.NativePlayer, result));
        }

        [RogueCommand(
            "rrgrid.power.remove",
            Aliases = new[] { "gridpower.power.remove" },
            Description = "Removes the active controlled GridPower test entity.",
            Usage = "rrgrid.power.remove",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandPowerRemove(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));

            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            if (context.NativePlayer == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            string result = RogueGridPowerControlledTest.Remove();
            return RogueCommandResult.Ok(Localize("Command.PowerRemove", context.NativePlayer, result));
        }

        private RogueCommandResult CheckPowerTestAvailability(RogueCommandContext context)
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));

            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            if (context.NativePlayer == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            if (_config.PlayerGrid == null || !_config.PlayerGrid.Enabled)
                return RogueCommandResult.Fail(Localize("Command.PowerTestDisabled", context.NativePlayer));

            return null;
        }

        private RogueCommandResult CheckCommandAvailability()
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));

            if (!_config.StreetLights.Enabled)
                return RogueCommandResult.Fail(Message("Error.StreetlightsDisabled"));

            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            return null;
        }

        /// <summary>
        /// Repairs a schema-4 discovery edge case caused by RogueRust's own saving
        /// PowergridIOAccessPoint transformers being rediscovered as vanilla IO.
        /// Native vanilla poles have powergridAccess=true (native access spawn point).
        /// A pole with powergridAccess=false + ioAccessPoint=true after RogueRust has
        /// populated transformers is our self-contamination signature.
        /// </summary>
        private int RepairSelfContaminatedPlayerReferences()
        {
            if (_gridPower == null)
                return 0;

            string path = _gridPower.PlayerReferenceFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;

            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(path));
                if ((doc.Value<int?>("schema") ?? 0) != 4)
                    return 0;

                JArray poles = doc["playerPoles"] as JArray;
                if (poles == null || poles.Count == 0)
                    return 0;

                int repaired = 0;
                foreach (JToken token in poles)
                {
                    JObject pole = token as JObject;
                    if (pole == null)
                        continue;

                    bool powergridAccess = pole.Value<bool?>("powergridAccess") ?? false;
                    bool ioAccessPoint = pole.Value<bool?>("ioAccessPoint") ?? false;
                    string classification = pole.Value<string>("classification") ?? string.Empty;

                    if (powergridAccess ||
                        !ioAccessPoint ||
                        !string.Equals(classification, "vanillaPowergrid", StringComparison.OrdinalIgnoreCase))
                        continue;

                    pole["classification"] = "candidate";
                    pole["ioAccessPoint"] = false;
                    repaired++;
                }

                if (repaired == 0)
                    return 0;

                JObject stats = doc["stats"] as JObject;
                if (stats != null)
                {
                    int vanilla = 0;
                    foreach (JToken token in poles)
                    {
                        JObject pole = token as JObject;
                        if (pole != null &&
                            string.Equals(pole.Value<string>("classification"), "vanillaPowergrid",
                                StringComparison.OrdinalIgnoreCase))
                            vanilla++;
                    }
                    stats["vanillaPowergrid"] = vanilla;
                }

                File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.Indented));
                DiagnosticPuts("[RogueRust/GridPower] Compatibility repair :: corrected " + repaired +
                     " self-owned transformer IO classifications in " + Path.GetFileName(path) + ".");
                return repaired;
            }
            catch (Exception ex)
            {
                PrintError("Failed to repair GridPower player references: " + ex.Message);
                return 0;
            }
        }


        #endregion

        #region Permissions

        // Permission registration and command permission checks are owned by RogueRust.
        // roguerustgridpower.admin is declared through [RoguePermission] and RogueCommand metadata.

        #endregion

        #region Cooldowns

        // GridPower administration commands intentionally have no cooldown.
        // They are permission-protected and execute only explicit administrative actions.

        #endregion

        #region Hooks

        // Keep hooks thin. World discovery and cinematic light ownership remain in the DLL.

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!_serverReady || player == null || input == null ||
                !input.WasJustPressed(BUTTON.FIRE_PRIMARY) || _config == null ||
                !_config.General.Enabled || _config.PlayerGrid == null || !_config.PlayerGrid.Enabled ||
                _config.PlayerGrid.PowerPoles == null || !_config.PlayerGrid.PowerPoles.Enabled ||
                !_config.PlayerGrid.PowerPoles.AllowLaddersOnPoles)
                return;

            Item item = player.GetActiveItem();
            if (item == null || item.info == null ||
                !string.Equals(item.info.shortname, "ladder.wooden.wall", StringComparison.OrdinalIgnoreCase))
                return;

            HeldEntity held = player.GetHeldEntity();
            Planner planner = held as Planner;
            if (planner == null)
                return;

            UnityEngine.RaycastHit hit;
            UnityEngine.Ray ray = player.eyes.BodyRay();
            int mask = UnityEngine.LayerMask.GetMask("World", "Deployed");
            if (!UnityEngine.Physics.Raycast(ray, out hit, 5f, mask, UnityEngine.QueryTriggerInteraction.Ignore))
                return;

            float poleDistance;
            if (!IsGridPoleLadderTarget(hit.point, out poleDistance))
                return;

            string colliderName = hit.collider != null ? (hit.collider.name ?? string.Empty) : string.Empty;
            if (colliderName.IndexOf("powerline_pole", StringComparison.OrdinalIgnoreCase) < 0)
            {
                DiagnosticPuts("[RogueRust/GridPower Ladder] Near GRID pole but ray hit '" + colliderName + "'; native placement left unchanged.");
                return;
            }

            BaseEntity ladder = SpawnGridPoleLadder(player, item, hit);
            if (ladder == null)
            {
                ShowPlayerGameTip(player, "GridPower could not attach the ladder at this position.", 2.5f);
                return;
            }

            item.UseItem(1);
            if (_config.PlayerGrid.PowerPoles.ShowLadderPlacementGameTip)
                ShowPlayerGameTip(player, _config.PlayerGrid.PowerPoles.LadderPlacementSuccessMessage, _config.PlayerGrid.PowerPoles.LadderPlacementGameTipDurationSeconds);

            DiagnosticPuts("[RogueRust/GridPower Ladder] Placed wooden ladder | Player=" + player.UserIDString +
                " | PoleDistance=" + poleDistance.ToString("0.00") + "m | Collider=" + colliderName +
                " | Position=" + ladder.transform.position);
        }

        private bool IsGridPoleLadderTarget(UnityEngine.Vector3 position, out float distance)
        {
            distance = float.MaxValue;
            RefreshLadderPoleCache(false);
            if (_ladderPoleCache.Count == 0)
                return false;

            float radius = _config.PlayerGrid.PowerPoles.LadderPoleDetectionRadius;
            float radiusSqr = radius * radius;
            for (int i = 0; i < _ladderPoleCache.Count; i++)
            {
                UnityEngine.Vector3 delta = _ladderPoleCache[i] - position;
                delta.y = 0f;
                float sqr = delta.sqrMagnitude;
                if (sqr > radiusSqr)
                    continue;

                distance = UnityEngine.Mathf.Sqrt(sqr);
                return true;
            }
            return false;
        }

        private BaseEntity SpawnGridPoleLadder(BasePlayer player, Item item, UnityEngine.RaycastHit hit)
        {
            const string ladderPrefab = "assets/prefabs/building/ladder.wall.wood/ladder.wooden.wall.prefab";

            UnityEngine.Vector3 normal = hit.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.001f)
                normal = (player.transform.position - hit.point);
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.001f)
                normal = player.eyes.BodyRay().direction;
            normal.y = 0f;
            normal.Normalize();

            // Keep the ladder just off the pole surface so its collider does not begin embedded.
            UnityEngine.Vector3 position = hit.point + normal * 0.08f;
            UnityEngine.Quaternion rotation = UnityEngine.Quaternion.LookRotation(-normal, UnityEngine.Vector3.up);

            BaseEntity entity = GameManager.server.CreateEntity(ladderPrefab, position, rotation, true);
            if (entity == null)
                return null;

            entity.OwnerID = player.userID;
            entity.skinID = item.skin;
            entity.Spawn();
            entity.SendMessage("SetDeployedBy", player, UnityEngine.SendMessageOptions.DontRequireReceiver);

            Interface.CallHook("OnEntityBuilt", player.GetHeldEntity() as Planner, entity.gameObject);
            return entity;
        }

        private void RefreshLadderPoleCache(bool force)
        {
            if (!force && _ladderPoleCache.Count > 0 &&
                (DateTime.UtcNow - _ladderPoleCacheUtc).TotalSeconds < 30d)
                return;

            _ladderPoleCacheUtc = DateTime.UtcNow;
            _ladderPoleCache.Clear();
            if (_gridPower == null || string.IsNullOrEmpty(_gridPower.PlayerReferenceFilePath) ||
                !File.Exists(_gridPower.PlayerReferenceFilePath))
                return;

            try
            {
                JObject doc = JObject.Parse(File.ReadAllText(_gridPower.PlayerReferenceFilePath));
                JArray poles = doc["playerPoles"] as JArray;
                if (poles == null)
                    return;

                foreach (JToken token in poles)
                {
                    JObject pole = token as JObject;
                    if (pole == null)
                        continue;
                    string classification = pole.Value<string>("classification") ?? string.Empty;
                    if (!string.Equals(classification, "candidate", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(classification, "vanillaPowergrid", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _ladderPoleCache.Add(new UnityEngine.Vector3(
                        pole.Value<float?>("x") ?? 0f,
                        pole.Value<float?>("y") ?? 0f,
                        pole.Value<float?>("z") ?? 0f));
                }
            }
            catch (Exception ex)
            {
                DiagnosticPuts("[RogueRust/GridPower Ladder] Failed to refresh pole cache: " + ex.Message);
            }
        }

        private void OnGridStateChanged(bool online)
        {
            if (_config == null || _config.Developer == null || !DiagnosticsEnabled)
                return;

            LogInformation("GridPower",
                Localize("Grid.StateChanged", null, online ? "ONLINE" : "OFFLINE"));
        }

        #endregion

        #region Core Logic

        private void StartStreetlightController()
        {
            DestroyTimers();

            int discovered = _gridPower.RefreshStreetlights(false);

            if (DiagnosticsEnabled)
            {
                LogInformation("GridPower",
                    "Controller ready. Discovered=" + discovered +
                    ", Density=" + _gridPower.StreetlightDensity + "%" +
                    ", Selected=" + _gridPower.SelectedStreetlights + ".");
            }

            // Startup/reload applies state silently. GameTips are reserved for a real
            // live transition across the configured day/night boundary.
            EvaluateStreetlights(true, false);

            _streetlightTimer = timer.Every(
                _config.Performance.CheckIntervalSeconds,
                () => EvaluateStreetlights(false, true));

            _refreshTimer = timer.Every(
                _config.Performance.RefreshCacheMinutes * 60f,
                RefreshStreetlightCache);
        }

        private void RefreshStreetlightCache()
        {
            if (!CanOperate())
                return;

            int before = _gridPower.TrackedStreetlights;
            int after = _gridPower.RefreshStreetlights(false);

            if (DiagnosticsEnabled && before != after)
            {
                LogInformation("GridPower",
                    "Streetlight cache refreshed. Discovered=" + after +
                    ", Previous=" + before + ".");
            }

            // Refresh can rebuild DLL-owned light entities but must not generate a
            // duplicate player notification for the same day/night state.
            EvaluateStreetlights(true, false);
        }

        private void EvaluateStreetlights(bool force, bool allowNotification)
        {
            if (!CanOperate())
                return;

            float hour = GetWorldHour();
            bool shouldBeOn = IsNightWindow(
                hour,
                _config.StreetLights.TurnOnHour,
                _config.StreetLights.TurnOffHour);

            if (!force &&
                _lastDesiredState.HasValue &&
                _lastDesiredState.Value == shouldBeOn)
                return;

            bool stateChanged =
                _lastDesiredState.HasValue &&
                _lastDesiredState.Value != shouldBeOn;

            int changed = _gridPower.SetStreetlights(shouldBeOn, false);
            _lastDesiredState = shouldBeOn;

            if (DiagnosticsEnabled &&
                (force || stateChanged || changed > 0))
            {
                LogInformation("GridPower",
                    "Street lights " + (shouldBeOn ? "ON" : "OFF") +
                    " at world hour " + hour.ToString("0.00") + ". " +
                    _gridPower.GetStreetlightStatus() +
                    " | Changed=" + changed);
            }

            if (allowNotification && stateChanged)
                ShowStateGameTip(shouldBeOn);
        }

        private void ApplyBackendConfiguration()
        {
            if (_gridPower == null)
                return;

            _gridPower.ConfigureStreetlights(
                _config.StreetLights.DensityPercent,
                _config.StreetLights.SelectionSeed);
        }

        private bool CanOperate()
        {
            return _serverReady &&
                   _config != null &&
                   _config.General.Enabled &&
                   _config.StreetLights.Enabled &&
                   _gridPower != null;
        }

        #endregion

        #region RogueRust Services

        // Rogue.GridPower owns:
        // - static world streetlight discovery
        // - deterministic density selection
        // - cinematic light creation/recovery
        // - owned-entity cleanup
        // - backend diagnostics
        //
        // This plugin owns policy:
        // - enable/disable
        // - day/night schedule
        // - density/seed configuration
        // - administrator commands
        // - player-facing GameTips

        #endregion

        #region Player Notifications

        private void ShowPlayerGameTip(BasePlayer player, string message, float durationSeconds)
        {
            if (player == null || !player.IsConnected || string.IsNullOrWhiteSpace(message))
                return;

            player.SendConsoleCommand("gametip.showgametip", message);
            timer.Once(Math.Max(1f, durationSeconds), () =>
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("gametip.hidegametip");
            });
        }

        private void ShowStateGameTip(bool streetlightsOn)
        {
            if (!_config.Notifications.ShowGameTips)
                return;

            string message = streetlightsOn
                ? _config.Notifications.NightMessage
                : _config.Notifications.DawnMessage;

            if (string.IsNullOrWhiteSpace(message))
                return;

            HideOwnedGameTip();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                player.SendConsoleCommand("gametip.showgametip", message);
            }

            _gameTipVisible = true;
            _gameTipTimer = timer.Once(
                _config.Notifications.DurationSeconds,
                HideOwnedGameTip);
        }

        private void HideOwnedGameTip()
        {
            _gameTipTimer?.Destroy();
            _gameTipTimer = null;

            if (!_gameTipVisible)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                player.SendConsoleCommand("gametip.hidegametip");
            }

            _gameTipVisible = false;
        }

        #endregion

        #region Data

        // v1.3.0 has no persistent plugin-owned data.
        // Future GridPower state should use data/RogueRust/RogueRustGridPower/...

        #endregion

        #region Performance

        // Expensive world discovery is cached in RogueRust's GridPower backend.
        // This plugin only polls world time at the configured low-frequency interval.
        // Refresh is intentionally separate and defaults to ten minutes.

        #endregion

        #region Helpers

        private static float GetWorldHour()
        {
            try
            {
                if (TOD_Sky.Instance != null && TOD_Sky.Instance.Cycle != null)
                    return TOD_Sky.Instance.Cycle.Hour;
            }
            catch
            {
                // World time can briefly be unavailable during startup/shutdown.
            }

            return 12f;
        }

        private static bool IsNightWindow(float hour, float turnOn, float turnOff)
        {
            if (Math.Abs(turnOn - turnOff) < 0.001f)
                return true;

            if (turnOn < turnOff)
                return hour >= turnOn && hour < turnOff;

            return hour >= turnOn || hour < turnOff;
        }

        private static float ClampHour(float value)
        {
            if (value < 0f) return 0f;
            if (value > 23.99f) return 23.99f;
            return value;
        }

        #endregion

        #region Cleanup

        private void DestroyTimers()
        {
            _startupTimer?.Destroy();
            _startupTimer = null;

            _streetlightTimer?.Destroy();
            _streetlightTimer = null;

            _refreshTimer?.Destroy();
            _refreshTimer = null;

            _playerGridTimer?.Destroy();
            _playerGridTimer = null;
        }

        private void Unload()
        {
            if (_config != null &&
                _config.Developer != null &&
                DiagnosticsEnabled)
            {
                LogInformation("Lifecycle",
                    "Unloading. ServerReady=" + _serverReady +
                    ", LastDesiredState=" +
                    (_lastDesiredState.HasValue
                        ? (_lastDesiredState.Value ? "ON" : "OFF")
                        : "Unknown") + ".");
            }

            _serverReady = false;
            _commandQueueTimer?.Destroy();
            _commandQueueTimer = null;
            DestroyTimers();
            HideOwnedGameTip();

            // Rogue-owned player-grid access points are non-saving runtime entities.
            RogueGridPowerPlayerService.Shutdown();

            // Controlled test entities are deliberately non-persistent and must never
            // survive a plugin reload/unload.
            RogueGridPowerControlledTest.Shutdown();

            if (_gridPower != null)
                _gridPower.GridStateChanged -= OnGridStateChanged;

            _ladderPoleCache.Clear();
            _ladderPoleCacheUtc = DateTime.MinValue;
            _lastDesiredState = null;
        }

        #endregion
    }
}
