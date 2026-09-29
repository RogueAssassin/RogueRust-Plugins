using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RogueRustTurretLimitOverride", "RogueAssassin", "2.1.2")]
    [Description("Configures Rust's turret interference ConVars.")]
    public sealed class RogueRustTurretLimitOverride : RogueRustPlugin
    {
        [RoguePermission]
        private const string DefaultAdminPermission = "roguerustturretlimitoverride.admin";
        private const float VanillaRadius = 40f;
        private const int VanillaMaximum = 12;
        private const string PluginVersion = "2.1.2";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 2);
        private Configuration _config;

        private string AdminPermission { get { return DefaultAdminPermission; } }

        private sealed class Configuration
        {
            [JsonProperty("Turret Settings", Order = 10)]
            public TurretSettings Turret = new TurretSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class TurretSettings
        {
            [JsonProperty("Enable Override")]
            public bool EnableOverride = true;

            [JsonProperty("Interference Radius (0 disables the radius check)")]
            public float InterferenceRadius = 0f;

            [JsonProperty("Maximum Active Turrets in the Interference Radius")]
            public int MaxInterference = 100;
        }

        private sealed class LegacyConfiguration
        {
            [JsonProperty("Version (DO NOT CHANGE)")]
            public VersionNumber Version = new VersionNumber(2, 0, 0);

            [JsonProperty("Enable override")]
            public bool EnableOverride = true;

            [JsonProperty("Interference radius (0 disables the radius check)")]
            public float InterferenceRadius = 0f;

            [JsonProperty("Maximum active turrets in the interference radius")]
            public int MaxInterference = 100;

            [JsonProperty("Admin permission")]
            public string AdminPermission = DefaultAdminPermission;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            bool migrated = false;
            try
            {
                Newtonsoft.Json.Linq.JObject raw = Config.ReadObject<Newtonsoft.Json.Linq.JObject>();
                if (raw != null && raw["Turret Settings"] == null && raw["01 - Turret Settings"] != null)
                {
                    raw["Turret Settings"] = raw["01 - Turret Settings"].DeepClone();
                    raw.Remove("01 - Turret Settings");
                    _config = raw.ToObject<Configuration>() ?? new Configuration();
                    migrated = true;
                }
                else if (raw != null && raw["Turret Settings"] == null &&
                    (raw["Enable override"] != null || raw["Interference radius (0 disables the radius check)"] != null ||
                     raw["Maximum active turrets in the interference radius"] != null))
                {
                    LegacyConfiguration legacy = raw.ToObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    _config = new Configuration();
                    _config.Turret.EnableOverride = legacy.EnableOverride;
                    _config.Turret.InterferenceRadius = legacy.InterferenceRadius;
                    _config.Turret.MaxInterference = legacy.MaxInterference;
                    migrated = true;
                }
                else
                {
                    _config = raw == null ? new Configuration() : raw.ToObject<Configuration>();
                }
            }
            catch (Exception exception)
            {
                LogError("Configuration", "Invalid configuration: " + exception.Message);
                _config = new Configuration();
            }

            if (_config == null) _config = new Configuration();
            if (_config.Turret == null) _config.Turret = new TurretSettings();

            float radius = Clamp(_config.Turret.InterferenceRadius, 0f, 10000f);
            int maximum = Math.Max(1, Math.Min(_config.Turret.MaxInterference, 10000));
            if (Math.Abs(radius - _config.Turret.InterferenceRadius) > 0.001f || maximum != _config.Turret.MaxInterference)
            {
                _config.Turret.InterferenceRadius = radius;
                _config.Turret.MaxInterference = maximum;
                migrated = true;
            }

            if (_config.Version < CurrentVersion)
            {
                _config.Version = CurrentVersion;
                migrated = true;
            }

            if (migrated)
                LogWarning("Configuration", "Configuration was migrated and validated for v" + CurrentVersion + ".");

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            LoadLanguageFiles();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=<language>/RogueRust/RogueRustTurretLimitOverride/messages.json");
        }

        private void OnServerInitialized()
        {
            ExecuteProtected(ApplyTurretSettings, "ServerInitialized");
        }

        [RogueCommand("turretlimit",
            Aliases = new[] { "roguerust.turretlimit" },
            Description = "Shows or changes Rust turret interference limits.",
            Usage = "/turretlimit status | enabled <true|false> | radius <0-10000> | max <1-10000>",
            Category = "Server", CooldownSeconds = 0.5, AllowConsole = true)]
        private RogueCommandResult TurretLimitCommand(RogueCommandContext context)
        {
            if (!CanAdmin(context))
                return RogueCommandResult.Fail(GetMessage("NoPermission", context.NativePlayer));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length == 0 || args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                return RogueCommandResult.Ok(string.Format(GetMessage("Status", context.NativePlayer),
                    _config.Turret.EnableOverride,
                    _config.Turret.EnableOverride ? _config.Turret.InterferenceRadius : VanillaRadius,
                    _config.Turret.EnableOverride ? _config.Turret.MaxInterference : VanillaMaximum));
            }

            switch (args[0].ToLowerInvariant())
            {
                case "enabled":
                    bool enabled;
                    if (args.Length != 2 || !bool.TryParse(args[1], out enabled))
                        return RogueCommandResult.Fail(GetMessage("Usage", context.NativePlayer));
                    _config.Turret.EnableOverride = enabled;
                    break;

                case "radius":
                    float radius;
                    if (args.Length != 2 ||
                        !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                        radius < 0f || radius > 10000f)
                        return RogueCommandResult.Fail(GetMessage("Usage", context.NativePlayer));
                    _config.Turret.InterferenceRadius = radius;
                    break;

                case "max":
                    int maximum;
                    if (args.Length != 2 || !int.TryParse(args[1], out maximum) || maximum < 1 || maximum > 10000)
                        return RogueCommandResult.Fail(GetMessage("Usage", context.NativePlayer));
                    _config.Turret.MaxInterference = maximum;
                    break;

                default:
                    return RogueCommandResult.Fail(GetMessage("Usage", context.NativePlayer));
            }

            SaveConfig();
            ExecuteProtected(ApplyTurretSettings, "ApplyCommand");
            return RogueCommandResult.Ok(GetMessage("Applied", context.NativePlayer));
        }

        private void ExecuteProtected(Action action, string operation = "Runtime")
        {
            if (action == null) return;
            try
            {
                using (Measure("TurretLimitOverride", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private bool CanAdmin(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player != null)
            {
                if (player.IsAdmin)
                    return true;

                string adminPermission = AdminPermission;
                return Rogue.Permissions.Has(player.UserIDString, adminPermission);
            }

            ConsoleSystem.Arg arg = context.ConsoleArgument;
            return arg == null || arg.Connection == null || arg.Connection.authLevel >= 2;
        }

        private void ApplyTurretSettings()
        {
            float radius = _config.Turret.EnableOverride ? _config.Turret.InterferenceRadius : VanillaRadius;
            int maximum = _config.Turret.EnableOverride ? _config.Turret.MaxInterference : VanillaMaximum;

            ConsoleSystem.Run(ConsoleSystem.Option.Server,
                "sentry.interferenceradius " + radius.ToString(CultureInfo.InvariantCulture));
            ConsoleSystem.Run(ConsoleSystem.Option.Server,
                "sentry.maxinterference " + maximum.ToString(CultureInfo.InvariantCulture));
            LogInformation("TurretSettings", string.Format(CultureInfo.InvariantCulture,
                "Applied sentry.interferenceradius={0} and sentry.maxinterference={1}. Existing powered turrets may need a power cycle before their state is recalculated.",
                radius, maximum));
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        protected override void LoadDefaultMessages()
        {
            // Localization is stored under lang/RogueRust/TurretLimitOverride.
        }

        private string GetMessage(string key, BasePlayer player)
        {
            string language = player == null ? "en" : lang.GetLanguage(player.UserIDString);
            if (string.IsNullOrWhiteSpace(language)) language = "en";

            Dictionary<string, string> catalog;
            string value;
            if (_messages.TryGetValue(language, out catalog) && catalog.TryGetValue(key, out value))
                return value;
            if (_messages.TryGetValue("en", out catalog) && catalog.TryGetValue(key, out value))
                return value;
            return key;
        }

        private void LoadLanguageFiles()
        {
            _messages.Clear();
            string langRoot = Interface.Oxide.LangDirectory;
            string[] languages = System.IO.Directory.Exists(langRoot)
                ? System.IO.Directory.GetDirectories(langRoot)
                    .Select(System.IO.Path.GetFileName)
                    .Where(x => !string.IsNullOrWhiteSpace(x) && !x.Equals("RogueRust", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
                : Array.Empty<string>();

            HashSet<string> languageSet = new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase) { "en" };
            foreach (string language in languageSet)
            {
                string directory = System.IO.Path.Combine(langRoot, language, "RogueRust", "RogueRustTurretLimitOverride");
                System.IO.Directory.CreateDirectory(directory);
                string target = System.IO.Path.Combine(directory, "messages.json");

                string oldRogue = System.IO.Path.Combine(langRoot, "RogueRust", "TurretLimitOverride", language + ".json");
                string oldPlugin = System.IO.Path.Combine(langRoot, language, Name + ".json");
                string oldOriginal = System.IO.Path.Combine(langRoot, language, "Turret Limit Override.json");

                if (!System.IO.File.Exists(target))
                {
                    if (System.IO.File.Exists(oldRogue)) System.IO.File.Copy(oldRogue, target, false);
                    else if (System.IO.File.Exists(oldPlugin)) System.IO.File.Copy(oldPlugin, target, false);
                    else if (System.IO.File.Exists(oldOriginal)) System.IO.File.Copy(oldOriginal, target, false);
                    else if (language.Equals("en", StringComparison.OrdinalIgnoreCase))
                        System.IO.File.WriteAllText(target, JsonConvert.SerializeObject(CreateDefaultMessages(), Formatting.Indented));
                }

                if (!System.IO.File.Exists(target)) continue;
                try
                {
                    Dictionary<string, string> catalog =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText(target));
                    if (catalog != null) _messages[language] = catalog;
                }
                catch (Exception exception)
                {
                    LogWarning("Localization", "Could not load language file '" + target + "': " + exception.Message);
                }
            }

            if (!_messages.ContainsKey("en"))
                _messages["en"] = CreateDefaultMessages();
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["NoPermission"] = "You do not have permission to change turret limits.",
                ["Usage"] = "Usage: turretlimit status | enabled <true|false> | radius <0-10000> | max <1-10000>",
                ["Status"] = "Turret override enabled: {0}; radius: {1}; maximum: {2}.",
                ["Applied"] = "Turret interference settings were saved and applied."
            };
        }

}
}
