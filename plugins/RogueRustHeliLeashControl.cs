using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("RogueRustHeliLeashControl", "RogueAssassin", "2.1.0")]
    [Description("Keeps a heavily damaged Patrol Helicopter near the last valid attacker.")]
    public sealed class RogueRustHeliLeashControl : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);
        private const ulong ChatIconSteamId = 76561198639403056UL;
        private ConfigData _config;
        private float _maxDistanceSquared;
        private readonly Dictionary<BaseHelicopter, LeashState> _trackedHelicopters =
            new Dictionary<BaseHelicopter, LeashState>();

        private class ConfigData
        {
            [JsonProperty("Leash Settings", Order = 10)]
            public LeashSettings Leash = new LeashSettings();

            [JsonProperty("Message Settings", Order = 20)]
            public MessageSettings Messages = new MessageSettings();

            [JsonProperty("Developer Settings", Order = 30)]
            public DeveloperSettings Developer = new DeveloperSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            [JsonIgnore] public bool EnableLeash { get { return Leash.Enabled; } set { Leash.Enabled = value; } }
            [JsonIgnore] public float HealthThreshold { get { return Leash.HealthThreshold; } set { Leash.HealthThreshold = value; } }
            [JsonIgnore] public float MaxDistance { get { return Leash.MaxDistance; } set { Leash.MaxDistance = value; } }
            [JsonIgnore] public float CheckInterval { get { return Leash.CheckInterval; } set { Leash.CheckInterval = value; } }
            [JsonIgnore] public bool EnableDebug { get { return Developer.EnableDebug; } set { Developer.EnableDebug = value; } }
            [JsonIgnore] public bool SendChatMessage { get { return Messages.SendChatMessage; } set { Messages.SendChatMessage = value; } }
            [JsonIgnore] public float MessageCooldown { get { return Messages.MessageCooldown; } set { Messages.MessageCooldown = value; } }
            [JsonIgnore] public string GlobalMessageFormat { get { return Messages.GlobalMessageFormat; } set { Messages.GlobalMessageFormat = value; } }
        }

        private class LeashSettings
        {
            [JsonProperty("Enable Leash Behavior")]
            public bool Enabled = true;

            [JsonProperty("Health Threshold to Enable Leash")]
            public float HealthThreshold = 400f;

            [JsonProperty("Maximum Allowed Distance From Last Attacker")]
            public float MaxDistance = 150f;

            [JsonProperty("Leash Check Interval in Seconds")]
            public float CheckInterval = 1f;
        }

        private class MessageSettings
        {
            [JsonProperty("Send Global Message When Helicopter Is Redirected")]
            public bool SendChatMessage = true;

            [JsonProperty("Minimum Seconds Between Global Messages Per Helicopter")]
            public float MessageCooldown = 30f;

            [JsonProperty("Global Message Format ({0}=player, {1}=grid)")]
            public string GlobalMessageFormat =
                "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>";
        }

        private class DeveloperSettings
        {
            [JsonProperty("Enable Debug Messages in Console")]
            public bool EnableDebug;
        }

        private class LegacyConfigData
        {
            [JsonProperty("Enable leash behavior")] public bool EnableLeash = true;
            [JsonProperty("Health threshold to enable leash")] public float HealthThreshold = 400f;
            [JsonProperty("Maximum allowed distance from the last attacker")] public float MaxDistance = 150f;
            [JsonProperty("Leash check interval in seconds")] public float CheckInterval = 1f;
            [JsonProperty("Enable debug messages in console")] public bool EnableDebug;
            [JsonProperty("Send a global message when the helicopter is redirected")] public bool SendChatMessage = true;
            [JsonProperty("Minimum seconds between global messages per helicopter")] public float MessageCooldown = 30f;
            [JsonProperty("Global message format ({0}=player, {1}=grid)")]
            public string GlobalMessageFormat =
                "🚁 <color=#ff4d4d>Helicopter is staying close to {0} at [<color=#ffd700>{1}</color>]</color>";
        }

        private class LeashState
        {
            public BasePlayer Attacker;
            public float NextMessageAt;
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
                Newtonsoft.Json.Linq.JObject raw = Config.ReadObject<Newtonsoft.Json.Linq.JObject>();
                bool legacy = raw != null && raw["Leash Settings"] == null &&
                              (raw["Enable leash behavior"] != null ||
                               raw["Health threshold to enable leash"] != null ||
                               raw["Maximum allowed distance from the last attacker"] != null);

                if (legacy)
                {
                    LegacyConfigData old = raw.ToObject<LegacyConfigData>() ?? new LegacyConfigData();
                    _config = new ConfigData
                    {
                        Leash = new LeashSettings
                        {
                            Enabled = old.EnableLeash,
                            HealthThreshold = old.HealthThreshold,
                            MaxDistance = old.MaxDistance,
                            CheckInterval = old.CheckInterval
                        },
                        Messages = new MessageSettings
                        {
                            SendChatMessage = old.SendChatMessage,
                            MessageCooldown = old.MessageCooldown,
                            GlobalMessageFormat = old.GlobalMessageFormat
                        },
                        Developer = new DeveloperSettings
                        {
                            EnableDebug = old.EnableDebug
                        }
                    };
                    LogInformation("Configuration", "Migrated HeliLeashControl configuration to RogueRust grouped settings.");
                }
                else
                {
                    _config = raw == null ? new ConfigData() : raw.ToObject<ConfigData>();
                }
            }
            catch (Exception exception)
            {
                PrintError("Invalid configuration: " + exception.Message);
                _config = new ConfigData();
            }

            if (_config == null) _config = new ConfigData();
            if (_config.Leash == null) _config.Leash = new LeashSettings();
            if (_config.Messages == null) _config.Messages = new MessageSettings();
            if (_config.Developer == null) _config.Developer = new DeveloperSettings();

            _config.Version = CurrentVersion;
            _config.HealthThreshold = Mathf.Clamp(_config.HealthThreshold, 1f, 10000f);
            _config.MaxDistance = Mathf.Clamp(_config.MaxDistance, 25f, 2000f);
            _config.CheckInterval = Mathf.Clamp(_config.CheckInterval, 0.25f, 10f);
            _config.MessageCooldown = Mathf.Clamp(_config.MessageCooldown, 5f, 600f);
            _maxDistanceSquared = _config.MaxDistance * _config.MaxDistance;

            if (string.IsNullOrWhiteSpace(_config.GlobalMessageFormat))
                _config.GlobalMessageFormat = "Helicopter is staying close to {0} at {1}.";

            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=None; Lang=None");
        }

        private void OnServerInitialized()
        {
            ExecuteProtected(() =>
            {
                if (!_config.EnableLeash)
                {
                    LogInformation("Lifecycle", "Leash behavior is disabled in configuration.");
                    return;
                }

                Repeat(TimeSpan.FromSeconds(_config.CheckInterval),
                    delegate { ExecuteProtected(CheckTrackedHelicopters, "LeashCheck"); },
                    "heli-leash-check");
                LogInformation("Lifecycle", string.Format(
                    "Leash active below {0:0} HP with a {1:0} metre radius.",
                    _config.HealthThreshold, _config.MaxDistance));
            });
        }

        private void Unload()
        {
            _trackedHelicopters.Clear();
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            ExecuteProtected(() =>
            {
                if (!_config.EnableLeash || info == null || info.InitiatorPlayer == null)
                    return;

                BaseHelicopter helicopter = entity as BaseHelicopter;
                if (helicopter == null || !string.Equals(helicopter.ShortPrefabName, "patrolhelicopter", StringComparison.OrdinalIgnoreCase))
                    return;

                float projectedHealth = helicopter.Health() - info.damageTypes.Total();
                if (projectedHealth > _config.HealthThreshold)
                    return;

                LeashState state;
                if (!_trackedHelicopters.TryGetValue(helicopter, out state))
                {
                    state = new LeashState();
                    _trackedHelicopters[helicopter] = state;
                }

                state.Attacker = info.InitiatorPlayer;
            });
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            ExecuteProtected(() =>
            {
                BaseHelicopter helicopter = entity as BaseHelicopter;
                if (helicopter != null)
                    _trackedHelicopters.Remove(helicopter);
            });
        }

        private void CheckTrackedHelicopters()
        {
            if (_trackedHelicopters.Count == 0)
                return;

            List<BaseHelicopter> remove = null;
            foreach (KeyValuePair<BaseHelicopter, LeashState> pair in _trackedHelicopters)
            {
                BaseHelicopter helicopter = pair.Key;
                BasePlayer attacker = pair.Value.Attacker;
                if (helicopter == null || helicopter.IsDestroyed || helicopter.Health() <= 0f ||
                    attacker == null || !attacker.IsConnected || attacker.IsDead())
                {
                    if (remove == null)
                        remove = new List<BaseHelicopter>();
                    remove.Add(helicopter);
                    continue;
                }

                Vector3 helicopterPosition = helicopter.transform.position;
                Vector3 attackerPosition = attacker.transform.position;
                float distanceSquared = (helicopterPosition - attackerPosition).sqrMagnitude;
                if (distanceSquared <= _maxDistanceSquared)
                    continue;

                PatrolHelicopterAI helicopterAi = helicopter.GetComponent<PatrolHelicopterAI>();
                if (helicopterAi == null)
                    continue;

                Vector3 direction = (attackerPosition - helicopterPosition).normalized;
                Vector3 destination = attackerPosition - direction * (_config.MaxDistance * 0.5f);
                helicopterAi.SetTargetDestination(destination);

                if (_config.EnableDebug)
                {
                    float distance = Mathf.Sqrt(distanceSquared);
                    LogInformation("Debug", string.Format(
                        "Redirected Patrol Helicopter toward {0}; distance was {1:0.0}m.",
                        attacker.UserIDString, distance));
                }

                float now = Time.realtimeSinceStartup;
                if (_config.SendChatMessage && now >= pair.Value.NextMessageAt)
                {
                    pair.Value.NextMessageAt = now + _config.MessageCooldown;
                    SendLeashMessage(attacker);
                }
            }

            if (remove == null)
                return;

            for (int i = 0; i < remove.Count; i++)
                _trackedHelicopters.Remove(remove[i]);
        }

        private void ExecuteProtected(Action action, string operation = "Runtime")
        {
            if (action == null) return;
            try
            {
                using (Measure("HeliLeashControl", operation))
                    action();
            }
            catch (Exception exception)
            {
                LogError("Runtime", "Unhandled error in " + operation + ".", exception);
            }
        }

        private void SendLeashMessage(BasePlayer attacker)
        {
            string playerName = EscapeRichText(attacker.displayName);
            string grid = MapHelper.PositionToString(attacker.transform.position);
            string message;
            try
            {
                message = string.Format(_config.GlobalMessageFormat, playerName, grid);
            }
            catch (FormatException)
            {
                LogWarning("Configuration", "GlobalMessageFormat contains invalid placeholders; using the safe fallback.");
                message = "Helicopter is staying close to " + playerName + " at " + grid + ".";
            }

            SendLeashChat(message);
        }

        private static void SendLeashChat(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                if (player == null || !player.IsConnected)
                    continue;

                player.SendConsoleCommand("chat.add", 2, ChatIconSteamId, message);
            }
        }

        private static string EscapeRichText(string value)
        {
            return string.IsNullOrEmpty(value) ? "Unknown" : value.Replace("<", "‹").Replace(">", "›");
        }
    
}
}
