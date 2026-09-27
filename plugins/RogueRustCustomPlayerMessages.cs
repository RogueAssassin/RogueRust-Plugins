using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;

namespace Oxide.Plugins
{
    [Info("CustomPlayerMessages", "RogueAssassin", "3.1.0")]
    [Description("Custom connect/disconnect messages and public player-list commands powered by RogueRust.")]
    public sealed class RogueRustCustomPlayerMessages : RogueRustPlugin
    {
        [RoguePermission]
        private const string HiddenPermission = "roguerustcustomplayermessages.hidden";
        [RoguePermission]
        private const string AdminPermission = "roguerustcustomplayermessages.admin";
        private const string DataKey = "RogueRustCustomPlayerMessages/country-cache";
        private const string LegacyDataKey = "CustomPlayerMessages/country-cache";
        private const string LangPluginDirectory = "RogueRustCustomPlayerMessages";
        private const string LegacyLangPluginDirectory = "CustomPlayerMessages";
        private const string LanguageFileName = "messages.json";
        private const string DefaultLanguage = "en";
        private const string DefaultCountryLookupUrl = "http://ip-api.com/json/{0}?fields=status,message,country,countryCode";

        private const string PluginVersion = "3.1.0";

        private static readonly VersionNumber CurrentVersion = new VersionNumber(3, 1, 0);

        private Configuration _config;
        private CountryCacheStore _cacheStore;
        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextCacheCleanupUtc = DateTime.MinValue;
        private bool _cacheDirty;
        private bool _isUnloading;
        private readonly Dictionary<string, List<ulong>> _pendingCountryLookups =
            new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTime> _failedCountryLookups =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        private sealed class Configuration
        {
            [JsonProperty("Message Settings", Order = 10)]
            public MessageSettings Messages = new MessageSettings();

            [JsonProperty("Country Lookup Settings", Order = 20)]
            public CountryLookupSettings CountryLookup = new CountryLookupSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class MessageSettings
        {
            [JsonProperty("Show join message")]
            public bool ShowJoinMessage = true;

            [JsonProperty("Show leave message")]
            public bool ShowLeaveMessage = true;

            [JsonProperty("Show disconnect reason")]
            public bool ShowDisconnectReason = true;

            [JsonProperty("Show country in join message")]
            public bool ShowCountryMessage = true;

            [JsonProperty("Use country code instead of country name")]
            public bool UseCountryCode = false;
        }

        private sealed class CountryLookupSettings
        {
            [JsonProperty("Country lookup timeout in seconds")]
            public float TimeoutSeconds = 5f;

            [JsonProperty("Country cache lifetime in hours")]
            public int CacheHours = 24;

            [JsonProperty("Failed country lookup cache lifetime in minutes")]
            public int FailedCacheMinutes = 5;

            [JsonProperty("Minimum seconds between requests to the country lookup host")]
            public float MinimumHostIntervalSeconds = 0.25f;

            [JsonProperty("Country lookup URL (HTTP is required by ip-api's free endpoint)")]
            public string Url = DefaultCountryLookupUrl;
        }

        private sealed class LegacyConfiguration
        {
            [JsonProperty("Show join message")] public bool ShowJoinMessage = true;
            [JsonProperty("Show leave message")] public bool ShowLeaveMessage = true;
            [JsonProperty("Show disconnect reason")] public bool ShowDisconnectReason = true;
            [JsonProperty("Show country in join message")] public bool ShowCountryMessage = true;
            [JsonProperty("Use country code instead of country name")] public bool UseCountryCode;
            [JsonProperty("Country lookup timeout in seconds")] public float CountryLookupTimeout = 5f;
            [JsonProperty("Country cache lifetime in hours")] public int CountryCacheHours = 24;
            [JsonProperty("Failed country lookup cache lifetime in minutes")] public int FailedCountryLookupCacheMinutes = 5;
            [JsonProperty("Minimum seconds between requests to the country lookup host")] public float CountryLookupMinimumHostInterval = 0.25f;
            [JsonProperty("Country lookup URL (HTTP is required by ip-api's free endpoint)")] public string CountryLookupUrl = DefaultCountryLookupUrl;
        }

        private sealed class CountryResponse
        {
            [JsonProperty("status")] public string Status;
            [JsonProperty("message")] public string Message;
            [JsonProperty("country")] public string Country;
            [JsonProperty("countryCode")] public string CountryCode;
        }

        private sealed class CountryCacheEntry
        {
            [JsonProperty("value")] public string Value;
            [JsonProperty("expiresUtc")] public DateTime ExpiresAtUtc;
        }

        private sealed class CountryCacheStore
        {
            [JsonProperty("entries")]
            public Dictionary<string, CountryCacheEntry> Entries =
                new Dictionary<string, CountryCacheEntry>(StringComparer.OrdinalIgnoreCase);
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
                Dictionary<string, object> raw = Config.ReadObject<Dictionary<string, object>>();
                bool structured = raw != null &&
                    (raw.ContainsKey("Message Settings") || raw.ContainsKey("Country Lookup Settings"));

                if (structured)
                {
                    _config = Config.ReadObject<Configuration>() ?? new Configuration();
                }
                else
                {
                    LegacyConfiguration legacy = Config.ReadObject<LegacyConfiguration>() ?? new LegacyConfiguration();
                    _config = new Configuration
                    {
                        Messages = new MessageSettings
                        {
                            ShowJoinMessage = legacy.ShowJoinMessage,
                            ShowLeaveMessage = legacy.ShowLeaveMessage,
                            ShowDisconnectReason = legacy.ShowDisconnectReason,
                            ShowCountryMessage = legacy.ShowCountryMessage,
                            UseCountryCode = legacy.UseCountryCode
                        },
                        CountryLookup = new CountryLookupSettings
                        {
                            TimeoutSeconds = legacy.CountryLookupTimeout,
                            CacheHours = legacy.CountryCacheHours,
                            FailedCacheMinutes = legacy.FailedCountryLookupCacheMinutes,
                            MinimumHostIntervalSeconds = legacy.CountryLookupMinimumHostInterval,
                            Url = legacy.CountryLookupUrl
                        },
                        Version = CurrentVersion
                    };
                    LogInformation("Configuration", "Migrated v3.0.0 flat configuration to the RogueRust family layout.");
                }
            }
            catch (Exception exception)
            {
                PrintError("Invalid configuration; defaults will be used: " + exception.Message);
                _config = new Configuration();
            }

            NormalizeConfig();
            SaveConfig();
        }

