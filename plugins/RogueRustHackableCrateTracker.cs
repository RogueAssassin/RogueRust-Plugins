using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustHackableCrateTracker", "RogueAssassin", "2.2.0")]
    [Description("Reports who starts hacking a locked crate, who loots it, and crates left unopened after they unlock.")]
    public sealed class RogueRustHackableCrateTracker : RogueRustPlugin
    {
        private const string PluginVersion = "2.2.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 2, 0);
        [RoguePermission]
        private const string PermissionUse = "roguerusthackablecratetracker.use";

        [RoguePermission]
        private const string PermissionAdmin = "roguerusthackablecratetracker.admin";
        private const ulong ChatIconSteamId = 76561198641214711UL;

        // Discord embed colours.
        private const int ColourStarted = 16753920;
        private const int ColourUnlocked = 3447003;
        private const int ColourUnopened = 15158332;
        private const int ColourLooted = 5763719;

        private Configuration _config;
        private readonly Dictionary<ulong, TrackedCrate> _tracked = new Dictionary<ulong, TrackedCrate>();

        private sealed class TrackedCrate
        {
            public HackableLockedCrate Crate;
            public ulong HackerId;
            public string HackerName = string.Empty;
            public Vector3 Position;
            public string Grid = string.Empty;
            public bool Started;
            public bool Unlocked;
            public bool Looted;
        }

        #region Configuration

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
        }

        private sealed class ReportingSettings
        {
            [JsonProperty("Enable In-Game Reporting")]
            public bool Chat = true;

            [JsonProperty("Only Send In-Game Reports to Permitted Players")]
            public bool RestrictChat;

            [JsonProperty("Enable Console Reporting")]
            public bool Console = true;

            [JsonProperty("Report When A Player Starts Hacking A Crate")]
            public bool HackStarted = true;

            [JsonProperty("Report The Moment A Crate Unlocks")]
            public bool Unlocked;

            [JsonProperty("Report A Crate Still Unopened This Many Minutes After It Unlocked (0 = Off)")]
            public float UnopenedMinutes = 3f;

            [JsonProperty("Report The Player Who Loots A Crate")]
            public bool Looted = true;
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
            [JsonProperty("Include Exact XYZ Coordinates In Console And Discord Reports")]
            public bool IncludeCoordinates = true;
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
                LogError("Configuration", "Invalid configuration: " + exception.Message + " Loading safe defaults.");
                _config = new Configuration();
            }

            if (_config == null) _config = new Configuration();
            if (_config.Reporting == null) _config.Reporting = new ReportingSettings();
            if (_config.Discord == null) _config.Discord = new DiscordSettings();
            if (_config.Location == null) _config.Location = new LocationSettings();
            if (_config.Discord.WebhookUrl == null) _config.Discord.WebhookUrl = string.Empty;
            if (_config.Discord.MapUrlTemplate == null) _config.Discord.MapUrlTemplate = string.Empty;
            _config.Reporting.UnopenedMinutes = Mathf.Clamp(_config.Reporting.UnopenedMinutes, 0f, 120f);
            _config.Version = CurrentVersion;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Lifecycle

        private void Init()
        {
            LogInformation("Lifecycle", Name + " " + PluginVersion + " initialized.");
        }

        private void OnServerInitialized()
        {
            if (_config.Discord.Enabled && !IsValidWebhook(_config.Discord.WebhookUrl))
                LogWarning("Networking", "Discord reporting is enabled, but the webhook URL is empty or invalid.");
        }

        private void Unload()
        {
            _tracked.Clear();
        }

        #endregion

        #region Crate hooks

        // Asked before the game accepts a hack. Nothing is reported here; it only records who pressed
        // the button, because the hook that follows does not say. Returning null leaves the decision to Rust.
        private object CanHackCrate(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null || crate.net == null || crate.IsDestroyed) return null;

            TrackedCrate state = GetOrCreate(crate);
            if (!state.Started)
            {
                state.HackerId = player.userID;
                state.HackerName = player.displayName ?? string.Empty;
            }
            return null;
        }

        // The hack has started and the countdown is running.
        private void OnCrateHack(HackableLockedCrate crate)
        {
            if (crate == null || crate.net == null || crate.IsDestroyed) return;

            TrackedCrate state = GetOrCreate(crate);
            if (state.Started) return;
            state.Started = true;

            if (state.HackerId == 0UL && crate.originalHackerPlayerId != 0UL)
            {
                state.HackerId = crate.originalHackerPlayerId;
                BasePlayer hacker = BasePlayer.FindByID(state.HackerId) ?? BasePlayer.FindSleeping(state.HackerId);
                if (hacker != null) state.HackerName = hacker.displayName ?? string.Empty;
            }
            if (state.HackerName.Length == 0) state.HackerName = Message("Player.Unknown");

            float seconds = Mathf.Max(1f, HackableLockedCrate.requiredHackSeconds - crate.hackSeconds);
            if (_config.Reporting.HackStarted)
                Report(state, "Hack.Started", ColourStarted, CleanName(state.HackerName), state.Grid, FormatDuration(seconds));

            // OnCrateHackEnd is the authoritative signal; this covers a framework build that does not raise it.
            ulong id = crate.net.ID.Value;
            Delay(TimeSpan.FromSeconds(seconds + 2f), () => MarkUnlocked(id), "crate-unlock-" + id);
        }

        private void OnCrateHackEnd(HackableLockedCrate crate)
        {
            if (crate != null && crate.net != null) MarkUnlocked(crate.net.ID.Value);
        }

        private void MarkUnlocked(ulong id)
        {
            TrackedCrate state;
            if (!_tracked.TryGetValue(id, out state) || state.Unlocked) return;
            if (state.Crate == null || state.Crate.IsDestroyed)
            {
                _tracked.Remove(id);
                return;
            }
            if (!state.Crate.HasFlag(HackableLockedCrate.Flag_FullyHacked)) return;

            state.Unlocked = true;
            if (_config.Reporting.Unlocked)
                Report(state, "Hack.Unlocked", ColourUnlocked, state.Grid);

            float minutes = _config.Reporting.UnopenedMinutes;
            if (minutes > 0f)
                Delay(TimeSpan.FromMinutes(minutes), () => RemindUnopened(id), "crate-unopened-" + id);
        }

        private void RemindUnopened(ulong id)
        {
            TrackedCrate state;
            if (!_tracked.TryGetValue(id, out state) || state.Looted) return;
            if (state.Crate == null || state.Crate.IsDestroyed)
            {
                _tracked.Remove(id);
                return;
            }

            Report(state, "Hack.Unopened", ColourUnopened, state.Grid,
                _config.Reporting.UnopenedMinutes.ToString("0.#", CultureInfo.InvariantCulture));
        }

        // Typed, so Oxide only calls this when the container being opened is a hackable crate.
        private void OnLootEntity(BasePlayer player, HackableLockedCrate crate)
        {
            if (player == null || crate == null || crate.net == null) return;
            if (!crate.HasFlag(HackableLockedCrate.Flag_FullyHacked)) return;

            TrackedCrate state = GetOrCreate(crate);
            if (state.Looted) return;
            state.Unlocked = true;
            state.Looted = true;

            if (_config.Reporting.Looted)
                Report(state, "Hack.Looted", ColourLooted, CleanName(player.displayName), state.Grid);
        }

        // Typed so Oxide only calls this for hackable crates instead of every entity the server removes.
        private void OnEntityKill(HackableLockedCrate crate)
        {
            if (crate != null && crate.net != null) _tracked.Remove(crate.net.ID.Value);
        }

        private TrackedCrate GetOrCreate(HackableLockedCrate crate)
        {
            ulong id = crate.net.ID.Value;
            TrackedCrate state;
            if (_tracked.TryGetValue(id, out state)) return state;

            Vector3 position = crate.transform.position;
            state = new TrackedCrate
            {
                Crate = crate,
                Position = position,
                Grid = MapHelper.PositionToString(position)
            };
            _tracked[id] = state;
            return state;
        }

        #endregion

        #region Reporting

        private void Report(TrackedCrate state, string messageKey, int discordColour, params object[] arguments)
        {
            ReportingSettings settings = _config.Reporting;

            if (settings.Chat)
            {
                foreach (BasePlayer recipient in BasePlayer.activePlayerList)
                {
                    if (recipient == null || !recipient.IsConnected) continue;
                    if (settings.RestrictChat && !HasAccess(recipient)) continue;
                    string text = Message("Chat.Prefix", recipient) +
                                  string.Format(CultureInfo.InvariantCulture, Message(messageKey, recipient), arguments);
                    recipient.SendConsoleCommand("chat.add", 2, ChatIconSteamId, text);
                }
            }

            bool discord = _config.Discord.Enabled && IsValidWebhook(_config.Discord.WebhookUrl);
            if (!settings.Console && !discord) return;

            // Console and Discord get the server-language text without colour tags.
            string plain = StripRichText(string.Format(CultureInfo.InvariantCulture, Message(messageKey), arguments));
            if (settings.Console)
                LogInformation("CrateEvent", plain + " | " + FormatCoordinates(state.Position) +
                    " | hacker=" + state.HackerName + " (" + state.HackerId + ")");
            if (discord) SendDiscordWebhook(BuildDiscordPayload(plain, state, discordColour));
        }

        private bool HasAccess(BasePlayer player) =>
            player.IsAdmin || Rogue.Permissions.Has(player.UserIDString, PermissionUse) ||
            Rogue.Permissions.Has(player.UserIDString, PermissionAdmin);

        private static readonly Regex RichText = new Regex("<[^>]+>", RegexOptions.Compiled);

        private static string StripRichText(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : RichText.Replace(text, string.Empty);

        // A player name can contain rich-text tags; keep them out of the announcement.
        private static string CleanName(string name) => StripRichText(name);

        private static string FormatCoordinates(Vector3 position) =>
            string.Format(CultureInfo.InvariantCulture, "X {0:0.0}, Y {1:0.0}, Z {2:0.0}", position.x, position.y, position.z);

        private static string FormatDuration(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            if (total < 60) return total.ToString(CultureInfo.InvariantCulture) + "s";
            int minutes = total / 60;
            int rest = total % 60;
            return rest == 0
                ? minutes.ToString(CultureInfo.InvariantCulture) + "m"
                : minutes.ToString(CultureInfo.InvariantCulture) + "m " + rest.ToString(CultureInfo.InvariantCulture) + "s";
        }

        #endregion

        #region Discord

        private Dictionary<string, object> BuildDiscordPayload(string title, TrackedCrate state, int colour)
        {
            var fields = new List<Dictionary<string, object>> { Field("Grid", state.Grid, true) };
            if (_config.Location.IncludeCoordinates) fields.Add(Field("Coordinates", FormatCoordinates(state.Position), true));
            if (state.HackerId != 0UL) fields.Add(Field("Hacker", state.HackerName + " (" + state.HackerId + ")", false));

            string mapUrl = BuildMapUrl(state);
            var embed = new Dictionary<string, object>
            {
                ["title"] = title,
                ["description"] = string.IsNullOrEmpty(mapUrl) ? "Location: `" + state.Grid + "`" : "[Open map waypoint](" + mapUrl + ")",
                ["color"] = colour,
                ["fields"] = fields,
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };
            return new Dictionary<string, object> { ["embeds"] = new[] { embed } };
        }

        private static Dictionary<string, object> Field(string name, string value, bool inline) =>
            new Dictionary<string, object> { ["name"] = name, ["value"] = value, ["inline"] = inline };

        private string BuildMapUrl(TrackedCrate state)
        {
            string template = _config.Discord.MapUrlTemplate;
            if (string.IsNullOrWhiteSpace(template)) return string.Empty;

            return template
                .Replace("{grid}", Uri.EscapeDataString(state.Grid ?? string.Empty))
                .Replace("{x}", state.Position.x.ToString("0.0", CultureInfo.InvariantCulture))
                .Replace("{z}", state.Position.z.ToString("0.0", CultureInfo.InvariantCulture))
                .Replace("{worldsize}", World.WorldSize.ToString("0", CultureInfo.InvariantCulture))
                .Replace("{seed}", global::World.Seed.ToString(CultureInfo.InvariantCulture));
        }

        private static bool IsValidWebhook(string url) =>
            !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out Uri parsed) && parsed.Scheme == Uri.UriSchemeHttps;

        private async void SendDiscordWebhook(Dictionary<string, object> payload)
        {
            try
            {
                RogueHttpRequest request = new RogueHttpRequest(_config.Discord.WebhookUrl, RogueHttpMethod.Post)
                {
                    Owner = Name,
                    Name = "discord-webhook",
                    Body = JsonConvert.SerializeObject(payload),
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

                RogueHttpResponse response = await Rogue.Http.SendAsync(request);
                if (response == null || response.StatusCode < 200 || response.StatusCode >= 300)
                    LogWarning("Networking", "Discord webhook failed: HTTP " + (response == null ? 0 : response.StatusCode) + ".");
            }
            catch (Exception exception)
            {
                LogWarning("Networking", "Discord webhook failed: " + exception.Message);
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
                    ["Chat.Prefix"] = "<color=#F2A900>[Locked Crate]</color> ",
                    ["Player.Unknown"] = "Someone",
                    ["Hack.Started"] = "<color=#C4FF00>{0}</color> started hacking a locked crate at <color=#C4FF00>{1}</color>. It unlocks in {2}.",
                    ["Hack.Unlocked"] = "The locked crate at <color=#C4FF00>{0}</color> has unlocked.",
                    ["Hack.Unopened"] = "The locked crate at <color=#C4FF00>{0}</color> unlocked {1} minutes ago and nobody has opened it.",
                    ["Hack.Looted"] = "<color=#C4FF00>{0}</color> has looted the locked crate at <color=#C4FF00>{1}</color>."
                };
            }
        }

        #endregion
    }
}
