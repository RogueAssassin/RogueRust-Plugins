using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustHackableCrateTracker", "RogueAssassin", "2.1.0")]
    [Description("Accurately tracks hackable crates, hackers, looters, map grids, world metadata, and Discord notifications.")]
    public sealed class RogueRustHackableCrateTracker : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);
        [RoguePermission]
        private const string PermissionUse = "roguerusthackablecratetracker.use";

        [RoguePermission]
        private const string PermissionAdmin = "roguerusthackablecratetracker.admin";
        private const ulong ChatIconSteamId = 76561198641214711UL;
        private const float RustGridCellSize = 146.28572f; // 1024 metres / 7 map-grid cells

        private Configuration _config;
        private readonly Dictionary<NetworkableId, TrackedCrate> _tracked = new Dictionary<NetworkableId, TrackedCrate>();
        private readonly Dictionary<ulong, float> _attemptDebounce = new Dictionary<ulong, float>();
        private float _nextAttemptDebounceCleanup;
        private float _worldSize;
        private uint _worldSeed;

        private sealed class TrackedCrate
        {
            public HackableLockedCrate Crate;
            public ulong HackerId;
            public string HackerName;
            public Vector3 Position;
            public string Grid;
            public DateTime StartedUtc;
            public bool HackCompleted;
            public bool LootReported;
        }

        private sealed class Configuration
        {
            [JsonProperty("Reporting Settings", Order = 10)]
            public ReportingSettings Reporting = new ReportingSettings();

            [JsonProperty("Discord Settings", Order = 20)]
            public DiscordSettings Discord = new DiscordSettings();

            [JsonProperty("Location Settings", Order = 30)]
            public LocationSettings Location = new LocationSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            [JsonIgnore] public string DiscordWebhookUrl { get { return Discord.WebhookUrl; } set { Discord.WebhookUrl = value; } }
            [JsonIgnore] public bool EnableDiscordNotifications { get { return Discord.Enabled; } set { Discord.Enabled = value; } }
            [JsonIgnore] public bool EnableIngameReporting { get { return Reporting.EnableIngameReporting; } set { Reporting.EnableIngameReporting = value; } }
            [JsonIgnore] public bool RestrictIngameReports { get { return Reporting.RestrictIngameReports; } set { Reporting.RestrictIngameReports = value; } }
            [JsonIgnore] public bool ReportHackAttempts { get { return Reporting.ReportHackAttempts; } set { Reporting.ReportHackAttempts = value; } }
            [JsonIgnore] public bool EnableConsoleReporting { get { return Reporting.EnableConsoleReporting; } set { Reporting.EnableConsoleReporting = value; } }
            [JsonIgnore] public bool ReportHackCompletion { get { return Reporting.ReportHackCompletion; } set { Reporting.ReportHackCompletion = value; } }
            [JsonIgnore] public bool ReportActualLooter { get { return Reporting.ReportActualLooter; } set { Reporting.ReportActualLooter = value; } }
            [JsonIgnore] public bool IncludeCoordinates { get { return Location.IncludeCoordinates; } set { Location.IncludeCoordinates = value; } }
            [JsonIgnore] public bool IncludeWorldMetadata { get { return Location.IncludeWorldMetadata; } set { Location.IncludeWorldMetadata = value; } }
            [JsonIgnore] public string MapUrlTemplate { get { return Discord.MapUrlTemplate; } set { Discord.MapUrlTemplate = value; } }
        }

        private sealed class ReportingSettings
        {
            [JsonProperty("Enable In-Game Reporting")]
            public bool EnableIngameReporting = true;

            [JsonProperty("Only Send In-Game Reports to Permitted Players")]
            public bool RestrictIngameReports;

            [JsonProperty("Report Hack Attempts")]
            public bool ReportHackAttempts = true;

            [JsonProperty("Enable Console Reporting")]
            public bool EnableConsoleReporting = true;

            [JsonProperty("Report Hack Completion")]
            public bool ReportHackCompletion = true;

            [JsonProperty("Report First Actual Looter")]
            public bool ReportActualLooter = true;
        }

        private sealed class DiscordSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled;

            [JsonProperty("Webhook URL")]
            public string WebhookUrl = string.Empty;

            [JsonProperty("Map URL Template ({grid}, {x}, {z}, {worldsize}, {seed})")]
            public string MapUrlTemplate = string.Empty;
        }

        private sealed class LocationSettings
        {
            [JsonProperty("Include Exact XYZ Coordinates")]
            public bool IncludeCoordinates = true;

            [JsonProperty("Include World Metadata")]
            public bool IncludeWorldMetadata = true;
        }

        private sealed class LegacyConfiguration
        {
            [JsonProperty("Discord webhook URL")] public string DiscordWebhookUrl = string.Empty;
            [JsonProperty("Enable Discord notifications")] public bool EnableDiscordNotifications;
            [JsonProperty("Enable in-game reporting")] public bool EnableIngameReporting = true;
            [JsonProperty("Only send in-game reports to permitted players")] public bool RestrictIngameReports;
            [JsonProperty("Report hack attempts")] public bool ReportHackAttempts = true;
            [JsonProperty("Enable console reporting")] public bool EnableConsoleReporting = true;
            [JsonProperty("Report hack completion")] public bool ReportHackCompletion = true;
            [JsonProperty("Report the first actual looter")] public bool ReportActualLooter = true;
            [JsonProperty("Configuration version")] public int ConfigurationVersion = 2;
            [JsonProperty("Include exact XYZ coordinates")] public bool IncludeCoordinates = true;
            [JsonProperty("Include world metadata")] public bool IncludeWorldMetadata = true;
            [JsonProperty("Discord map URL template ({grid}, {x}, {z}, {worldsize}, {seed})")]
            public string MapUrlTemplate = string.Empty;
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
                bool legacy = raw != null && raw["Reporting Settings"] == null &&
                              (raw["Enable in-game reporting"] != null ||
                               raw["Discord webhook URL"] != null ||
                               raw["Configuration version"] != null);

                if (legacy)
                {
                    LegacyConfiguration old = raw.ToObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    _config = new Configuration();

                    _config.DiscordWebhookUrl = old.DiscordWebhookUrl;
                    _config.EnableDiscordNotifications = old.EnableDiscordNotifications;
                    _config.EnableIngameReporting = old.EnableIngameReporting;
                    _config.RestrictIngameReports = old.ConfigurationVersion < 2 ? false : old.RestrictIngameReports;
                    _config.ReportHackAttempts = old.ConfigurationVersion < 2 ? true : old.ReportHackAttempts;
                    _config.EnableConsoleReporting = old.EnableConsoleReporting;
                    _config.ReportHackCompletion = old.ReportHackCompletion;
                    _config.ReportActualLooter = old.ReportActualLooter;
                    _config.IncludeCoordinates = old.IncludeCoordinates;
                    _config.IncludeWorldMetadata = old.IncludeWorldMetadata;
                    _config.MapUrlTemplate = old.MapUrlTemplate;

                    LogInformation("Configuration", "Migrated HackableCrateTracker configuration to RogueRust grouped settings.");
                }
                else
                {
                    _config = raw == null ? new Configuration() : raw.ToObject<Configuration>();
                }

                if (_config == null)
                    _config = new Configuration();
                if (_config.Reporting == null)
                    _config.Reporting = new ReportingSettings();
                if (_config.Discord == null)
                    _config.Discord = new DiscordSettings();
                if (_config.Location == null)
                    _config.Location = new LocationSettings();

                _config.Version = CurrentVersion;
                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError($"Configuration error: {ex.Message}. Loading safe defaults.");
                _config = new Configuration();
                SaveConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Init()
        {
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=None");
        }

        private void OnServerInitialized()
        {
            _worldSize = ResolveWorldSize();
            _worldSeed = global::World.Seed;
            ValidateWorldMetadata();
            if (_config.EnableDiscordNotifications && !IsValidWebhook(_config.DiscordWebhookUrl))
                LogWarning("Networking", "Discord reporting is enabled, but the webhook URL is empty or invalid.");
        }

        private void Unload()
        {
            _tracked.Clear();
            _attemptDebounce.Clear();
        }

        // Current Oxide/Carbon hook fired when the crate hack starts.
        private void OnCrateHack(HackableLockedCrate crate) => TrackHackStarted(crate);

        // Kept as a compatibility alias for older/custom hook providers.
        private object OnHackableCrateStartedHacking(HackableLockedCrate crate)
        {
            TrackHackStarted(crate);
            return null;
        }

        // Reports interaction attempts before the game accepts or rejects the hack.
        // Returning null preserves Rust's normal behaviour.
        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (!_config.ReportHackAttempts || player == null || crate == null || crate.IsDestroyed) return null;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now >= _nextAttemptDebounceCleanup)
                CleanupAttemptDebounce(now);

            float last;
            if (_attemptDebounce.TryGetValue(player.userID, out last) && now - last < 2f) return null;
            _attemptDebounce[player.userID] = now;

            TrackedCrate state = GetOrCreateState(crate, player);
            Publish($"{player.displayName} is attempting to hack a locked crate", state, player, "hack_attempt");
            return null;
        }

        private void TrackHackStarted(HackableLockedCrate crate)
        {
            if (crate == null || crate.IsDestroyed) return;

            BasePlayer hacker = ResolveHacker(crate);
            TrackedCrate state = GetOrCreateState(crate, hacker);
            state.HackerId = hacker != null ? hacker.userID : state.HackerId;
            state.HackerName = hacker != null ? hacker.displayName : state.HackerName;
            state.StartedUtc = DateTime.UtcNow;
            state.HackCompleted = false;
            state.LootReported = false;
            NetworkableId completionId = crate.net.ID;
            Delay(TimeSpan.FromSeconds(Mathf.Max(1f, HackableLockedCrate.requiredHackSeconds)),
                delegate { OnExpectedHackComplete(completionId); },
                "crate-complete-" + completionId);

            Publish($"{state.HackerName} started hacking a locked crate", state, hacker, "hack_started");
        }

        // Authoritative current hook. The timer remains as a fallback for older framework builds.
        private void OnCrateHackEnd(HackableLockedCrate crate)
        {
            if (crate == null || crate.IsDestroyed || !_config.ReportHackCompletion) return;
            TrackedCrate state = GetOrCreateState(crate, ResolveHacker(crate));
            if (state.HackCompleted) return;
            state.HackCompleted = true;

            Publish("Locked crate hack completed", state, null, "hack_completed");
        }

        private BasePlayer ResolveHacker(HackableLockedCrate crate)
        {
            if (crate == null) return null;

            TrackedCrate tracked;
            if (_tracked.TryGetValue(crate.net.ID, out tracked) && tracked.HackerId != 0UL)
            {
                BasePlayer known = BasePlayer.FindByID(tracked.HackerId) ?? BasePlayer.FindSleeping(tracked.HackerId);
                if (known != null) return known;
            }

            ulong hackerId = crate.originalHackerPlayerId;
            if (hackerId == 0UL) return null;

            return BasePlayer.FindByID(hackerId) ?? BasePlayer.FindSleeping(hackerId);
        }

        private TrackedCrate GetOrCreateState(HackableLockedCrate crate, BasePlayer actor)
        {
            TrackedCrate state;
            if (_tracked.TryGetValue(crate.net.ID, out state)) return state;

            Vector3 position = crate.transform.position;
            state = new TrackedCrate
            {
                Crate = crate,
                HackerId = actor != null ? actor.userID : 0UL,
                HackerName = actor != null ? actor.displayName : "Unknown player",
                Position = position,
                Grid = GetAccurateGrid(position),
                StartedUtc = DateTime.UtcNow
            };
            _tracked[crate.net.ID] = state;
            return state;
        }

        private void OnExpectedHackComplete(NetworkableId id)
        {
            TrackedCrate state;
            if (!_tracked.TryGetValue(id, out state) || state.Crate == null || state.Crate.IsDestroyed)
            {
                RemoveTracked(id);
                return;
            }

            if (state.HackCompleted) return;

            state.HackCompleted = state.Crate.isLootable;
            if (state.HackCompleted && _config.ReportHackCompletion)
                Publish("Locked crate hack completed", state, null, "hack_completed");
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (!_config.ReportActualLooter || player == null || entity == null) return;

            HackableLockedCrate crate = entity as HackableLockedCrate;
            if (crate == null) return;

            TrackedCrate state;
            if (!_tracked.TryGetValue(crate.net.ID, out state) || state.LootReported) return;

            state.LootReported = true;
            state.HackCompleted = crate.isLootable;
            Publish($"{player.displayName} opened the hacked crate", state, player, "crate_looted");
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            HackableLockedCrate crate = entity as HackableLockedCrate;
            if (crate != null) RemoveTracked(crate.net.ID);
        }

        private void RemoveTracked(NetworkableId id)
        {
            _tracked.Remove(id);
        }

        private void Publish(string action, TrackedCrate state, BasePlayer actor, string eventName)
        {
            string location = BuildLocation(state.Position, state.Grid);
            string message = $"[{DateTime.Now:HH:mm:ss}] {action} at {location}";

            if (_config.EnableConsoleReporting)
                LogInformation("CrateEvent",
                    $"[{eventName}] {message} | crate={state.Crate?.net.ID} | hacker={state.HackerName} ({state.HackerId})");

            if (_config.EnableIngameReporting)
            {
                foreach (BasePlayer recipient in BasePlayer.activePlayerList)
                {
                    if (recipient == null || !recipient.IsConnected) continue;
                    if (_config.RestrictIngameReports && !HasAccess(recipient)) continue;
                    SendTrackerChat(recipient, message);
                }
            }

            if (_config.EnableDiscordNotifications && IsValidWebhook(_config.DiscordWebhookUrl))
            {
                Dictionary<string, object> embed = BuildDiscordEmbed(action, state, actor, eventName);
                SendDiscordWebhook(new Dictionary<string, object> { ["embeds"] = new[] { embed } });
            }
        }

        private static void SendTrackerChat(BasePlayer recipient, string message)
        {
            if (recipient == null || !recipient.IsConnected || string.IsNullOrEmpty(message)) return;
            recipient.SendConsoleCommand("chat.add", 2, ChatIconSteamId, message);
        }

        private Dictionary<string, object> BuildDiscordEmbed(string action, TrackedCrate state, BasePlayer actor, string eventName)
        {
            List<Dictionary<string, object>> fields = new List<Dictionary<string, object>>
            {
                Field("Grid", state.Grid, true),
                Field("Coordinates", FormatCoordinates(state.Position), true),
                Field("Hacker", $"{state.HackerName} ({state.HackerId})", false)
            };

            if (actor != null)
                fields.Add(Field("Actor", $"{actor.displayName} ({actor.userID})", false));

            if (_config.IncludeWorldMetadata)
            {
                fields.Add(Field("World", $"Size {GetWorldSize():0} | Seed {_worldSeed}", true));
            }

            string mapUrl = BuildMapUrl(state);
            Dictionary<string, object> embed = new Dictionary<string, object>
            {
                ["title"] = action,
                ["description"] = string.IsNullOrEmpty(mapUrl) ? $"Location: `{state.Grid}`" : $"[Open map waypoint]({mapUrl})",
                ["color"] = eventName == "hack_attempt" ? 15158332 : eventName == "hack_started" ? 16753920 : eventName == "crate_looted" ? 5763719 : 3447003,
                ["fields"] = fields,
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
            return embed;
        }

        private static Dictionary<string, object> Field(string name, string value, bool inline) =>
            new Dictionary<string, object> { ["name"] = name, ["value"] = value, ["inline"] = inline };

        private bool HasAccess(BasePlayer player) =>
            player.IsAdmin || Rogue.Permissions.Has(player.UserIDString, PermissionUse) ||
            Rogue.Permissions.Has(player.UserIDString, PermissionAdmin);

        private string BuildLocation(Vector3 position, string grid)
        {
            if (!_config.IncludeCoordinates) return grid;
            return $"{grid} ({FormatCoordinates(position)})";
        }

        private static string FormatCoordinates(Vector3 position) => $"X {position.x:0.0}, Y {position.y:0.0}, Z {position.z:0.0}";

        private string GetAccurateGrid(Vector3 position)
        {
            string nativeGrid = null;
            try { nativeGrid = MapHelper.PositionToString(position); }
            catch (Exception ex) { LogWarning("Grid", $"MapHelper grid conversion failed: {ex.Message}"); }

            string fallback = PositionToGridFallback(position);
            if (string.IsNullOrEmpty(nativeGrid)) return fallback;

            if (!string.Equals(nativeGrid, fallback, StringComparison.OrdinalIgnoreCase))
                LogWarning("Grid", $"Grid conversion mismatch at {FormatCoordinates(position)}: MapHelper={nativeGrid}, fallback={fallback}, worldSize={GetWorldSize():0}.");

            return nativeGrid;
        }

        private string PositionToGridFallback(Vector3 position)
        {
            float worldSize = GetWorldSize();
            if (worldSize <= 0f) return "Unknown";

            int columns = Mathf.Max(1, Mathf.CeilToInt(worldSize / RustGridCellSize));
            float half = worldSize * 0.5f;
            int column = Mathf.Clamp(Mathf.FloorToInt((position.x + half) / RustGridCellSize), 0, columns - 1);
            int row = Mathf.Clamp(Mathf.FloorToInt((half - position.z) / RustGridCellSize), 0, columns - 1);
            return ColumnName(column) + row;
        }

        private static string ColumnName(int zeroBased)
        {
            string result = string.Empty;
            int value = zeroBased + 1;
            while (value > 0)
            {
                value--;
                result = (char)('A' + value % 26) + result;
                value /= 26;
            }
            return result;
        }

        private float GetWorldSize()
        {
            return _worldSize > 0f ? _worldSize : ResolveWorldSize();
        }

        private static float ResolveWorldSize()
        {
            if (TerrainMeta.Size.x > 0f) return TerrainMeta.Size.x;
            if (global::World.Size > 0) return global::World.Size;
            return ConVar.Server.worldsize;
        }

        private void ValidateWorldMetadata()
        {
            float terrain = TerrainMeta.Size.x;
            float world = global::World.Size;
            float configured = ConVar.Server.worldsize;
            uint worldSeed = global::World.Seed;
            string worldUrl = global::World.Url ?? "procedural";
            LogInformation("World", $"Map metadata: TerrainMeta.Size.x={terrain:0}, World.Size={world:0}, server.worldsize={configured:0}, seed={worldSeed}, url={worldUrl}.");

            if (terrain <= 0f)
                LogWarning("World", "TerrainMeta.Size is unavailable; grid conversion will use the server world-size fallback.");
            else if (world > 0f && Mathf.Abs(terrain - world) > 1f)
                LogWarning("World", $"World-size mismatch detected: terrain={terrain:0}, world={world:0}.");
        }

        private string BuildMapUrl(TrackedCrate state)
        {
            if (string.IsNullOrWhiteSpace(_config.MapUrlTemplate)) return string.Empty;

            return _config.MapUrlTemplate
                .Replace("{grid}", Uri.EscapeDataString(state.Grid ?? string.Empty))
                .Replace("{x}", state.Position.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{z}", state.Position.z.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{worldsize}", GetWorldSize().ToString("0", System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{seed}", _worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private void CleanupAttemptDebounce(float now)
        {
            _nextAttemptDebounceCleanup = now + 300f;
            if (_attemptDebounce.Count == 0) return;

            List<ulong> stale = new List<ulong>();
            foreach (KeyValuePair<ulong, float> entry in _attemptDebounce)
            {
                if (now - entry.Value > 300f)
                    stale.Add(entry.Key);
            }

            for (int i = 0; i < stale.Count; i++)
                _attemptDebounce.Remove(stale[i]);
        }

        private static bool IsValidWebhook(string url) =>
            !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) && parsed.Scheme == Uri.UriSchemeHttps;

        private async void SendDiscordWebhook(Dictionary<string, object> payload)
        {
            if (payload == null || !IsValidWebhook(_config.DiscordWebhookUrl)) return;

            try
            {
                string json = JsonConvert.SerializeObject(payload);
                RogueHttpRequest request = new RogueHttpRequest(_config.DiscordWebhookUrl, RogueHttpMethod.Post)
                {
                    Owner = Name,
                    Name = "discord-webhook",
                    Body = json,
                    ContentType = "application/json",
                    Timeout = TimeSpan.FromSeconds(10),
                    MaximumResponseBytes = 64 * 1024,
                    MinimumHostInterval = TimeSpan.FromMilliseconds(250),
                    RetryPolicy = new RogueHttpRetryPolicy
                    {
                        MaxAttempts = 3,
                        InitialDelay = TimeSpan.FromSeconds(1),
                        MaximumDelay = TimeSpan.FromSeconds(5),
                        BackoffMultiplier = 2d,
                        RetryOnTimeout = true,
                        RetryOnRateLimit = true,
                        RetryOnServerError = true
                    }
                };

                RogueHttpResponse response;
                using (Measure("HackableCrateTracker", "DiscordWebhook"))
                    response = await Rogue.Http.SendAsync(request);

                if (response == null || response.StatusCode < 200 || response.StatusCode >= 300)
                    LogWarning("Networking", "Discord webhook failed: HTTP " +
                        (response == null ? 0 : response.StatusCode) + ".");
            }
            catch (Exception exception)
            {
                LogWarning("Networking", "Discord webhook failed: " + exception.Message);
            }
        }

}
}
