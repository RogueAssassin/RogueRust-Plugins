using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Ext.RogueRust;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using System;
using System.Collections.Generic;
using System.IO;

namespace Oxide.Plugins
{
    [Info("RogueRustGridPower", "RogueAssassin", "2.0.2")]
    [Description("RogueRust GridPower: automatic world streetlights plus output control for Rust's native power-grid access points.")]
    public sealed class RogueRustGridPower : RogueRustPlugin
    {
        #region Constants

        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 0, 2);
        private static readonly VersionNumber MinimumBackend = new VersionNumber(4, 4, 56);

        [RoguePermission]
        private const string PermissionAdmin = "roguerustgridpower.admin";

        private const string LadderPrefab = "assets/prefabs/building/ladder.wall.wood/ladder.wooden.wall.prefab";
        private static int _ladderRaycastMask = -1;

        #endregion

        #region Configuration

        private PluginConfig _config;

        private sealed class PluginConfig
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Street Lights", Order = 20)]
            public StreetLightSettings StreetLights = new StreetLightSettings();

            [JsonProperty("Native Power Grid", Order = 30)]
            public NativeGridSettings NativeGrid = new NativeGridSettings();

            [JsonProperty("Native Pole Transformers", Order = 32)]
            public PoleTransformerSettings PoleTransformers = new PoleTransformerSettings();

            [JsonProperty("Power Pole Ladders", Order = 35)]
            public LadderSettings Ladders = new LadderSettings();

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

        private sealed class NativeGridSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = false;

            [JsonProperty("Output Mode (Vanilla, Convars, Fixed)")]
            public string Mode = "Convars";

            [JsonProperty("Convars: Base Output (-1 = keep server value)")]
            public int BaseOutput = -1;

            [JsonProperty("Convars: Maximum Output (-1 = keep server value)")]
            public int MaxOutput = 600;

            [JsonProperty("Override Power Plant Requirement")]
            public bool OverridePowerPlantRequirement = false;

            [JsonProperty("Fixed: Output")]
            public int FixedOutput = 100;

            [JsonProperty("Only While Grid Online (follows street light hours)")]
            public bool FixedRequiresGridOnline = false;

