using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("DayNightScheduler", "RogueAssassin", "3.1.0")]
    [Description("RogueRust-powered day/night duration scheduling, cycle skipping, and protected time controls.")]
    public sealed class RogueRustDayNightScheduler : RogueRustPlugin
    {
        [RoguePermission]
        private const string UsePermission = "roguerustdaynightscheduler.use";
        private const string LangPluginDirectory = "RogueRustDayNightScheduler";
        private const string LegacyLangPluginDirectory = "DayNightScheduler";
        private const string LanguageFileName = "messages.json";
        private const string DefaultLanguage = "en";
        private const string InitRetryWorkload = "tod-initialize";

        private const string PluginVersion = "3.1.0";

        private static readonly VersionNumber CurrentVersion = new VersionNumber(3, 1, 0);

        private ConfigData _config;
        private TOD_Sky _sky;
        private TOD_Time _timeComponent;
        private bool _initialized;
        private bool _dayActive;
        private bool _applyingCycle;
        private int _initializationAttempts;
        private bool _previousProgressTime;
        private bool _previousUseTimeCurve;
        private float _previousDayLength;
        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private sealed class ConfigData
        {
            [JsonProperty("Cycle Settings", Order = 10)]
            public CycleSettings Cycle = new CycleSettings();

            [JsonProperty("Permission Settings", Order = 20)]
            public PermissionSettings Permissions = new PermissionSettings();

            [JsonProperty("Time Control Settings", Order = 30)]
            public TimeControlSettings TimeControl = new TimeControlSettings();

            [JsonProperty("Developer Settings", Order = 40)]
            public DeveloperSettings Developer = new DeveloperSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class CycleSettings
        {
            [JsonProperty("DayLength (Length of the day in real minutes)")]
            public int DayLength = 30;

            [JsonProperty("NightLength (Length of the night in real minutes)")]
            public int NightLength = 30;

            [JsonProperty("Skip night immediately when the night cycle starts.")]
            public bool AutoSkipNight;

            [JsonProperty("Skip day immediately when the day cycle starts.")]
            public bool AutoSkipDay;

            [JsonProperty("Write automatic cycle skips to the server console.")]
            public bool LogAutoSkipConsole = true;
        }

        private sealed class PermissionSettings
        {
            [JsonProperty("AuthLevelCmds (Minimum auth level for duration commands)")]
            public int AuthLevelCmds = 1;

            [JsonProperty("AuthLevelFreeze (Minimum auth level for freeze/time commands)")]
            public int AuthLevelFreeze = 2;
        }

        private sealed class TimeControlSettings
        {
            [JsonProperty("Freeze world time at the configured hour after startup.")]
            public bool FreezeTimeOnLoad;

            [JsonProperty("TimeToFreeze (0-24)")]
            public float TimeToFreeze = 12f;
        }

        private sealed class DeveloperSettings
        {
            [JsonProperty("LogLevel (Error, Warning, Info, Debug)")]
            public string LogLevel = "Info";
        }

        // v3.0.0 flat configuration shape.
        private sealed class LegacyConfigData
        {
            [JsonProperty("DayLength (Length of the day in real minutes)")] public int DayLength = 30;
            [JsonProperty("NightLength (Length of the night in real minutes)")] public int NightLength = 30;
            [JsonProperty("AuthLevelCmds (Minimum auth level for duration commands)")] public int AuthLevelCmds = 1;
            [JsonProperty("AuthLevelFreeze (Minimum auth level for freeze/time commands)")] public int AuthLevelFreeze = 2;
            [JsonProperty("Skip night immediately when the night cycle starts.")] public bool AutoSkipNight;
            [JsonProperty("Skip day immediately when the day cycle starts.")] public bool AutoSkipDay;
            [JsonProperty("Write automatic cycle skips to the server console.")] public bool LogAutoSkipConsole = true;
            [JsonProperty("Freeze world time at the configured hour after startup.")] public bool FreezeTimeOnLoad;
            [JsonProperty("TimeToFreeze (0-24)")] public float TimeToFreeze = 12f;
            [JsonProperty("LogLevel (Error, Warning, Info, Debug)")] public string LogLevel = "Info";
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
                Dictionary<string, object> raw = Config.ReadObject<Dictionary<string, object>>();
                bool structured = raw != null &&
                    (raw.ContainsKey("Cycle Settings") || raw.ContainsKey("Permission Settings") ||
                     raw.ContainsKey("Time Control Settings") || raw.ContainsKey("Developer Settings"));

                if (structured)
                {
                    _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
                }
                else
                {
                    LegacyConfigData legacy = Config.ReadObject<LegacyConfigData>() ?? new LegacyConfigData();
                    _config = new ConfigData
                    {
                        Cycle = new CycleSettings
                        {
                            DayLength = legacy.DayLength,
                            NightLength = legacy.NightLength,
                            AutoSkipNight = legacy.AutoSkipNight,
                            AutoSkipDay = legacy.AutoSkipDay,
                            LogAutoSkipConsole = legacy.LogAutoSkipConsole
                        },
                        Permissions = new PermissionSettings
                        {
                            AuthLevelCmds = legacy.AuthLevelCmds,
                            AuthLevelFreeze = legacy.AuthLevelFreeze
                        },
                        TimeControl = new TimeControlSettings
                        {
                            FreezeTimeOnLoad = legacy.FreezeTimeOnLoad,
                            TimeToFreeze = legacy.TimeToFreeze
                        },
                        Developer = new DeveloperSettings
                        {
                            LogLevel = legacy.LogLevel
                        },
                        Version = CurrentVersion
                    };
                    LogInformation("Configuration", "Migrated v3.0.0 flat configuration to the RogueRust family layout.");
                }
            }
            catch (Exception exception)
            {
                PrintError("Invalid configuration; defaults will be used: " + exception.Message);
                _config = new ConfigData();
            }

            NormalizeConfig();
            SaveConfig();
        }

        private void NormalizeConfig()
        {
            _config ??= new ConfigData();
            _config.Cycle ??= new CycleSettings();
            _config.Permissions ??= new PermissionSettings();
            _config.TimeControl ??= new TimeControlSettings();
            _config.Developer ??= new DeveloperSettings();

            _config.Version = CurrentVersion;
            _config.Cycle.DayLength = Mathf.Clamp(_config.Cycle.DayLength, 1, 1440);
            _config.Cycle.NightLength = Mathf.Clamp(_config.Cycle.NightLength, 1, 1440);
            _config.Permissions.AuthLevelCmds = Mathf.Clamp(_config.Permissions.AuthLevelCmds, 0, 2);
            _config.Permissions.AuthLevelFreeze = Mathf.Clamp(_config.Permissions.AuthLevelFreeze, 0, 2);
            _config.TimeControl.TimeToFreeze = Mathf.Repeat(_config.TimeControl.TimeToFreeze, 24f);

            string level = (_config.Developer.LogLevel ?? string.Empty).Trim();
            if (!level.Equals("Error", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("Warning", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("Info", StringComparison.OrdinalIgnoreCase) &&
                !level.Equals("Debug", StringComparison.OrdinalIgnoreCase))
                level = "Info";
            _config.Developer.LogLevel = level;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        protected override void LoadDefaultMessages()
        {
            // Localization uses lang/<language>/RogueRust/RogueRustDayNightScheduler/messages.json.
        }

        private void Init()
        {
            LoadLanguageFiles();

            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=<language>/RogueRust/RogueRustDayNightScheduler/messages.json");
        }

        private void OnServerInitialized()
        {
            InitializeTimeComponent();
        }

        private void InitializeTimeComponent()
        {
            if (_initialized)
                return;

            TOD_Sky sky = TOD_Sky.Instance;
            if (sky == null || sky.Components == null || sky.Components.Time == null)
            {
                _initializationAttempts++;
                if (_initializationAttempts <= 10)
                {
                    Delay(TimeSpan.FromSeconds(1), InitializeTimeComponent, InitRetryWorkload);
                }
                else
                {
                    LogError("Lifecycle", "TOD time component was unavailable after 10 attempts; plugin was not activated.");
                }
                return;
            }

            _sky = sky;
            _timeComponent = sky.Components.Time;
            _previousProgressTime = _timeComponent.ProgressTime;
            _previousUseTimeCurve = _timeComponent.UseTimeCurve;
            _previousDayLength = _timeComponent.DayLengthInMinutes;

            _timeComponent.ProgressTime = true;
            _timeComponent.UseTimeCurve = false;
            _timeComponent.OnSunrise += OnSunrise;
            _timeComponent.OnSunset += OnSunset;
            _timeComponent.OnHour += OnHour;

            _initialized = true;
            _dayActive = IsDaytime();
            ApplyCycle(_dayActive, false, true);

            if (_config.TimeControl.FreezeTimeOnLoad)
                FreezeAt(_config.TimeControl.TimeToFreeze);

            PluginLog("Info", "DayNightScheduler activated with " + _config.Cycle.DayLength +
                " minute days and " + _config.Cycle.NightLength + " minute nights.");
        }

        private void Unload()
        {
            TOD_Time time = _timeComponent;
            if (time != null)
            {
                time.OnSunrise -= OnSunrise;
                time.OnSunset -= OnSunset;
                time.OnHour -= OnHour;
                time.ProgressTime = _previousProgressTime;
                time.UseTimeCurve = _previousUseTimeCurve;
                time.DayLengthInMinutes = _previousDayLength;
            }

            _initialized = false;
            _applyingCycle = false;
            _timeComponent = null;
            _sky = null;
        }

        private void OnSunrise()
        {
            ExecuteProtected(delegate
            {
                if (_initialized)
                    ApplyCycle(true, true, true);
            }, "OnSunrise");
        }

        private void OnSunset()
        {
            ExecuteProtected(delegate
            {
                if (_initialized)
                    ApplyCycle(false, true, true);
            }, "OnSunset");
        }

        private void OnHour()
        {
            ExecuteProtected(delegate
            {
                if (!_initialized || _timeComponent == null || !_timeComponent.ProgressTime)
                    return;

                bool isDay = IsDaytime();
                if (isDay != _dayActive)
                    ApplyCycle(isDay, true, true);
            }, "OnHour");
        }

        private void ApplyCycle(bool isDay, bool announceHook, bool allowSkip)
        {
            if (!_initialized || _timeComponent == null || _sky == null || _sky.Cycle == null || _applyingCycle)
                return;

            _applyingCycle = true;
            try
            {
                float sunrise = _sky.SunriseTime;
                float sunset = _sky.SunsetTime;
                float dayHours = Mathf.Max(0.01f, sunset - sunrise);
                float nightHours = Mathf.Max(0.01f, 24f - dayHours);

                if (isDay && allowSkip && _config.Cycle.AutoSkipDay && !_config.Cycle.AutoSkipNight)
                {
                    _sky.Cycle.Hour = Mathf.Repeat(sunset + 0.01f, 24f);
                    if (_config.Cycle.LogAutoSkipConsole)
                        PluginLog("Info", "Daytime automatically skipped.");
                    ApplyCycleCore(false, announceHook, dayHours, nightHours);
                    return;
                }

                if (!isDay && allowSkip && _config.Cycle.AutoSkipNight)
                {
                    _sky.Cycle.Hour = Mathf.Repeat(sunrise + 0.01f, 24f);
                    if (_config.Cycle.LogAutoSkipConsole)
                        PluginLog("Info", "Nighttime automatically skipped.");
                    ApplyCycleCore(true, announceHook, dayHours, nightHours);
                    return;
                }

                ApplyCycleCore(isDay, announceHook, dayHours, nightHours);
            }
            finally
            {
                _applyingCycle = false;
            }
        }

        private void ApplyCycleCore(bool isDay, bool announceHook, float dayHours, float nightHours)
        {
            bool changed = isDay != _dayActive;
            _dayActive = isDay;

            float requestedMinutes = isDay ? _config.Cycle.DayLength : _config.Cycle.NightLength;
            float cycleHours = isDay ? dayHours : nightHours;
            float calculatedLength = requestedMinutes * (24f / cycleHours);

            if (Math.Abs(_timeComponent.DayLengthInMinutes - calculatedLength) > 0.001f)
                _timeComponent.DayLengthInMinutes = calculatedLength;

            if (announceHook && changed)
                Interface.CallHook(isDay ? "OnTimeSunrise" : "OnTimeSunset");
        }

        private bool IsDaytime()
        {
            TOD_Sky sky = _sky ?? TOD_Sky.Instance;
            if (sky == null || sky.Cycle == null)
                return false;

            float hour = sky.Cycle.Hour;
            return hour >= sky.SunriseTime && hour < sky.SunsetTime;
        }

        private void SetWorldTime(float hour)
        {
            if (!_initialized || _timeComponent == null || _sky == null || _sky.Cycle == null)
                return;

            bool wasProgressing = _timeComponent.ProgressTime;
            float targetHour = Mathf.Repeat(hour, 24f);
            ConVar.Env.time = targetHour;
            _sky.Cycle.Hour = targetHour;

            bool isDay = IsDaytime();
            if (wasProgressing)
                ApplyCycle(isDay, false, false);
            else
                _dayActive = isDay;
        }

        private static bool TryParseTime(string value, out float hour)
        {
            hour = 0f;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            int colon = value.IndexOf(':');
            if (colon >= 0)
            {
                int hours;
                int minutes;
                if (!int.TryParse(value.Substring(0, colon), out hours) ||
                    !int.TryParse(value.Substring(colon + 1), out minutes) ||
                    hours < 0 || hours > 24 || minutes < 0 || minutes > 59 ||
                    (hours == 24 && minutes != 0))
                    return false;

                hour = hours == 24 ? 0f : hours + (minutes / 60f);
                return true;
            }

            float numeric;
            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out numeric) ||
                numeric < 0f || numeric > 24f)
                return false;

            hour = numeric == 24f ? 0f : numeric;
            return true;
        }

        private void FreezeAt(float hour)
        {
            if (!_initialized || _timeComponent == null || _sky == null || _sky.Cycle == null)
                return;

            float frozenHour = Mathf.Repeat(hour, 24f);
            ConVar.Env.time = frozenHour;
            _sky.Cycle.Hour = frozenHour;
            _timeComponent.ProgressTime = false;
            _dayActive = IsDaytime();
        }

        private void Unfreeze()
        {
            if (!_initialized || _timeComponent == null)
                return;

            _timeComponent.ProgressTime = true;
            ApplyCycle(IsDaytime(), false, true);
        }

        [RogueCommand(
            "tod",
            Description = "Shows or sets world time and freezes/unfreezes time progression.",
            Usage = "/tod [0-24|HH:MM|freeze|unfreeze]",
            Category = "World",
            CooldownSeconds = 0.5,
            AllowConsole = true)]
        private RogueCommandResult TimeOfDayCommand(RogueCommandContext context, string[] commandArgs)
        {
            if (!_initialized || _timeComponent == null || _sky == null || _sky.Cycle == null)
                return RogueCommandResult.Fail(GetMessage("NotReady", context.NativePlayer));

            string[] args = commandArgs ?? Array.Empty<string>();
            if (args.Length > 0 && args[0].Equals("freeze", StringComparison.OrdinalIgnoreCase))
            {
                string denial;
                if (!CanUse(context, _config.Permissions.AuthLevelFreeze, out denial))
                    return RogueCommandResult.Fail(denial);

                FreezeAt(_sky.Cycle.Hour);
                return RogueCommandResult.Ok(GetMessage("TimeFrozen", context.NativePlayer));
            }

            if (args.Length > 0 && args[0].Equals("unfreeze", StringComparison.OrdinalIgnoreCase))
            {
                string denial;
                if (!CanUse(context, _config.Permissions.AuthLevelFreeze, out denial))
                    return RogueCommandResult.Fail(denial);

                Unfreeze();
                return RogueCommandResult.Ok(GetMessage("TimeUnfrozen", context.NativePlayer));
            }

            if (args.Length > 0)
            {
                string denial;
                if (!CanUse(context, _config.Permissions.AuthLevelFreeze, out denial))
                    return RogueCommandResult.Fail(denial);

                float requestedHour;
                if (!TryParseTime(args[0], out requestedHour))
                    return RogueCommandResult.Fail(GetMessage("TimeUsage", context.NativePlayer));

                SetWorldTime(requestedHour);
                return RogueCommandResult.Ok(string.Format(
                    GetMessage("TimeSet", context.NativePlayer), FormatTime(requestedHour)));
            }

            float hour = _sky.Cycle.Hour;
            return RogueCommandResult.Ok(string.Format(
                GetMessage("Status", context.NativePlayer),
                FormatTime(hour),
                _sky.SunriseTime,
                _sky.SunsetTime,
                _config.Cycle.DayLength,
                _config.Cycle.NightLength,
                _timeComponent.ProgressTime));
        }

        [RogueCommand(
            "tod.freeze",
            Description = "Freezes world time at the current hour.",
            Usage = "/tod.freeze",
            Category = "World",
            CooldownSeconds = 0.5,
            AllowConsole = true)]
        private RogueCommandResult FreezeCommand(RogueCommandContext context)
        {
            if (!_initialized || _timeComponent == null || _sky == null || _sky.Cycle == null)
                return RogueCommandResult.Fail(GetMessage("NotReady", context.NativePlayer));

            string denial;
            if (!CanUse(context, _config.Permissions.AuthLevelFreeze, out denial))
                return RogueCommandResult.Fail(denial);

            FreezeAt(_sky.Cycle.Hour);
            return RogueCommandResult.Ok(GetMessage("TimeFrozen", context.NativePlayer));
        }

        [RogueCommand(
            "tod.unfreeze",
            Description = "Resumes normal world time progression.",
            Usage = "/tod.unfreeze",
            Category = "World",
            CooldownSeconds = 0.5,
            AllowConsole = true)]
        private RogueCommandResult UnfreezeCommand(RogueCommandContext context)
        {
            if (!_initialized || _timeComponent == null)
                return RogueCommandResult.Fail(GetMessage("NotReady", context.NativePlayer));

            string denial;
            if (!CanUse(context, _config.Permissions.AuthLevelFreeze, out denial))
                return RogueCommandResult.Fail(denial);

            Unfreeze();
            return RogueCommandResult.Ok(GetMessage("TimeUnfrozen", context.NativePlayer));
        }

        [RogueCommand(
            "daynight.daylength",
            Description = "Sets the real-time day duration in minutes.",
            Usage = "/daynight.daylength <1-1440>",
            Category = "World",
            CooldownSeconds = 0.5,
            AllowConsole = true)]
        private RogueCommandResult DayLengthCommand(RogueCommandContext context, string[] commandArgs)
        {
            string denial;
            if (!CanUse(context, _config.Permissions.AuthLevelCmds, out denial))
                return RogueCommandResult.Fail(denial);

            int minutes;
            if (commandArgs == null || commandArgs.Length != 1 ||
                !int.TryParse(commandArgs[0], out minutes) || minutes < 1 || minutes > 1440)
                return RogueCommandResult.Fail(GetMessage("DayLengthUsage", context.NativePlayer));

            SetDuration(true, minutes);
            return RogueCommandResult.Ok(string.Format(GetMessage("DayLengthSet", context.NativePlayer), _config.Cycle.DayLength));
        }

        [RogueCommand(
            "daynight.nightlength",
            Description = "Sets the real-time night duration in minutes.",
            Usage = "/daynight.nightlength <1-1440>",
            Category = "World",
            CooldownSeconds = 0.5,
            AllowConsole = true)]
        private RogueCommandResult NightLengthCommand(RogueCommandContext context, string[] commandArgs)
        {
            string denial;
            if (!CanUse(context, _config.Permissions.AuthLevelCmds, out denial))
                return RogueCommandResult.Fail(denial);

            int minutes;
            if (commandArgs == null || commandArgs.Length != 1 ||
                !int.TryParse(commandArgs[0], out minutes) || minutes < 1 || minutes > 1440)
                return RogueCommandResult.Fail(GetMessage("NightLengthUsage", context.NativePlayer));

            SetDuration(false, minutes);
            return RogueCommandResult.Ok(string.Format(GetMessage("NightLengthSet", context.NativePlayer), _config.Cycle.NightLength));
        }

        private void SetDuration(bool day, int minutes)
        {
            minutes = Mathf.Clamp(minutes, 1, 1440);
            int current = day ? _config.Cycle.DayLength : _config.Cycle.NightLength;
            if (current == minutes)
                return;

            if (day)
                _config.Cycle.DayLength = minutes;
            else
                _config.Cycle.NightLength = minutes;

            SaveConfig();

            if (_initialized && day == _dayActive)
                ApplyCycle(_dayActive, false, false);
        }

        private bool CanUse(RogueCommandContext context, int requiredAuthLevel, out string denial)
        {
            denial = GetMessage("NoPermission", context.NativePlayer);

            BasePlayer player = context.NativePlayer;
            if (player != null)
            {
                if ((player.Connection != null && player.Connection.authLevel >= requiredAuthLevel) ||
                    Rogue.Permissions.Has(player.UserIDString, UsePermission))
                    return true;
                return false;
            }

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            if (arg == null || arg.Connection == null)
                return true;

            if (arg.Connection.authLevel >= requiredAuthLevel)
                return true;

            return false;
        }

        private void ExecuteProtected(Action action, string operation)
        {
            if (action == null)
                return;

            try
            {
                using (Measure("DayNightScheduler", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private static string FormatTime(float hour)
        {
            hour = Mathf.Repeat(hour, 24f);
            int wholeHour = Mathf.FloorToInt(hour);
            int minutes = Mathf.FloorToInt((hour - wholeHour) * 60f);
            return string.Format("{0:00}:{1:00}", wholeHour, minutes);
        }

        private void PluginLog(string level, string message)
        {
            int configured = LogRank(_config == null ? "Info" : _config.Developer.LogLevel);
            int requested = LogRank(level);
            if (requested > configured)
                return;

            if (requested == 0)
                LogError("DayNightScheduler", message);
            else if (requested == 1)
                LogWarning("DayNightScheduler", message);
            else
                LogInformation("DayNightScheduler", message);
        }

        private static int LogRank(string level)
        {
            if (string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase)) return 0;
            if (string.Equals(level, "Warning", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(level, "Debug", StringComparison.OrdinalIgnoreCase)) return 3;
            return 2;
        }

        private string GetMessage(string key, BasePlayer player)
        {
            string language = ResolveLanguage(player);
            Dictionary<string, string> catalog;
            string value;

            if (_messages.TryGetValue(language, out catalog) && catalog.TryGetValue(key, out value))
                return value;
            if (_messages.TryGetValue(DefaultLanguage, out catalog) && catalog.TryGetValue(key, out value))
                return value;

            return key;
        }

        private string ResolveLanguage(BasePlayer player)
        {
            if (player == null)
                return DefaultLanguage;

            string language = lang.GetLanguage(player.UserIDString);
            return string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;
        }

        private void LoadLanguageFiles()
        {
            _messages.Clear();
            Dictionary<string, string> defaults = CreateDefaultMessages();
            string langRoot = Interface.Oxide.LangDirectory;
            HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { DefaultLanguage };

            if (Directory.Exists(langRoot))
            {
                string[] languageDirectories = Directory.GetDirectories(langRoot, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < languageDirectories.Length; i++)
                {
                    string language = Path.GetFileName(languageDirectories[i]);
                    if (!string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                        languages.Add(language);
                }
            }

            // Previous layout: lang/RogueRust/DayNightScheduler/<language>.json.
            string oldRogueRoot = Path.Combine(langRoot, "RogueRust", LegacyLangPluginDirectory);
            if (Directory.Exists(oldRogueRoot))
            {
                string[] oldFiles = Directory.GetFiles(oldRogueRoot, "*.json", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < oldFiles.Length; i++)
                    languages.Add(Path.GetFileNameWithoutExtension(oldFiles[i]));
            }

            foreach (string language in languages)
                LoadLanguageCatalog(langRoot, oldRogueRoot, language, defaults);

            if (!_messages.ContainsKey(DefaultLanguage))
                _messages[DefaultLanguage] = defaults;
        }

        private void LoadLanguageCatalog(
            string langRoot,
            string oldRogueRoot,
            string language,
            Dictionary<string, string> defaults)
        {
            string root = Path.Combine(langRoot, language, "RogueRust", LangPluginDirectory);
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, LanguageFileName);

            if (!File.Exists(target))
            {
                string oldRogue = Path.Combine(oldRogueRoot, language + ".json");
                string legacyCurrent = Path.Combine(langRoot, language, Name + ".json");
                string legacyOriginal = Path.Combine(langRoot, language, "DayNightScheduler.json");

                try
                {
                    if (File.Exists(oldRogue))
                        File.Copy(oldRogue, target, false);
                    else if (File.Exists(legacyCurrent))
                        File.Copy(legacyCurrent, target, false);
                    else if (File.Exists(legacyOriginal))
                        File.Copy(legacyOriginal, target, false);
                    else if (string.Equals(language, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                        WriteLanguageFile(target, defaults);
                }
                catch (Exception exception)
                {
                    LogWarning("Localization", "Could not migrate language '" + language + "': " + exception.Message);
                }
            }

            if (!File.Exists(target))
                return;

            try
            {
                Dictionary<string, string> catalog =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(target));
                if (catalog == null)
                    return;

                if (string.Equals(language, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    bool changed = false;
                    foreach (KeyValuePair<string, string> pair in defaults)
                    {
                        if (catalog.ContainsKey(pair.Key))
                            continue;
                        catalog[pair.Key] = pair.Value;
                        changed = true;
                    }
                    if (changed)
                        WriteLanguageFile(target, catalog);
                }

                _messages[language] = catalog;
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Could not load language file '" + target + "': " + exception.Message);
            }
        }

        private static void WriteLanguageFile(string path, Dictionary<string, string> messages)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(messages, Formatting.Indented));
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NoPermission"] = "You do not have permission to use this command.",
                ["NotReady"] = "The time system is not ready.",
                ["TimeFrozen"] = "The game time has been frozen.",
                ["TimeUnfrozen"] = "The game time has been unfrozen.",
                ["TimeSet"] = "Game time set to {0}.",
                ["TimeUsage"] = "Usage: /tod [0-24|HH:MM|freeze|unfreeze]",
                ["Status"] = "Time: {0}; sunrise: {1:0.00}; sunset: {2:0.00}; day: {3}m; night: {4}m; progressing: {5}.",
                ["DayLengthUsage"] = "Usage: /daynight.daylength <1-1440>",
                ["NightLengthUsage"] = "Usage: /daynight.nightlength <1-1440>",
                ["DayLengthSet"] = "Day length set to {0} minutes.",
                ["NightLengthSet"] = "Night length set to {0} minutes."
            };
        }

    }
}
