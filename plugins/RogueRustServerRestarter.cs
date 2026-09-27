using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using Oxide.Game.Rust.Cui;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RogueRustServerRestarter", "RogueAssassin", "2.1.0")]
    [Description("Schedules safe server restarts with RogueRust runtime protection, chat, game-tip, and GUI warnings.")]
    public sealed class RogueRustServerRestarter : RogueRustPlugin
    {
        private const string UiName = "ServerRestarter.UI";
        private const ulong ChatIconSteamId = 76561198641042212UL;
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);
        private Configuration _config;

        [RoguePermission]
        private const string UsePermission = "roguerustserverrestarter.use";

        [RoguePermission]
        private const string AdminPermission = "roguerustserverrestarter.admin";
        private readonly List<TimeSpan> _restartTimes = new List<TimeSpan>();
        private readonly HashSet<ulong> _testCountdowns = new HashSet<ulong>();
        private int _scheduleGeneration;
        private bool _countdownRunning;
        private DateTime _restartTime;
        private bool _hasScheduledRestart;
        private bool _isRestarting;

        private class Configuration
        {
            [JsonProperty("Schedule Settings", Order = 10)]
            public ScheduleSettings Schedule = new ScheduleSettings();

            [JsonProperty("Warning Settings", Order = 20)]
            public WarningSettings Warnings = new WarningSettings();

            [JsonProperty("UI Settings", Order = 30)]
            public UiSettings UI = new UiSettings();

            [JsonProperty("Permission Settings", Order = 40)]
            public PermissionSettings Permissions = new PermissionSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            [JsonIgnore] public List<string> RestartTimes { get { return Schedule.RestartTimes; } set { Schedule.RestartTimes = value; } }
            [JsonIgnore] public string RestartCommand { get { return Schedule.RestartCommand; } set { Schedule.RestartCommand = value; } }
            [JsonIgnore] public List<int> WarningMinutes { get { return Warnings.WarningMinutes; } set { Warnings.WarningMinutes = value; } }
            [JsonIgnore] public Dictionary<int, string> WarningMessages { get { return Warnings.WarningMessages; } set { Warnings.WarningMessages = value; } }
            [JsonIgnore] public bool UseChat { get { return Warnings.UseChat; } set { Warnings.UseChat = value; } }
            [JsonIgnore] public float GametipDelay { get { return Warnings.GametipDelay; } set { Warnings.GametipDelay = value; } }
            [JsonIgnore] public float GametipDuration { get { return Warnings.GametipDuration; } set { Warnings.GametipDuration = value; } }
            [JsonIgnore] public bool UseGUI { get { return UI.Enabled; } set { UI.Enabled = value; } }
            [JsonIgnore] public float GUIDuration { get { return UI.Duration; } set { UI.Duration = value; } }
            [JsonIgnore] public int LiveCountdownMinutes { get { return UI.LiveCountdownMinutes; } set { UI.LiveCountdownMinutes = value; } }
            [JsonIgnore] public string GUIColor { get { return UI.PanelColor; } set { UI.PanelColor = value; } }
            [JsonIgnore] public string GUITextColor { get { return UI.TextColor; } set { UI.TextColor = value; } }
            [JsonIgnore] public Vector2 GUIAnchorMin { get { return UI.AnchorMin; } set { UI.AnchorMin = value; } }
            [JsonIgnore] public Vector2 GUIAnchorMax { get { return UI.AnchorMax; } set { UI.AnchorMax = value; } }
            [JsonIgnore] public int FontSize { get { return UI.FontSize; } set { UI.FontSize = value; } }
            [JsonIgnore] public Dictionary<string, string> RestartColors { get { return UI.RestartColors; } set { UI.RestartColors = value; } }
            [JsonIgnore] public bool GrantPermissions { get { return Permissions.GrantPermissions; } set { Permissions.GrantPermissions = value; } }
        }

        private class ScheduleSettings
        {
            [JsonProperty("Scheduled Restart Times (HH:mm, 24-hour server-local time)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RestartTimes = new List<string> { "03:00", "15:00" };

            [JsonProperty("Server Command to Execute After Saving")]
            public string RestartCommand = "restart 0";
        }

        private class WarningSettings
        {
            [JsonProperty("Warning Times in Minutes Before Restart", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> WarningMinutes = new List<int> { 60, 30, 15, 5, 1 };

            [JsonProperty("Custom Warning Messages by Minute", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<int, string> WarningMessages = new Dictionary<int, string>
            {
                [60] = "Server restart in 1 hour!",
                [30] = "Server restart in 30 minutes!",
                [15] = "Server restart in 15 minutes!",
                [5] = "Server restart in 5 minutes!",
                [1] = "Server restart in 1 minute!"
            };

            [JsonProperty("Enable Chat Messages")]
            public bool UseChat = true;

            [JsonProperty("Game Tip Delay in Seconds")]
            public float GametipDelay;

            [JsonProperty("Game Tip Duration in Seconds")]
            public float GametipDuration = 10f;
        }

        private class UiSettings
        {
            [JsonProperty("Enable GUI Overlay")]
            public bool Enabled = true;

            [JsonProperty("Non-Live Warning Duration in Seconds")]
            public float Duration = 30f;

            [JsonProperty("Start Live One-Second Countdown This Many Minutes Before Restart")]
            public int LiveCountdownMinutes = 5;

            [JsonProperty("Panel Background Color RGBA")]
            public string PanelColor = "0 0 0 0.7";

            [JsonProperty("Text Color RGBA")]
            public string TextColor = "1 1 1 1";

            [JsonProperty("Panel Minimum Anchor Position")]
            public Vector2 AnchorMin = new Vector2(0.25f, 0.9f);

            [JsonProperty("Panel Maximum Anchor Position")]
            public Vector2 AnchorMax = new Vector2(0.75f, 0.96f);

            [JsonProperty("Font Size")]
            public int FontSize = 20;

            [JsonProperty("Colors by Warning Reason", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> RestartColors = new Dictionary<string, string>
            {
                ["M_DEFAULT"] = "0 0 0 0.7",
                ["M_1"] = "0.8 0.4 0 0.7",
                ["M_2"] = "0.8 0 0 0.7"
            };
        }

        private class PermissionSettings
        {
            [JsonProperty("Automatically Grant Use/Admin Permissions to Built-In Admin Group")]
            public bool GrantPermissions = true;
        }

        private class LegacyConfiguration
        {
            [JsonProperty("Version of the config file. DO NOT CHANGE.")]
            public VersionNumber Version = new VersionNumber(2, 0, 0);
            [JsonProperty("Scheduled restart times. Format HH:mm, 24-hour server-local time.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> RestartTimes = new List<string> { "03:00", "15:00" };
            [JsonProperty("Warning times in minutes before restart.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<int> WarningMinutes = new List<int> { 60, 30, 15, 5, 1 };
            [JsonProperty("Custom warning messages by minute.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<int, string> WarningMessages = new Dictionary<int, string>();
            [JsonProperty("Server command to execute after saving.")] public string RestartCommand = "restart 0";
            [JsonProperty("Enable chat messages for warnings.")] public bool UseChat = true;
            [JsonProperty("Enable GUI overlay.")] public bool UseGUI = true;
            [JsonProperty("Duration of non-live GUI warnings in seconds.")] public float GUIDuration = 30f;
            [JsonProperty("Start the live one-second GUI countdown this many minutes before restart.")] public int LiveCountdownMinutes = 5;
            [JsonProperty("GUI panel background color RGBA.")] public string GUIColor = "0 0 0 0.7";
            [JsonProperty("GUI text color RGBA.")] public string GUITextColor = "1 1 1 1";
            [JsonProperty("GUI panel minimum anchor position.")] public Vector2 GUIAnchorMin = new Vector2(0.25f, 0.9f);
            [JsonProperty("GUI panel maximum anchor position.")] public Vector2 GUIAnchorMax = new Vector2(0.75f, 0.96f);
            [JsonProperty("GUI font size.")] public int FontSize = 20;
            [JsonProperty("Automatically grant use/admin permissions to the built-in admin group only.")] public bool GrantPermissions = true;
            [JsonProperty("Delay before showing a game tip in seconds.")] public float GametipDelay;
            [JsonProperty("Duration for game-tip display in seconds.")] public float GametipDuration = 10f;
            [JsonProperty("Custom GUI colors by warning reason.", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> RestartColors = new Dictionary<string, string>();
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Newtonsoft.Json.Linq.JObject raw = Config.ReadObject<Newtonsoft.Json.Linq.JObject>();
                bool legacy = raw != null && raw["Schedule Settings"] == null &&
                              (raw["Scheduled restart times. Format HH:mm, 24-hour server-local time."] != null ||
                               raw["Version of the config file. DO NOT CHANGE."] != null);

                if (legacy)
                {
                    LegacyConfiguration old = raw.ToObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    _config = new Configuration
                    {
                        Schedule = new ScheduleSettings
                        {
                            RestartTimes = old.RestartTimes ?? new List<string>(),
                            RestartCommand = old.RestartCommand
                        },
                        Warnings = new WarningSettings
                        {
                            WarningMinutes = old.WarningMinutes ?? new List<int>(),
                            WarningMessages = old.WarningMessages ?? new Dictionary<int, string>(),
                            UseChat = old.UseChat,
                            GametipDelay = old.Version < CurrentVersion && Math.Abs(old.GametipDelay - 60f) < 0.01f ? 0f : old.GametipDelay,
                            GametipDuration = old.GametipDuration
                        },
                        UI = new UiSettings
                        {
                            Enabled = old.UseGUI,
                            Duration = old.GUIDuration,
                            LiveCountdownMinutes = old.LiveCountdownMinutes,
                            PanelColor = old.GUIColor,
                            TextColor = old.GUITextColor,
                            AnchorMin = old.GUIAnchorMin,
                            AnchorMax = old.GUIAnchorMax,
                            FontSize = old.FontSize,
                            RestartColors = old.RestartColors ?? new Dictionary<string, string>()
                        },
                        Permissions = new PermissionSettings
                        {
                            GrantPermissions = old.GrantPermissions
                        }
                    };
                    LogInformation("Configuration", "Migrated ServerRestarter configuration to RogueRust grouped settings.");
                }
                else
                {
                    _config = raw == null ? new Configuration() : raw.ToObject<Configuration>();
                }
            }
            catch (Exception exception)
            {
                PrintError("Invalid configuration: " + exception.Message);
                _config = new Configuration();
            }

            if (_config == null) _config = new Configuration();
            if (_config.Schedule == null) _config.Schedule = new ScheduleSettings();
            if (_config.Warnings == null) _config.Warnings = new WarningSettings();
            if (_config.UI == null) _config.UI = new UiSettings();
            if (_config.Permissions == null) _config.Permissions = new PermissionSettings();

            _config.Version = CurrentVersion;
            if (_config.RestartTimes == null) _config.RestartTimes = new List<string>();
            if (_config.WarningMinutes == null) _config.WarningMinutes = new List<int>();
            if (_config.WarningMessages == null) _config.WarningMessages = new Dictionary<int, string>();
            if (_config.RestartColors == null) _config.RestartColors = new Dictionary<string, string>();

            _config.RestartTimes = _config.RestartTimes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _config.WarningMinutes = _config.WarningMinutes
                .Where(value => value > 0)
                .Distinct()
                .OrderByDescending(value => value)
                .ToList();

            if (string.IsNullOrWhiteSpace(_config.RestartCommand) ||
                _config.RestartCommand.Trim().Equals("restart", StringComparison.OrdinalIgnoreCase))
                _config.RestartCommand = "restart 0";

            _config.GUIDuration = Mathf.Clamp(_config.GUIDuration, 1f, 300f);
            _config.LiveCountdownMinutes = Mathf.Clamp(_config.LiveCountdownMinutes, 0, 15);
            _config.GametipDuration = Mathf.Clamp(_config.GametipDuration, 1f, 60f);
            _config.GametipDelay = Mathf.Clamp(_config.GametipDelay, 0f, 60f);
            _config.FontSize = Mathf.Clamp(_config.FontSize, 8, 40);

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            RegisterPermissions();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=None");
        }

        private void OnServerInitialized()
        {
            ExecuteProtected(() =>
            {
                ParseRestartTimes();
                ScheduleNextRestart();
            });
        }

        private void Unload()
        {
            CancelSchedule();
            _testCountdowns.Clear();
            DestroyUiForAll();
        }

        private void RegisterPermissions()
        {
            if (!_config.GrantPermissions) return;
            if (!permission.GroupHasPermission("admin", UsePermission))
                permission.GrantGroupPermission("admin", UsePermission, this);
            if (!permission.GroupHasPermission("admin", AdminPermission))
                permission.GrantGroupPermission("admin", AdminPermission, this);
        }

        private void ParseRestartTimes()
        {
            _restartTimes.Clear();
            foreach (string configuredTime in _config.RestartTimes)
            {
                TimeSpan value;
                if (TimeSpan.TryParseExact(configuredTime, "hh\\:mm", CultureInfo.InvariantCulture, out value) &&
                    value >= TimeSpan.Zero && value < TimeSpan.FromDays(1))
                {
                    if (!_restartTimes.Contains(value))
                        _restartTimes.Add(value);
                }
                else
                {
                    PrintWarning("Ignored invalid restart time '" + configuredTime + "'. Expected HH:mm.");
                }
            }
            _restartTimes.Sort();
        }

        private void ScheduleNextRestart()
        {
            CancelSchedule();
            _isRestarting = false;

            if (_restartTimes.Count == 0)
            {
                _hasScheduledRestart = false;
                PrintWarning("No valid restart times are configured; automatic restarts are disabled.");
                return;
            }

            DateTime now = DateTime.Now;
            DateTime next = DateTime.MaxValue;
            foreach (TimeSpan configuredTime in _restartTimes)
            {
                DateTime candidate = now.Date.Add(configuredTime);
                if (candidate <= now.AddSeconds(5))
                    candidate = candidate.AddDays(1);
                if (candidate < next)
                    next = candidate;
            }

            _restartTime = next;
            _hasScheduledRestart = true;
            int generation = _scheduleGeneration;
            Delay(_restartTime - now,
                delegate { if (generation == _scheduleGeneration) ExecuteProtected(DoRestart, "Restart"); },
                "serverrestart-main");
            ScheduleWarnings(now, generation);
            LogInformation("Schedule", "Next restart scheduled for " +
                _restartTime.ToString("yyyy-MM-dd HH:mm:ss") + " server-local time.");
        }

        private void ScheduleWarnings(DateTime now, int generation)
        {
            for (int i = 0; i < _config.WarningMinutes.Count; i++)
            {
                int minutes = _config.WarningMinutes[i];
                float delay = (float)(_restartTime.AddMinutes(-minutes) - now).TotalSeconds;
                if (delay <= 0f)
                    continue;
                int capturedMinutes = minutes;
                Delay(TimeSpan.FromSeconds(delay),
                    delegate
                    {
                        if (generation == _scheduleGeneration)
                            ExecuteProtected(delegate { SendWarning(capturedMinutes, generation); }, "Warning");
                    }, "serverrestart-warning-" + capturedMinutes);
            }

            if (_config.UseGUI && _config.LiveCountdownMinutes > 0)
            {
                float liveDelay = (float)(_restartTime.AddMinutes(-_config.LiveCountdownMinutes) - now).TotalSeconds;
                if (liveDelay > 0f)
                    Delay(TimeSpan.FromSeconds(liveDelay),
                        delegate
                        {
                            if (generation == _scheduleGeneration)
                                ExecuteProtected(delegate { StartLiveCountdown(generation); }, "CountdownStart");
                        }, "serverrestart-countdown-start");
                else if ((_restartTime - now).TotalMinutes <= _config.LiveCountdownMinutes)
                    StartLiveCountdown(generation);
            }
        }

        private void CancelSchedule()
        {
            _scheduleGeneration++;
            _countdownRunning = false;
            DestroyUiForAll();
        }

        private void SendWarning(int minutes, int generation)
        {
            string message;
            if (!_config.WarningMessages.TryGetValue(minutes, out message) || string.IsNullOrWhiteSpace(message))
                message = "Server restart in " + minutes + " minute(s)!";

            if (_config.UseChat)
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (player != null && player.IsConnected)
                        SendRestartChat(player, message);
                }
            }

            if (_config.UseGUI && (_config.LiveCountdownMinutes == 0 || minutes > _config.LiveCountdownMinutes))
            {
                ShowUiForAll(message, "M_DEFAULT");
                Delay(TimeSpan.FromSeconds(_config.GUIDuration),
                    delegate
                    {
                        if (generation == _scheduleGeneration)
                            ExecuteProtected(DestroyUiForAll, "UiCleanup");
                    }, "serverrestart-ui-cleanup");
            }

            ScheduleGameTip(message, generation);
        }

        private static void SendRestartChat(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected || string.IsNullOrEmpty(message))
                return;

            player.SendConsoleCommand("chat.add", 2, ChatIconSteamId, message);
        }

        private static void SendCommandChat(RogueCommandContext context, string message)
        {
            BasePlayer player = context == null ? null : context.NativePlayer;
            if (player != null && player.IsConnected)
                SendRestartChat(player, message);
        }

        private void ScheduleGameTip(string message, int generation)
        {
            Action show = () =>
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    if (player != null && player.IsConnected)
                        player.SendConsoleCommand("gametip.showgametip", message);
                }
                Delay(TimeSpan.FromSeconds(_config.GametipDuration), delegate
                {
                    if (generation != _scheduleGeneration) return;
                    ExecuteProtected(delegate
                    {
                        foreach (BasePlayer player in BasePlayer.activePlayerList)
                        {
                            if (player != null && player.IsConnected)
                                player.SendConsoleCommand("gametip.hidegametip");
                        }
                    }, "GametipHide");
                }, "serverrestart-gametip-hide");
            };

            if (_config.GametipDelay > 0f)
                Delay(TimeSpan.FromSeconds(_config.GametipDelay),
                    delegate
                    {
                        if (generation == _scheduleGeneration)
                            ExecuteProtected(show, "GametipShow");
                    }, "serverrestart-gametip-show");
            else
                show();
        }

        private void StartLiveCountdown(int generation)
        {
            if (!_config.UseGUI || _countdownRunning)
                return;

            _countdownRunning = true;
            UpdateLiveCountdown(generation);
            Repeat(TimeSpan.FromSeconds(1), delegate
            {
                if (!_countdownRunning || generation != _scheduleGeneration) return;
                ExecuteProtected(delegate { UpdateLiveCountdown(generation); }, "LiveCountdown");
            }, "serverrestart-live-countdown");
        }

        private void UpdateLiveCountdown(int generation)
        {
            TimeSpan remaining = _restartTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                _countdownRunning = false;
                DestroyUiForAll();
                return;
            }

            string text = remaining.TotalMinutes < 1
                ? "Server restart in " + Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds)) + " second(s)!"
                : string.Format("{0:00}:{1:00}:{2:00} until server restart",
                    (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
            ShowUiForAll(text, remaining.TotalMinutes <= 1 ? "M_2" : "M_1");
        }

        private void DoRestart()
        {
            if (_isRestarting)
                return;

            _isRestarting = true;
            DestroyUiForAll();
            LogInformation("Restart", "Saving server before scheduled restart.");
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "server.save");
            Delay(TimeSpan.FromSeconds(2),
                delegate { ExecuteProtected(delegate { ConsoleSystem.Run(ConsoleSystem.Option.Server, _config.RestartCommand); }, "RestartCommand"); },
                "serverrestart-command");
        }

        private void ShowUiForAll(string text, string reasonKey)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected)
                    ShowUi(player, text, reasonKey);
            }
        }

        private void ShowUi(BasePlayer player, string text, string reasonKey)
        {
            CuiHelper.DestroyUi(player, UiName);
            string color;
            if (!_config.RestartColors.TryGetValue(reasonKey, out color) || string.IsNullOrWhiteSpace(color))
                color = _config.GUIColor;

            CuiElementContainer container = new CuiElementContainer();
            string panel = container.Add(new CuiPanel
            {
                Image = { Color = color },
                RectTransform =
                {
                    AnchorMin = _config.GUIAnchorMin.x.ToString(CultureInfo.InvariantCulture) + " " +
                                _config.GUIAnchorMin.y.ToString(CultureInfo.InvariantCulture),
                    AnchorMax = _config.GUIAnchorMax.x.ToString(CultureInfo.InvariantCulture) + " " +
                                _config.GUIAnchorMax.y.ToString(CultureInfo.InvariantCulture)
                },
                CursorEnabled = false
            }, "Overlay", UiName);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = text,
                    FontSize = _config.FontSize,
                    Align = TextAnchor.MiddleCenter,
                    Color = _config.GUITextColor
                },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, panel);

            CuiHelper.AddUi(player, container);
        }

        private void DestroyUiForAll()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player != null)
                    CuiHelper.DestroyUi(player, UiName);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            ExecuteProtected(() =>
            {
                if (player == null)
                    return;
                CuiHelper.DestroyUi(player, UiName);
                _testCountdowns.Remove(player.userID);
            });
        }

        [RogueCommand("serverrestart",
            Aliases = new[] { "roguerust.restart" },
            Permission = UsePermission,
            Description = "Shows the next scheduled server restart.",
            Usage = "/serverrestart", Category = "Server", CooldownSeconds = 1, AllowConsole = true)]
        private RogueCommandResult RestartStatusCommand(RogueCommandContext context)
        {
            if (!CanUse(context, UsePermission))
                return RogueCommandResult.Fail("You do not have permission to use this command.");
            if (!_hasScheduledRestart)
            {
                const string disabledMessage = "Automatic restarts are disabled because no valid times are configured.";
                SendCommandChat(context, disabledMessage);
                return context.NativePlayer != null ? RogueCommandResult.Ok(string.Empty) : RogueCommandResult.Ok(disabledMessage);
            }

            TimeSpan remaining = _restartTime - DateTime.Now;
            string statusMessage = string.Format("Next server restart is in {0} hour(s) {1} minute(s).",
                Math.Max(0, (int)remaining.TotalHours), Math.Max(0, remaining.Minutes));
            SendCommandChat(context, statusMessage);
            return context.NativePlayer != null ? RogueCommandResult.Ok(string.Empty) : RogueCommandResult.Ok(statusMessage);
        }

        [RogueCommand("serverrestart.reload",
            Aliases = new[] { "roguerust.restart.reload" },
            Permission = AdminPermission,
            Description = "Reloads restart configuration and reschedules.",
            Usage = "/serverrestart.reload", Category = "Server", CooldownSeconds = 2, AllowConsole = true)]
        private RogueCommandResult ReloadCommand(RogueCommandContext context)
        {
            if (!CanUse(context, AdminPermission))
                return RogueCommandResult.Fail("You do not have permission to use this command.");
            ReloadConfiguration();
            const string message = "Configuration reloaded and restarts rescheduled.";
            SendCommandChat(context, message);
            return context.NativePlayer != null ? RogueCommandResult.Ok(string.Empty) : RogueCommandResult.Ok(message);
        }

        [RogueCommand("serverrestart.test",
            Aliases = new[] { "roguerust.restart.test" },
            Permission = AdminPermission,
            Description = "Runs a 30-second player-only GUI countdown test.",
            Usage = "/serverrestart.test", Category = "Server", CooldownSeconds = 2, AllowConsole = false)]
        private RogueCommandResult TestCommand(RogueCommandContext context)
        {
            if (!CanUse(context, AdminPermission))
                return RogueCommandResult.Fail("You do not have permission to use this command.");
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail("This command is player-only.");
            const int seconds = 30;
            ulong playerId = player.userID;
            _testCountdowns.Add(playerId);
            int remaining = seconds;
            string testMessage = "Server restart test: " + remaining + " seconds.";
            SendRestartChat(player, testMessage);
            ShowUi(player, "Test countdown: " + remaining + " seconds", "M_DEFAULT");
            Repeat(TimeSpan.FromSeconds(1), delegate
            {
                if (!_testCountdowns.Contains(playerId)) return;
                if (player == null || !player.IsConnected || --remaining <= 0)
                {
                    if (player != null) CuiHelper.DestroyUi(player, UiName);
                    _testCountdowns.Remove(playerId);
                    return;
                }
                ShowUi(player, "Test countdown: " + remaining + " seconds", "M_DEFAULT");
            }, "serverrestart-test-" + playerId);
            const string startedMessage = "Test countdown started.";
            SendCommandChat(context, startedMessage);
            return RogueCommandResult.Ok(string.Empty);
        }

        private void ReloadConfiguration()
        {
            LoadConfig();
            RegisterPermissions();
            ParseRestartTimes();
            ScheduleNextRestart();
        }

        private void ExecuteProtected(Action action, string operation = "Runtime")
        {
            if (action == null) return;
            try
            {
                using (Measure("ServerRestarter", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private bool CanUse(RogueCommandContext context, string requiredPermission)
        {
            BasePlayer player = context.NativePlayer;
            if (player != null)
                return player.IsAdmin || Rogue.Permissions.Has(player.UserIDString, requiredPermission);

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            return arg == null || arg.Connection == null || arg.Connection.authLevel >= 2;
        }
    
}
}