        private void NormalizeConfig()
        {
            _config ??= new Configuration();
            _config.Messages ??= new MessageSettings();
            _config.CountryLookup ??= new CountryLookupSettings();
            _config.Version = CurrentVersion;

            _config.CountryLookup.TimeoutSeconds = Clamp(_config.CountryLookup.TimeoutSeconds, 1f, 30f);
            _config.CountryLookup.CacheHours = Math.Max(1, Math.Min(_config.CountryLookup.CacheHours, 168));
            _config.CountryLookup.FailedCacheMinutes = Math.Max(1, Math.Min(_config.CountryLookup.FailedCacheMinutes, 60));
            _config.CountryLookup.MinimumHostIntervalSeconds =
                Clamp(_config.CountryLookup.MinimumHostIntervalSeconds, 0f, 10f);

            if (string.IsNullOrWhiteSpace(_config.CountryLookup.Url) || !_config.CountryLookup.Url.Contains("{0}"))
                _config.CountryLookup.Url = DefaultCountryLookupUrl;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        protected override void LoadDefaultMessages()
        {
            // Localization uses lang/<language>/RogueRust/RogueRustCustomPlayerMessages/messages.json.
        }

        private void Init()
        {
            LoadLanguageFiles();
            LoadCountryCache();

            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustCustomPlayerMessages/; Lang=<language>/RogueRust/RogueRustCustomPlayerMessages/messages.json");
        }

        private void OnServerInitialized()
        {
            CleanupCountryCache(true);
        }

        private void Unload()
        {
            _isUnloading = true;
            _pendingCountryLookups.Clear();
            _failedCountryLookups.Clear();
            SaveCountryCache();
        }

        [RogueCommand(
            "roguerust.online",
            Aliases = new[] { "online" },
            Description = "Shows the number of online players.",
            Usage = "/online",
            Category = "Players",
            CooldownSeconds = 1,
            AllowConsole = true)]
        private RogueCommandResult OnlineCommand(RogueCommandContext context)
        {
            BasePlayer requester = context.NativePlayer;
            bool includeHidden = CanSeeHidden(requester);
            int count = 0;

            foreach (BasePlayer connected in BasePlayer.activePlayerList)
            {
                if (connected == null || (!includeHidden && HasHiddenPermission(connected)))
                    continue;
                count++;
            }

            return RogueCommandResult.Ok(GetMessage("PlayerCount", requester, count));
        }

        [RogueCommand(
            "roguerust.players",
            Aliases = new[] { "players", "who" },
            Description = "Lists online players.",
            Usage = "/players",
            Category = "Players",
            CooldownSeconds = 1,
            AllowConsole = true)]
        private RogueCommandResult PlayersCommand(RogueCommandContext context)
        {
            BasePlayer requester = context.NativePlayer;
            bool includeHidden = CanSeeHidden(requester);
            StringBuilder names = new StringBuilder(Math.Max(64, BasePlayer.activePlayerList.Count * 24));
            int visibleCount = 0;
            bool requesterIsOnlyVisiblePlayer = false;

            foreach (BasePlayer connected in BasePlayer.activePlayerList)
            {
                if (connected == null || (!includeHidden && HasHiddenPermission(connected)))
                    continue;

                if (visibleCount > 0)
                    names.Append(", ");
                names.Append(EscapeRichText(connected.displayName));
                visibleCount++;
                requesterIsOnlyVisiblePlayer = visibleCount == 1 && requester != null && connected.userID == requester.userID;
            }

            if (visibleCount == 0)
                return RogueCommandResult.Ok(GetMessage("NobodyOnline", requester));

            if (visibleCount == 1 && requesterIsOnlyVisiblePlayer)
                return RogueCommandResult.Ok(GetMessage("OnlyYou", requester));

            return RogueCommandResult.Ok(GetMessage("OnlinePlayersList", requester, visibleCount, names.ToString()));
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            ExecuteProtected(delegate { HandlePlayerConnected(player); }, "OnPlayerConnected");
        }

        private void HandlePlayerConnected(BasePlayer player)
        {
            if (!_config.Messages.ShowJoinMessage || player == null || HasHiddenPermission(player))
                return;

            CleanupCountryCache(false);

            if (!_config.Messages.ShowCountryMessage)
            {
                Broadcast("Join Message", player);
                return;
            }

            IPAddress address;
            if (!TryGetAddress(player.net != null && player.net.connection != null ? player.net.connection.ipaddress : null, out address))
            {
                Broadcast("Join Message", player);
                return;
            }

            if (!IsPublicAddress(address))
            {
                Broadcast("Join Country Message", player, GetMessage("Local Network", player));
                return;
            }

            string addressText = address.ToString();
            string cacheKey = HashAddress(addressText);
            DateTime now = DateTime.UtcNow;
            CountryCacheEntry cached;
            if (_cacheStore.Entries.TryGetValue(cacheKey, out cached) && cached != null && cached.ExpiresAtUtc > now)
            {
                BroadcastIfStillConnected(player, "Join Country Message", cached.Value);
                return;
            }

            DateTime failedUntil;
            if (_failedCountryLookups.TryGetValue(cacheKey, out failedUntil))
            {
                if (failedUntil > now)
                {
                    BroadcastIfStillConnected(player, "Join Message");
                    return;
                }
                _failedCountryLookups.Remove(cacheKey);
            }

            List<ulong> waiters;
            if (_pendingCountryLookups.TryGetValue(cacheKey, out waiters))
            {
                if (!waiters.Contains(player.userID))
                    waiters.Add(player.userID);
                return;
            }

            _pendingCountryLookups[cacheKey] = new List<ulong>(2) { player.userID };
            string url = string.Format(_config.CountryLookup.Url, Uri.EscapeDataString(addressText));
            FetchCountry(cacheKey, url);
        }

        private async void FetchCountry(string cacheKey, string url)
        {
            try
            {
                RogueHttpRequest request = new RogueHttpRequest(url, RogueHttpMethod.Get)
                {
                    Owner = Name,
                    Name = "country-lookup",
                    Timeout = TimeSpan.FromSeconds(_config.CountryLookup.TimeoutSeconds),
                    MaximumResponseBytes = 32 * 1024,
                    MinimumHostInterval = TimeSpan.FromSeconds(_config.CountryLookup.MinimumHostIntervalSeconds),
                    RetryPolicy = new RogueHttpRetryPolicy
                    {
                        MaxAttempts = 2,
                        InitialDelay = TimeSpan.FromSeconds(2),
                        MaximumDelay = TimeSpan.FromSeconds(2),
                        BackoffMultiplier = 1d,
                        RetryOnTimeout = true,
                        RetryOnRateLimit = true,
                        RetryOnServerError = true
                    }
                };

                RogueHttpResponse response;
                using (Measure("CustomPlayerMessages", "CountryLookup"))
                    response = await Rogue.Http.SendAsync(request);

                if (_isUnloading)
                    return;

                NextTick(delegate
                {
                    if (!_isUnloading)
                        ExecuteProtected(delegate { ProcessCountryResponse(cacheKey, response); }, "CountryLookupResponse");
                });
            }
            catch (Exception exception)
            {
                if (_isUnloading)
                    return;

                NextTick(delegate
                {
                    if (_isUnloading)
                        return;
                    LogWarning("Networking", "Country lookup failed: " + exception.Message);
                    CompleteCountryLookup(cacheKey, null, true);
                });
            }
        }

        private void ProcessCountryResponse(string cacheKey, RogueHttpResponse response)
        {
            if (response == null || !response.Success || response.StatusCode != 200 || string.IsNullOrWhiteSpace(response.Content))
            {
                LogWarning("Networking", "Country lookup failed for a player; using the basic join message. " +
                    (response == null ? "No response." : "HTTP " + response.StatusCode + ": " + (response.Error ?? "request failed")));
                CompleteCountryLookup(cacheKey, null, true);
                return;
            }

            CountryResponse result;
            try
            {
                result = JsonConvert.DeserializeObject<CountryResponse>(response.Content);
            }
            catch (JsonException exception)
            {
                LogWarning("Networking", "Country lookup returned invalid JSON: " + exception.Message);
                CompleteCountryLookup(cacheKey, null, true);
                return;
            }

            if (result == null || !string.Equals(result.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                LogWarning("Networking", "Country lookup was unsuccessful: " + (result == null ? "empty response" : result.Message));
                CompleteCountryLookup(cacheKey, null, true);
                return;
            }

            string country = _config.Messages.UseCountryCode ? result.CountryCode : result.Country;
            if (string.IsNullOrWhiteSpace(country))
            {
                CompleteCountryLookup(cacheKey, null, true);
                return;
            }

            country = EscapeRichText(country.Trim());
            _cacheStore.Entries[cacheKey] = new CountryCacheEntry
            {
                Value = country,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(_config.CountryLookup.CacheHours)
            };
            _cacheDirty = true;
            _failedCountryLookups.Remove(cacheKey);

            Debounce("country-cache-save", TimeSpan.FromSeconds(2), SaveCountryCache);
            CompleteCountryLookup(cacheKey, country, false);
        }

        private void CompleteCountryLookup(string cacheKey, string country, bool cacheFailure)
        {
            List<ulong> waiters;
            if (!_pendingCountryLookups.TryGetValue(cacheKey, out waiters))
                return;

            _pendingCountryLookups.Remove(cacheKey);
            if (cacheFailure)
                _failedCountryLookups[cacheKey] = DateTime.UtcNow.AddMinutes(_config.CountryLookup.FailedCacheMinutes);

            for (int i = 0; i < waiters.Count; i++)
            {
                BasePlayer player = BasePlayer.FindByID(waiters[i]);
                if (player == null || !player.IsConnected || HasHiddenPermission(player))
                    continue;

                if (string.IsNullOrEmpty(country))
                    Broadcast("Join Message", player);
                else
                    Broadcast("Join Country Message", player, country);
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            ExecuteProtected(delegate
            {
                if (player == null)
                    return;

                RemovePendingPlayer(player.userID);

                if (!_config.Messages.ShowLeaveMessage || HasHiddenPermission(player))
                    return;

                Broadcast("Leave Message", player,
                    _config.Messages.ShowDisconnectReason ? EscapeRichText(reason ?? string.Empty) : string.Empty);
            }, "OnPlayerDisconnected");
        }

        private void RemovePendingPlayer(ulong userId)
        {
            foreach (KeyValuePair<string, List<ulong>> pair in _pendingCountryLookups)
                pair.Value.Remove(userId);
        }

        private void LoadCountryCache()
        {
            _cacheStore = LoadData(DataKey, delegate { return new CountryCacheStore(); });
            if (_cacheStore == null)
                _cacheStore = new CountryCacheStore();
            if (_cacheStore.Entries == null)
                _cacheStore.Entries = new Dictionary<string, CountryCacheEntry>(StringComparer.OrdinalIgnoreCase);

            // Migrate the previous RogueRust data key only when the new cache is empty.
            if (_cacheStore.Entries.Count == 0)
            {
                CountryCacheStore legacy = LoadData(LegacyDataKey, delegate { return new CountryCacheStore(); });
                if (legacy != null && legacy.Entries != null && legacy.Entries.Count > 0)
                {
                    _cacheStore.Entries = legacy.Entries;
                    _cacheDirty = true;
                    SaveCountryCache();
                    LogInformation("Data", "Migrated country cache to RogueRust/" + DataKey + ".json.");
                }
            }

            CleanupCountryCache(false);
        }

        private void SaveCountryCache()
        {
            if (_cacheStore == null || !_cacheDirty)
                return;
            SaveData(DataKey, _cacheStore);
            _cacheDirty = false;
        }

        private void CleanupCountryCache(bool saveIfChanged)
        {
            if (_cacheStore == null || _cacheStore.Entries == null)
                return;

            DateTime now = DateTime.UtcNow;
            if (!saveIfChanged && now < _nextCacheCleanupUtc)
                return;

            _nextCacheCleanupUtc = now.AddHours(1);
            List<string> expired = null;
            foreach (KeyValuePair<string, CountryCacheEntry> pair in _cacheStore.Entries)
            {
                if (pair.Value != null && pair.Value.ExpiresAtUtc > now)
                    continue;

                if (expired == null)
                    expired = new List<string>();
                expired.Add(pair.Key);
            }

            bool changed = false;
            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++)
                    changed |= _cacheStore.Entries.Remove(expired[i]);
            }

            List<string> failedExpired = null;
            foreach (KeyValuePair<string, DateTime> pair in _failedCountryLookups)
            {
                if (pair.Value > now)
                    continue;

                if (failedExpired == null)
                    failedExpired = new List<string>();
                failedExpired.Add(pair.Key);
            }

            if (failedExpired != null)
            {
                for (int i = 0; i < failedExpired.Count; i++)
                    _failedCountryLookups.Remove(failedExpired[i]);
            }

            if (!changed)
                return;

            _cacheDirty = true;
            if (saveIfChanged)
                SaveCountryCache();
            else
                Debounce("country-cache-save", TimeSpan.FromSeconds(2), SaveCountryCache);
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

            // Previous RogueRust layout: lang/RogueRust/CustomPlayerMessages/<language>.json.
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
                string legacyOriginal = Path.Combine(langRoot, language, "CustomPlayerMessages.json");

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
                Dictionary<string, string> loaded =
                    JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(target));
                if (loaded != null)
                    _messages[language] = loaded;
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Could not load language file '" + target + "': " + exception.Message);
            }
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Join Country Message"] = "[{1}] {0} joined the game.",
                ["Join Message"] = "{0} joined the game.",
                ["Leave Message"] = "{0} left the game.{1}",
                ["Local Network"] = "Local Network",
                ["NobodyOnline"] = "No players are currently online.",
                ["OnlyYou"] = "You are the only one online!",
                ["PlayerCount"] = "{0} player(s) online.",
                ["OnlinePlayersList"] = "Players online ({0}): {1}"
            };
        }

        private static void WriteLanguageFile(string path, Dictionary<string, string> messages)
        {
            File.WriteAllText(path, JsonConvert.SerializeObject(messages, Formatting.Indented));
        }

        private string GetMessage(string key, BasePlayer player, params object[] arguments)
        {
            string language = DefaultLanguage;
            if (player != null)
            {
                string selected = lang.GetLanguage(player.UserIDString);
                if (!string.IsNullOrWhiteSpace(selected))
                    language = selected;
            }

            Dictionary<string, string> catalog;
            string template;
            if (!_messages.TryGetValue(language, out catalog) || !catalog.TryGetValue(key, out template))
            {
                if (!_messages.TryGetValue(DefaultLanguage, out catalog) || !catalog.TryGetValue(key, out template))
                    template = key;
            }

            if (arguments == null || arguments.Length == 0)
                return template;

            try
            {
                return string.Format(template, arguments);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        private void BroadcastIfStillConnected(BasePlayer player, string key, string detail = null)
        {
            if (player != null && player.IsConnected)
                Broadcast(key, player, detail);
        }

        private void Broadcast(string key, BasePlayer player, string detail = null)
        {
            string playerName = EscapeRichText(player == null ? "Unknown" : player.displayName);
            string detailValue = detail ?? string.Empty;
            if (string.Equals(key, "Leave Message", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(detailValue))
                detailValue = " (" + detailValue + ")";
            string message = GetMessage(key, null, playerName, detailValue);
            Server.Broadcast(message.Trim());
        }

        private bool HasHiddenPermission(BasePlayer player)
        {
            return player != null && Rogue.Permissions.Has(player.UserIDString, HiddenPermission);
        }

        private bool CanSeeHidden(BasePlayer player)
        {
            return player == null || player.IsAdmin || Rogue.Permissions.Has(player.UserIDString, AdminPermission);
        }

        private void ExecuteProtected(Action action, string operation)
        {
            if (action == null)
                return;

            try
            {
                using (Measure("CustomPlayerMessages", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOf('<') < 0 && value.IndexOf('>') < 0)
                return value;
            return value.Replace("<", "‹").Replace(">", "›");
        }

        private static string HashAddress(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(value, maximum));
        }

        private static bool ContainsSingleColon(string value)
        {
            bool found = false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != ':')
                    continue;
                if (found)
                    return false;
                found = true;
            }
            return found;
        }

        private static bool TryGetAddress(string input, out IPAddress address)
        {
            address = null;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string candidate = input.Trim();
            if (candidate.StartsWith("[", StringComparison.Ordinal))
            {
                int closingBracket = candidate.IndexOf(']');
                if (closingBracket > 1)
                    candidate = candidate.Substring(1, closingBracket - 1);
            }
            else if (ContainsSingleColon(candidate))
            {
                int colon = candidate.LastIndexOf(':');
                if (colon > 0)
                    candidate = candidate.Substring(0, colon);
            }

            return IPAddress.TryParse(candidate, out address);
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            if (address == null || IPAddress.IsLoopback(address))
                return false;

            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                return !(bytes[0] == 10 ||
                         bytes[0] == 127 ||
                         (bytes[0] == 169 && bytes[1] == 254) ||
                         (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                         (bytes[0] == 192 && bytes[1] == 168) ||
                         (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) ||
                         bytes[0] >= 224);
            }

            return !(address.IsIPv6LinkLocal ||
                     address.IsIPv6SiteLocal ||
                     address.IsIPv6Multicast ||
                     (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc));
        }
    }
}