            [JsonProperty("Respect powergrid.enabled")]
            public bool RespectPowergridDisabled = true;
        }

        private sealed class PoleTransformerSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Density Percent (share of native poles with a transformer)")]
            public int DensityPercent = 100;

            [JsonProperty("Selection Seed")]
            public int SelectionSeed = 0;

            [JsonProperty("Transformers Fitted Per Frame")]
            public int SpawnsPerFrame = 4;
        }

        private sealed class LadderSettings
        {
            [JsonProperty("Allow Wooden Ladders On Power Poles")]
            public bool Enabled = true;

            [JsonProperty("Pole Detection Radius")]
            public float DetectionRadius = 4f;

            [JsonProperty("Show Placement GameTip")]
            public bool ShowGameTip = true;

            [JsonProperty("Placement GameTip Duration Seconds")]
            public float GameTipDurationSeconds = 2.5f;

            [JsonProperty("Placement Success Message")]
            public string SuccessMessage = "Ladder attached to the power pole.";
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

            [JsonProperty("Street Light Frame Budget Milliseconds")]
            public double StreetlightFrameBudgetMs = 1.0d;

            [JsonProperty("Street Light Fixtures Per Frame (0 = all at once)")]
            public int StreetlightFixturesPerFrame = 12;

            [JsonProperty("Native Grid Refresh Budget Milliseconds")]
            public double NativeRefreshBudgetMs = 1.0d;

            [JsonProperty("Native Grid Access Points Refreshed Per Frame")]
            public int NativeRefreshPerFrame = 32;
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
                // Read raw JSON first: typed deserialization fills defaults in memory, which would
                // hide sections missing from an older config. Keys no longer used are dropped on save.
                JObject raw = Config.ReadObject<JObject>();
                bool missingSections =
                    raw == null ||
                    raw["General Settings"] == null ||
                    raw["Street Lights"] == null ||
                    raw["Native Power Grid"] == null ||
                    raw["Native Pole Transformers"] == null ||
                    raw["Power Pole Ladders"] == null ||
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

                if (_config.Version != CurrentVersion)
                {
                    LogInformation("Configuration", "Configuration migrated from v" + loadedVersion + " to v" + CurrentVersion + ".");
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
            if (_config.NativeGrid == null) { _config.NativeGrid = new NativeGridSettings(); changed = true; }
            if (_config.Ladders == null) { _config.Ladders = new LadderSettings(); changed = true; }
            if (_config.PoleTransformers == null) { _config.PoleTransformers = new PoleTransformerSettings(); changed = true; }
            changed |= ClampInt(ref _config.PoleTransformers.DensityPercent, 0, 100);
            changed |= ClampInt(ref _config.PoleTransformers.SpawnsPerFrame, 1, 64);
            if (_config.Notifications == null) { _config.Notifications = new NotificationSettings(); changed = true; }
            if (_config.Performance == null) { _config.Performance = new PerformanceSettings(); changed = true; }
            if (_config.Developer == null) { _config.Developer = new DeveloperSettings(); changed = true; }

            RogueNativeGridMode mode;
            if (!TryParseMode(_config.NativeGrid.Mode, out mode))
            {
                PrintWarning("Unknown native grid output mode '" + _config.NativeGrid.Mode + "'; using Convars.");
                mode = RogueNativeGridMode.Convars;
            }
            string modeName = mode.ToString();
            if (!string.Equals(modeName, _config.NativeGrid.Mode, StringComparison.Ordinal)) { _config.NativeGrid.Mode = modeName; changed = true; }

            changed |= ClampInt(ref _config.NativeGrid.BaseOutput, -1, 100000);
            changed |= ClampInt(ref _config.NativeGrid.MaxOutput, -1, 100000);
            changed |= ClampInt(ref _config.NativeGrid.FixedOutput, 0, 100000);
            if (_config.NativeGrid.BaseOutput >= 0 && _config.NativeGrid.MaxOutput >= 0 && _config.NativeGrid.MaxOutput < _config.NativeGrid.BaseOutput)
            {
                _config.NativeGrid.MaxOutput = _config.NativeGrid.BaseOutput;
                changed = true;
            }

            changed |= ClampFloat(ref _config.Ladders.DetectionRadius, 1f, 8f);
            changed |= ClampFloat(ref _config.Ladders.GameTipDurationSeconds, 1f, 10f);

            changed |= ClampFloat(ref _config.StreetLights.TurnOnHour, 0f, 23.99f);
            changed |= ClampFloat(ref _config.StreetLights.TurnOffHour, 0f, 23.99f);
            changed |= ClampInt(ref _config.StreetLights.DensityPercent, 0, 100);

            changed |= ClampFloat(ref _config.Performance.CheckIntervalSeconds, 5f, 3600f);
            changed |= ClampFloat(ref _config.Performance.RefreshCacheMinutes, 1f, 1440f);
            changed |= ClampDouble(ref _config.Performance.StreetlightFrameBudgetMs, 0d, 8d);
            changed |= ClampInt(ref _config.Performance.StreetlightFixturesPerFrame, 0, 256);
            changed |= ClampDouble(ref _config.Performance.NativeRefreshBudgetMs, 0d, 8d);
            changed |= ClampInt(ref _config.Performance.NativeRefreshPerFrame, 1, 512);

            changed |= ClampFloat(ref _config.Notifications.DurationSeconds, 1f, 30f);

            return changed;
        }

        private static bool ClampInt(ref int value, int min, int max)
        {
            int clamped = Math.Max(min, Math.Min(max, value));
            if (clamped == value) return false;
            value = clamped;
            return true;
        }

        private static bool ClampFloat(ref float value, float min, float max)
        {
            float clamped = Math.Max(min, Math.Min(max, value));
            if (Math.Abs(clamped - value) < 0.0001f) return false;
            value = clamped;
            return true;
        }

        private static bool ClampDouble(ref double value, double min, double max)
        {
            double clamped = double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
            if (Math.Abs(clamped - value) < 0.0001d) return false;
            value = clamped;
            return true;
        }

        private static bool TryParseMode(string value, out RogueNativeGridMode mode)
        {
            mode = RogueNativeGridMode.Vanilla;
            if (string.IsNullOrWhiteSpace(value)) return false;
            try
            {
                mode = (RogueNativeGridMode)Enum.Parse(typeof(RogueNativeGridMode), value.Trim(), true);
                return Enum.IsDefined(typeof(RogueNativeGridMode), mode);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Logging Policy

        // Routine operational logging stays light. Detailed diagnostics are emitted only when
        // Developer Settings -> Log Diagnostics is enabled.
        private bool DiagnosticsEnabled =>
            _config != null &&
            _config.Developer != null &&
            _config.Developer.LogDiagnostics;

        private void DiagnosticLog(string area, string message)
        {
            if (DiagnosticsEnabled)
                LogInformation(area, message);
        }

        private void DiagnosticPuts(string message)
        {
            if (DiagnosticsEnabled)
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
                ["Command.Status"] = "RogueRustGridPower v{0} | Backend=RogueRust {1} | WorldHour={2:0.00} | Desired={3} | {4}",
                ["Command.Refresh"] = "GridPower refresh complete. Discovered={0}. {1}",
                ["Command.Apply"] = "GridPower configuration reapplied. {0} | {1}",
                ["Command.Grid"] = "Public Grid | State={0} | Cell={1} | PoweredHere={2} | GridStateChanges={3}",
                ["Command.Native"] = "{0}",
                ["Command.NativeRefresh"] = "Native grid re-indexed and policy reapplied. {0}",
                ["Command.NativeInspect"] = "{0}",
                ["Command.NativePoles"] = "{0}",
                ["Command.Debug"] = "GridPower debug markers drawn for 30 seconds within {0:0}m. AccessPoints={1} (wired {2}) | Streetlights={3}",
                ["Command.Target"] = "Target inspector | Name={0} | Path={1} | Position={2}. Full hierarchy written to server console.",
                ["Command.TargetNone"] = "No world object was found under your cursor within {0:0}m.",
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
        private Timer _gameTipTimer;
        private Timer _commandQueueTimer;
        private bool? _lastDesiredState;
        private bool _serverReady;
        private bool _gameTipVisible;
        private bool _nativeGridApplied;

        #endregion

        #region Initialization

        private void Init()
        {
            // Only pay for OnPlayerInput (every input tick, every player) when ladders are enabled.
            Unsubscribe(nameof(OnPlayerInput));

            LogInformation("Lifecycle", Name + " v" + CurrentVersion + " initialized using RogueRust services.");

            try
            {
                _gridPower = Rogue.GridPower;
                if (_gridPower != null)
                    _gridPower.GridStateChanged += OnGridStateChanged;
            }
            catch (Exception ex)
            {
                LogError("GridPower", "RogueRust GridPower backend could not be acquired: " + ex.Message);
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
                LogError("GridPower", "RogueRust GridPower backend is unavailable. Install RogueRust " + MinimumBackend + " or newer and restart the server.");
                return;
            }

            if (RogueRustExtension.CurrentVersion < MinimumBackend)
                LogWarning("GridPower", "RogueRust " + RogueRustExtension.CurrentVersion + " is older than the required " + MinimumBackend + "; native grid features may be unavailable.");

            ApplyBackendConfiguration();

            // Pump RogueRust's shared cooperative command queue at a conservative rate.
            _commandQueueTimer?.Destroy();
            _commandQueueTimer = timer.Every(0.35f, () =>
            {
                if (_serverReady)
                    RogueCommandQueueService.Pump(1);
            });

            if (_config.Ladders.Enabled)
                Subscribe(nameof(OnPlayerInput));

            // Keep OnServerInitialized light; streetlights and native grid policy start next slice.
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

                ApplyNativeGrid();
                ApplyPoleTransformers();
            });
        }

        #endregion

        #region RogueRust Commands

        [RogueCommand(
            "rrgrid.status",
            Aliases = new[] { "gridpower.status" },
            Description = "Shows RogueRust GridPower streetlight and native grid status.",
            Usage = "rrgrid.status",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandStatus(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            float hour = GetWorldHour();
            bool desired = IsNightWindow(hour, _config.StreetLights.TurnOnHour, _config.StreetLights.TurnOffHour);

            return RogueCommandResult.Ok(Localize(
                "Command.Status",
                context.NativePlayer,
                CurrentVersion,
                RogueRustExtension.CurrentVersion,
                hour,
                desired ? "ON" : "OFF",
                _gridPower.GetStreetlightStatus() + "\n" + _gridPower.GetNativeGridStatus() + "\n" + _gridPower.GetNativePoleTransformerStatus()));
        }

        [RogueCommand(
            "rrgrid.refresh",
            Aliases = new[] { "gridpower.refresh" },
            Description = "Rebuilds the streetlight references and reapplies the current GridPower state.",
            Usage = "rrgrid.refresh",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandRefresh(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckStreetlights();
            if (unavailable != null)
                return unavailable;

            ApplyBackendConfiguration();
            int discovered = _gridPower.RefreshStreetlights(true);
            _lastDesiredState = null;
            EvaluateStreetlights(true, false);

            return RogueCommandResult.Ok(Localize("Command.Refresh", context.NativePlayer, discovered, _gridPower.GetStreetlightStatus()));
        }

        [RogueCommand(
            "rrgrid.apply",
            Aliases = new[] { "gridpower.apply" },
            Description = "Reapplies configuration: streetlight density/streaming, the current world-time streetlight state and the native grid output policy.",
            Usage = "rrgrid.apply",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandApply(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            ApplyBackendConfiguration();
            if (_config.StreetLights.Enabled)
            {
                _lastDesiredState = null;
                EvaluateStreetlights(true, false);
            }
            ApplyNativeGrid();
            ApplyPoleTransformers();

            return RogueCommandResult.Ok(Localize("Command.Apply", context.NativePlayer, _gridPower.GetStreetlightStatus(), _gridPower.GetNativeGridStatus()));
        }

        [RogueCommand(
            "rrgrid.grid",
            Aliases = new[] { "gridpower.grid" },
            Description = "Shows the public-grid state and the caller's backend grid cell.",
            Usage = "rrgrid.grid",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandGrid(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            BasePlayer player = context.NativePlayer;
            UnityEngine.Vector3 position = player != null ? player.transform.position : UnityEngine.Vector3.zero;

            return RogueCommandResult.Ok(Localize(
                "Command.Grid",
                player,
                _gridPower.GridOnline ? "ONLINE" : "OFFLINE",
                _gridPower.GetGridCell(position),
                _gridPower.IsPowered(position) ? "YES" : "NO",
                _gridPower.GridStateChanges));
        }

        [RogueCommand(
            "rrgrid.native",
            Aliases = new[] { "gridpower.native" },
            Description = "Shows Rust native power-grid access points, wiring and the active output policy.",
            Usage = "rrgrid.native",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandNative(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            return RogueCommandResult.Ok(Localize("Command.Native", context.NativePlayer, _gridPower.GetNativeGridStatus()));
        }

        [RogueCommand(
            "rrgrid.native.refresh",
            Aliases = new[] { "gridpower.native.refresh" },
            Description = "Re-indexes native access points and re-pushes the configured output policy in frame-budgeted batches.",
            Usage = "rrgrid.native.refresh",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandNativeRefresh(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            _gridPower.RefreshNativeAccessPoints();
            ApplyNativeGrid();
            return RogueCommandResult.Ok(Localize("Command.NativeRefresh", context.NativePlayer, _gridPower.GetNativeGridStatus()));
        }

        [RogueCommand(
            "rrgrid.native.inspect",
            Aliases = new[] { "gridpower.native.inspect" },
            Description = "Inspects the nearest native access point: saving flag, wired slots, current energy and maximum output.",
            Usage = "rrgrid.native.inspect",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandNativeInspect(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            const float radius = 30f;
            string result = _gridPower.InspectNativeAccessPoint(player.transform.position, radius);

            PowergridIOAccessPoint accessPoint;
            if (_gridPower.TryFindNearestAccessPoint(player.transform.position, radius, out accessPoint) && accessPoint != null)
            {
                UnityEngine.Vector3 p = accessPoint.transform.position;
                player.SendConsoleCommand("ddraw.sphere", 20f, UnityEngine.Color.magenta, p, 0.6f);
                player.SendConsoleCommand("ddraw.text", 20f, UnityEngine.Color.magenta, p + UnityEngine.Vector3.up, "NATIVE ACCESS POINT");
            }

            DiagnosticPuts("[RogueRust/GridPower Native] " + result);
            return RogueCommandResult.Ok(Localize("Command.NativeInspect", player, result));
        }

        [RogueCommand(
            "rrgrid.native.poles",
            Aliases = new[] { "gridpower.native.poles" },
            Description = "Shows native pole transformer fitting. Add 'apply' to re-run it with the current config (fills poles that are missing one; never adds a second).",
            Usage = "rrgrid.native.poles [apply]",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = true)]
        private RogueCommandResult CommandNativePoles(RogueCommandContext context, string[] commandArgs)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            bool apply = commandArgs != null && commandArgs.Length > 0 &&
                         string.Equals(commandArgs[0], "apply", StringComparison.OrdinalIgnoreCase);
            if (apply)
                ApplyPoleTransformers();
            return RogueCommandResult.Ok(Localize("Command.NativePoles", context.NativePlayer, _gridPower.GetNativePoleTransformerStatus()));
        }

        [RogueCommand(
            "rrgrid.debug",
            Aliases = new[] { "gridpower.debug" },
            Description = "Draws temporary admin-only markers for native access points and streetlight fixtures within 200m.",
            Usage = "rrgrid.debug",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandDebug(RogueCommandContext context)
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            const float radius = 200f;
            const float duration = 30f;
            float radiusSqr = radius * radius;
            UnityEngine.Vector3 origin = player.transform.position;
            int accessPoints = 0;
            int wired = 0;
            int streetlights = 0;

            try
            {
                // Admin-only, on demand: a typed lookup of ~1k access points is cheap.
                PowergridIOAccessPoint[] found = UnityEngine.Object.FindObjectsByType<PowergridIOAccessPoint>(UnityEngine.FindObjectsSortMode.None);
                for (int i = 0; i < found.Length; i++)
                {
                    PowergridIOAccessPoint ap = found[i];
                    if (ap == null || ap.IsDestroyed)
                        continue;

                    UnityEngine.Vector3 pos = ap.transform.position;
                    if ((pos - origin).sqrMagnitude > radiusSqr)
                        continue;

                    bool isWired = IsWired(ap);
                    if (isWired) wired++;
                    accessPoints++;
                    DrawDebugMarker(player, pos, isWired ? UnityEngine.Color.green : UnityEngine.Color.cyan, isWired ? "ACCESS POINT (WIRED)" : "ACCESS POINT", duration);
                }

                string streetlightPath = _gridPower.ReferenceFilePath;
                if (File.Exists(streetlightPath))
                {
                    JArray fixtures = JObject.Parse(File.ReadAllText(streetlightPath))["streetlights"] as JArray;
                    if (fixtures != null)
                    {
                        foreach (JToken entry in fixtures)
                        {
                            UnityEngine.Vector3 pos = new UnityEngine.Vector3(
                                entry.Value<float?>("x") ?? 0f,
                                entry.Value<float?>("y") ?? 0f,
                                entry.Value<float?>("z") ?? 0f);
                            if ((pos - origin).sqrMagnitude > radiusSqr)
                                continue;

                            DrawDebugMarker(player, pos, UnityEngine.Color.yellow, "STREETLIGHT", duration);
                            streetlights++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError("GridPower debug visualization failed: " + ex.Message);
                return RogueCommandResult.Fail("GridPower debug visualization failed. Check the server console.");
            }

            return RogueCommandResult.Ok(Localize("Command.Debug", player, radius, accessPoints, wired, streetlights));
        }

        [RogueCommand(
            "rrgrid.target",
            Aliases = new[] { "gridpower.target", "rrgrid.cursor" },
            Description = "Inspects the exact world object under the admin cursor without modifying it.",
            Usage = "rrgrid.target",
            Category = "GridPower",
            Permission = PermissionAdmin,
            AllowChat = true,
            AllowConsole = false)]
        private RogueCommandResult CommandTarget(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail(Message("Error.PlayerOnly"));

            const float maxDistance = 250f;
            UnityEngine.Ray ray = new UnityEngine.Ray(player.eyes.position, player.eyes.HeadForward());
            UnityEngine.RaycastHit hit;
            if (!UnityEngine.Physics.Raycast(ray, out hit, maxDistance, UnityEngine.Physics.DefaultRaycastLayers, UnityEngine.QueryTriggerInteraction.Collide) || hit.transform == null)
                return RogueCommandResult.Fail(Localize("Command.TargetNone", player, maxDistance));

            UnityEngine.Transform target = hit.transform;
            string path = BuildTransformPath(target);
            DiagnosticPuts("[RogueRust/GridPower Target] ===== CURSOR TARGET =====");
            DiagnosticPuts("[RogueRust/GridPower Target] Hit=" + target.name + " | Path=" + path + " | Position=" + hit.point +
                           " | Collider=" + (hit.collider != null ? hit.collider.GetType().Name : "<none>"));

            UnityEngine.Transform current = target;
            int level = 0;
            while (current != null && level < 16)
            {
                BaseEntity entity = current.GetComponent<BaseEntity>();
                DiagnosticPuts("[RogueRust/GridPower Target] Level=" + level +
                               " | Name=" + current.name +
                               " | Components=" + GetComponentTypeList(current.gameObject) +
                               " | EntityType=" + (entity != null ? entity.GetType().Name : "<none>") +
                               " | Prefab=" + (entity != null ? (entity.PrefabName ?? "<none>") : "<none>") +
                               " | Position=" + current.position);
                current = current.parent;
                level++;
            }

            player.SendConsoleCommand("ddraw.sphere", 15f, UnityEngine.Color.cyan, hit.point, 0.25f);
            player.SendConsoleCommand("ddraw.text", 15f, UnityEngine.Color.cyan, hit.point + UnityEngine.Vector3.up * 0.5f, target.name);

            return RogueCommandResult.Ok(Localize("Command.Target", player, target.name, path, hit.point));
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
                "RogueRustGridPower v" + CurrentVersion + " commands\n" +
                "rrgrid.status - streetlight + native grid status\n" +
                "rrgrid.apply - reapply configuration\n" +
                "rrgrid.refresh - rebuild streetlight references\n" +
                "rrgrid.grid - public grid state at your position\n" +
                "rrgrid.native - native access points, wiring, output policy\n" +
                "rrgrid.native.refresh - re-index and re-push output\n" +
                "rrgrid.native.inspect - nearest access point details (in-game)\n" +
                "rrgrid.native.poles [apply] - transformers on native poles (density, fill missing)\n" +
                "rrgrid.debug - markers within 200m (in-game)\n" +
                "rrgrid.target - inspect the object under your cursor (in-game)\n" +
                "Chat: /rrgrid ... | F1/server/RCON: rrgrid....";
            return RogueCommandResult.Ok(help);
        }

        private RogueCommandResult CheckBackend()
        {
            if (!_config.General.Enabled)
                return RogueCommandResult.Fail(Message("Error.PluginDisabled"));

            if (_gridPower == null)
                return RogueCommandResult.Fail(Message("Error.BackendUnavailable"));

            return null;
        }

        private RogueCommandResult CheckStreetlights()
        {
            RogueCommandResult unavailable = CheckBackend();
            if (unavailable != null)
                return unavailable;

            if (!_config.StreetLights.Enabled)
                return RogueCommandResult.Fail(Message("Error.StreetlightsDisabled"));

            return null;
        }

        #endregion

        #region Permissions

        // Permission registration and command permission checks are owned by RogueRust.
        // roguerustgridpower.admin is declared through [RoguePermission] and RogueCommand metadata.

        #endregion

        #region Hooks

        // Subscribed only while ladders are enabled (see Init / OnServerInitialized).
        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (!_serverReady || player == null || input == null || !input.WasJustPressed(BUTTON.FIRE_PRIMARY))
                return;

            Item item = player.GetActiveItem();
            if (item == null || item.info == null || !string.Equals(item.info.shortname, "ladder.wooden.wall", StringComparison.OrdinalIgnoreCase))
                return;

            if (!(player.GetHeldEntity() is Planner) || _gridPower == null || !_config.Ladders.Enabled)
                return;

            if (_ladderRaycastMask < 0)
                _ladderRaycastMask = UnityEngine.LayerMask.GetMask("World", "Deployed");

            UnityEngine.RaycastHit hit;
            if (!UnityEngine.Physics.Raycast(player.eyes.BodyRay(), out hit, 5f, _ladderRaycastMask, UnityEngine.QueryTriggerInteraction.Ignore))
                return;

            // Native access points sit on their poles, so the backend's O(nearby) access-point
            // index replaces the old per-click reference-file parse.
            UnityEngine.Vector3 polePosition;
            int poleId;
            if (!_gridPower.TryFindNearestPlayerPole(hit.point, _config.Ladders.DetectionRadius, out polePosition, out poleId))
                return;

            string colliderName = hit.collider != null ? (hit.collider.name ?? string.Empty) : string.Empty;
            if (colliderName.IndexOf("powerline_pole", StringComparison.OrdinalIgnoreCase) < 0)
            {
                DiagnosticPuts("[RogueRust/GridPower Ladder] Near pole but ray hit '" + colliderName + "'; native placement left unchanged.");
                return;
            }

            BaseEntity ladder = SpawnPoleLadder(player, item, hit);
            if (ladder == null)
            {
                ShowPlayerGameTip(player, "GridPower could not attach the ladder at this position.", 2.5f);
                return;
            }

            item.UseItem(1);
            if (_config.Ladders.ShowGameTip)
                ShowPlayerGameTip(player, _config.Ladders.SuccessMessage, _config.Ladders.GameTipDurationSeconds);

            DiagnosticPuts("[RogueRust/GridPower Ladder] Placed wooden ladder | Player=" + player.UserIDString +
                           " | Pole=" + poleId + " | Collider=" + colliderName + " | Position=" + ladder.transform.position);
        }

        private BaseEntity SpawnPoleLadder(BasePlayer player, Item item, UnityEngine.RaycastHit hit)
        {
            UnityEngine.Vector3 normal = hit.normal;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.001f)
                normal = player.transform.position - hit.point;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.001f)
                normal = player.eyes.BodyRay().direction;
            normal.y = 0f;
            normal.Normalize();

            // Keep the ladder just off the pole surface so its collider does not begin embedded.
            UnityEngine.Vector3 position = hit.point + normal * 0.08f;
            UnityEngine.Quaternion rotation = UnityEngine.Quaternion.LookRotation(-normal, UnityEngine.Vector3.up);

            BaseEntity entity = GameManager.server.CreateEntity(LadderPrefab, position, rotation, true);
            if (entity == null)
                return null;

            entity.OwnerID = player.userID;
            entity.skinID = item.skin;
            entity.Spawn();
            entity.SendMessage("SetDeployedBy", player, UnityEngine.SendMessageOptions.DontRequireReceiver);

            Interface.CallHook("OnEntityBuilt", player.GetHeldEntity() as Planner, entity.gameObject);
            return entity;
        }

        private void OnGridStateChanged(bool online)
        {
            DiagnosticLog("GridPower", Localize("Grid.StateChanged", null, online ? "ONLINE" : "OFFLINE"));
        }

        #endregion

        #region Core Logic

        private void StartStreetlightController()
        {
            DestroyTimers();

            int discovered = _gridPower.RefreshStreetlights(false);
            DiagnosticLog("GridPower",
                "Controller ready. Discovered=" + discovered +
                ", Density=" + _gridPower.StreetlightDensity + "%" +
                ", Selected=" + _gridPower.SelectedStreetlights + ".");

            // Startup/reload applies state silently. GameTips are reserved for a real
            // live transition across the configured day/night boundary.
            EvaluateStreetlights(true, false);

            _streetlightTimer = timer.Every(_config.Performance.CheckIntervalSeconds, () => EvaluateStreetlights(false, true));
            _refreshTimer = timer.Every(_config.Performance.RefreshCacheMinutes * 60f, RefreshStreetlightCache);
        }

        private void RefreshStreetlightCache()
        {
            if (!CanOperateStreetlights())
                return;

            int before = _gridPower.TrackedStreetlights;
            int after = _gridPower.RefreshStreetlights(false);
            if (before != after)
                DiagnosticLog("GridPower", "Streetlight cache refreshed. Discovered=" + after + ", Previous=" + before + ".");

            // Refresh can rebuild DLL-owned light entities but must not repeat the player notification.
            EvaluateStreetlights(true, false);
        }

        private void EvaluateStreetlights(bool force, bool allowNotification)
        {
            if (!CanOperateStreetlights())
                return;

            float hour = GetWorldHour();
            bool shouldBeOn = IsNightWindow(hour, _config.StreetLights.TurnOnHour, _config.StreetLights.TurnOffHour);

            if (!force && _lastDesiredState.HasValue && _lastDesiredState.Value == shouldBeOn)
                return;

            bool stateChanged = _lastDesiredState.HasValue && _lastDesiredState.Value != shouldBeOn;

            // Streamed by the backend: dusk/dawn spreads entity work across frames.
            int changed = _gridPower.SetStreetlights(shouldBeOn, false);
            _lastDesiredState = shouldBeOn;

            if (force || stateChanged || changed > 0)
                DiagnosticLog("GridPower",
                    "Street lights " + (shouldBeOn ? "ON" : "OFF") + " at world hour " + hour.ToString("0.00") + ". " +
                    _gridPower.GetStreetlightStatus() + " | Changed=" + changed);

            if (allowNotification && stateChanged)
                ShowStateGameTip(shouldBeOn);
        }

        private void ApplyBackendConfiguration()
        {
            if (_gridPower == null)
                return;

            _gridPower.ConfigureStreaming(_config.Performance.StreetlightFrameBudgetMs, _config.Performance.StreetlightFixturesPerFrame);
            _gridPower.ConfigureStreetlights(_config.StreetLights.DensityPercent, _config.StreetLights.SelectionSeed);
        }

        /// <summary>
        /// Gives the configured share of Rust's native poles a transformer on the pole's own mount slot.
        /// Poles that already have one are skipped; lowering the density never removes any.
        /// </summary>
        private void ApplyPoleTransformers()
        {
            if (_gridPower == null)
                return;
            if (RogueRustExtension.CurrentVersion < MinimumBackend)
            {
                LogWarning("GridPower", "Native pole transformers need RogueRust " + MinimumBackend + " or newer.");
                return;
            }

            PoleTransformerSettings settings = _config.PoleTransformers;
            string result = _gridPower.ConfigureNativePoleTransformers(settings.Enabled, settings.DensityPercent, settings.SelectionSeed, settings.SpawnsPerFrame);
            LogInformation("GridPower", result);
        }

        private void ApplyNativeGrid()
        {
            if (_gridPower == null)
                return;

            NativeGridSettings settings = _config.NativeGrid;
            if (!_config.General.Enabled || !settings.Enabled)
            {
                // Only touch the native grid when this plugin previously changed it.
                if (_nativeGridApplied)
                {
                    _gridPower.ConfigureNativeGrid(new RogueNativeGridOptions { Mode = RogueNativeGridMode.Vanilla });
                    _nativeGridApplied = false;
                }
                return;
            }

            RogueNativeGridMode mode;
            TryParseMode(settings.Mode, out mode);

            _gridPower.ConfigureNativeGrid(new RogueNativeGridOptions
            {
                Mode = mode,
                BaseOutput = settings.BaseOutput,
                MaxOutput = settings.MaxOutput,
                FixedOutput = settings.FixedOutput,
                OverridePowerPlantRequirement = settings.OverridePowerPlantRequirement,
                FixedRequiresGridOnline = settings.FixedRequiresGridOnline,
                RespectPowergridDisabled = settings.RespectPowergridDisabled,
                RefreshFrameBudgetMilliseconds = _config.Performance.NativeRefreshBudgetMs,
                MaxRefreshesPerFrame = _config.Performance.NativeRefreshPerFrame
            });
            _nativeGridApplied = mode != RogueNativeGridMode.Vanilla;

            bool pinned = mode == RogueNativeGridMode.Fixed ||
                          (mode == RogueNativeGridMode.Convars && settings.OverridePowerPlantRequirement);
            if (pinned && settings.FixedRequiresGridOnline && !_config.StreetLights.Enabled)
                LogWarning("GridPower", "Output is set to follow the grid-online window, but street lights (which drive it) are disabled. Access points will deliver 0.");
            if (settings.OverridePowerPlantRequirement && mode != RogueNativeGridMode.Convars)
                LogWarning("GridPower", "'Override Power Plant Requirement' only applies in Convars mode (Fixed mode never needs the power plant).");

            LogInformation("GridPower", "Native grid policy applied :: " + _gridPower.GetNativeGridStatus());
        }

        private bool CanOperateStreetlights()
        {
            return _serverReady &&
                   _config != null &&
                   _config.General.Enabled &&
                   _config.StreetLights.Enabled &&
                   _gridPower != null;
        }

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

            string message = streetlightsOn ? _config.Notifications.NightMessage : _config.Notifications.DawnMessage;
            if (string.IsNullOrWhiteSpace(message))
                return;

            HideOwnedGameTip();

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("gametip.showgametip", message);
            }

            _gameTipVisible = true;
            _gameTipTimer = timer.Once(_config.Notifications.DurationSeconds, HideOwnedGameTip);
        }

        private void HideOwnedGameTip()
        {
            _gameTipTimer?.Destroy();
            _gameTipTimer = null;

            if (!_gameTipVisible)
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    player.SendConsoleCommand("gametip.hidegametip");
            }

            _gameTipVisible = false;
        }

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

        private static bool IsWired(IOEntity io)
        {
            return HasConnection(io.inputs) || HasConnection(io.outputs);
        }

        private static bool HasConnection(IOEntity.IOSlot[] slots)
        {
            if (slots == null)
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null && slots[i].IsConnected())
                    return true;
            }

            return false;
        }

        private static void DrawDebugMarker(BasePlayer player, UnityEngine.Vector3 position, UnityEngine.Color color, string label, float duration)
        {
            UnityEngine.Vector3 marker = position + UnityEngine.Vector3.up * 2f;
            player.SendConsoleCommand("ddraw.sphere", duration, color, marker, 0.45f);
            player.SendConsoleCommand("ddraw.text", duration, color, marker + UnityEngine.Vector3.up * 0.75f, label);
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
        }

        private void Unload()
        {
            DiagnosticLog("Lifecycle",
                "Unloading. ServerReady=" + _serverReady + ", LastDesiredState=" +
                (_lastDesiredState.HasValue ? (_lastDesiredState.Value ? "ON" : "OFF") : "Unknown") + ".");

            _serverReady = false;
            _commandQueueTimer?.Destroy();
            _commandQueueTimer = null;
            DestroyTimers();
            HideOwnedGameTip();

            // Return Rust's native grid to its own behaviour (restores convars, disables the
            // fixed-output override). Native access points and their wiring are untouched.
            if (_gridPower != null && _nativeGridApplied)
            {
                try { _gridPower.ConfigureNativeGrid(new RogueNativeGridOptions { Mode = RogueNativeGridMode.Vanilla }); }
                catch (Exception ex) { PrintError("Failed to restore native grid behaviour: " + ex.Message); }
                _nativeGridApplied = false;
            }

            if (_gridPower != null)
                _gridPower.GridStateChanged -= OnGridStateChanged;

            _lastDesiredState = null;
        }

        #endregion
    }
}
