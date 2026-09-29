// RogueRustAdminMenu v2.5.0 RRAM release build: RRADMIN-250
// Requires Oxide.Ext.RogueRust.dll: https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases
// Coordinated AdminMenu/UI/teleport/ImageLibrary/AdminVanishUncharted release build
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.Diagnostics;
using Oxide.Ext.RogueRust.SDK;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins;

[Info("RogueRustAdminMenu", "RogueAssassin", "2.5.0")]
[Description("RogueRust Extension DLL advanced administration workspace with F1-style RogueUI, non-blocking workflows, DLL-backed diagnostics, bulk administration, advanced plugin/ConVar management, teleport return, inventory inspection, spectate, moderation notes, audit history and AdminVanishUncharted.")]
public sealed class RogueRustAdminMenu : RogueRustPlugin
{
    private const string PluginVersion = "2.5.0";
    private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 5, 0);
    private const string Root = "RogueRustAdminMenu.Main";
    private const string Popup = "RogueRustAdminMenu.Popup";
    private const string Overlay = "RogueRustAdminMenu.Overlay";
    private const string CursorRoot = "RogueRustAdminMenu.Cursor";

    // RogueRust family storage standard. Rogue.Data is already rooted at data/RogueRust/,
    // so keys must start with the plugin name (do not prefix them with RogueRust again).
    private const string DataRoot = "RogueRustAdminMenu";
    private const string RecentPlayersDataKey = DataRoot + "/recent_players";
    private const string SavedLocationsDataKey = DataRoot + "/saved_locations";
    private const string ModerationNotesDataKey = DataRoot + "/moderation_notes";
    private const string AuditHistoryDataKey = DataRoot + "/audit_history";
    private const string LanguageFolder = "RogueRustAdminMenu";
    private const string LanguageFile = "messages.json";

    // Kept intentionally compatible with ChaosCode AdminMenu permission deployments.
    [RoguePermission]
    private const string UsePermission = "roguerustadminmenu.use";
    [RoguePermission]
    private const string PermissionPermission = "roguerustadminmenu.permissions";
    [RoguePermission]
    private const string GroupPermission = "roguerustadminmenu.groups";
    [RoguePermission]
    private const string ConvarPermission = "roguerustadminmenu.convars";
    [RoguePermission]
    private const string PluginPermission = "roguerustadminmenu.plugins";
    [RoguePermission]
    private const string GivePermission = "roguerustadminmenu.give";
    [RoguePermission]
    private const string GiveSelfPermission = "roguerustadminmenu.give.selfonly";
    [RoguePermission]
    private const string PlayerPermission = "roguerustadminmenu.players";
    [RoguePermission]
    private const string KickBanPermission = "roguerustadminmenu.players.kickban";
    [RoguePermission]
    private const string MutePermission = "roguerustadminmenu.players.mute";
    [RoguePermission]
    private const string BlueprintPermission = "roguerustadminmenu.players.blueprints";
    [RoguePermission]
    private const string HurtPermission = "roguerustadminmenu.players.hurt";
    [RoguePermission]
    private const string HealPermission = "roguerustadminmenu.players.heal";
    [RoguePermission]
    private const string KillPermission = "roguerustadminmenu.players.kill";
    [RoguePermission]
    private const string StripPermission = "roguerustadminmenu.players.strip";
    [RoguePermission]
    private const string TeleportPermission = "roguerustadminmenu.players.teleport";
    [RoguePermission]
    private const string VanishPermission = "roguerustadminmenu.vanish";
    [RoguePermission]
    private const string VehiclePermission = "roguerustadminmenu.vehicles";
    [RoguePermission]
    private const string CommandsPermission = "roguerustadminmenu.commands";
    [RoguePermission]
    private const string InventoryPermission = "roguerustadminmenu.players.inventory";
    [RoguePermission]
    private const string SpectatePermission = "roguerustadminmenu.players.spectate";
    [RoguePermission]
    private const string NotesPermission = "roguerustadminmenu.players.notes";
    [RoguePermission]
    private const string AuditPermission = "roguerustadminmenu.audit";
    [RoguePermission]
    private const string DiagnosticsPermission = "roguerustadminmenu.diagnostics";
    [RoguePermission]
    private const string BulkPermission = "roguerustadminmenu.players.bulk";
    private const string VanishHud = "RogueRustAdminMenu.VanishHud";
    private static readonly string[] CorePermissions =
    {
        UsePermission, PermissionPermission, GroupPermission, ConvarPermission, PluginPermission,
        GivePermission, GiveSelfPermission, PlayerPermission, KickBanPermission, MutePermission,
        BlueprintPermission, HurtPermission, HealPermission, KillPermission, StripPermission,
        TeleportPermission, VanishPermission, VehiclePermission, CommandsPermission, InventoryPermission, SpectatePermission, NotesPermission, AuditPermission, DiagnosticsPermission, BulkPermission
    };

    private static readonly string[] VanishHooks =
    {
        nameof(CanBeTargeted), nameof(CanBradleyApcTarget), nameof(CanHelicopterTarget),
        nameof(CanHelicopterStrafeTarget), nameof(OnNpcTarget), nameof(CanNpcAttack),
        nameof(CanEntityBeHostile), nameof(OnEntityMarkHostile), nameof(OnRunPlayerMetabolism)
    };

    private readonly Dictionary<ulong, Session> _sessions = new();
    private readonly Dictionary<string, RecentPlayer> _recentPlayers = new(StringComparer.Ordinal);
    private readonly Dictionary<ItemCategory, List<ItemDefinition>> _items = new();
    private readonly List<ItemDefinition> _allItems = new();
    private readonly List<KeyValuePair<string, bool>> _permissionTree = new();
    private readonly Dictionary<string, SavedLocation> _savedLocations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ModerationNote>> _moderationNotes = new(StringComparer.Ordinal);
    private readonly List<AuditEntry> _auditHistory = new();
    private readonly Dictionary<ulong, Vector3> _spectateReturn = new();
    private bool _auditSaveQueued;
    private readonly List<RogueMonumentInfo> _menuMonumentCache = new();
    private readonly Dictionary<ulong, float> _teleportProtectionUntil = new();
    private readonly HashSet<ulong> _vanishedAdmins = new();
    private readonly HashSet<ulong> _vanishGodMode = new();
    private readonly HashSet<ulong> _vanishFlight = new();
    // Read-mostly caches. These are refreshed on lifecycle events or lazily on demand;
    // no background polling is used.
    private readonly List<PluginInfo> _pluginInfoCache = new();
    private float _pluginInfoCacheExpiresAt;
    private bool _pluginInfoCacheDirty = true;
    private readonly List<ConsoleSystem.Command> _serverConvarCache = new();
    private readonly Dictionary<int, string> _itemSearchKeys = new();
    private readonly Dictionary<int, string> _itemImageCache = new();
    private readonly Dictionary<int, float> _itemImageMissUntil = new();
    private readonly Dictionary<string, string> _vehicleImageCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, float> _vehicleImageMissUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ItemDefinition?> _vehicleIconItemCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _vehicleImageRedrawQueued;
    private bool _recentPlayersDirty;
    private readonly HashSet<string> _ignoredItems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ammo.snowballgun", "blueprintbase", "rhib", "spraycandecal", "vehicle.chassis", "vehicle.module", "water", "water.salt"
    };

    private ConfigData _config = new();
    private IRogueTeleportService? _teleport;
    private IRogueVanishService? _vanish;
    private IRogueVehicleService? _vehicles;
    private const int DefaultPageSize = 18;

    protected override IReadOnlyDictionary<string, string> DefaultMessages => new Dictionary<string, string>
    {
        ["Error.NoPermission"] = "You do not have permission to use this command.",
        ["Error.PlayerOnly"] = "This command can only be used by a player.",
        ["Error.TargetMissing"] = "That player is no longer available.",
        ["Error.InvalidValue"] = "The supplied value is invalid.",
        ["Notice.CommandRun"] = "Command executed: {0}",
        ["Notice.Given"] = "Gave {0} x {1} to {2}.",
        ["Notice.PermissionGranted"] = "Granted {0}.",
        ["Notice.PermissionRevoked"] = "Revoked {0}.",
        ["Notice.Saved"] = "Saved.",
    };

    private readonly Dictionary<string, Dictionary<string, string>> _messages =
        new(StringComparer.OrdinalIgnoreCase);

    protected override void LoadDefaultMessages()
    {
        // RogueRust family language files are managed explicitly below:
        // lang/<language>/RogueRust/RogueRustAdminMenu/messages.json
    }

    private void LoadLanguageFiles()
    {
        _messages.Clear();
        MigrateLegacyLanguageFiles();

        string langRoot = Interface.Oxide.LangDirectory;
        Directory.CreateDirectory(langRoot);

        foreach (string languageDirectory in Directory.GetDirectories(langRoot))
        {
            string language = Path.GetFileName(languageDirectory);
            if (string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                continue;

            string file = Path.Combine(languageDirectory, "RogueRust", LanguageFolder, LanguageFile);
            if (File.Exists(file))
                TryLoadLanguageCatalog(language, file);
        }

        if (!_messages.ContainsKey("en"))
        {
            string englishRoot = Path.Combine(langRoot, "en", "RogueRust", LanguageFolder);
            Directory.CreateDirectory(englishRoot);
            string englishFile = Path.Combine(englishRoot, LanguageFile);
            Dictionary<string, string> defaults =
                new(DefaultMessages, StringComparer.Ordinal);
            File.WriteAllText(englishFile, JsonConvert.SerializeObject(defaults, Formatting.Indented));
            _messages["en"] = defaults;
        }
    }

    private void TryLoadLanguageCatalog(string language, string file)
    {
        try
        {
            Dictionary<string, string>? catalog =
                JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
            if (catalog != null)
                _messages[language] = catalog;
        }
        catch (Exception exception)
        {
            LogWarning("Localization", $"Could not load '{file}': {exception.Message}");
        }
    }

    private void MigrateLegacyLanguageFiles()
    {
        try
        {
            string langRoot = Interface.Oxide.LangDirectory;

            // Earlier RogueRust family layout:
            // lang/RogueRust/RogueRustAdminMenu/<language>.json
            string previousRoot = Path.Combine(langRoot, "RogueRust", LanguageFolder);
            if (Directory.Exists(previousRoot))
            {
                foreach (string file in Directory.GetFiles(previousRoot, "*.json", SearchOption.TopDirectoryOnly))
                {
                    string language = Path.GetFileNameWithoutExtension(file);
                    string targetRoot = Path.Combine(langRoot, language, "RogueRust", LanguageFolder);
                    Directory.CreateDirectory(targetRoot);
                    string target = Path.Combine(targetRoot, LanguageFile);
                    if (!File.Exists(target))
                        File.Copy(file, target, false);
                }
            }

            // Standard Oxide/RogueRustPlugin location used by earlier AdminMenu builds:
            // lang/<language>/RogueRustAdminMenu.json
            foreach (string languageDirectory in Directory.GetDirectories(langRoot))
            {
                string language = Path.GetFileName(languageDirectory);
                if (string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                    continue;

                string targetRoot = Path.Combine(languageDirectory, "RogueRust", LanguageFolder);
                Directory.CreateDirectory(targetRoot);
                string target = Path.Combine(targetRoot, LanguageFile);
                if (File.Exists(target))
                    continue;

                string[] candidates =
                {
                    Path.Combine(languageDirectory, "RogueRustAdminMenu.json"),
                    Path.Combine(languageDirectory, "AdminMenu.json")
                };

                foreach (string candidate in candidates)
                {
                    if (!File.Exists(candidate))
                        continue;

                    File.Copy(candidate, target, false);
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            LogWarning("Localization", "Legacy language migration failed: " + exception.Message);
        }
    }

    private string GetLang(string key, BasePlayer? player = null, params object[] args)
    {
        string language = player == null ? "en" : lang.GetLanguage(player.UserIDString);
        if (string.IsNullOrWhiteSpace(language))
            language = "en";

        if (!_messages.TryGetValue(language, out Dictionary<string, string>? catalog) ||
            !catalog.TryGetValue(key, out string? template))
        {
            if (!_messages.TryGetValue("en", out catalog) ||
                !catalog.TryGetValue(key, out template))
                template = key;
        }

        return args == null || args.Length == 0 ? template : string.Format(template, args);
    }

    private void Init()
    {
        LoadLanguageFiles();

        // Core permissions and commands are discovered by RogueRust metadata.
        // Config-defined adminmenu.* permissions remain dynamic and are registered explicitly.
        RegisterConfiguredPermissions();
        AutoGrantAdminPermissions();
    }

    private void OnServerInitialized()
    {
        _teleport = RogueTeleportBootstrap.Resolve(Rogue);
        _vanish = RogueVanishBootstrap.Resolve(Rogue);
        _vehicles = RogueVehicleBootstrap.Resolve(Rogue);
        UpdateVanishHookSubscriptions();
        LoadRecentPlayers();
        LoadSavedLocations();
        LoadWorkflowData();
        BuildItemCache();
        BuildConvarCache();
        UpdatePermissionList();
        Adapters.Refresh();

        foreach (BasePlayer player in BasePlayer.activePlayerList)
            RememberPlayer(player, true);

        LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustAdminMenu/; Lang=<language>/RogueRust/RogueRustAdminMenu/messages.json");
    }

    private void OnPlayerConnected(BasePlayer player) => RememberPlayer(player, true);

    private void OnPlayerDisconnected(BasePlayer player, string reason)
    {
        RememberPlayer(player, false);
        _sessions.Remove(player.userID);
        _teleportProtectionUntil.Remove(player.userID);
        if (_vanishedAdmins.Contains(player.userID)) DisableVanish(player, false);
        DestroyUi(player, Root);
        DestroyUi(player, Popup);
        DestroyUi(player, Overlay);
        DestroyUi(player, CursorRoot);
    }

    private void OnPermissionRegistered(string name, Plugin owner) => UpdatePermissionList();
    private void OnPluginLoaded(Plugin plugin)
    {
        _pluginInfoCacheDirty = true;
        UpdatePermissionList();
        Adapters.Refresh();
    }

    private void OnPluginUnloaded(Plugin plugin)
    {
        _pluginInfoCacheDirty = true;
        UpdatePermissionList();
        Adapters.Refresh();
    }
    private void OnServerSave() => SaveRecentPlayers();

    private void Unload()
    {
        SaveRecentPlayers();
        SaveSavedLocations();
        SaveWorkflowData();
        foreach (BasePlayer player in BasePlayer.activePlayerList)
        {
            DestroyUi(player, Root);
            DestroyUi(player, Popup);
            DestroyUi(player, Overlay);
            DestroyUi(player, CursorRoot);
            DestroyUi(player, VanishHud);
            if (_vanishedAdmins.Contains(player.userID)) DisableVanish(player, false);
        }
        _sessions.Clear();
        _teleportProtectionUntil.Clear();
        _vanishedAdmins.Clear();
        _vanishGodMode.Clear();
        _vanishFlight.Clear();
        _pluginInfoCache.Clear();
        _serverConvarCache.Clear();
        _itemSearchKeys.Clear();
        _itemImageCache.Clear();
        _itemImageMissUntil.Clear();
        _vehicleImageCache.Clear();
        _vehicleImageMissUntil.Clear();
        _vehicleIconItemCache.Clear();
        _spectateReturn.Clear();
    }

    [RogueCommand(
        "admin",
        Aliases = new[] { "radmin" },
        Description = "Opens the RogueRust administration menu.",
        Usage = "admin",
        Category = "Administration",
        AllowChat = true,
        AllowConsole = false)]
    private RogueCommandResult AdminCommand(RogueCommandContext context)
    {
        BasePlayer player = context.NativePlayer;
        if (player == null)
            return RogueCommandResult.Fail(GetLang("Error.PlayerOnly"));

        // Keep AutoAdminAccess semantics instead of using command metadata permission checks.
        if (!HasAdminPermission(player, UsePermission))
            return RogueCommandResult.Fail(GetLang("Error.NoPermission", player));

        Open(player);
        return RogueCommandResult.Ok();
    }

    [RogueCommand(
        "vanish",
        Description = "Toggles RogueRust AdminVanishUncharted.",
        Usage = "vanish",
        Category = "Administration",
        AllowChat = true,
        AllowConsole = false)]
    private RogueCommandResult VanishCommand(RogueCommandContext context)
    {
        BasePlayer player = context.NativePlayer;
        if (player == null)
            return RogueCommandResult.Fail(GetLang("Error.PlayerOnly"));

        if (!_config.Vanish.Enabled)
            return RogueCommandResult.Fail("AdminVanishUncharted is disabled in RogueRustAdminMenu.json.");

        if (!HasAdminPermission(player, VanishPermission))
            return RogueCommandResult.Fail(GetLang("Error.NoPermission", player));

        if (_vanishedAdmins.Contains(player.userID)) DisableVanish(player, true);
        else EnableVanish(player);
        return RogueCommandResult.Ok();
    }

    private void EnableVanish(BasePlayer player)
    {
        if (player == null || !player.IsConnected || !_vanishedAdmins.Add(player.userID)) return;
        _vanish?.SetVanished(player.userID, true, "RogueRustAdminMenu");
        UpdateVanishHookSubscriptions();

        if (_config.Vanish.GodMode) _vanishGodMode.Add(player.userID);
        RefreshVanishMetabolism(player);
        if (_config.Vanish.Fly)
        {
            _vanishFlight.Add(player.userID);
            player.SendConsoleCommand("noclip");
        }

        // limitNetworking is the native Rust fast-path used to stop normal player replication.
        // Reflection keeps this plugin resilient if Facepunch changes field visibility between builds.
        if (_config.Vanish.Invisible)
            TrySetPlayerMember(player, "limitNetworking", true);
        player.SendNetworkUpdateImmediate();
        DrawVanishHud(player);
        Audit(player, "Enabled AdminVanishUncharted");
        player.ChatMessage("<color=#C4FF00>Vanish enabled</color> • invisible" + (_config.Vanish.GodMode ? " • god" : string.Empty) + (_config.Vanish.Fly ? " • fly" : string.Empty));
    }

    private void DisableVanish(BasePlayer player, bool notify)
    {
        if (player == null || !_vanishedAdmins.Remove(player.userID)) return;
        _vanish?.SetVanished(player.userID, false, "RogueRustAdminMenu");
        UpdateVanishHookSubscriptions();
        TrySetPlayerMember(player, "limitNetworking", false);

        if (_vanishFlight.Remove(player.userID) && player.IsConnected) player.SendConsoleCommand("noclip");
        _vanishGodMode.Remove(player.userID);
        RefreshVanishMetabolism(player);
        if (player.IsConnected) player.SendNetworkUpdateImmediate();
        DestroyUi(player, VanishHud);
        Audit(player, "Disabled AdminVanishUncharted");
        if (notify && player.IsConnected) player.ChatMessage("<color=#C4FF00>Vanish disabled</color> • normal visibility restored");
    }

    private void DrawVanishHud(BasePlayer player)
    {
        DestroyUi(player, VanishHud);
        CuiElementContainer ui = new();
        string panel = ui.Add(new CuiPanel
        {
            Image = { Color = "0.035 0.038 0.040 0.94" },
            RectTransform = { AnchorMin = "0.835 0.935", AnchorMax = "0.985 0.982" }
        }, "Overlay", VanishHud);
        ui.Add(new CuiLabel
        {
            Text = { Text = "VANISH" + (_config.Vanish.GodMode ? "  •  GOD" : string.Empty) + (_config.Vanish.Fly ? "  •  FLY" : string.Empty) + "  •  HIDDEN", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "0.768627 1 0 1" },
            RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
        }, panel);
        CuiHelper.AddUi(player, ui);
    }


    private static void RefreshVanishMetabolism(BasePlayer player)
    {
        if (player == null || player.metabolism == null) return;
        player.metabolism.bleeding.value = 0f;
        player.metabolism.radiation_level.value = 0f;
        player.metabolism.radiation_poison.value = 0f;
        player.metabolism.poison.value = 0f;
        player.metabolism.wetness.value = 0f;
        player.metabolism.comfort.value = 0f;
        player.metabolism.calories.value = player.metabolism.calories.max;
        player.metabolism.hydration.value = player.metabolism.hydration.max;
        player.metabolism.SendChanges();
    }

    // Vanish targeting is entirely event-driven. These hooks are subscribed only while at least
    // one administrator is vanished, avoiding NPC scans/timers and keeping the hot paths tiny.
    private void UpdateVanishHookSubscriptions()
    {
        bool active = _vanishedAdmins.Count != 0;
        foreach (string hook in VanishHooks)
        {
            if (active) Subscribe(hook);
            else Unsubscribe(hook);
        }
    }



    // Rust already runs metabolism on its own cadence. Subscribing only while vanish is active
    // avoids a plugin timer/per-frame loop while suppressing environmental screen/status effects.
    private void OnRunPlayerMetabolism(PlayerMetabolism metabolism, BaseCombatEntity entity, float delta)
    {
        BasePlayer player = entity as BasePlayer;
        if (player == null || metabolism == null || !_vanishedAdmins.Contains(player.userID)) return;

        metabolism.bleeding.value = 0f;
        metabolism.radiation_level.value = 0f;
        metabolism.radiation_poison.value = 0f;
        metabolism.poison.value = 0f;
        metabolism.wetness.value = 0f;
        metabolism.comfort.value = 0f;
        metabolism.temperature.value = 20f;
        metabolism.calories.value = metabolism.calories.max;
        metabolism.hydration.value = metabolism.hydration.max;
    }

    private object? CanBeTargeted(BaseCombatEntity entity, MonoBehaviour behaviour)
    {
        BasePlayer player = entity as BasePlayer;
        return player != null && _vanishedAdmins.Contains(player.userID) ? false : null;
    }

    // Rust's OnNpcTarget convention cancels acquisition with a non-null/true return.
    private object? OnNpcTarget(BaseCombatEntity entity, BasePlayer player)
        => player != null && _vanishedAdmins.Contains(player.userID) ? true : null;

    // Second line of defence: even if an AI already acquired the player before vanish was enabled,
    // deny the actual NPC attack. This also covers animals using BaseNpc attack logic.
    private object? CanNpcAttack(BaseNpc npc, BaseEntity target)
    {
        BasePlayer player = target as BasePlayer;
        return player != null && _vanishedAdmins.Contains(player.userID) ? false : null;
    }

    private object? CanBradleyApcTarget(BradleyAPC apc, BaseEntity entity)
    {
        BasePlayer player = entity as BasePlayer;
        return player != null && _vanishedAdmins.Contains(player.userID) ? false : null;
    }

    private object? CanHelicopterTarget(PatrolHelicopterAI heli, BasePlayer player)
        => player != null && _vanishedAdmins.Contains(player.userID) ? false : null;

    private object? CanHelicopterStrafeTarget(PatrolHelicopterAI heli, BasePlayer player)
        => player != null && _vanishedAdmins.Contains(player.userID) ? false : null;

    // Do not let vanished administrators become hostile targets through Rust's hostility system.
    private object? CanEntityBeHostile(BaseCombatEntity entity)
    {
        BasePlayer player = entity as BasePlayer;
        return player != null && _vanishedAdmins.Contains(player.userID) ? false : null;
    }

    private object? OnEntityMarkHostile(BaseCombatEntity entity, float duration)
    {
        BasePlayer player = entity as BasePlayer;
        return player != null && _vanishedAdmins.Contains(player.userID) ? true : null;
    }

    private static void TrySetPlayerMember(BasePlayer player, string name, bool value)
    {
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var field = typeof(BasePlayer).GetField(name, flags);
            if (field != null && field.FieldType == typeof(bool)) { field.SetValue(player, value); return; }
            var property = typeof(BasePlayer).GetProperty(name, flags);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool)) property.SetValue(player, value, null);
        }
        catch { }
    }

    #region Configuration

    protected override void LoadDefaultConfig()
    {
        _config = ConfigData.CreateDefault();
        Config.WriteObject(_config, true);
        LogInformation("Configuration", "Configuration changes saved to RogueRustAdminMenu.json");
    }

    protected override void LoadConfig()
    {
        base.LoadConfig();
        bool migratedLegacy = false;
        string loadedVersion = "unknown";
        try
        {
            JObject raw = Config.ReadObject<JObject>() ?? new JObject();
            JToken legacyVersion = raw["Config Version"];
            loadedVersion = legacyVersion?.ToString() ?? raw["Version (DO NOT CHANGE)"]?.ToString() ?? "legacy";

            _config = raw.ToObject<ConfigData>() ?? ConfigData.CreateDefault();
            migratedLegacy = raw["General Settings"] == null;
            if (migratedLegacy)
                MigrateLegacyFlatConfig(raw);
        }
        catch (Exception ex)
        {
            LogWarning("Configuration", $"Configuration was invalid and defaults were loaded: {ex.Message}");
            _config = ConfigData.CreateDefault();
        }

        bool changed = EnsureConfigDefaults();
        if (_config.Version != CurrentVersion)
        {
            LogInformation("Configuration", $"Migrating RogueRustAdminMenu.json from {loadedVersion} to {CurrentVersion}.");
            _config.Version = CurrentVersion;
            changed = true;
        }

        NormalizeConfig();
        // Always rewrite in the family-standard grouped/pretty format after a legacy migration.
        if (migratedLegacy || changed)
            SaveConfig();
    }

    protected override void SaveConfig() => Config.WriteObject(_config, true);

    private bool EnsureConfigDefaults()
    {
        bool changed = false;
        if (_config.General == null) { _config.General = new GeneralSettings(); changed = true; }
        if (_config.Permissions == null) { _config.Permissions = new PermissionSettings(); changed = true; }
        if (_config.Commands == null) { _config.Commands = CommandSettings.CreateDefault(); changed = true; }
        if (_config.Teleport == null) { _config.Teleport = new TeleportSettings(); changed = true; }
        if (_config.Vanish == null) { _config.Vanish = new VanishSettings(); changed = true; }
        if (_config.UI == null) { _config.UI = new UiSettings(); changed = true; }
        if (_config.Integrations == null) { _config.Integrations = new IntegrationSettings(); changed = true; }
        if (_config.Data == null) { _config.Data = new DataSettings(); changed = true; }
        if (_config.Commands.PlayerInfoCommands == null) { _config.Commands.PlayerInfoCommands = new List<CustomCommandGroup>(); changed = true; }
        if (_config.Commands.AdminCommands == null) { _config.Commands.AdminCommands = CommandSettings.DefaultAdminCommands(); changed = true; }
        if (_config.UI.Theme == null) { _config.UI.Theme = UiTheme.CreateDefault(); changed = true; }
        return changed;
    }

    private void MigrateLegacyFlatConfig(JObject raw)
    {
        CopyLegacy(raw, "Player Info Custom Commands", v => _config.Commands.PlayerInfoCommands = v, _config.Commands.PlayerInfoCommands);
        CopyLegacy(raw, "Use different permissions for each section of the player administration tab", v => _config.Permissions.UsePlayerAdminPermissions = v, _config.Permissions.UsePlayerAdminPermissions);
        CopyLegacy(raw, "Log menu actions to Discord webhook (webhook URL)", v => _config.Integrations.LogWebhook = v, _config.Integrations.LogWebhook);
        CopyLegacy(raw, "Recent players purge time (days)", v => _config.Data.RecentPlayerPurgeDays = v, _config.Data.RecentPlayerPurgeDays);
        CopyLegacy(raw, "Items / rows per page", v => _config.UI.PageSize = v, _config.UI.PageSize);
        CopyLegacy(raw, "Confirm destructive player actions", v => _config.General.ConfirmDestructiveActions = v, _config.General.ConfirmDestructiveActions);
        CopyLegacy(raw, "Automatically allow Rust server admins all AdminMenu permissions", v => _config.Permissions.AutoAdminAccess = v, _config.Permissions.AutoAdminAccess);
        CopyLegacy(raw, "Automatically grant all AdminMenu permissions to the Oxide admin group", v => _config.Permissions.AutoGrantAdminGroupPermissions = v, _config.Permissions.AutoGrantAdminGroupPermissions);
        CopyLegacy(raw, "Use native Rust item icons as Give fallback", v => _config.UI.UseNativeItemIcons = v, _config.UI.UseNativeItemIcons);
        CopyLegacy(raw, "Prefer ImageLibrary item images in Give", v => _config.UI.UseItemImages = v, _config.UI.UseItemImages);
        CopyLegacy(raw, "AdminVanishUncharted - Enable lightweight RogueRust admin vanish module", v => _config.Vanish.Enabled = v, _config.Vanish.Enabled);
        CopyLegacy(raw, "AdminVanishUncharted - Include god mode while vanished", v => _config.Vanish.GodMode = v, _config.Vanish.GodMode);
        CopyLegacy(raw, "AdminVanishUncharted - Include flying/noclip while vanished", v => _config.Vanish.Fly = v, _config.Vanish.Fly);
        CopyLegacy(raw, "AdminVanishUncharted - Hide vanished admins from normal player networking", v => _config.Vanish.Invisible = v, _config.Vanish.Invisible);
        CopyLegacy(raw, "Teleport arrival protection seconds (0-60)", v => _config.Teleport.ProtectionSeconds = v, _config.Teleport.ProtectionSeconds);
        CopyLegacy(raw, "Remove teleport protection when protected player attacks", v => _config.Teleport.BreakProtectionOnAttack = v, _config.Teleport.BreakProtectionOnAttack);
        CopyLegacy(raw, "Server time confirmation duration seconds (10-30)", v => _config.UI.ServerTimeToastSeconds = v, _config.UI.ServerTimeToastSeconds);
        CopyLegacy(raw, "AdminMenu UI scale (0.85-1.15)", v => _config.UI.Scale = v, _config.UI.Scale);
        CopyLegacy(raw, "UI Theme", v => _config.UI.Theme = v, _config.UI.Theme);
    }

    private static void CopyLegacy<T>(JObject raw, string key, Action<T> setter, T fallback)
    {
        JToken token = raw[key];
        if (token == null || token.Type == JTokenType.Null) return;
        try { setter(token.ToObject<T>()); } catch { setter(fallback); }
    }

    private void NormalizeConfig()
    {
        _config.Commands.PlayerInfoCommands ??= new List<CustomCommandGroup>();
        _config.Commands.AdminCommands ??= CommandSettings.DefaultAdminCommands();
        _config.Data.MaxAuditEntries = Math.Max(50, Math.Min(2000, _config.Data.MaxAuditEntries));
        _config.Data.MaxNotesPerPlayer = Math.Max(5, Math.Min(100, _config.Data.MaxNotesPerPlayer));
        _config.UI.PageSize = Math.Max(9, Math.Min(30, _config.UI.PageSize <= 0 ? DefaultPageSize : _config.UI.PageSize));
        _config.Data.RecentPlayerPurgeDays = Math.Max(1, _config.Data.RecentPlayerPurgeDays);
        _config.Teleport.ProtectionSeconds = Math.Max(0f, Math.Min(60f, _config.Teleport.ProtectionSeconds));
        _config.UI.ServerTimeToastSeconds = Math.Max(10f, Math.Min(30f, _config.UI.ServerTimeToastSeconds));
        _config.UI.Scale = Math.Max(0.85f, Math.Min(1.15f, _config.UI.Scale));
        _config.UI.Theme ??= UiTheme.CreateDefault();
        if (_config.UI.Theme.IsLegacyDefault())
            _config.UI.Theme = UiTheme.CreateDefault();
    }

    public sealed class ConfigData
    {
        [JsonProperty("General Settings", Order = 10)]
        public GeneralSettings General { get; set; } = new();

        [JsonProperty("Permission Settings", Order = 20)]
        public PermissionSettings Permissions { get; set; } = new();

        [JsonProperty("Command Settings", Order = 30)]
        public CommandSettings Commands { get; set; } = CommandSettings.CreateDefault();

        [JsonProperty("Teleport Settings", Order = 40)]
        public TeleportSettings Teleport { get; set; } = new();

        [JsonProperty("Admin Vanish Settings", Order = 50)]
        public VanishSettings Vanish { get; set; } = new();

        [JsonProperty("UI Settings", Order = 60)]
        public UiSettings UI { get; set; } = new();

        [JsonProperty("Integration Settings", Order = 70)]
        public IntegrationSettings Integrations { get; set; } = new();

        [JsonProperty("Data Settings", Order = 80)]
        public DataSettings Data { get; set; } = new();

        [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
        public VersionNumber Version = CurrentVersion;

        public static ConfigData CreateDefault() => new();
    }

    public sealed class GeneralSettings
    {
        [JsonProperty("Confirm Destructive Player Actions")]
        public bool ConfirmDestructiveActions { get; set; } = true;
    }

    public sealed class PermissionSettings
    {
        [JsonProperty("Use Different Permissions For Each Player Administration Section")]
        public bool UsePlayerAdminPermissions { get; set; }

        [JsonProperty("Automatically Allow Rust Server Admins All AdminMenu Permissions")]
        public bool AutoAdminAccess { get; set; } = true;

        [JsonProperty("Automatically Grant All AdminMenu Permissions To The Oxide Admin Group")]
        public bool AutoGrantAdminGroupPermissions { get; set; } = true;
    }

    public sealed class CommandSettings
    {
        [JsonProperty("Player Info Custom Commands")]
        public List<CustomCommandGroup> PlayerInfoCommands { get; set; } = new();

        [JsonProperty("Admin Commands")]
        public List<AdminCommandEntry> AdminCommands { get; set; } = DefaultAdminCommands();

        public static List<AdminCommandEntry> DefaultAdminCommands() => new()
        {
            new() { Name = "RogueRust Status", Description = "Shared DLL status", Command = "roguerust.status", SubType = CommandSubType.Console },
            new() { Name = "RogueRust Health", Description = "Runtime health snapshot", Command = "roguerust.health", SubType = CommandSubType.Console },
            new() { Name = "RogueRust Performance", Description = "Performance diagnostics", Command = "roguerust.performance", SubType = CommandSubType.Console },
            new() { Name = "RogueRust Services", Description = "Registered shared services", Command = "roguerust.services", SubType = CommandSubType.Console }
        };

        public static CommandSettings CreateDefault() => new()
        {
            PlayerInfoCommands = new List<CustomCommandGroup>
            {
                new() { Name = "Backpacks", Commands = new List<PlayerInfoCommandEntry> { new() { RequiredPlugin = "Backpacks", RequiredPermission = "backpacks.admin", Name = "View Backpack", CloseOnRun = true, Command = "/viewbackpack {target1_id}", SubType = CommandSubType.Chat } } },
                new() { Name = "InventoryViewer", Commands = new List<PlayerInfoCommandEntry> { new() { RequiredPlugin = "InventoryViewer", RequiredPermission = "inventoryviewer.allowed", Name = "View Inventory", CloseOnRun = true, Command = "/viewinv {target1_id}", SubType = CommandSubType.Chat } } },
                new() { Name = "Freeze", Commands = new List<PlayerInfoCommandEntry> { new() { RequiredPlugin = "Freeze", RequiredPermission = "freeze.use", Name = "Freeze", Command = "/freeze {target1_id}", SubType = CommandSubType.Chat }, new() { RequiredPlugin = "Freeze", RequiredPermission = "freeze.use", Name = "Unfreeze", Command = "/unfreeze {target1_id}", SubType = CommandSubType.Chat } } }
            }
        };
    }

    public sealed class TeleportSettings
    {
        [JsonProperty("Arrival Protection Seconds (0-60)")]
        public float ProtectionSeconds { get; set; } = 45f;
        [JsonProperty("Remove Protection When Protected Player Attacks")]
        public bool BreakProtectionOnAttack { get; set; } = true;
    }

    public sealed class VanishSettings
    {
        [JsonProperty("Enable Lightweight RogueRust Admin Vanish Module")]
        public bool Enabled { get; set; } = true;
        [JsonProperty("Include God Mode While Vanished")]
        public bool GodMode { get; set; } = true;
        [JsonProperty("Include Flying / Noclip While Vanished")]
        public bool Fly { get; set; } = true;
        [JsonProperty("Hide Vanished Admins From Normal Player Networking")]
        public bool Invisible { get; set; } = true;
    }

    public sealed class UiSettings
    {
        [JsonProperty("Items / Rows Per Page")]
        public int PageSize { get; set; } = DefaultPageSize;
        [JsonProperty("Use Native Rust Item Icons As Give Fallback")]
        public bool UseNativeItemIcons { get; set; } = true;
        [JsonProperty("Prefer ImageLibrary Item Images In Give")]
        public bool UseItemImages { get; set; } = true;
        [JsonProperty("Server Time Confirmation Duration Seconds (10-30)")]
        public float ServerTimeToastSeconds { get; set; } = 10f;
        [JsonProperty("AdminMenu UI Scale (0.85-1.15)")]
        public float Scale { get; set; } = 1.0f;
        [JsonProperty("UI Theme")]
        public UiTheme Theme { get; set; } = UiTheme.CreateDefault();
    }

    public sealed class IntegrationSettings
    {
        [JsonProperty("Log Menu Actions To Discord Webhook (webhook URL)")]
        public string LogWebhook { get; set; } = string.Empty;
    }

    public sealed class DataSettings
    {
        [JsonProperty("Recent Players Purge Time (days)")]
        public int RecentPlayerPurgeDays { get; set; } = 7;
        [JsonProperty("Maximum Stored Admin Audit Entries")]
        public int MaxAuditEntries { get; set; } = 500;
        [JsonProperty("Maximum Moderation Notes Per Player")]
        public int MaxNotesPerPlayer { get; set; } = 25;
    }

    public class CommandEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool CloseOnRun { get; set; }
        public string RequiredPermission { get; set; } = string.Empty;
    }

    public sealed class CustomCommandGroup
    {
        public string Name { get; set; } = string.Empty;
        public List<PlayerInfoCommandEntry> Commands { get; set; } = new();
    }

    public sealed class AdminCommandEntry : CommandEntry
    {
        [JsonProperty("Command Type ( Chat, Console )")]
        [JsonConverter(typeof(StringEnumConverter))]
        public CommandSubType SubType { get; set; } = CommandSubType.Console;
        public bool RequireConfirmation { get; set; }
    }

    public sealed class PlayerInfoCommandEntry : CommandEntry
    {
        public string RequiredPlugin { get; set; } = string.Empty;

        [JsonProperty("Command Type ( Chat, Console )")]
        [JsonConverter(typeof(StringEnumConverter))]
        public CommandSubType SubType { get; set; }
    }

    public sealed class UiTheme
    {
        // RRAM CUIHelper parity palette: deep blue/black glass, cyan information and restrained orange branding.
        public string Background { get; set; } = "0.008 0.020 0.028 0.94";
        public string Sidebar { get; set; } = "0.012 0.030 0.040 0.94";
        public string Surface { get; set; } = "0.018 0.045 0.056 0.96";
        public string SurfaceAlt { get; set; } = "0.025 0.060 0.072 0.98";
        public string Accent { get; set; } = "0.10 0.68 0.82 1";
        public string AccentMuted { get; set; } = "0.035 0.095 0.115 0.96";
        public string Success { get; set; } = "0.20 0.72 0.40 1";
        public string Warning { get; set; } = "0.94 0.58 0.16 1";
        public string Danger { get; set; } = "0.70 0.15 0.13 1";
        public string Text { get; set; } = "0.92 0.95 0.96 1";
        public string MutedText { get; set; } = "0.58 0.68 0.72 1";
        public static UiTheme CreateDefault() => new();
        public bool IsLegacyDefault() =>
            (Accent == "0.76 0.31 0.10 1" && Background == "0.035 0.038 0.040 0.965") ||
            Accent == "0.768627 1 0 1";
    }

    #endregion

    #region State and data

    private enum MenuType { Dashboard, Players, Give, Vehicles, Teleport, Commands, Permissions, Groups, Convars, Plugins, Diagnostics }
    private enum WindowMode { Compact, Standard, Full }
    public enum CommandSubType { Chat, Console, PlayerInfo }
    private enum PermissionSubType { Player, Group }
    private enum GroupSubType { List, Create, UserGroups, GroupUsers }
    private enum SelectionPurpose { None, PermissionPlayer, GroupUser, GiveTarget }

    private sealed class Session
    {
        public BasePlayer Player = null!;
        public MenuType Menu = MenuType.Dashboard;
        public int SubMenu;
        public int Page;
        public string Search = string.Empty;
        public string Character = "~";
        public bool ShowOnline = true;
        public bool ShowOffline = true;
        public string SelectedPlayerId = string.Empty;
        public string SelectedPlayerName = string.Empty;
        public string SelectedGroup = string.Empty;
        public string SelectedMonumentGroup = string.Empty;
        public SelectionPurpose Selection;
        public string Target1Id = string.Empty;
        public string Target1Name = string.Empty;
        public string Target2Id = string.Empty;
        public string Target2Name = string.Empty;
        public ItemDefinition? PendingItem;
        public int GiveAmount = 1;
        public ulong SkinId;
        public int GiveCategory = -1;
        public bool GiveRecentOnly;
        public bool GiveFavouritesOnly;
        public readonly List<string> RecentGiveItems = new();
        public readonly HashSet<string> FavouriteGiveItems = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<ItemDefinition> NativeGiveIcons = new();
        public string VehicleCategory = "ALL";
        public string SelectedVehicleId = string.Empty;
        public string KickBanReason = string.Empty;
        public string ModerationNoteDraft = string.Empty;
        public bool BulkMode;
        public readonly HashSet<string> BulkSelectedPlayers = new(StringComparer.Ordinal);
        public readonly HashSet<string> FavouriteConvars = new(StringComparer.OrdinalIgnoreCase);
        public WindowMode Window = WindowMode.Standard;
        public bool AppearanceOpen;
        public float WorldDimming = 0.16f;

        public void ResetListState()
        {
            Page = 0;
            Search = string.Empty;
            Character = "~";
            Selection = SelectionPurpose.None;
        }

        public void ClearCommand()
        {
            Target1Id = Target1Name = Target2Id = Target2Name = string.Empty;
            Selection = SelectionPurpose.None;
        }
    }

    private sealed class RecentPlayer
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long LastSeenUtc { get; set; }
        public bool Online { get; set; }
    }

    private sealed class SavedLocation
    {
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public long SavedUtc { get; set; }
        public string SavedBy { get; set; } = string.Empty;
        [JsonIgnore] public Vector3 Position => new Vector3(X, Y, Z);
    }


    private sealed class ModerationNote
    {
        public long Utc { get; set; }
        public string AdminId { get; set; } = string.Empty;
        public string AdminName { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }

    private sealed class AuditEntry
    {
        public long Utc { get; set; }
        public string AdminId { get; set; } = string.Empty;
        public string AdminName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    private void LoadWorkflowData()
    {
        _moderationNotes.Clear();
        foreach (var pair in LoadData(ModerationNotesDataKey, () => new Dictionary<string, List<ModerationNote>>()))
            _moderationNotes[pair.Key] = pair.Value ?? new List<ModerationNote>();
        _auditHistory.Clear();
        _auditHistory.AddRange(LoadData(AuditHistoryDataKey, () => new List<AuditEntry>()));
        if (_auditHistory.Count > _config.Data.MaxAuditEntries)
            _auditHistory.RemoveRange(0, _auditHistory.Count - _config.Data.MaxAuditEntries);
    }

    private void SaveWorkflowData()
    {
        SaveData(ModerationNotesDataKey, _moderationNotes);
        SaveData(AuditHistoryDataKey, _auditHistory);
    }
    private void LoadSavedLocations()
    {
        Dictionary<string, SavedLocation> loaded = LoadData(SavedLocationsDataKey, () => new Dictionary<string, SavedLocation>());
        _savedLocations.Clear();
        foreach (KeyValuePair<string, SavedLocation> entry in loaded)
            if (entry.Value != null && !string.IsNullOrWhiteSpace(entry.Key)) _savedLocations[entry.Key] = entry.Value;
    }

    private void SaveSavedLocations() => SaveData(SavedLocationsDataKey, _savedLocations);

    private void LoadRecentPlayers()
    {
        Dictionary<string, RecentPlayer> loaded = LoadData(RecentPlayersDataKey, () => new Dictionary<string, RecentPlayer>());
        _recentPlayers.Clear();
        foreach (KeyValuePair<string, RecentPlayer> entry in loaded)
            _recentPlayers[entry.Key] = entry.Value;
        PurgeRecentPlayers();
    }

    private void SaveRecentPlayers()
    {
        if (!_recentPlayersDirty) return;
        SaveData(RecentPlayersDataKey, _recentPlayers);
        _recentPlayersDirty = false;
    }

    private void RememberPlayer(BasePlayer player, bool online)
    {
        if (player == null || !player.userID.IsSteamId()) return;

        string id = player.UserIDString;
        string name = StripName(player.displayName);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        if (_recentPlayers.TryGetValue(id, out RecentPlayer existing) && existing != null)
        {
            bool changed = existing.Online != online || !string.Equals(existing.Name, name, StringComparison.Ordinal);
            if (!changed && online) return;

            existing.Name = name;
            existing.Online = online;
            existing.LastSeenUtc = now;
            _recentPlayersDirty = true;
            return;
        }

        _recentPlayers[id] = new RecentPlayer
        {
            Id = id,
            Name = name,
            Online = online,
            LastSeenUtc = now
        };
        _recentPlayersDirty = true;
    }

    private void PurgeRecentPlayers()
    {
        long cutoff = DateTimeOffset.UtcNow.AddDays(-_config.Data.RecentPlayerPurgeDays).ToUnixTimeSeconds();
        List<string>? expired = null;
        foreach (KeyValuePair<string, RecentPlayer> entry in _recentPlayers)
        {
            if (entry.Value == null || entry.Value.Online || entry.Value.LastSeenUtc >= cutoff) continue;
            (expired ??= new List<string>()).Add(entry.Key);
        }
        if (expired == null) return;
        for (int i = 0; i < expired.Count; i++)
            _recentPlayers.Remove(expired[i]);
        _recentPlayersDirty = true;
    }

    private void BuildItemCache()
    {
        _items.Clear();
        _allItems.Clear();
        _itemSearchKeys.Clear();
        foreach (ItemDefinition definition in ItemManager.itemList)
        {
            if (definition == null || definition.hidden || _ignoredItems.Contains(definition.shortname)) continue;
            if (!_items.TryGetValue(definition.category, out List<ItemDefinition>? list))
                _items[definition.category] = list = new List<ItemDefinition>();
            list.Add(definition);
            _allItems.Add(definition);

            // Avoid rebuilding "display name + shortname" strings on every Give redraw/search.
            string displayName = definition.displayName?.english ?? string.Empty;
            _itemSearchKeys[definition.itemid] = (displayName + " " + definition.shortname).ToLowerInvariant();
        }
        foreach (List<ItemDefinition> list in _items.Values)
            list.Sort((a, b) => string.Compare(a.displayName.english, b.displayName.english, StringComparison.OrdinalIgnoreCase));
        _allItems.Sort((a, b) => string.Compare(a.displayName.english, b.displayName.english, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildConvarCache()
    {
        _serverConvarCache.Clear();
        _serverConvarCache.AddRange(ConsoleGen.All
            .Where(x => x != null && x.ServerAdmin && x.Variable)
            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase));
    }

    private void UpdatePermissionList()
    {
        _permissionTree.Clear();
        List<string> permissions = permission.GetPermissions().Where(x => !x.StartsWith("oxide.", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x).ToList();
        Dictionary<string, string> pluginNames = plugins.PluginManager.GetPlugins()
            .Where(x => x != null).ToDictionary(x => x.Name.ToLowerInvariant(), x => x.Title, StringComparer.OrdinalIgnoreCase);
        string last = string.Empty;
        foreach (string perm in permissions)
        {
            string prefix = perm.Contains('.') ? perm.Substring(0, perm.IndexOf('.')) : perm;
            string title = pluginNames.TryGetValue(prefix, out string? pluginTitle) ? pluginTitle : prefix;
            if (!string.Equals(last, title, StringComparison.OrdinalIgnoreCase))
            {
                _permissionTree.Add(new KeyValuePair<string, bool>(title, false));
                last = title;
            }
            _permissionTree.Add(new KeyValuePair<string, bool>(perm, true));
        }
    }

    #endregion

    #region UI shell

    private void Open(BasePlayer player)
    {
        if (_teleportProtectionUntil.TryGetValue(player.userID, out float expires) && Time.realtimeSinceStartup >= expires)
            _teleportProtectionUntil.Remove(player.userID);
        if (!_sessions.TryGetValue(player.userID, out Session? session))
            _sessions[player.userID] = session = new Session { Player = player };
        EnsurePersistentCursor(player);
        Draw(session);
    }

    private void Draw(Session s)
    {
        if (s.Player == null || !s.Player.IsConnected) return;
        RogueUiDocument ui = CreateUi(Root);
        ApplyTheme(ui);

        // RRAM 2.4.9 shell mirrors the approved RustCUIHelper reference pages.
        // Header/navigation/content/footer geometry is intentionally shared by every page.
        ui.Panel(Root, "Overlay", RogueUiRect.Full, $"0 0 0 {s.WorldDimming.ToString("0.##", CultureInfo.InvariantCulture)}", cursorEnabled: false);
        RogueUiRect windowRect = s.Window == WindowMode.Compact ? Rect(0.10f, 0.11f, 0.90f, 0.89f) : s.Window == WindowMode.Full ? Rect(0.02f, 0.025f, 0.98f, 0.975f) : Rect(0.055f, 0.065f, 0.945f, 0.935f);
        ui.Panel("rram.frame", Root, windowRect, "0.008 0.020 0.028 0.94");
        ui.Panel("rram.header", "rram.frame", Rect(0f, 0.918f, 1f, 1f), "0.012 0.035 0.045 0.97");
        ui.Panel("rram.nav", "rram.frame", Rect(0f, 0.052f, 0.155f, 0.918f), "0.012 0.030 0.040 0.94");
        ui.Panel("rram.body", "rram.frame", Rect(0.155f, 0.052f, 1f, 0.918f), "0.010 0.027 0.035 0.88");
        ui.Panel("rram.footer", "rram.frame", Rect(0f, 0f, 1f, 0.052f), "0.012 0.027 0.034 0.95");

        ui.Panel("rram.logo", "rram.header", Rect(0.014f, 0.14f, 0.060f, 0.86f), "0.045 0.13 0.16 0.94");
        ui.Label("rram.logo.text", "rram.logo", RogueUiRect.Full, "RR", 16, "0.95 0.52 0.20 1", "MiddleCenter");
        ui.Label("rram.brand", "rram.header", Rect(0.071f, 0.39f, 0.38f, 0.91f), "ROGUERUST", 18, _config.UI.Theme.Text, "MiddleLeft");
        ui.Label("rram.product", "rram.header", Rect(0.071f, 0.08f, 0.55f, 0.43f), "ADMINISTRATION  •  AdminMenu 2.4.9  •  RogueRust 4.2.9", 8, "0.50 0.78 0.83 1", "MiddleLeft");
        ui.Label("rram.health", "rram.header", Rect(0.70f, 0f, 0.91f, 1f), "● SYSTEM HEALTHY", 9, _config.UI.Theme.Success, "MiddleRight");
        string appearance = UiActionCallback(s.Player, "ui.appearance", () => { s.AppearanceOpen = !s.AppearanceOpen; Draw(s); });
        ui.Button("rram.appearance", "rram.header", Rect(0.920f, 0.20f, 0.950f, 0.80f), "⚙", appearance, "0.025 0.060 0.072 0.98", 13);
        string closeAdmin = UiActionCallback(s.Player, "ui.close.admin", () => CloseAdminMenu(s));
        ui.Button("rram.close", "rram.header", Rect(0.958f, 0.20f, 0.988f, 0.80f), "×", closeAdmin, _config.UI.Theme.Danger, 15);

        DrawNavigation(ui, s);
        DrawHeader(ui, s);
        ui.Label("rram.footer.left", "rram.footer", Rect(0.015f, 0f, 0.62f, 1f), "RogueRust Administration  |  Oxide / Carbon", 8, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.footer.right", "rram.footer", Rect(0.65f, 0f, 0.985f, 1f), "● Healthy  •  Admin  •  " + BasePlayer.activePlayerList.Count + " Players", 8, _config.UI.Theme.MutedText, "MiddleRight");

        switch (s.Menu)
        {
            case MenuType.Dashboard: DrawDashboard(ui, s); break;
            case MenuType.Players: DrawPlayers(ui, s); break;
            case MenuType.Teleport: DrawTeleport(ui, s); break;
            case MenuType.Commands: DrawAdminCommands(ui, s); break;
            case MenuType.Permissions: DrawPermissions(ui, s); break;
            case MenuType.Groups: DrawGroups(ui, s); break;
            case MenuType.Convars: DrawConvars(ui, s); break;
            case MenuType.Plugins: DrawPlugins(ui, s); break;
            case MenuType.Give: DrawGive(ui, s); break;
            case MenuType.Vehicles: DrawVehicles(ui, s); break;
            case MenuType.Diagnostics: DrawDiagnostics(ui, s); break;
        }
        if (s.AppearanceOpen) DrawAppearance(ui, s);

        ShowUiIfChanged(s.Player, ui);
        if (s.Menu == MenuType.Give && _config.UI.UseNativeItemIcons && s.NativeGiveIcons.Count > 0)
            NextTick(() => { if (s.Player != null && s.Player.IsConnected && s.Menu == MenuType.Give) ShowNativeGiveIcons(s); });
        if (s.Menu == MenuType.Vehicles && _config.UI.UseNativeItemIcons)
            NextTick(() => { if (s.Player != null && s.Player.IsConnected && s.Menu == MenuType.Vehicles) ShowNativeVehicleIcons(s); });
    }

    private void DrawAppearance(RogueUiDocument ui, Session s)
    {
        ui.Panel("rram.appearance.overlay", "rram.frame", Rect(0.57f, 0.12f, 0.965f, 0.89f), "0.008 0.020 0.028 0.985");
        ui.Panel("rram.appearance.accent", "rram.appearance.overlay", Rect(0f, 0.992f, 1f, 1f), "0.10 0.68 0.82 1");
        ui.Label("rram.appearance.title", "rram.appearance.overlay", Rect(0.06f, 0.88f, 0.72f, 0.97f), "APPEARANCE & WINDOW", 16, _config.UI.Theme.Text, "MiddleLeft");
        string close = UiActionCallback(s.Player, "appearance.close", () => { s.AppearanceOpen = false; Draw(s); });
        ui.Button("rram.appearance.close", "rram.appearance.overlay", Rect(0.86f, 0.89f, 0.95f, 0.96f), "×", close, _config.UI.Theme.Danger, 13);

        ui.Label("rram.appearance.window.label", "rram.appearance.overlay", Rect(0.06f, 0.77f, 0.45f, 0.84f), "WINDOW SIZE", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        WindowMode[] modes = { WindowMode.Compact, WindowMode.Standard, WindowMode.Full };
        for (int i = 0; i < modes.Length; i++)
        {
            WindowMode mode = modes[i]; float x = 0.06f + i * 0.295f;
            string cb = UiActionCallback(s.Player, "appearance.window." + mode, () => { s.Window = mode; Draw(s); });
            ui.Button("rram.appearance.window." + mode, "rram.appearance.overlay", Rect(x, 0.69f, x + 0.26f, 0.76f), mode.ToString().ToUpperInvariant(), cb, s.Window == mode ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 10);
        }

        ui.Label("rram.appearance.scale.label", "rram.appearance.overlay", Rect(0.06f, 0.57f, 0.60f, 0.64f), $"UI SCALE  {_config.UI.Scale:0.00}×", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        string scaleDown = UiActionCallback(s.Player, "appearance.scale.down", () => SetUiScale(s, _config.UI.Scale - 0.05f));
        string scaleUp = UiActionCallback(s.Player, "appearance.scale.up", () => SetUiScale(s, _config.UI.Scale + 0.05f));
        ui.Button("rram.appearance.scale.down", "rram.appearance.overlay", Rect(0.06f, 0.49f, 0.20f, 0.56f), "−", scaleDown, _config.UI.Theme.SurfaceAlt, 13);
        ui.Button("rram.appearance.scale.up", "rram.appearance.overlay", Rect(0.22f, 0.49f, 0.36f, 0.56f), "+", scaleUp, _config.UI.Theme.SurfaceAlt, 13);

        ui.Label("rram.appearance.dim.label", "rram.appearance.overlay", Rect(0.06f, 0.38f, 0.60f, 0.45f), $"WORLD DIMMING  {(int)(s.WorldDimming * 100)}%", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        string dimDown = UiActionCallback(s.Player, "appearance.dim.down", () => { s.WorldDimming = Math.Max(0f, s.WorldDimming - 0.05f); Draw(s); });
        string dimUp = UiActionCallback(s.Player, "appearance.dim.up", () => { s.WorldDimming = Math.Min(0.45f, s.WorldDimming + 0.05f); Draw(s); });
        ui.Button("rram.appearance.dim.down", "rram.appearance.overlay", Rect(0.06f, 0.30f, 0.20f, 0.37f), "−", dimDown, _config.UI.Theme.SurfaceAlt, 13);
        ui.Button("rram.appearance.dim.up", "rram.appearance.overlay", Rect(0.22f, 0.30f, 0.36f, 0.37f), "+", dimUp, _config.UI.Theme.SurfaceAlt, 13);

        ui.Panel("rram.appearance.sample", "rram.appearance.overlay", Rect(0.53f, 0.30f, 0.94f, 0.62f), _config.UI.Theme.Surface);
        ui.Label("rram.appearance.sample.title", "rram.appearance.sample", Rect(0.08f, 0.67f, 0.92f, 0.90f), "LIVE SAMPLE", 11, _config.UI.Theme.Text, "MiddleLeft");
        ui.Label("rram.appearance.sample.glass", "rram.appearance.sample", Rect(0.08f, 0.47f, 0.92f, 0.64f), "Blue/black glass", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.appearance.sample.info", "rram.appearance.sample", Rect(0.08f, 0.31f, 0.92f, 0.48f), "Cyan information", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.appearance.sample.brand", "rram.appearance.sample", Rect(0.08f, 0.15f, 0.92f, 0.32f), "Orange brand accent", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.appearance.note", "rram.appearance.overlay", Rect(0.06f, 0.08f, 0.94f, 0.20f), "Window presets resize the whole RRAM shell without expensive drag updates.", 9, _config.UI.Theme.MutedText, "MiddleLeft");
    }

    private void EnsurePersistentCursor(BasePlayer player)
    {
        if (player == null || !player.IsConnected) return;
        DestroyUi(player, CursorRoot);
        CuiElementContainer cursor = new CuiElementContainer();
        cursor.Add(new CuiElement
        {
            Name = CursorRoot,
            Parent = "Overlay",
            Components =
            {
                new CuiNeedsCursorComponent(),
                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
            }
        });
        CuiHelper.AddUi(player, cursor);
    }

    private void CloseAdminMenu(Session s)
    {
        if (s?.Player == null) return;
        DestroyUi(s.Player, Overlay);
        DestroyUi(s.Player, Popup);
        DestroyUi(s.Player, Root);
        DestroyUi(s.Player, CursorRoot);
        _sessions.Remove(s.Player.userID);
    }

    private void SetUiScale(Session s, float value)
    {
        _config.UI.Scale = Math.Max(0.85f, Math.Min(1.15f, value));
        SaveConfig();
        Draw(s);
    }

    private void ApplyTheme(RogueUiDocument ui)
    {
        ui.Theme.PanelColor = _config.UI.Theme.Background;
        ui.Theme.SurfaceColor = _config.UI.Theme.Surface;
        ui.Theme.MutedSurfaceColor = _config.UI.Theme.SurfaceAlt;
        ui.Theme.HeaderColor = _config.UI.Theme.AccentMuted;
        ui.Theme.ButtonColor = _config.UI.Theme.SurfaceAlt;
        ui.Theme.PrimaryColor = _config.UI.Theme.Accent;
        ui.Theme.HighlightColor = _config.UI.Theme.Accent;
        ui.Theme.SuccessColor = _config.UI.Theme.Success;
        ui.Theme.DangerColor = _config.UI.Theme.Danger;
        ui.Theme.CloseColor = _config.UI.Theme.Danger;
        ui.Theme.TextColor = _config.UI.Theme.Text;
        ui.Theme.MutedTextColor = _config.UI.Theme.MutedText;
        ui.Theme.TitleFontSize = 20;
        ui.Theme.BodyFontSize = 12;
        ui.Theme.ButtonFontSize = 12;
    }

    private void DrawNavigation(RogueUiDocument ui, Session s)
    {
        MenuType[] all = (MenuType[])Enum.GetValues(typeof(MenuType));
        List<MenuType> available = all.Where(x => CanAccess(s.Player, x)).ToList();
        const float top = 0.965f;
        const float row = 0.078f;
        const float height = 0.061f;
        for (int i = 0; i < available.Count; i++)
        {
            MenuType menu = available[i];
            float yMax = top - i * row;
            float yMin = yMax - height;
            string cb = UiActionCallback(s.Player, $"nav.{menu}", () => { s.Menu = menu; s.SubMenu = 0; s.ResetListState(); Draw(s); });
            if (s.Menu == menu) ui.Panel("rram.nav.rail." + menu, "rram.nav", Rect(0.015f, yMin, 0.030f, yMax), "0.95 0.42 0.12 1");
            ui.Button("rram.nav." + menu, "rram.nav", Rect(0.04f, yMin, 0.94f, yMax), ShortMenuName(menu), cb, s.Menu == menu ? "0.035 0.095 0.115 0.96" : "0.012 0.030 0.040 0.42", 9);
        }
    }

    private static string ShortMenuName(MenuType menu) => menu switch
    {
        MenuType.Dashboard => "HOME",
        MenuType.Permissions => "PERMS",
        MenuType.Diagnostics => "DIAG",
        MenuType.Teleport => "TP",
        MenuType.Commands => "COMMANDS",
        MenuType.Give => "ITEMS",
        MenuType.Vehicles => "VEHICLES",
        _ => menu.ToString().ToUpperInvariant()
    };

    private void DrawHeader(RogueUiDocument ui, Session s)
    {
        ui.Label("rram.title", "rram.body", Rect(0.03f, 0.91f, 0.75f, 0.98f), GetHeaderTitle(s), 17, _config.UI.Theme.Text, "MiddleLeft");
        if (MenuSupportsSearch(s.Menu))
        {
            string searchCb = UiCallback(s.Player, "search", arg => { s.Search = UiCallbackArgument(arg, 0).Trim(); s.Page = 0; Draw(s); });
            // The input owns the full visible search region so every point in the field is clickable.
            // Use the input's own text as the empty-state hint; a separate label can intercept Rust CUI pointer events.
            ui.Input("rram.search", "rram.body", Rect(0.58f, 0.91f, 0.86f, 0.965f),
                string.IsNullOrWhiteSpace(s.Search) ? "SEARCH" : s.Search, searchCb, 10,
                string.IsNullOrWhiteSpace(s.Search) ? _config.UI.Theme.MutedText : _config.UI.Theme.Text, 64);
            string clear = UiActionCallback(s.Player, "search.clear", () => { s.Search = string.Empty; s.Character = "~"; s.Page = 0; Draw(s); });
            ui.Button("rram.search.clear", "rram.body", Rect(0.87f, 0.91f, 0.95f, 0.965f), "CLEAR", clear, _config.UI.Theme.SurfaceAlt, 9);
        }
    }

    private string GetHeaderTitle(Session s)
    {
        if (s.Selection != SelectionPurpose.None) return "SELECT PLAYER";
        if (s.Menu == MenuType.Players && !string.IsNullOrEmpty(s.SelectedPlayerId)) return "PLAYER • " + s.SelectedPlayerName;
        if (s.Menu == MenuType.Permissions && !string.IsNullOrEmpty(s.SelectedPlayerName)) return "PERMISSIONS • " + s.SelectedPlayerName;
        if (s.Menu == MenuType.Groups && !string.IsNullOrEmpty(s.SelectedGroup)) return "GROUPS • " + s.SelectedGroup;
        return s.Menu.ToString().ToUpperInvariant();
    }

    private bool MenuSupportsSearch(MenuType menu) => menu is MenuType.Players or MenuType.Teleport or MenuType.Permissions or MenuType.Groups or MenuType.Convars or MenuType.Plugins or MenuType.Give or MenuType.Vehicles;

    private void DrawCharacterFilter<T>(RogueUiDocument ui, Session s, IReadOnlyList<T> source, Func<T, string> name)
    {
        string chars = "~ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (int i = 0; i < chars.Length; i++)
        {
            string c = chars[i].ToString();
            float top = 0.915f - i * (0.835f / chars.Length);
            float bottom = top - (0.75f / chars.Length);
            string cb = UiActionCallback(s.Player, "char." + c, () => { s.Character = c; s.Page = 0; Draw(s); });
            ui.Button("rram.char." + i, "rram.body", Rect(0.01f, bottom, 0.035f, top), c, cb,
                s.Character == c ? _config.UI.Theme.Accent : "0 0 0 0", 9);
        }
    }

    private void DrawPager(RogueUiDocument ui, Session s, int count, float y = 0.018f)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(count / (double)_config.UI.PageSize));
        s.Page = Math.Max(0, Math.Min(s.Page, pages - 1));
        if (s.Page > 0)
        {
            string prev = UiActionCallback(s.Player, "page.prev", () => { s.Page--; Draw(s); });
            ui.Button("rram.prev", "rram.body", Rect(0.40f, y, 0.47f, y + 0.045f), "‹ PREV", prev, _config.UI.Theme.SurfaceAlt, 10);
        }
        ui.Label("rram.page", "rram.body", Rect(0.475f, y, 0.555f, y + 0.045f), $"{s.Page + 1} / {pages}", 10, _config.UI.Theme.MutedText);
        if (s.Page + 1 < pages)
        {
            string next = UiActionCallback(s.Player, "page.next", () => { s.Page++; Draw(s); });
            ui.Button("rram.next", "rram.body", Rect(0.56f, y, 0.63f, y + 0.045f), "NEXT ›", next, _config.UI.Theme.SurfaceAlt, 10);
        }
    }

    private List<T> PageWithSize<T>(List<T> source, int page, int pageSize) =>
        source.Skip(Math.Max(0, page) * Math.Max(1, pageSize)).Take(Math.Max(1, pageSize)).ToList();

    private void DrawPagerWithSize(RogueUiDocument ui, Session s, int count, int pageSize, float y = 0.018f)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(count / (double)Math.Max(1, pageSize)));
        s.Page = Math.Max(0, Math.Min(s.Page, pages - 1));
        if (s.Page > 0)
        {
            string prev = UiActionCallback(s.Player, "dense.page.prev", () => { s.Page--; Draw(s); });
            ui.Button("rram.dense.prev", "rram.body", Rect(0.405f, y, 0.470f, y + 0.038f), "‹ PREV", prev, _config.UI.Theme.SurfaceAlt, 9);
        }
        ui.Label("rram.dense.page", "rram.body", Rect(0.475f, y, 0.555f, y + 0.038f), $"{s.Page + 1} / {pages}", 9, _config.UI.Theme.MutedText);
        if (s.Page + 1 < pages)
        {
            string next = UiActionCallback(s.Player, "dense.page.next", () => { s.Page++; Draw(s); });
            ui.Button("rram.dense.next", "rram.body", Rect(0.560f, y, 0.625f, y + 0.038f), "NEXT ›", next, _config.UI.Theme.SurfaceAlt, 9);
        }
    }


    #endregion

    #region Dashboard

    private void DrawDashboard(RogueUiDocument ui, Session s)
    {
        int online = Rogue.Players.ActiveCount;
        int sleepers = Rogue.Players.SleepingCount;
        int pluginsLoaded = Interface.Oxide.RootPluginManager.GetPlugins().Count(p => p != null && !p.IsCorePlugin);
        int monumentCount = 0;
        try { monumentCount = DiscoverMenuMonuments(false).Count; } catch { }
        int entities = 0;
        try { entities = BaseNetworkable.serverEntities.Count; } catch { }
        string uptime = FormatDuration(Time.realtimeSinceStartup);
        string worldSize = GetServerSetting("worldsize", "Unknown");
        string seed = GetServerSetting("seed", "Unknown");

        ui.Label("rram.dashboard.welcome", "rram.body", Rect(0.055f, 0.855f, 0.94f, 0.91f), "SERVER OVERVIEW", 16, _config.UI.Theme.Text, "MiddleLeft");
        string dashboardAdminBadge = GetServerAdminBadge(s.Player.UserIDString);
        if (!string.IsNullOrEmpty(dashboardAdminBadge))
            ui.Badge("rram.dashboard.admin", "rram.body", Rect(0.785f, 0.858f, 0.94f, 0.905f), dashboardAdminBadge, _config.UI.Theme.Success);
        var cards = new (string Title, string Value)[]
        {
            ("ONLINE", online.ToString()), ("SLEEPERS", sleepers.ToString()), ("PLUGINS", pluginsLoaded.ToString()),
            ("MONUMENTS", monumentCount.ToString()), ("ENTITIES", entities.ToString()), ("UPTIME", uptime)
        };
        for (int i = 0; i < cards.Length; i++)
        {
            int col = i % 3, row = i / 3; float left = 0.055f + col * 0.295f; float top = 0.80f - row * 0.15f;
            ui.Panel("rram.dashboard.card." + i, "rram.body", Rect(left, top - 0.115f, left + 0.275f, top), _config.UI.Theme.SurfaceAlt);
            ui.Label("rram.dashboard.card.title." + i, "rram.dashboard.card." + i, Rect(0.06f, 0.60f, 0.94f, 0.90f), cards[i].Title, 9, _config.UI.Theme.MutedText, "MiddleLeft");
            ui.Label("rram.dashboard.card.value." + i, "rram.dashboard.card." + i, Rect(0.06f, 0.12f, 0.94f, 0.62f), cards[i].Value, 17, _config.UI.Theme.Text, "MiddleLeft");
        }

        ui.Panel("rram.dashboard.map", "rram.body", Rect(0.055f, 0.40f, 0.94f, 0.53f), _config.UI.Theme.Background);
        ui.Label("rram.dashboard.map.title", "rram.dashboard.map", Rect(0.025f, 0.60f, 0.30f, 0.90f), "MAP", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.dashboard.map.value", "rram.dashboard.map", Rect(0.025f, 0.12f, 0.97f, 0.60f), $"Size {worldSize}  •  Seed {seed}  •  Saved locations {_savedLocations.Count}", 11, _config.UI.Theme.Text, "MiddleLeft");

        DrawEnvironmentControls(ui, s);

        DrawDashboardShortcut(ui, s, 0, "PLAYERS", MenuType.Players, 0.055f);
        DrawDashboardShortcut(ui, s, 1, "TELEPORT", MenuType.Teleport, 0.265f);
        DrawDashboardShortcut(ui, s, 2, "GIVE", MenuType.Give, 0.475f);
        DrawDashboardShortcut(ui, s, 3, "PLUGINS", MenuType.Plugins, 0.685f);

        if (CanAccess(s.Player, MenuType.Diagnostics))
        {
            string diag = UiActionCallback(s.Player, "dashboard.diagnostics", () => { s.Menu = MenuType.Diagnostics; s.SubMenu = 0; s.ResetListState(); Draw(s); });
            ui.Button("rram.dashboard.diagnostics", "rram.body", Rect(0.055f, 0.165f, 0.235f, 0.225f), "DLL DIAGNOSTICS", diag, _config.UI.Theme.SurfaceAlt, 10);
            string kernelState = Rogue.Lifecycle.State.ToString();
            string healthColor = Rogue.Dependencies.UnsatisfiedRequiredCount == 0 ? _config.UI.Theme.Success : _config.UI.Theme.Warning;
            ui.Badge("rram.dashboard.kernel", "rram.body", Rect(0.250f, 0.172f, 0.455f, 0.218f), "KERNEL " + kernelState, healthColor);
        }
    }

    private void DrawEnvironmentControls(RogueUiDocument ui, Session s)
    {
        ui.Label("rram.dashboard.env.title", "rram.body", Rect(0.055f, 0.342f, 0.205f, 0.382f), "SERVER TIME", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        var times = new (string Label, float Hour)[]
        {
            ("DAWN 06", 6f),
            ("MORNING 09", 9f),
            ("NOON 12", 12f),
            ("EVENING 18", 18f),
            ("NIGHT 22", 22f)
        };

        for (int i = 0; i < times.Length; i++)
        {
            var time = times[i];
            float left = 0.205f + i * 0.145f;
            float hour = time.Hour;
            string cb = UiActionCallback(s.Player, "dashboard.env." + i, () => SetEnvironmentTime(s, hour));
            ui.Button("rram.dashboard.env." + i, "rram.body", Rect(left, 0.335f, left + 0.132f, 0.382f), time.Label, cb, _config.UI.Theme.SurfaceAlt, 9);
        }
    }

    private void SetEnvironmentTime(Session s, float hour)
    {
        hour = Math.Max(0f, Math.Min(24f, hour));
        string value = hour.ToString("0.##", CultureInfo.InvariantCulture);
        rust.RunServerCommand("env.time " + value);
        Audit(s.Player, "Set environment time to " + value);
        Toast(s.Player, "Server Time", "Environment time set to " + value + ":00", _config.UI.Theme.Success, _config.UI.ServerTimeToastSeconds);
        Draw(s);
    }

    private void DrawDashboardShortcut(RogueUiDocument ui, Session s, int index, string label, MenuType menu, float left)
    {
        if (!CanAccess(s.Player, menu)) return;
        string cb = UiActionCallback(s.Player, "dashboard." + label.ToLowerInvariant(), () => { s.Menu = menu; s.SubMenu = 0; s.ResetListState(); Draw(s); });
        ui.Button("rram.dashboard.shortcut." + index, "rram.body", Rect(left, 0.24f, left + 0.18f, 0.31f), label, cb, _config.UI.Theme.AccentMuted, 11);
    }

    private static string GetServerSetting(string name, string fallback)
    {
        try
        {
            Type type = Type.GetType("ConVar.Server, Assembly-CSharp", false);
            if (type == null) return fallback;
            var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
            object value = field?.GetValue(null);
            if (value == null)
            {
                var property = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase);
                value = property?.GetValue(null, null);
            }
            return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture) ?? fallback;
        }
        catch { return fallback; }
    }

    #endregion

    #region Contextual plugin commands

    private void ExecuteCommand(Session s, CommandEntry entry, bool chat)
    {
        string command = (entry.Command ?? string.Empty)
            .Replace("{target1_name}", Quote(s.Target1Name))
            .Replace("{target1_id}", s.Target1Id ?? string.Empty)
            .Replace("{target2_name}", Quote(s.Target2Name))
            .Replace("{target2_id}", s.Target2Id ?? string.Empty);
        if (chat)
            rust.RunClientCommand(s.Player, "chat.say", command);
        else
            rust.RunServerCommand(command);

        Audit(s.Player, "Ran command: " + command);
        Toast(s.Player, "Command", string.Format(GetLang("Notice.CommandRun", s.Player), command.Replace("\"", string.Empty)), _config.UI.Theme.Success);
        s.ClearCommand();
        if (entry.CloseOnRun) { DestroyUi(s.Player, Root); DestroyUi(s.Player, CursorRoot); _sessions.Remove(s.Player.userID); }
        else Draw(s);
    }

    private void DrawAdminCommands(RogueUiDocument ui, Session s)
    {
        ui.Label("rram.commands.help", "rram.body", Rect(0.055f, 0.855f, 0.94f, 0.91f), "CONFIGURED ADMIN COMMANDS • event-driven • no polling", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        var commands = (_config.Commands.AdminCommands ?? new List<AdminCommandEntry>())
            .Where(x => PermissionRequirementMet(s.Player, x.RequiredPermission)).ToList();
        for (int i = 0; i < commands.Count; i++)
        {
            AdminCommandEntry entry = commands[i];
            int col = i % 3, row = i / 3;
            float left = 0.055f + col * 0.30f;
            float top = 0.80f - row * 0.105f;
            string cb = UiActionCallback(s.Player, "admincmd." + i, () =>
            {
                Action run = () => ExecuteCommand(s, entry, entry.SubType == CommandSubType.Chat);
                if (entry.RequireConfirmation) Confirm(s, entry.Name, entry.Description.Length > 0 ? entry.Description : "Run this command?", run);
                else run();
            });
            ui.Button("rram.admincmd." + i, "rram.body", Rect(left, top - 0.06f, left + 0.27f, top), entry.Name.ToUpperInvariant(), cb, entry.RequireConfirmation ? _config.UI.Theme.Warning : _config.UI.Theme.SurfaceAlt, 10);
            if (!string.IsNullOrWhiteSpace(entry.Description))
                ui.Label("rram.admincmd.desc." + i, "rram.body", Rect(left, top - 0.095f, left + 0.27f, top - 0.062f), entry.Description, 8, _config.UI.Theme.MutedText, "UpperLeft");
        }
    }

    #endregion

    #region Players and player actions

    private void DrawPlayers(RogueUiDocument ui, Session s)
    {
        if (s.Selection != SelectionPurpose.None) { DrawPlayerSelection(ui, s); return; }
        if (string.IsNullOrEmpty(s.SelectedPlayerId)) { DrawPlayerBrowser(ui, s); return; }
        DrawPlayerDetails(ui, s);
    }

    private void DrawPlayerBrowser(RogueUiDocument ui, Session s)
    {
        string online = UiActionCallback(s.Player, "players.online", () => { s.ShowOnline = !s.ShowOnline; s.Page = 0; Draw(s); });
        string offline = UiActionCallback(s.Player, "players.offline", () => { s.ShowOffline = !s.ShowOffline; s.Page = 0; Draw(s); });
        ui.Toggle("rram.players.online", "rram.body", Rect(0.052f, 0.862f, 0.088f, 0.900f), s.ShowOnline, online);
        ui.Label("rram.players.online.label", "rram.body", Rect(0.093f, 0.862f, 0.160f, 0.900f), "ONLINE", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Toggle("rram.players.offline", "rram.body", Rect(0.170f, 0.862f, 0.206f, 0.900f), s.ShowOffline, offline);
        ui.Label("rram.players.offline.label", "rram.body", Rect(0.211f, 0.862f, 0.330f, 0.900f), "RECENT / OFFLINE", 9, _config.UI.Theme.MutedText, "MiddleLeft");

        if (HasAdminPermission(s.Player, BulkPermission))
        {
            string bulk = UiActionCallback(s.Player, "players.bulk.toggle", () => { s.BulkMode = !s.BulkMode; if (!s.BulkMode) s.BulkSelectedPlayers.Clear(); Draw(s); });
            ui.Button("rram.players.bulk.toggle", "rram.body", Rect(0.345f, 0.858f, 0.475f, 0.902f), s.BulkMode ? "BULK • ON" : "BULK SELECT", bulk, s.BulkMode ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
            if (s.BulkMode)
            {
                ui.Label("rram.players.bulk.count", "rram.body", Rect(0.485f, 0.858f, 0.60f, 0.902f), s.BulkSelectedPlayers.Count + " SELECTED", 8, _config.UI.Theme.MutedText, "MiddleLeft");
                string heal = UiActionCallback(s.Player, "players.bulk.heal", () => BulkHeal(s));
                string reset = UiActionCallback(s.Player, "players.bulk.reset", () => BulkResetMetabolism(s));
                string strip = UiActionCallback(s.Player, "players.bulk.strip", () => Confirm(s, "BULK STRIP", "Strip inventory from all selected online players?", () => BulkStrip(s)));
                ui.Button("rram.players.bulk.heal", "rram.body", Rect(0.605f, 0.858f, 0.705f, 0.902f), "HEAL ALL", heal, _config.UI.Theme.Success, 8);
                ui.Button("rram.players.bulk.reset", "rram.body", Rect(0.710f, 0.858f, 0.820f, 0.902f), "RESET META", reset, _config.UI.Theme.SurfaceAlt, 8);
                ui.Button("rram.players.bulk.strip", "rram.body", Rect(0.825f, 0.858f, 0.940f, 0.902f), "STRIP ALL", strip, _config.UI.Theme.Danger, 8);
            }
        }

        List<RecentPlayer> players = GetVisiblePlayers(s);
        const int pageSize = 32;
        DrawCharacterFilter(ui, s, players, x => x.Name);
        List<RecentPlayer> page = PageWithSize(players, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            RecentPlayer rp = page[i]; int col = i % 4, row = i / 4;
            float left = 0.052f + col * 0.222f; float top = 0.825f - row * 0.088f;
            string cb = UiActionCallback(s.Player, $"player.open.{s.Page}.{i}", () => { if (s.BulkMode) { if (!s.BulkSelectedPlayers.Add(rp.Id)) s.BulkSelectedPlayers.Remove(rp.Id); Draw(s); } else { s.SelectedPlayerId = rp.Id; s.SelectedPlayerName = rp.Name; Draw(s); } });
            string status = IsOnline(rp.Id) ? "ONLINE" : "OFFLINE";
            string card = $"rram.player.{i}";
            ui.Panel(card, "rram.body", Rect(left, top - 0.067f, left + 0.210f, top), s.BulkMode && s.BulkSelectedPlayers.Contains(rp.Id) ? _config.UI.Theme.AccentMuted : _config.UI.Theme.SurfaceAlt);
            ui.Button($"rram.player.btn.{i}", card, Rect(0.02f, 0.30f, 0.98f, 0.94f), rp.Name, cb, "0 0 0 0", 9, "MiddleCenter");
            string adminBadge = GetServerAdminBadge(rp.Id);
            if (!string.IsNullOrEmpty(adminBadge))
                ui.Badge($"rram.player.admin.{i}", card, Rect(0.70f, 0.58f, 0.97f, 0.91f), adminBadge, _config.UI.Theme.Success);
            ui.Label($"rram.player.status.{i}", card, Rect(0.03f, 0.06f, 0.97f, 0.30f), status + "  •  " + rp.Id, 7, IsOnline(rp.Id) ? _config.UI.Theme.Success : _config.UI.Theme.MutedText, "MiddleLeft");
        }
        DrawPagerWithSize(ui, s, players.Count, pageSize);
    }


    private List<BasePlayer> GetBulkOnlineTargets(Session s)
    {
        if (s == null || s.BulkSelectedPlayers.Count == 0) return new List<BasePlayer>();
        return BasePlayer.activePlayerList.Where(x => x != null && x.IsConnected && s.BulkSelectedPlayers.Contains(x.UserIDString)).ToList();
    }

    private void BulkHeal(Session s)
    {
        if (!HasAdminPermission(s.Player, HealPermission)) { Toast(s.Player, "Bulk", GetLang("Error.NoPermission", s.Player), _config.UI.Theme.Danger); return; }
        List<BasePlayer> targets = GetBulkOnlineTargets(s);
        foreach (BasePlayer target in targets) { if (target.IsWounded()) target.StopWounded(); target.Heal(target.MaxHealth()); }
        Audit(s.Player, $"Bulk healed {targets.Count} player(s)");
        Toast(s.Player, "Bulk", $"Healed {targets.Count} online player(s).", _config.UI.Theme.Success);
        Draw(s);
    }

    private void BulkResetMetabolism(Session s)
    {
        if (!HasAdminPermission(s.Player, HealPermission)) { Toast(s.Player, "Bulk", GetLang("Error.NoPermission", s.Player), _config.UI.Theme.Danger); return; }
        List<BasePlayer> targets = GetBulkOnlineTargets(s);
        foreach (BasePlayer target in targets)
        {
            target.metabolism.bleeding.value = 0; target.metabolism.calories.value = target.metabolism.calories.max;
            target.metabolism.hydration.value = target.metabolism.hydration.max; target.metabolism.radiation_level.value = 0;
            target.metabolism.radiation_poison.value = 0; target.metabolism.poison.value = 0; target.metabolism.wetness.value = 0;
            target.metabolism.SendChanges();
        }
        Audit(s.Player, $"Bulk reset metabolism for {targets.Count} player(s)");
        Toast(s.Player, "Bulk", $"Reset metabolism for {targets.Count} online player(s).", _config.UI.Theme.Success);
        Draw(s);
    }

    private void BulkStrip(Session s)
    {
        if (!HasAdminPermission(s.Player, StripPermission)) { DestroyUi(s.Player, Overlay); Toast(s.Player, "Bulk", GetLang("Error.NoPermission", s.Player), _config.UI.Theme.Danger); return; }
        List<BasePlayer> targets = GetBulkOnlineTargets(s);
        foreach (BasePlayer target in targets) target.inventory.Strip();
        DestroyUi(s.Player, Overlay);
        Audit(s.Player, $"Bulk stripped inventory from {targets.Count} player(s)");
        Toast(s.Player, "Bulk", $"Stripped {targets.Count} online player(s).", _config.UI.Theme.Success);
        Draw(s);
    }

    private void DrawPlayerSelection(RogueUiDocument ui, Session s)
    {
        List<RecentPlayer> players = GetVisiblePlayers(s, forceBoth: true);
        const int pageSize = 32;
        DrawCharacterFilter(ui, s, players, x => x.Name);
        List<RecentPlayer> page = PageWithSize(players, s.Page, pageSize);

        // Keep selection controls below the shared sub-tab/search row. The old CANCEL
        // rectangle crossed into that row and made Permissions > Player look misaligned.
        string cancel = UiActionCallback(s.Player, "select.cancel", () =>
        {
            s.Selection = SelectionPurpose.None;
            s.Page = 0;
            Draw(s);
        });
        ui.Button("rram.select.cancel", "rram.body", Rect(0.800f, 0.825f, 0.940f, 0.862f),
            "CANCEL", cancel, _config.UI.Theme.Danger, 8);

        for (int i = 0; i < page.Count; i++)
        {
            RecentPlayer rp = page[i];
            int col = i % 4, row = i / 4;
            float left = 0.052f + col * 0.222f;
            float top = 0.805f - row * 0.086f;
            string card = $"rram.select.player.{i}";
            string cb = UiActionCallback(s.Player, $"select.player.{s.Page}.{i}", () => CompletePlayerSelection(s, rp));

            ui.Panel(card, "rram.body", Rect(left, top - 0.062f, left + 0.210f, top), _config.UI.Theme.SurfaceAlt);
            ui.Button(card + ".hit", card, RogueUiRect.Full, string.Empty, cb, "0 0 0 0", 1);
            ui.Label(card + ".name", card, Rect(0.035f, 0.43f, 0.70f, 0.90f), rp.Name, 8, _config.UI.Theme.Text, "MiddleLeft");
            ui.Label(card + ".id", card, Rect(0.035f, 0.08f, 0.78f, 0.43f), rp.Id, 6, _config.UI.Theme.MutedText, "MiddleLeft");

            string adminBadge = GetServerAdminBadge(rp.Id);
            if (!string.IsNullOrEmpty(adminBadge))
                ui.Badge(card + ".admin", card, Rect(0.72f, 0.53f, 0.97f, 0.88f), adminBadge, _config.UI.Theme.Success);
        }
        DrawPagerWithSize(ui, s, players.Count, pageSize);
    }

    private void CompletePlayerSelection(Session s, RecentPlayer rp)
    {
        SelectionPurpose purpose = s.Selection;
        s.Selection = SelectionPurpose.None;
        s.Page = 0;
        switch (purpose)
        {
            case SelectionPurpose.PermissionPlayer:
                s.SelectedPlayerId = rp.Id; s.SelectedPlayerName = rp.Name; break;
            case SelectionPurpose.GroupUser:
                s.SelectedPlayerId = rp.Id; s.SelectedPlayerName = rp.Name; break;
            case SelectionPurpose.GiveTarget:
                s.SelectedPlayerId = rp.Id; s.SelectedPlayerName = rp.Name; break;
        }
        Draw(s);
    }

    private void DrawPlayerDetails(RogueUiDocument ui, Session s)
    {
        IPlayer? covPlayer = covalence.Players.FindPlayerById(s.SelectedPlayerId);
        BasePlayer? bp = FindBasePlayer(s.SelectedPlayerId);
        string back = UiActionCallback(s.Player, "player.back", () => { s.SelectedPlayerId = s.SelectedPlayerName = string.Empty; Draw(s); });
        ui.Button("rram.player.back", "rram.body", Rect(0.055f, 0.85f, 0.16f, 0.895f), "‹ PLAYERS", back, _config.UI.Theme.SurfaceAlt, 10);

        string selectedAdminBadge = GetServerAdminBadge(s.SelectedPlayerId);
        if (!string.IsNullOrEmpty(selectedAdminBadge))
            ui.Badge("rram.player.summary.admin", "rram.body", Rect(0.48f, 0.85f, 0.60f, 0.895f), selectedAdminBadge, _config.UI.Theme.Success);

        if (bp != null)
        {
            string state = bp.IsDead() ? "DEAD" : bp.IsWounded() ? "WOUNDED" : bp.IsSleeping() ? "SLEEPING" : bp.IsConnected ? "ONLINE" : "OFFLINE";
            string stateColor = bp.IsDead() ? _config.UI.Theme.Danger : bp.IsWounded() ? _config.UI.Theme.Warning : bp.IsConnected ? _config.UI.Theme.Success : _config.UI.Theme.SurfaceAlt;
            ui.Badge("rram.player.summary.state", "rram.body", Rect(0.18f, 0.85f, 0.27f, 0.895f), state, stateColor);
            ui.Badge("rram.player.summary.grid", "rram.body", Rect(0.28f, 0.85f, 0.36f, 0.895f), SafeGridReference(bp.transform.position), _config.UI.Theme.AccentMuted);
            ui.Badge("rram.player.summary.hp", "rram.body", Rect(0.37f, 0.85f, 0.47f, 0.895f), "HP " + Math.Round(bp.health, 0), bp.health > bp.MaxHealth() * 0.5f ? _config.UI.Theme.Success : _config.UI.Theme.Warning);
        }

        float y = 0.79f;
        AddInfo(ui, "Name", s.SelectedPlayerName, ref y);
        AddInfo(ui, "Steam ID", s.SelectedPlayerId, ref y);
        AddInfo(ui, "Status", bp != null && bp.IsConnected ? "Online" : "Offline", ref y);
        if (bp != null)
        {
            ulong uid = bp.userID;
            AddInfo(ui, "Auth Level", DeveloperList.Contains(uid) ? "Developer" : (ServerUsers.Get(uid)?.group ?? ServerUsers.UserGroup.None).ToString(), ref y);
            AddInfo(ui, "Idle Time", FormatDuration(bp.IdleTime), ref y);
            AddInfo(ui, "Position", bp.transform.position.ToString(), ref y);
            AddInfo(ui, "Grid", SafeGridReference(bp.transform.position), ref y);
            AddInfo(ui, "Health", Math.Round(bp.health, 1) + " / " + Math.Round(bp.MaxHealth(), 1), ref y);
            AddInfo(ui, "Calories", Math.Round(bp.metabolism.calories.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Hydration", Math.Round(bp.metabolism.hydration.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Temperature", Math.Round(bp.metabolism.temperature.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Comfort", Math.Round(bp.metabolism.comfort.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Wetness", Math.Round(bp.metabolism.wetness.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Bleeding", Math.Round(bp.metabolism.bleeding.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
            AddInfo(ui, "Radiation", Math.Round(bp.metabolism.radiation_level.value, 1).ToString(CultureInfo.InvariantCulture), ref y);
        }
        if (Adapters.Clans.TryGetClan(s.SelectedPlayerId, out string? clan)) AddInfo(ui, "Clan", clan ?? "None", ref y);
        if (Rogue.Integrations.TryCall("PlaytimeTracker", "GetPlayTime", out object? playtimeRaw, s.SelectedPlayerId) && TrySeconds(playtimeRaw, out double playtime)) AddInfo(ui, "Playtime", FormatDuration(playtime), ref y);
        if (Rogue.Integrations.TryCall("PlaytimeTracker", "GetAFKTime", out object? afkRaw, s.SelectedPlayerId) && TrySeconds(afkRaw, out double afk)) AddInfo(ui, "AFK Time", FormatDuration(afk), ref y);
        if (Adapters.Rewards.TryGetPoints(s.SelectedPlayerId, out int points)) AddInfo(ui, "RP", points.ToString(), ref y);
        if (Adapters.Economy.TryGetBalance(s.SelectedPlayerId, out double balance)) AddInfo(ui, "Economics", Math.Round(balance, 2).ToString(CultureInfo.InvariantCulture), ref y);

        DrawPlayerActionButtons(ui, s, bp, covPlayer);
    }

    private void AddInfo(RogueUiDocument ui, string label, string value, ref float y)
    {
        ui.Label("rram.info.l." + y.ToString(CultureInfo.InvariantCulture), "rram.body", Rect(0.055f, y, 0.18f, y + 0.035f), label.ToUpperInvariant(), 9, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.info.v." + y.ToString(CultureInfo.InvariantCulture), "rram.body", Rect(0.18f, y, 0.43f, y + 0.035f), value ?? string.Empty, 10, _config.UI.Theme.Text, "MiddleLeft");
        y -= 0.045f;
    }

    private void DrawTeleport(RogueUiDocument ui, Session s)
    {
        DrawSubTabs(ui, s, new[] { "MONUMENTS", "SAVED", "COORDINATES", "PLAYERS" }, 4);
        switch (Math.Max(0, Math.Min(3, s.SubMenu)))
        {
            case 0: DrawTeleportMonuments(ui, s); break;
            case 1: DrawTeleportSaved(ui, s); break;
            case 2: DrawTeleportCoordinates(ui, s); break;
            case 3: DrawTeleportPlayers(ui, s); break;
        }
    }

    private void DrawTeleportMonuments(RogueUiDocument ui, Session s)
    {
        List<RogueMonumentInfo> monuments;
        try { monuments = DiscoverMenuMonuments(false).OrderBy(MonumentLabel, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Position.x).ThenBy(x => x.Position.z).ToList(); }
        catch (Exception ex)
        {
            LogWarning("World", $"Monument discovery failed: {ex.GetType().Name}: {ex.Message}");
            ui.Label("rram.teleport.error", "rram.body", Rect(0.06f, 0.68f, 0.94f, 0.78f), "MONUMENT DATA IS CURRENTLY UNAVAILABLE", 14, _config.UI.Theme.Warning, "MiddleCenter");
            return;
        }
        if (monuments.Count == 0)
        {
            ui.Label("rram.teleport.empty", "rram.body", Rect(0.06f, 0.68f, 0.94f, 0.78f), "NO MONUMENTS WERE DISCOVERED ON THIS MAP", 14, _config.UI.Theme.MutedText, "MiddleCenter");
            return;
        }

        Dictionary<string, List<RogueMonumentInfo>> groups = monuments.GroupBy(MonumentLabel, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        IEnumerable<KeyValuePair<string, List<RogueMonumentInfo>>> groupQuery = groups.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(s.Search)) groupQuery = groupQuery.Where(x => x.Key.IndexOf(s.Search, StringComparison.OrdinalIgnoreCase) >= 0 || x.Value.Any(m => SafeGridReference(m.Position).IndexOf(s.Search, StringComparison.OrdinalIgnoreCase) >= 0));
        if (!string.IsNullOrEmpty(s.Character) && s.Character != "~") groupQuery = groupQuery.Where(x => x.Key.StartsWith(s.Character, StringComparison.OrdinalIgnoreCase));
        List<KeyValuePair<string, List<RogueMonumentInfo>>> visibleGroups = groupQuery.ToList();
        if (!string.IsNullOrEmpty(s.SelectedMonumentGroup) && !groups.ContainsKey(s.SelectedMonumentGroup)) s.SelectedMonumentGroup = string.Empty;

        DrawCharacterFilter(ui, s, visibleGroups, x => x.Key);
        RogueMonumentInfo nearest = monuments.OrderBy(x => (x.Position - s.Player.transform.position).sqrMagnitude).FirstOrDefault();
        string nearestText = nearest == null ? string.Empty : $"NEAREST • {MonumentLabel(nearest)} • {SafeGridReference(nearest.Position)} • {Vector3.Distance(s.Player.transform.position, nearest.Position):0}m";
        ui.Label("rram.teleport.tree", "rram.body", Rect(0.055f, 0.825f, 0.35f, 0.865f), $"MONUMENT TREE  •  {monuments.Count} LOCATIONS", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.teleport.nearest", "rram.body", Rect(0.36f, 0.825f, 0.68f, 0.865f), nearestText, 8, _config.UI.Theme.MutedText, "MiddleLeft");
        if (nearest != null) { RogueMonumentInfo nearestCapture = nearest; string nearestCb = UiActionCallback(s.Player, "teleport.nearest.go", () => TeleportToMonument(s, nearestCapture)); ui.Button("rram.teleport.nearest.go", "rram.body", Rect(0.69f, 0.825f, 0.78f, 0.865f), "NEAREST", nearestCb, _config.UI.Theme.Success, 8); }
        string refreshCb = UiActionCallback(s.Player, "teleport.refresh", () => { try { DiscoverMenuMonuments(true); } catch { } s.SelectedMonumentGroup = string.Empty; s.Page = 0; Draw(s); });
        ui.Button("rram.teleport.refresh", "rram.body", Rect(0.79f, 0.825f, 0.94f, 0.865f), "REFRESH", refreshCb, _config.UI.Theme.SurfaceAlt, 9);

        int groupPageSize = Math.Max(10, Math.Min(_config.UI.PageSize, 18));
        int pages = Math.Max(1, (int)Math.Ceiling(visibleGroups.Count / (double)groupPageSize));
        s.Page = Math.Max(0, Math.Min(s.Page, pages - 1));
        List<KeyValuePair<string, List<RogueMonumentInfo>>> page = visibleGroups.Skip(s.Page * groupPageSize).Take(groupPageSize).ToList();
        for (int i = 0; i < page.Count; i++)
        {
            KeyValuePair<string, List<RogueMonumentInfo>> group = page[i]; int row = i % 9; int col = i / 9; float left = 0.055f + col * 0.19f; float top = 0.785f - row * 0.073f;
            string label = group.Value.Count > 1 ? $"{group.Key}  ({group.Value.Count})" : group.Key; string key = group.Key;
            string cb = UiActionCallback(s.Player, "teleport.group." + i, () => { s.SelectedMonumentGroup = key; Draw(s); });
            ui.Button("rram.teleport.group." + i, "rram.body", Rect(left, top - 0.052f, left + 0.18f, top), label, cb, string.Equals(s.SelectedMonumentGroup, group.Key, StringComparison.OrdinalIgnoreCase) ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 9);
        }
        if (pages > 1)
        {
            if (s.Page > 0) { string prev = UiActionCallback(s.Player, "teleport.page.prev", () => { s.Page--; Draw(s); }); ui.Button("rram.teleport.prev", "rram.body", Rect(0.055f, 0.055f, 0.15f, 0.10f), "‹ PREV", prev, _config.UI.Theme.SurfaceAlt, 9); }
            ui.Label("rram.teleport.page", "rram.body", Rect(0.155f, 0.055f, 0.25f, 0.10f), $"{s.Page + 1} / {pages}", 9, _config.UI.Theme.MutedText);
            if (s.Page + 1 < pages) { string next = UiActionCallback(s.Player, "teleport.page.next", () => { s.Page++; Draw(s); }); ui.Button("rram.teleport.next", "rram.body", Rect(0.255f, 0.055f, 0.35f, 0.10f), "NEXT ›", next, _config.UI.Theme.SurfaceAlt, 9); }
        }

        ui.Panel("rram.teleport.details", "rram.body", Rect(0.445f, 0.055f, 0.94f, 0.80f), _config.UI.Theme.Background);
        if (string.IsNullOrEmpty(s.SelectedMonumentGroup) || !groups.TryGetValue(s.SelectedMonumentGroup, out List<RogueMonumentInfo> instances))
        {
            // Rust CUI renders escaped newlines inconsistently across runtimes; use two centered labels instead.
            ui.Label("rram.teleport.hint.title", "rram.teleport.details", Rect(0.08f, 0.50f, 0.92f, 0.58f), "SELECT A MONUMENT TYPE", 13, _config.UI.Theme.MutedText, "MiddleCenter");
            ui.Label("rram.teleport.hint.body", "rram.teleport.details", Rect(0.08f, 0.44f, 0.92f, 0.52f), "TO VIEW EVERY LOCATION ON THIS MAP", 11, _config.UI.Theme.MutedText, "MiddleCenter");
            return;
        }
        ui.Label("rram.teleport.selected", "rram.teleport.details", Rect(0.05f, 0.92f, 0.95f, 0.985f), s.SelectedMonumentGroup.ToUpperInvariant(), 15, _config.UI.Theme.Text, "MiddleLeft");
        ui.Label("rram.teleport.count", "rram.teleport.details", Rect(0.05f, 0.875f, 0.95f, 0.925f), $"{instances.Count} LOCATION{(instances.Count == 1 ? string.Empty : "S")}", 9, _config.UI.Theme.MutedText, "MiddleLeft");
        for (int i = 0; i < instances.Count && i < 10; i++)
        {
            RogueMonumentInfo monument = instances[i]; float top = 0.83f - i * 0.075f; float distance = Vector3.Distance(s.Player.transform.position, monument.Position); string grid = SafeGridReference(monument.Position);
            string instanceName = MonumentInstanceLabel(monument, i, instances.Count); string location = $"{instanceName}   •   {grid}   •   {distance:0}m";
            ui.Label("rram.teleport.instance." + i, "rram.teleport.details", Rect(0.05f, top - 0.048f, 0.70f, top), location, 10, _config.UI.Theme.Text, "MiddleLeft");
            RogueMonumentInfo selected = monument; string tp = UiActionCallback(s.Player, "teleport.monument." + i, () => TeleportToMonument(s, selected));
            ui.Button("rram.teleport.go." + i, "rram.teleport.details", Rect(0.73f, top - 0.05f, 0.95f, top), "TELEPORT", tp, _config.UI.Theme.Success, 9);
        }
    }

    private void DrawTeleportSaved(RogueUiDocument ui, Session s)
    {
        string nameCb = UiCallback(s.Player, "teleport.saved.name", arg =>
        {
            string typed = UiCallbackArgument(arg, 0).Trim();
            SetUiState(s.Player, "teleport.saved.name", typed);
            if (!string.IsNullOrWhiteSpace(typed)) SaveCurrentLocation(s, typed);
        });
        string currentName = GetUiState(s.Player, "teleport.saved.name", "") ?? "";
        ui.Label("rram.teleport.saved.name.label", "rram.body", Rect(0.052f, 0.825f, 0.155f, 0.862f), "LOCATION NAME", 8, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Input("rram.teleport.saved.name", "rram.body", Rect(0.155f, 0.817f, 0.410f, 0.866f), currentName, nameCb, 10, _config.UI.Theme.Text, 48);
        string save = UiActionCallback(s.Player, "teleport.saved.save", () => SaveCurrentLocation(s, (GetUiState(s.Player, "teleport.saved.name", "") ?? "").Trim()));
        ui.Button("rram.teleport.saved.save", "rram.body", Rect(0.420f, 0.817f, 0.565f, 0.866f), "SAVE CURRENT", save, _config.UI.Theme.Success, 9);
        ui.Label("rram.teleport.saved.help", "rram.body", Rect(0.580f, 0.817f, 0.94f, 0.866f), "Type a name and press Enter, or click SAVE CURRENT", 8, _config.UI.Theme.MutedText, "MiddleRight");

        List<SavedLocation> locations = Filter(_savedLocations.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase), s, x => x.Name + " " + SafeGridReference(x.Position)).ToList();
        const int pageSize = 28;
        DrawCharacterFilter(ui, s, locations, x => x.Name); List<SavedLocation> page = PageWithSize(locations, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            SavedLocation loc = page[i]; int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.765f - row * 0.096f;
            string card = "rram.saved." + i;
            ui.Panel(card, "rram.body", Rect(left, top - 0.075f, left + 0.210f, top), _config.UI.Theme.SurfaceAlt);
            ui.Label("rram.saved.name." + i, card, Rect(0.03f, 0.53f, 0.96f, 0.90f), loc.Name, 9, _config.UI.Theme.Text, "MiddleLeft");
            ui.Label("rram.saved.grid." + i, card, Rect(0.03f, 0.12f, 0.58f, 0.50f), $"{SafeGridReference(loc.Position)} • {loc.X:0} {loc.Y:0} {loc.Z:0}", 7, _config.UI.Theme.MutedText, "MiddleLeft");
            SavedLocation selected = loc; string tp = UiActionCallback(s.Player, "saved.tp." + i, () => TeleportToSaved(s, selected));
            string delete = UiActionCallback(s.Player, "saved.delete." + i, () => Confirm(s, "DELETE LOCATION", "Delete saved location " + selected.Name + "?", () => DeleteSavedLocation(s, selected.Name)));
            ui.Button("rram.saved.tp." + i, card, Rect(0.61f, 0.53f, 0.96f, 0.90f), "TELEPORT", tp, _config.UI.Theme.Success, 8);
            ui.Button("rram.saved.delete." + i, card, Rect(0.61f, 0.12f, 0.96f, 0.48f), "DELETE", delete, _config.UI.Theme.Danger, 7);
        }
        DrawPagerWithSize(ui, s, locations.Count, pageSize);
    }

    private void DrawTeleportCoordinates(RogueUiDocument ui, Session s)
    {
        string xCb = UiCallback(s.Player, "teleport.coord.x", arg => SetUiState(s.Player, "teleport.coord.x", UiCallbackArgument(arg, 0)));
        string yCb = UiCallback(s.Player, "teleport.coord.y", arg => SetUiState(s.Player, "teleport.coord.y", UiCallbackArgument(arg, 0)));
        string zCb = UiCallback(s.Player, "teleport.coord.z", arg => SetUiState(s.Player, "teleport.coord.z", UiCallbackArgument(arg, 0)));
        ui.Label("rram.coord.title", "rram.body", Rect(0.055f, 0.75f, 0.55f, 0.82f), "TELEPORT TO WORLD COORDINATES", 15, _config.UI.Theme.Text, "MiddleLeft");
        ui.Input("rram.coord.x", "rram.body", Rect(0.055f, 0.64f, 0.25f, 0.70f), GetUiState(s.Player, "teleport.coord.x", "0") ?? "0", xCb, 12, _config.UI.Theme.Text, 24, false, "MiddleCenter");
        ui.Input("rram.coord.y", "rram.body", Rect(0.27f, 0.64f, 0.46f, 0.70f), GetUiState(s.Player, "teleport.coord.y", "0") ?? "0", yCb, 12, _config.UI.Theme.Text, 24, false, "MiddleCenter");
        ui.Input("rram.coord.z", "rram.body", Rect(0.48f, 0.64f, 0.67f, 0.70f), GetUiState(s.Player, "teleport.coord.z", "0") ?? "0", zCb, 12, _config.UI.Theme.Text, 24, false, "MiddleCenter");
        string fill = UiActionCallback(s.Player, "teleport.coord.current", () => { Vector3 p = s.Player.transform.position; SetUiState(s.Player, "teleport.coord.x", p.x.ToString("0.0", CultureInfo.InvariantCulture)); SetUiState(s.Player, "teleport.coord.y", p.y.ToString("0.0", CultureInfo.InvariantCulture)); SetUiState(s.Player, "teleport.coord.z", p.z.ToString("0.0", CultureInfo.InvariantCulture)); Draw(s); });
        string go = UiActionCallback(s.Player, "teleport.coord.go", () => TeleportToCoordinates(s));
        ui.Button("rram.coord.current", "rram.body", Rect(0.055f, 0.53f, 0.31f, 0.59f), "USE CURRENT POSITION", fill, _config.UI.Theme.SurfaceAlt, 9);
        ui.Button("rram.coord.go", "rram.body", Rect(0.33f, 0.53f, 0.52f, 0.59f), "TELEPORT", go, _config.UI.Theme.Success, 10);
        Vector3 current = s.Player.transform.position;
        ui.Panel("rram.coord.current.panel", "rram.body", Rect(0.055f, 0.35f, 0.67f, 0.46f), _config.UI.Theme.Background);
        ui.Label("rram.coord.current.info", "rram.coord.current.panel", Rect(0.04f, 0.18f, 0.96f, 0.82f), $"CURRENT  •  {SafeGridReference(current)}  •  X {current.x:0.0}  Y {current.y:0.0}  Z {current.z:0.0}", 10, _config.UI.Theme.MutedText, "MiddleLeft");
    }

    private void DrawTeleportPlayers(RogueUiDocument ui, Session s)
    {
        List<BasePlayer> players = BasePlayer.activePlayerList.Where(x => x != null && x.IsConnected && x != s.Player).OrderBy(x => StripName(x.displayName), StringComparer.OrdinalIgnoreCase).ToList();
        players = Filter(players, s, x => StripName(x.displayName) + " " + x.UserIDString + " " + SafeGridReference(x.transform.position)).ToList();
        const int pageSize = 32;
        DrawCharacterFilter(ui, s, players, x => StripName(x.displayName)); List<BasePlayer> page = PageWithSize(players, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            BasePlayer target = page[i]; int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.825f - row * 0.088f;
            string cb = UiActionCallback(s.Player, "teleport.player." + i, () => { TeleportSelfTo(s, target); });
            string card = "rram.teleport.player.card." + i;
            ui.Panel(card, "rram.body", Rect(left, top - 0.067f, left + 0.210f, top), _config.UI.Theme.SurfaceAlt);
            ui.Button("rram.teleport.player.btn." + i, card, Rect(0.02f, 0.30f, 0.98f, 0.94f), StripName(target.displayName), cb, "0 0 0 0", 9, "MiddleCenter");
            ui.Label("rram.teleport.player.grid." + i, card, Rect(0.03f, 0.06f, 0.97f, 0.30f), SafeGridReference(target.transform.position) + " • " + target.UserIDString, 7, _config.UI.Theme.MutedText, "MiddleLeft");
        }
        DrawPagerWithSize(ui, s, players.Count, pageSize);
    }

    private void SaveCurrentLocation(Session s, string requestedName = "")
    {
        string name = (requestedName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = (GetUiState(s.Player, "teleport.saved.name", "") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) { Toast(s.Player, "Teleport", "Type a location name first.", _config.UI.Theme.Warning); return; }
        Vector3 p = s.Player.transform.position;
        _savedLocations[name] = new SavedLocation { Name = name, X = p.x, Y = p.y, Z = p.z, SavedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), SavedBy = s.Player.UserIDString };
        SaveSavedLocations(); RemoveUiState(s.Player, "teleport.saved.name"); Audit(s.Player, $"Saved admin location {name} at {SafeGridReference(p)} ({p})"); Toast(s.Player, "Teleport", "Saved " + name, _config.UI.Theme.Success); Draw(s);
    }

    private void DeleteSavedLocation(Session s, string name) { if (_savedLocations.Remove(name)) { SaveSavedLocations(); Audit(s.Player, "Deleted saved admin location " + name); } Draw(s); }
    private void TeleportToSaved(Session s, SavedLocation location) { if (!TeleportPlayerWithProtection(s.Player, location.Position, "saved location " + location.Name)) return; Audit(s.Player, $"Teleported to saved location {location.Name} at {SafeGridReference(location.Position)}"); Toast(s.Player, "Teleport", "Teleported to " + location.Name, _config.UI.Theme.Success); Draw(s); }
    private void TeleportToCoordinates(Session s)
    {
        if (!float.TryParse(GetUiState(s.Player, "teleport.coord.x", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(GetUiState(s.Player, "teleport.coord.y", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.TryParse(GetUiState(s.Player, "teleport.coord.z", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) { Toast(s.Player, "Teleport", "Coordinates must be valid numbers.", _config.UI.Theme.Warning); return; }
        Vector3 destination = new Vector3(x, y, z); if (!TeleportPlayerWithProtection(s.Player, destination, "coordinates " + destination)) return; Audit(s.Player, $"Teleported to coordinates {destination} ({SafeGridReference(destination)})"); Toast(s.Player, "Teleport", "Teleported to " + SafeGridReference(destination), _config.UI.Theme.Success); Draw(s);
    }

    private void TeleportToMonument(Session s, RogueMonumentInfo monument)
    {
        string label = MonumentLabel(monument);
        Vector3 requested = monument.Position;
        Vector3 destination;

        if (!TryResolveSafeMonumentDestination(requested, out destination))
        {
            // Never trust an underground monument transform as a player destination.
            // If surface resolution fails, abort instead of risking an InsideTerrain violation.
            LogWarning("World", $"Safe teleport resolution failed for monument {label} at {requested}.");
            Toast(s.Player, "Teleport", $"No safe arrival point found for {label}.", _config.UI.Theme.Warning);
            return;
        }

        if (!TeleportPlayerWithProtection(s.Player, destination, "monument " + label)) return;
        string grid = SafeGridReference(destination);
        Audit(s.Player, $"Teleported safely to monument {label} at {grid} (monument={requested}, arrival={destination})");
        Toast(s.Player, "Teleport", $"Teleported to {label} • {grid}", _config.UI.Theme.Success);
        Draw(s);
    }

    private bool TryResolveSafeMonumentDestination(Vector3 requested, out Vector3 destination)
    {
        if (_teleport != null)
            return _teleport.TryResolveSafeDestination(requested, out destination, new RogueSafePositionOptions { GroundClearance = 2.5f, MinimumUpDot = 0.9f, RequireEntityClearance = true });

        destination = requested;
        return false;
    }

    private List<RogueMonumentInfo> DiscoverMenuMonuments(bool refresh)
    {
        if (!refresh && _menuMonumentCache.Count > 0) return new List<RogueMonumentInfo>(_menuMonumentCache);
        List<RogueMonumentInfo> service = Rogue.Monuments.GetAll(refresh).Where(x => x != null).ToList();
        List<RogueMonumentInfo> direct = DiscoverMonumentsFromRust();
        List<RogueMonumentInfo> best = direct.Count > 0 ? direct : service;
        _menuMonumentCache.Clear(); _menuMonumentCache.AddRange(best);
        return new List<RogueMonumentInfo>(_menuMonumentCache);
    }

    private List<RogueMonumentInfo> DiscoverMonumentsFromRust()
    {
        var results = new List<RogueMonumentInfo>();
        try
        {
            Type terrainMeta = Type.GetType("TerrainMeta, Assembly-CSharp", false); if (terrainMeta == null) return results;
            object path = GetReflectedMember(terrainMeta, null, "Path"); object monuments = path == null ? null : GetReflectedMember(path.GetType(), path, "Monuments");
            System.Collections.IEnumerable enumerable = monuments as System.Collections.IEnumerable; if (enumerable == null) return results;
            foreach (object monument in enumerable)
            {
                if (monument == null || !TryReflectedPosition(monument, out Vector3 position)) continue;
                string rawName = ReflectedString(monument, "name", "Name", "prefabName", "PrefabName");
                object phrase = GetReflectedMember(monument.GetType(), monument, "displayPhrase") ?? GetReflectedMember(monument.GetType(), monument, "DisplayPhrase");
                string display = ResolvePhraseText(phrase);
                if (string.IsNullOrWhiteSpace(display)) display = ReflectedString(monument, "displayName", "DisplayName");
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    object gameObject = GetReflectedMember(monument.GetType(), monument, "gameObject"); rawName = gameObject == null ? string.Empty : ReflectedString(gameObject, "name", "Name");
                }
                string label = FriendlyMonumentName(display, rawName); results.Add(new RogueMonumentInfo(rawName, label, position));
            }
        }
        catch (Exception ex) { LogWarning("World", "Direct Rust monument naming fallback failed: " + ex.Message); }
        return results;
    }

    private static object GetReflectedMember(Type type, object instance, string name)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.IgnoreCase;
        try { var property = type.GetProperty(name, flags); if (property != null) return property.GetValue(instance, null); } catch { }
        try { var field = type.GetField(name, flags); if (field != null) return field.GetValue(instance); } catch { }
        return null;
    }

    private static string ReflectedString(object source, params string[] names)
    {
        if (source == null) return string.Empty;
        foreach (string name in names) { object value = GetReflectedMember(source.GetType(), source, name); if (value != null) { string text = Convert.ToString(value, CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(text)) return text; } }
        return string.Empty;
    }

    private static string ResolvePhraseText(object phrase)
    {
        if (phrase == null) return string.Empty;
        string[] members = { "english", "English", "translated", "Translated", "text", "Text", "token", "Token" };
        foreach (string member in members) { object value = GetReflectedMember(phrase.GetType(), phrase, member); string text = value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture); if (IsUsefulMonumentText(text)) return text; }
        string fallback = Convert.ToString(phrase, CultureInfo.InvariantCulture) ?? string.Empty; return IsUsefulMonumentText(fallback) ? fallback : string.Empty;
    }

    private static bool TryReflectedPosition(object source, out Vector3 position)
    {
        position = Vector3.zero; object value = GetReflectedMember(source.GetType(), source, "position") ?? GetReflectedMember(source.GetType(), source, "Position");
        if (value is Vector3 direct) { position = direct; return true; }
        object transform = GetReflectedMember(source.GetType(), source, "transform") ?? GetReflectedMember(source.GetType(), source, "Transform");
        if (transform is Transform unityTransform) { position = unityTransform.position; return true; }
        if (transform != null) { object nested = GetReflectedMember(transform.GetType(), transform, "position"); if (nested is Vector3 nestedPosition) { position = nestedPosition; return true; } }
        return false;
    }

    private static string MonumentInstanceLabel(RogueMonumentInfo monument, int index, int count) => count > 1 ? MonumentLabel(monument) + " #" + (index + 1) : MonumentLabel(monument);

    private static string MonumentLabel(RogueMonumentInfo monument) => FriendlyMonumentName(monument?.DisplayName, monument?.Name);

    private static string FriendlyMonumentName(string display, string rawName)
    {
        display = (display ?? string.Empty).Trim(); rawName = (rawName ?? string.Empty).Trim();
        if (IsUsefulMonumentText(display)) return CleanMonumentText(display);
        string raw = rawName.Replace('\\', '/'); int slash = raw.LastIndexOf('/'); if (slash >= 0 && slash + 1 < raw.Length) raw = raw.Substring(slash + 1); if (raw.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(0, raw.Length - 7);
        string lower = raw.ToLowerInvariant();
        var aliases = new KeyValuePair<string, string>[]
        {
            new("launch_site", "Launch Site"), new("airfield", "Airfield"), new("powerplant", "Power Plant"), new("power_plant", "Power Plant"), new("trainyard", "Train Yard"), new("water_treatment", "Water Treatment Plant"),
            new("military_tunnel", "Military Tunnels"), new("military_tunnels", "Military Tunnels"), new("junkyard", "Junkyard"), new("sphere_tank", "The Dome"), new("satellite_dish", "Satellite Dish"), new("sewer_branch", "Sewer Branch"),
            new("supermarket", "Abandoned Supermarket"), new("gas_station", "Oxum's Gas Station"), new("bandit", "Bandit Camp"), new("compound", "Outpost"), new("outpost", "Outpost"), new("fishing_village", "Fishing Village"),
            new("stables", "Ranch / Stables"), new("arctic_research", "Arctic Research Base"), new("desert_military", "Desert Military Base"), new("ferry_terminal", "Ferry Terminal"), new("missile_silo", "Missile Silo"),
            new("harbor", "Harbor"), new("lighthouse", "Lighthouse"), new("cave", "Cave"), new("warehouse", "Mining Outpost"), new("oilrig", "Oil Rig"), new("oil_rig", "Oil Rig")
        };
        foreach (var alias in aliases) if (lower.Contains(alias.Key)) return alias.Value;
        if (string.IsNullOrWhiteSpace(raw)) return "Monument";
        return CleanMonumentText(raw);
    }

    private static bool IsUsefulMonumentText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false; string text = value.Trim();
        return text.Length < 100 && text.IndexOf("Translate.Phrase", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("UnityEngine", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("MonumentInfo", StringComparison.OrdinalIgnoreCase) < 0 && !string.Equals(text, "Monument", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsGenericMonumentLabel(string value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "Monument", StringComparison.OrdinalIgnoreCase);
    private static string CleanMonumentText(string value) { string text = (value ?? string.Empty).Replace('_', ' ').Replace('-', ' ').Trim(); while (text.Contains("  ")) text = text.Replace("  ", " "); return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()); }

    private void DrawPlayerActionButtons(RogueUiDocument ui, Session s, BasePlayer? target, IPlayer? covTarget)
    {
        var actions = new List<(string Label, string Permission, bool Dangerous, Action Action)>();
        if (target != null)
        {
            actions.Add(("KICK", KickBanPermission, true, () => PromptKickBan(s, true)));
            actions.Add(("BAN", KickBanPermission, true, () => PromptKickBan(s, false)));
            actions.Add((target.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute) ? "UNMUTE" : "MUTE", MutePermission, false, () => ToggleMute(s, target)));
            actions.Add(("STRIP INVENTORY", StripPermission, true, () => PlayerStrip(s, target)));
            actions.Add(("RESET METABOLISM", HealPermission, false, () => ResetMetabolism(s, target)));
            actions.Add(("UNLOCK BLUEPRINTS", BlueprintPermission, false, () => UnlockBlueprints(s, target)));
            actions.Add(("REVOKE BLUEPRINTS", BlueprintPermission, true, () => RevokeBlueprints(s, target)));
            actions.Add(("HURT 25%", HurtPermission, true, () => Hurt(s, target, 0.25f)));
            actions.Add(("HURT 50%", HurtPermission, true, () => Hurt(s, target, 0.50f)));
            actions.Add(("HURT 75%", HurtPermission, true, () => Hurt(s, target, 0.75f)));
            actions.Add(("HEAL 25%", HealPermission, false, () => Heal(s, target, 0.25f)));
            actions.Add(("HEAL 50%", HealPermission, false, () => Heal(s, target, 0.50f)));
            actions.Add(("HEAL 75%", HealPermission, false, () => Heal(s, target, 0.75f)));
            actions.Add(("HEAL 100%", HealPermission, false, () => Heal(s, target, 1f)));
            actions.Add(("KILL", KillPermission, true, () => Kill(s, target)));
            actions.Add(("TP TO PLAYER", TeleportPermission, false, () => TeleportSelfTo(s, target)));
            actions.Add(("TP PLAYER TO ME", TeleportPermission, false, () => TeleportPlayerToSelf(s, target)));
            actions.Add(("TP TO AUTHED ENTITY", TeleportPermission, false, () => TeleportToEntity(s, target, true)));
            actions.Add(("TP TO OWNED ENTITY", TeleportPermission, false, () => TeleportToEntity(s, target, false)));
            actions.Add(("INVENTORY", InventoryPermission, false, () => ShowInventoryInspection(s, target)));
            actions.Add(("SPECTATE", SpectatePermission, false, () => StartSpectate(s, target)));
            actions.Add(("MOD NOTES", NotesPermission, false, () => ShowModerationNotes(s)));
            actions.Add(("ACTION HISTORY", AuditPermission, false, () => ShowAuditHistory(s)));
        }
        actions.Add(("VIEW PERMISSIONS", PermissionPermission, false, () => { s.Menu = MenuType.Permissions; s.SubMenu = (int)PermissionSubType.Player; Draw(s); }));

        int visible = 0;
        foreach (var action in actions.Where(x => CanUsePlayerAction(s.Player, x.Label, x.Permission)))
        {
            int index = visible++;
            int col = index % 4, row = index / 4;
            float left = 0.47f + col * 0.122f;
            float top = 0.83f - row * 0.066f;
            string cb = UiActionCallback(s.Player, "player.action." + index, () =>
            {
                if (action.Dangerous && _config.General.ConfirmDestructiveActions) Confirm(s, action.Label, "Run this action on " + s.SelectedPlayerName + "?", action.Action);
                else action.Action();
            });
            ui.Button("rram.player.action." + index, "rram.body", Rect(left, top - 0.047f, left + 0.114f, top), action.Label, cb,
                action.Dangerous ? _config.UI.Theme.Danger : _config.UI.Theme.SurfaceAlt, 9);
        }

        int customBase = visible;
        foreach (CustomCommandGroup group in _config.Commands.PlayerInfoCommands)
        foreach (PlayerInfoCommandEntry command in group.Commands ?? new List<PlayerInfoCommandEntry>())
        {
            if (!PluginRequirementMet(command.RequiredPlugin) || !PermissionRequirementMet(s.Player, command.RequiredPermission)) continue;
            int index = customBase++;
            int col = index % 4, row = index / 4;
            float left = 0.47f + col * 0.122f;
            float top = 0.83f - row * 0.066f;
            string cb = UiActionCallback(s.Player, "player.custom." + index, () =>
            {
                s.Target1Id = s.SelectedPlayerId; s.Target1Name = s.SelectedPlayerName;
                ExecuteCommand(s, command, command.SubType == CommandSubType.Chat);
            });
            ui.Button("rram.player.custom." + index, "rram.body", Rect(left, top - 0.047f, left + 0.114f, top), command.Name, cb, _config.UI.Theme.AccentMuted, 9);
        }
    }

    private void PromptKickBan(Session s, bool kick)
    {
        string input = UiCallback(s.Player, "kickban.reason", arg => { s.KickBanReason = UiCallbackArgument(arg, 0); });
        string execute = UiActionCallback(s.Player, "kickban.go", () =>
        {
            IPlayer? target = covalence.Players.FindPlayerById(s.SelectedPlayerId);
            if (target == null) { Toast(s.Player, "Player", GetLang("Error.TargetMissing", s.Player), _config.UI.Theme.Danger); return; }
            string reason = string.IsNullOrWhiteSpace(s.KickBanReason) ? "Admin action" : s.KickBanReason;
            if (kick) target.Kick(reason); else target.Ban(reason);
            Audit(s.Player, (kick ? "Kicked " : "Banned ") + target.Name + " (" + target.Id + "): " + reason);
            DestroyUi(s.Player, Overlay); s.KickBanReason = string.Empty; s.SelectedPlayerId = s.SelectedPlayerName = string.Empty; Draw(s);
        });
        RogueUiDocument ui = CreateOverlayUi();
        ApplyTheme(ui);
        ui.Modal("rram.kickban.modal", Overlay, RogueUiRect.Centered(0.38f, 0.26f));
        ui.Title("rram.kickban.title", "rram.kickban.modal", Rect(0.07f, 0.73f, 0.93f, 0.92f), (kick ? "KICK " : "BAN ") + s.SelectedPlayerName, "MiddleCenter");
        ui.Input("rram.kickban.input", "rram.kickban.modal", Rect(0.08f, 0.43f, 0.92f, 0.60f), s.KickBanReason, input, 12, _config.UI.Theme.Text, 160);
        ui.Button("rram.kickban.cancel", "rram.kickban.modal", Rect(0.08f, 0.12f, 0.43f, 0.29f), "CANCEL", "roguerust.ui.close " + Overlay, _config.UI.Theme.SurfaceAlt);
        ui.Button("rram.kickban.go", "rram.kickban.modal", Rect(0.57f, 0.12f, 0.92f, 0.29f), kick ? "KICK" : "BAN", execute, _config.UI.Theme.Danger);
        ShowUi(s.Player, ui, true);
    }

    private void ToggleMute(Session s, BasePlayer target)
    {
        bool mute = !target.HasPlayerFlag(BasePlayer.PlayerFlags.ChatMute);
        if (mute && target == s.Player) { Toast(s.Player, "Mute", "You cannot mute yourself.", _config.UI.Theme.Warning); return; }
        target.SetPlayerFlag(BasePlayer.PlayerFlags.ChatMute, mute);
        Audit(s.Player, (mute ? "Muted " : "Unmuted ") + target.displayName + " (" + target.userID + ")"); Draw(s);
    }

    private void PlayerStrip(Session s, BasePlayer target) { target.inventory.Strip(); Audit(s.Player, "Stripped inventory of " + TargetText(target)); Toast(s.Player, "Player", "Inventory stripped.", _config.UI.Theme.Success); Draw(s); }
    private void ResetMetabolism(Session s, BasePlayer target) { target.metabolism.bleeding.value = 0; target.metabolism.calories.value = target.metabolism.calories.max; target.metabolism.hydration.value = target.metabolism.hydration.max; target.metabolism.radiation_level.value = 0; target.metabolism.radiation_poison.value = 0; target.metabolism.poison.value = 0; target.metabolism.wetness.value = 0; target.metabolism.SendChanges(); Audit(s.Player, "Reset metabolism of " + TargetText(target)); Draw(s); }

    private void UnlockBlueprints(Session s, BasePlayer target)
    {
        ProtoBuf.PersistantPlayer info = target.PersistantPlayerInfo;
        foreach (ItemBlueprint bp in ItemManager.bpList)
            if (bp.userCraftable && !bp.defaultBlueprint && !info.unlockedItems.Contains(bp.targetItem.itemid)) info.unlockedItems.Add(bp.targetItem.itemid);
        target.PersistantPlayerInfo = info; target.SendNetworkUpdateImmediate(); target.ClientRPC(RpcTarget.Player("UnlockedBlueprint", target), 0);
        Audit(s.Player, "Unlocked all blueprints for " + TargetText(target)); Draw(s);
    }

    private void RevokeBlueprints(Session s, BasePlayer target) { target.blueprints.Reset(); Audit(s.Player, "Revoked all blueprints from " + TargetText(target)); Draw(s); }
    private void Hurt(Session s, BasePlayer target, float pct) { target.Hurt(target.health * pct); Audit(s.Player, $"Hurt {TargetText(target)} {pct:P0}"); Draw(s); }
    private void Heal(Session s, BasePlayer target, float pct) { if (target.IsWounded()) target.StopWounded(); target.Heal(target.MaxHealth() * pct); Audit(s.Player, $"Healed {TargetText(target)} {pct:P0}"); Draw(s); }
    private void Kill(Session s, BasePlayer target) { target.DieInstantly(); Audit(s.Player, "Killed " + TargetText(target)); Draw(s); }
    private void TeleportSelfTo(Session s, BasePlayer target) { if (!TeleportPlayerWithProtection(s.Player, target.transform.position, "player " + TargetText(target))) return; Audit(s.Player, "Teleported to " + TargetText(target)); Draw(s); }
    private void TeleportPlayerToSelf(Session s, BasePlayer target) { if (!TeleportPlayerWithProtection(target, s.Player.transform.position, "admin " + TargetText(s.Player))) return; Audit(s.Player, "Teleported " + TargetText(target) + " to admin"); Draw(s); }

    private void TeleportToEntity(Session s, BasePlayer target, bool authed)
    {
        BaseEntity[] entities = authed ? BaseEntity.Util.FindTargetsAuthedTo(target.userID, string.Empty) : BaseEntity.Util.FindTargetsOwnedBy(target.userID, string.Empty);
        if (entities == null || entities.Length == 0) { Toast(s.Player, "Teleport", "No matching entities found for player.", _config.UI.Theme.Warning); return; }
        BaseEntity entity = entities[UnityEngine.Random.Range(0, entities.Length)];
        if (!TeleportPlayerWithProtection(s.Player, entity.transform.position, (authed ? "authed" : "owned") + " entity " + entity.ShortPrefabName)) return; Audit(s.Player, $"Teleported to {(authed ? "authed" : "owned")} entity {entity.ShortPrefabName} for {TargetText(target)}"); Draw(s);
    }

    private void TeleportBack(Session s)
    {
        if (_teleport == null) return;
        RogueTeleportResult result = _teleport.Back(Rogue.Player(s.Player), new RogueSafePositionOptions { SearchRadius = 18f, Attempts = 36, RequireEntityClearance = true });
        if (!result.Success) { Toast(s.Player, "Teleport", string.IsNullOrWhiteSpace(result.Error) ? "No previous location is available." : result.Error, _config.UI.Theme.Warning); return; }
        StartTeleportProtection(s.Player, "return teleport");
        Audit(s.Player, "Returned to previous teleport location at " + SafeGridReference(result.Destination));
        Toast(s.Player, "Teleport", "Returned to " + SafeGridReference(result.Destination), _config.UI.Theme.Success);
        Draw(s);
    }

    private void ShowInventoryInspection(Session s, BasePlayer target)
    {
        RogueUiDocument ui = CreateOverlayUi(); ApplyTheme(ui);
        ui.Modal("rram.inventory.modal", Overlay, RogueUiRect.Centered(0.68f, 0.72f));
        ui.Title("rram.inventory.title", "rram.inventory.modal", Rect(0.04f, 0.91f, 0.86f, 0.98f), "INVENTORY • " + StripName(target.displayName), "MiddleLeft");
        ui.Button("rram.inventory.close", "rram.inventory.modal", Rect(0.89f, 0.92f, 0.96f, 0.975f), "×", "roguerust.ui.close " + Overlay, _config.UI.Theme.Danger, 13);
        DrawInventoryContainer(ui, target.inventory.containerBelt, "BELT", 0.05f, 0.62f);
        DrawInventoryContainer(ui, target.inventory.containerWear, "WEAR", 0.36f, 0.62f);
        DrawInventoryContainer(ui, target.inventory.containerMain, "MAIN", 0.67f, 0.62f);
        ShowUi(s.Player, ui, true);
        Audit(s.Player, "Inspected inventory of " + TargetText(target));
    }

    private void DrawInventoryContainer(RogueUiDocument ui, ItemContainer container, string title, float left, float top)
    {
        ui.Label("rram.inv." + title + ".title", "rram.inventory.modal", Rect(left, top + 0.20f, left + 0.27f, top + 0.25f), title, 10, _config.UI.Theme.Text, "MiddleLeft");
        var items = container?.itemList ?? new List<Item>();
        int count = Math.Min(items.Count, 14);
        for (int i = 0; i < count; i++)
        {
            Item item = items[i]; float y = top + 0.17f - i * 0.045f;
            string name = item.info?.displayName?.english ?? item.info?.shortname ?? "item";
            ui.Label("rram.inv." + title + "." + i, "rram.inventory.modal", Rect(left, y, left + 0.27f, y + 0.038f), item.amount + " × " + name, 9, _config.UI.Theme.MutedText, "MiddleLeft");
        }
        if (items.Count > count) ui.Label("rram.inv." + title + ".more", "rram.inventory.modal", Rect(left, 0.05f, left + 0.27f, 0.09f), "+ " + (items.Count - count) + " more", 8, _config.UI.Theme.MutedText, "MiddleLeft");
    }

    private void StartSpectate(Session s, BasePlayer target)
    {
        if (target == s.Player) { Toast(s.Player, "Spectate", "You cannot spectate yourself.", _config.UI.Theme.Warning); return; }
        _spectateReturn[s.Player.userID] = s.Player.transform.position;
        CloseAdminMenu(s);
        s.Player.StartSpectating();
        s.Player.UpdateSpectateTarget(target.UserIDString);
        Audit(s.Player, "Started spectating " + TargetText(target));
        Toast(s.Player, "Spectate", "Spectating " + StripName(target.displayName) + ". Use native spectate controls to exit.", _config.UI.Theme.Success, 4f);
    }

    private void ReturnFromSpectate(Session s)
    {
        if (!_spectateReturn.TryGetValue(s.Player.userID, out Vector3 returnPosition)) return;
        s.Player.StopSpectating();
        s.Player.SetParent(null, true, true);
        _spectateReturn.Remove(s.Player.userID);
        if (_teleport != null)
        {
            RogueTeleportResult result = _teleport.Teleport(Rogue.Player(s.Player), returnPosition, new RogueSafePositionOptions { SearchRadius = 18f, Attempts = 36, RequireEntityClearance = true });
            if (result.Success) StartTeleportProtection(s.Player, "spectate return");
        }
        Audit(s.Player, "Returned from spectate mode");
        Toast(s.Player, "Spectate", "Returned from spectate mode.", _config.UI.Theme.Success);
        Draw(s);
    }

    private void ShowModerationNotes(Session s)
    {
        RogueUiDocument ui = CreateOverlayUi(); ApplyTheme(ui);
        ui.Modal("rram.notes.modal", Overlay, RogueUiRect.Centered(0.62f, 0.62f));
        ui.Title("rram.notes.title", "rram.notes.modal", Rect(0.05f, 0.88f, 0.82f, 0.97f), "MODERATION NOTES • " + s.SelectedPlayerName, "MiddleLeft");
        ui.Button("rram.notes.close", "rram.notes.modal", Rect(0.88f, 0.90f, 0.95f, 0.97f), "×", "roguerust.ui.close " + Overlay, _config.UI.Theme.Danger, 13);
        string input = UiCallback(s.Player, "notes.input", arg => s.ModerationNoteDraft = UiCallbackArgument(arg, 0));
        ui.Input("rram.notes.input", "rram.notes.modal", Rect(0.05f, 0.77f, 0.76f, 0.84f), s.ModerationNoteDraft, input, 10, _config.UI.Theme.Text, 240);
        string add = UiActionCallback(s.Player, "notes.add", () => AddModerationNote(s));
        ui.Button("rram.notes.add", "rram.notes.modal", Rect(0.78f, 0.77f, 0.95f, 0.84f), "ADD NOTE", add, _config.UI.Theme.Success, 9);
        if (!_moderationNotes.TryGetValue(s.SelectedPlayerId, out List<ModerationNote>? notes)) notes = new List<ModerationNote>();
        var recent = notes.OrderByDescending(x => x.Utc).Take(9).ToList();
        for (int i = 0; i < recent.Count; i++)
        {
            var note = recent[i]; float y = 0.70f - i * 0.066f;
            string stamp = DateTimeOffset.FromUnixTimeSeconds(note.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ui.Label("rram.notes." + i, "rram.notes.modal", Rect(0.05f, y, 0.95f, y + 0.055f), stamp + " • " + note.AdminName + " • " + note.Text, 8, _config.UI.Theme.MutedText, "MiddleLeft");
        }
        ShowUi(s.Player, ui, true);
    }

    private void AddModerationNote(Session s)
    {
        string text = (s.ModerationNoteDraft ?? string.Empty).Trim();
        if (text.Length == 0) { Toast(s.Player, "Notes", "Enter a note first.", _config.UI.Theme.Warning); return; }
        if (!_moderationNotes.TryGetValue(s.SelectedPlayerId, out List<ModerationNote>? notes)) _moderationNotes[s.SelectedPlayerId] = notes = new List<ModerationNote>();
        notes.Add(new ModerationNote { Utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), AdminId = s.Player.UserIDString, AdminName = StripName(s.Player.displayName), Text = text });
        while (notes.Count > _config.Data.MaxNotesPerPlayer) notes.RemoveAt(0);
        s.ModerationNoteDraft = string.Empty; SaveWorkflowData();
        Audit(s.Player, "Added moderation note for " + s.SelectedPlayerName + " (" + s.SelectedPlayerId + ")");
        DestroyUi(s.Player, Overlay); ShowModerationNotes(s);
    }

    private void ShowAuditHistory(Session s)
    {
        RogueUiDocument ui = CreateOverlayUi(); ApplyTheme(ui);
        ui.Modal("rram.audit.modal", Overlay, RogueUiRect.Centered(0.72f, 0.72f));
        ui.Title("rram.audit.title", "rram.audit.modal", Rect(0.04f, 0.91f, 0.84f, 0.98f), "ADMIN ACTION HISTORY • " + s.SelectedPlayerName, "MiddleLeft");
        ui.Button("rram.audit.close", "rram.audit.modal", Rect(0.90f, 0.92f, 0.96f, 0.975f), "×", "roguerust.ui.close " + Overlay, _config.UI.Theme.Danger, 13);
        string id = s.SelectedPlayerId;
        var rows = _auditHistory.Where(x => x.Message.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0 || x.Message.IndexOf(s.SelectedPlayerName, StringComparison.OrdinalIgnoreCase) >= 0).OrderByDescending(x => x.Utc).Take(14).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            AuditEntry entry = rows[i]; float y = 0.84f - i * 0.055f;
            string stamp = DateTimeOffset.FromUnixTimeSeconds(entry.Utc).ToLocalTime().ToString("MM-dd HH:mm");
            ui.Label("rram.audit." + i, "rram.audit.modal", Rect(0.04f, y, 0.96f, y + 0.045f), stamp + " • " + entry.AdminName + " • " + entry.Message, 8, _config.UI.Theme.MutedText, "MiddleLeft");
        }
        ShowUi(s.Player, ui, true);
    }

    private bool TeleportPlayerWithProtection(BasePlayer player, Vector3 destination, string reason)
    {
        if (player == null || !player.IsConnected || _teleport == null) return false;

        RogueTeleportResult result = _teleport.Teleport(Rogue.Player(player), destination, new RogueSafePositionOptions
        {
            SearchRadius = 24f,
            Attempts = 48,
            WaterClearance = 0.35f,
            GroundClearance = 1.75f,
            ClearanceRadius = 0.55f,
            MinimumUpDot = 0.88f,
            RequireEntityClearance = true
        });
        if (!result.Success)
        {
            LogWarning("World", $"Teleport rejected for {StripName(player.displayName)} ({player.userID}): {result.Error}");
            Toast(player, "Teleport", string.IsNullOrWhiteSpace(result.Error) ? "No safe destination was found." : result.Error, _config.UI.Theme.Warning);
            return false;
        }

        StartTeleportProtection(player, reason);
        return true;
    }

    private void StartTeleportProtection(BasePlayer player, string reason)
    {
        float seconds = _config.Teleport.ProtectionSeconds;
        if (seconds <= 0f || player == null || !player.IsConnected) return;

        float until = Time.realtimeSinceStartup + seconds;
        _teleportProtectionUntil[player.userID] = until;
        LogInformation("Security", $"Temporary teleport protection enabled for {StripName(player.displayName)} ({player.userID}) for {seconds:0.#}s after {reason}.");

        Delay(TimeSpan.FromSeconds(seconds + 0.25f), () =>
        {
            if (player == null) return;
            if (_teleportProtectionUntil.TryGetValue(player.userID, out float expiry) && Time.realtimeSinceStartup >= expiry)
            {
                _teleportProtectionUntil.Remove(player.userID);
                LogInformation("Security", $"Temporary teleport protection expired for {StripName(player.displayName)} ({player.userID}).");
            }
        }, "teleport-protection-" + player.userID);
    }

    private bool HasTeleportProtection(ulong userId)
    {
        if (!_teleportProtectionUntil.TryGetValue(userId, out float until)) return false;
        if (Time.realtimeSinceStartup < until) return true;
        _teleportProtectionUntil.Remove(userId);
        return false;
    }

    // Intentionally no CanNetworkTo hook here. Rust calls it on the networking hot path and it
    // caused visible flight stutter. limitNetworking is toggled once on vanish transitions instead.

    private object? OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
    {
        if (info != null && _config.Teleport.BreakProtectionOnAttack)
        {
            BasePlayer attacker = info.InitiatorPlayer;
            if (attacker != null && entity != attacker && HasTeleportProtection(attacker.userID))
            {
                _teleportProtectionUntil.Remove(attacker.userID);
                LogInformation("Security", $"Teleport protection removed early from {StripName(attacker.displayName)} ({attacker.userID}) because they attacked.");
            }
        }

        BasePlayer victim = entity as BasePlayer;
        if (victim != null && _config.Vanish.Enabled && _vanishGodMode.Contains(victim.userID))
            return true;
        if (victim != null && HasTeleportProtection(victim.userID))
            return true;

        return null;
    }

    #endregion

    #region Permissions

    private void DrawPermissions(RogueUiDocument ui, Session s)
    {
        PermissionSubType type = (PermissionSubType)Math.Max(0, Math.Min(1, s.SubMenu));
        DrawSubTabs(ui, s, new[] { "PLAYER", "GROUP" }, 2);

        if (type == PermissionSubType.Player)
        {
            if (string.IsNullOrEmpty(s.SelectedPlayerId))
            {
                string choose = UiActionCallback(s.Player, "perm.choose.player", () => { s.Selection = SelectionPurpose.PermissionPlayer; Draw(s); });
                ui.Button("rram.perm.choose", "rram.body", Rect(0.055f, 0.81f, 0.27f, 0.87f), "SELECT PLAYER", choose, _config.UI.Theme.Accent);
                if (s.Selection == SelectionPurpose.PermissionPlayer) DrawPlayerSelection(ui, s);
                return;
            }
            DrawPermissionTree(ui, s, s.SelectedPlayerId, false);
        }
        else
        {
            if (string.IsNullOrEmpty(s.SelectedGroup)) { DrawGroupSelector(ui, s, forPermissions: true); return; }
            DrawPermissionTree(ui, s, s.SelectedGroup, true);
        }
    }

    private void DrawPermissionTree(RogueUiDocument ui, Session s, string target, bool group)
    {
        string back = UiActionCallback(s.Player, "perm.back", () => { if (group) s.SelectedGroup = string.Empty; else s.SelectedPlayerId = s.SelectedPlayerName = string.Empty; s.Page = 0; Draw(s); });
        ui.Button("rram.perm.back", "rram.body", Rect(0.052f, 0.825f, 0.145f, 0.862f), "‹ CHANGE", back, _config.UI.Theme.SurfaceAlt, 9);

        List<KeyValuePair<string, bool>> filtered = Filter(_permissionTree, s, x => x.Key).ToList();
        const int pageSize = 32;
        DrawCharacterFilter(ui, s, filtered, x => x.Key);
        List<KeyValuePair<string, bool>> page = PageWithSize(filtered, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            KeyValuePair<string, bool> entry = page[i]; int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.795f - row * 0.084f;
            if (!entry.Value)
            {
                ui.Label("rram.perm.header." + i, "rram.body", Rect(left, top - 0.060f, left + 0.210f, top), entry.Key.ToUpperInvariant(), 8, _config.UI.Theme.MutedText, "MiddleLeft");
                continue;
            }
            bool direct = group ? GroupHasDirectPermission(target, entry.Key) : UserHasDirectPermission(target, entry.Key);
            bool inherited = group ? ParentGroupsHavePermission(target, entry.Key) : UserGroupsHavePermission(target, entry.Key);
            string cb = UiActionCallback(s.Player, $"perm.toggle.{s.Page}.{i}", () =>
            {
                if (group) { if (direct) permission.RevokeGroupPermission(target, entry.Key); else permission.GrantGroupPermission(target, entry.Key, this); }
                else { if (direct) permission.RevokeUserPermission(target, entry.Key); else permission.GrantUserPermission(target, entry.Key, this); }
                Audit(s.Player, $"{(direct ? "Revoked" : "Granted")} {(group ? "group" : "user")} permission {entry.Key} {(direct ? "from" : "to")} {target}"); Draw(s);
            });
            string state = direct ? "DIRECT" : inherited ? "INHERITED" : "OFF";
            string color = direct ? _config.UI.Theme.Success : inherited ? _config.UI.Theme.Warning : _config.UI.Theme.SurfaceAlt;
            ui.Button("rram.perm." + i, "rram.body", Rect(left, top - 0.060f, left + 0.210f, top), entry.Key + "  •  " + state, cb, color, 8, "MiddleCenter");
        }
        DrawPagerWithSize(ui, s, filtered.Count, pageSize);
    }

    private bool UserHasDirectPermission(string userId, string perm)
    {
        var data = permission.GetUserData(userId);
        return data != null && data.Perms.Contains(perm, StringComparer.OrdinalIgnoreCase);
    }
    private bool UserGroupsHavePermission(string userId, string perm)
    {
        var data = permission.GetUserData(userId);
        return data != null && permission.GroupsHavePermission(data.Groups, perm);
    }
    private bool GroupHasDirectPermission(string group, string perm)
    {
        var data = permission.GetGroupData(group);
        return data != null && data.Perms.Contains(perm, StringComparer.OrdinalIgnoreCase);
    }
    private bool ParentGroupsHavePermission(string group, string perm)
    {
        var data = permission.GetGroupData(group);
        return data != null && !string.IsNullOrEmpty(data.ParentGroup) && permission.GroupHasPermission(data.ParentGroup, perm);
    }

    #endregion

    #region Groups

    private void DrawGroups(RogueUiDocument ui, Session s)
    {
        GroupSubType type = (GroupSubType)Math.Max(0, Math.Min(3, s.SubMenu));
        DrawSubTabs(ui, s, new[] { "LIST", "CREATE", "USER GROUPS", "GROUP USERS" }, 4);
        switch (type)
        {
            case GroupSubType.List: DrawGroupList(ui, s); break;
            case GroupSubType.Create: DrawCreateGroup(ui, s); break;
            case GroupSubType.UserGroups: DrawUserGroups(ui, s); break;
            case GroupSubType.GroupUsers: DrawGroupUsers(ui, s); break;
        }
    }

    private void DrawGroupSelector(RogueUiDocument ui, Session s, bool forPermissions = false)
    {
        List<string> groups = Filter(permission.GetGroups(), s, x => x).ToList();
        const int pageSize = 32;
        DrawCharacterFilter(ui, s, groups, x => x); List<string> page = PageWithSize(groups, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            string group = page[i]; int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.815f - row * 0.086f;
            string cb = UiActionCallback(s.Player, $"group.select.{s.Page}.{i}", () => { s.SelectedGroup = group; Draw(s); });
            ui.Button("rram.group.select." + i, "rram.body", Rect(left, top - 0.060f, left + 0.210f, top), group, cb, _config.UI.Theme.SurfaceAlt, 9, "MiddleCenter");
        }
        DrawPagerWithSize(ui, s, groups.Count, pageSize);
    }

    private void DrawGroupList(RogueUiDocument ui, Session s)
    {
        if (string.IsNullOrEmpty(s.SelectedGroup)) { DrawGroupSelector(ui, s); return; }
        string group = s.SelectedGroup;
        var data = permission.GetGroupData(group);
        string back = UiActionCallback(s.Player, "group.back", () => { s.SelectedGroup = string.Empty; Draw(s); });
        ui.Button("rram.group.back", "rram.body", Rect(0.055f, 0.825f, 0.17f, 0.865f), "‹ GROUPS", back, _config.UI.Theme.SurfaceAlt, 9);
        ui.Label("rram.group.info.name", "rram.body", Rect(0.055f, 0.775f, 0.31f, 0.815f), "NAME  •  " + group, 9, _config.UI.Theme.Text, "MiddleLeft");
        ui.Label("rram.group.info.title", "rram.body", Rect(0.055f, 0.735f, 0.31f, 0.772f), "TITLE  •  " + (data?.Title ?? string.Empty), 8, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.group.info.rank", "rram.body", Rect(0.32f, 0.775f, 0.47f, 0.815f), "RANK  •  " + (data?.Rank ?? 0), 8, _config.UI.Theme.MutedText, "MiddleLeft");
        ui.Label("rram.group.info.parent", "rram.body", Rect(0.32f, 0.735f, 0.52f, 0.772f), "PARENT  •  " + (data?.ParentGroup ?? "None"), 8, _config.UI.Theme.MutedText, "MiddleLeft");

        string parent = UiActionCallback(s.Player, "group.parent", () => DrawParentGroupOverlay(s));
        string clone = UiActionCallback(s.Player, "group.clone", () => DrawCloneGroupOverlay(s));
        string clear = UiActionCallback(s.Player, "group.clear", () => Confirm(s, "CLEAR USERS", "Remove every user from " + group + "?", () => ClearGroupUsers(s, group)));
        string delete = UiActionCallback(s.Player, "group.delete", () => Confirm(s, "DELETE GROUP", "Delete group " + group + "?", () => DeleteGroup(s, group)));
        ui.Button("rram.group.parent", "rram.body", Rect(0.55f, 0.765f, 0.65f, 0.815f), "PARENT", parent, _config.UI.Theme.SurfaceAlt, 9);
        ui.Button("rram.group.clone", "rram.body", Rect(0.66f, 0.765f, 0.76f, 0.815f), "CLONE", clone, _config.UI.Theme.Success, 9);
        ui.Button("rram.group.clear", "rram.body", Rect(0.77f, 0.765f, 0.87f, 0.815f), "CLEAR USERS", clear, _config.UI.Theme.Warning, 9);
        ui.Button("rram.group.delete", "rram.body", Rect(0.88f, 0.765f, 0.96f, 0.815f), "DELETE", delete, _config.UI.Theme.Danger, 9);
    }

    private void DrawCreateGroup(RogueUiDocument ui, Session s)
    {
        string nameInput = UiCallback(s.Player, "group.create.name", arg => SetUiState(s.Player, "group.name", UiCallbackArgument(arg, 0)));
        string titleInput = UiCallback(s.Player, "group.create.title", arg => SetUiState(s.Player, "group.title", UiCallbackArgument(arg, 0)));
        string rankInput = UiCallback(s.Player, "group.create.rank", arg => SetUiState(s.Player, "group.rank", UiCallbackArgument(arg, 0)));
        string name = GetUiState(s.Player, "group.name", "") ?? "";
        string title = GetUiState(s.Player, "group.title", "") ?? "";
        string rank = GetUiState(s.Player, "group.rank", "0") ?? "0";
        ui.Label("rram.group.create.help", "rram.body", Rect(0.055f, 0.77f, 0.45f, 0.84f), "CREATE USER GROUP", 16, _config.UI.Theme.Text, "MiddleLeft");
        ui.Input("rram.group.create.name", "rram.body", Rect(0.055f, 0.66f, 0.45f, 0.72f), name, nameInput, 12, _config.UI.Theme.Text, 48);
        ui.Input("rram.group.create.title", "rram.body", Rect(0.055f, 0.57f, 0.45f, 0.63f), title, titleInput, 12, _config.UI.Theme.Text, 64);
        ui.Input("rram.group.create.rank", "rram.body", Rect(0.055f, 0.48f, 0.45f, 0.54f), rank, rankInput, 12, _config.UI.Theme.Text, 10);
        string create = UiActionCallback(s.Player, "group.create.go", () =>
        {
            string n = GetUiState(s.Player, "group.name", "") ?? "";
            string t = GetUiState(s.Player, "group.title", "") ?? "";
            int.TryParse(GetUiState(s.Player, "group.rank", "0"), out int r);
            if (string.IsNullOrWhiteSpace(n)) { Toast(s.Player, "Group", "Group name is required.", _config.UI.Theme.Warning); return; }
            if (!permission.CreateGroup(n, t, r)) { Toast(s.Player, "Group", "Could not create group (it may already exist).", _config.UI.Theme.Danger); return; }
            RemoveUiState(s.Player, "group.name"); RemoveUiState(s.Player, "group.title"); RemoveUiState(s.Player, "group.rank");
            Audit(s.Player, "Created usergroup " + n); s.SubMenu = (int)GroupSubType.List; s.SelectedGroup = n; Draw(s);
        });
        ui.Button("rram.group.create.go", "rram.body", Rect(0.055f, 0.37f, 0.22f, 0.43f), "CREATE", create, _config.UI.Theme.Success);
    }

    private void DrawUserGroups(RogueUiDocument ui, Session s)
    {
        if (string.IsNullOrEmpty(s.SelectedPlayerId))
        {
            s.Selection = SelectionPurpose.GroupUser; DrawPlayerSelection(ui, s); return;
        }
        string back = UiActionCallback(s.Player, "usergroups.back", () => { s.SelectedPlayerId = s.SelectedPlayerName = string.Empty; s.Selection = SelectionPurpose.GroupUser; Draw(s); });
        ui.Button("rram.usergroups.back", "rram.body", Rect(0.052f, 0.850f, 0.145f, 0.892f), "‹ PLAYER", back, _config.UI.Theme.SurfaceAlt, 9);
        string[] membership = permission.GetUserGroups(s.SelectedPlayerId) ?? Array.Empty<string>();
        List<string> groups = Filter(permission.GetGroups(), s, x => x).ToList();
        const int pageSize = 32; List<string> page = PageWithSize(groups, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            string group = page[i]; bool member = membership.Contains(group, StringComparer.OrdinalIgnoreCase);
            int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.810f - row * 0.086f;
            string cb = UiActionCallback(s.Player, $"usergroup.toggle.{i}", () =>
            {
                if (member) permission.RemoveUserGroup(s.SelectedPlayerId, group); else permission.AddUserGroup(s.SelectedPlayerId, group);
                Audit(s.Player, $"{(member ? "Removed" : "Added")} {s.SelectedPlayerName} {(member ? "from" : "to")} group {group}"); Draw(s);
            });
            ui.Button("rram.usergroup." + i, "rram.body", Rect(left, top - 0.060f, left + 0.210f, top), group + (member ? " • MEMBER" : ""), cb, member ? _config.UI.Theme.Success : _config.UI.Theme.SurfaceAlt, 8, "MiddleCenter");
        }
        DrawPagerWithSize(ui, s, groups.Count, pageSize);
    }

    private void DrawGroupUsers(RogueUiDocument ui, Session s)
    {
        if (string.IsNullOrEmpty(s.SelectedGroup)) { DrawGroupSelector(ui, s); return; }
        string back = UiActionCallback(s.Player, "groupusers.back", () => { s.SelectedGroup = string.Empty; Draw(s); });
        ui.Button("rram.groupusers.back", "rram.body", Rect(0.052f, 0.850f, 0.145f, 0.892f), "‹ GROUP", back, _config.UI.Theme.SurfaceAlt, 9);
        string[] ids = permission.GetUsersInGroup(s.SelectedGroup) ?? Array.Empty<string>();
        List<RecentPlayer> users = ids.Select(ParseGroupUser).Where(x => x != null).Cast<RecentPlayer>().ToList();
        users = Filter(users, s, x => x.Name + " " + x.Id).ToList();
        const int pageSize = 32; List<RecentPlayer> page = PageWithSize(users, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            RecentPlayer user = page[i]; int col = i % 4, row = i / 4; float left = 0.052f + col * 0.222f; float top = 0.810f - row * 0.086f;
            string cb = UiActionCallback(s.Player, $"groupuser.remove.{i}", () => { permission.RemoveUserGroup(user.Id, s.SelectedGroup); Audit(s.Player, $"Removed {user.Name} from group {s.SelectedGroup}"); Draw(s); });
            string card = "rram.groupuser." + i;
            ui.Panel(card, "rram.body", Rect(left, top - 0.060f, left + 0.210f, top), _config.UI.Theme.SurfaceAlt);
            ui.Button(card + ".hit", card, RogueUiRect.Full, string.Empty, cb, "0 0 0 0", 1);
            ui.Label(card + ".name", card, Rect(0.035f, 0.43f, 0.97f, 0.90f), user.Name, 8, _config.UI.Theme.Text, "MiddleLeft");
            ui.Label(card + ".id", card, Rect(0.035f, 0.08f, 0.97f, 0.43f), user.Id, 6, _config.UI.Theme.MutedText, "MiddleLeft");
        }
        DrawPagerWithSize(ui, s, users.Count, pageSize);
    }

    private RecentPlayer? ParseGroupUser(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string id = raw.Trim(); string name = id;
        int open = raw.LastIndexOf('('), close = raw.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            string candidate = raw.Substring(open + 1, close - open - 1);
            if (ulong.TryParse(candidate, out _)) { id = candidate; name = raw.Substring(0, open).Trim(); }
        }
        if (_recentPlayers.TryGetValue(id, out RecentPlayer? rp)) return rp;
        return new RecentPlayer { Id = id, Name = name, Online = IsOnline(id), LastSeenUtc = 0 };
    }

    private void DrawParentGroupOverlay(Session s)
    {
        List<string> groups = permission.GetGroups().Where(x => !string.Equals(x, s.SelectedGroup, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x).ToList();
        RogueUiDocument ui = CreateOverlayUi(); ui.Modal("rram.parent.modal", Overlay, RogueUiRect.Centered(0.48f, 0.58f));
        ui.Title("rram.parent.title", "rram.parent.modal", Rect(0.05f, 0.91f, 0.95f, 0.98f), "SET PARENT • " + s.SelectedGroup);
        string none = UiActionCallback(s.Player, "parent.none", () => { permission.SetGroupParent(s.SelectedGroup, null); Audit(s.Player, "Cleared parent group for " + s.SelectedGroup); DestroyUi(s.Player, Overlay); Draw(s); });
        ui.Button("rram.parent.none", "rram.parent.modal", Rect(0.05f, 0.82f, 0.30f, 0.88f), "NO PARENT", none, _config.UI.Theme.Warning);
        int max = Math.Min(24, groups.Count);
        for (int i = 0; i < max; i++)
        {
            string group = groups[i]; int col = i % 3, row = i / 3; float left = 0.05f + col * 0.305f; float top = 0.75f - row * 0.085f;
            string cb = UiActionCallback(s.Player, "parent." + i, () => { permission.SetGroupParent(s.SelectedGroup, group); Audit(s.Player, $"Set parent of {s.SelectedGroup} to {group}"); DestroyUi(s.Player, Overlay); Draw(s); });
            ui.Button("rram.parent." + i, "rram.parent.modal", Rect(left, top - 0.06f, left + 0.285f, top), group, cb, _config.UI.Theme.SurfaceAlt, 10);
        }
        ui.CloseButton("rram.parent.close", "rram.parent.modal", Rect(0.92f, 0.92f, 0.98f, 0.98f), Overlay);
        ShowUi(s.Player, ui, true);
    }

    private void DrawCloneGroupOverlay(Session s)
    {
        string nameCb = UiCallback(s.Player, "clone.name", arg => SetUiState(s.Player, "clone.name", UiCallbackArgument(arg, 0)));
        string titleCb = UiCallback(s.Player, "clone.title", arg => SetUiState(s.Player, "clone.title", UiCallbackArgument(arg, 0)));
        string rankCb = UiCallback(s.Player, "clone.rank", arg => SetUiState(s.Player, "clone.rank", UiCallbackArgument(arg, 0)));
        bool copyUsers = string.Equals(GetUiState(s.Player, "clone.users", "false"), "true", StringComparison.OrdinalIgnoreCase);
        string toggle = UiActionCallback(s.Player, "clone.users.toggle", () => { SetUiState(s.Player, "clone.users", (!copyUsers).ToString()); DrawCloneGroupOverlay(s); });
        RogueUiDocument ui = CreateOverlayUi(); ui.Modal("rram.clone.modal", Overlay, RogueUiRect.Centered(0.38f, 0.42f));
        ui.Title("rram.clone.title.main", "rram.clone.modal", Rect(0.07f, 0.84f, 0.93f, 0.94f), "CLONE • " + s.SelectedGroup, "MiddleCenter");
        ui.Input("rram.clone.name", "rram.clone.modal", Rect(0.08f, 0.65f, 0.92f, 0.73f), GetUiState(s.Player, "clone.name", "") ?? "", nameCb, 12, _config.UI.Theme.Text, 48);
        ui.Input("rram.clone.title", "rram.clone.modal", Rect(0.08f, 0.53f, 0.92f, 0.61f), GetUiState(s.Player, "clone.title", "") ?? "", titleCb, 12, _config.UI.Theme.Text, 64);
        ui.Input("rram.clone.rank", "rram.clone.modal", Rect(0.08f, 0.41f, 0.92f, 0.49f), GetUiState(s.Player, "clone.rank", "0") ?? "0", rankCb, 12, _config.UI.Theme.Text, 10);
        ui.Toggle("rram.clone.users", "rram.clone.modal", Rect(0.08f, 0.29f, 0.30f, 0.36f), copyUsers, toggle);
        ui.Label("rram.clone.users.label", "rram.clone.modal", Rect(0.32f, 0.29f, 0.72f, 0.36f), "COPY USERS", 10, _config.UI.Theme.MutedText, "MiddleLeft");
        string create = UiActionCallback(s.Player, "clone.go", () => CloneGroup(s));
        ui.Button("rram.clone.cancel", "rram.clone.modal", Rect(0.08f, 0.09f, 0.43f, 0.19f), "CANCEL", "roguerust.ui.close " + Overlay, _config.UI.Theme.SurfaceAlt);
        ui.Button("rram.clone.go", "rram.clone.modal", Rect(0.57f, 0.09f, 0.92f, 0.19f), "CLONE", create, _config.UI.Theme.Success);
        ShowUi(s.Player, ui, true);
    }

    private void CloneGroup(Session s)
    {
        string source = s.SelectedGroup; string dest = GetUiState(s.Player, "clone.name", "") ?? ""; string title = GetUiState(s.Player, "clone.title", "") ?? ""; int.TryParse(GetUiState(s.Player, "clone.rank", "0"), out int rank);
        if (string.IsNullOrWhiteSpace(dest) || !permission.CreateGroup(dest, title, rank)) { Toast(s.Player, "Clone", "Could not create destination group.", _config.UI.Theme.Danger); return; }
        var data = permission.GetGroupData(source);
        if (data != null)
        {
            foreach (string perm in data.Perms) permission.GrantGroupPermission(dest, perm, this);
            if (!string.IsNullOrWhiteSpace(data.ParentGroup)) permission.SetGroupParent(dest, data.ParentGroup);
        }
        bool copyUsers = string.Equals(GetUiState(s.Player, "clone.users", "false"), "true", StringComparison.OrdinalIgnoreCase);
        if (copyUsers)
            foreach (string raw in permission.GetUsersInGroup(source) ?? Array.Empty<string>()) { RecentPlayer? rp = ParseGroupUser(raw); if (rp != null) permission.AddUserGroup(rp.Id, dest); }
        Audit(s.Player, $"Cloned usergroup {source} to {dest}"); DestroyUi(s.Player, Overlay); s.SelectedGroup = dest; Draw(s);
    }

    private void ClearGroupUsers(Session s, string group)
    {
        foreach (string raw in permission.GetUsersInGroup(group) ?? Array.Empty<string>()) { RecentPlayer? rp = ParseGroupUser(raw); if (rp != null) permission.RemoveUserGroup(rp.Id, group); }
        Audit(s.Player, "Cleared users from group " + group); Draw(s);
    }
    private void DeleteGroup(Session s, string group) { permission.RemoveGroup(group); Audit(s.Player, "Deleted usergroup " + group); s.SelectedGroup = string.Empty; Draw(s); }

    #endregion

    #region RogueRust diagnostics

    private void DrawDiagnostics(RogueUiDocument ui, Session s)
    {
        RogueDatabaseSnapshot db = Rogue.Database.GetSnapshot();
        RogueHttpSnapshot http = Rogue.Http.GetSnapshot();
        TimeSpan dllUptime = RogueRustDiagnostics.Uptime;

        ui.Label("rram.diag.title", "rram.body", Rect(0.055f, 0.855f, 0.70f, 0.91f), "ROGUERUST DLL • LIVE SNAPSHOT", 15, _config.UI.Theme.Text, "MiddleLeft");
        ui.Label("rram.diag.help", "rram.body", Rect(0.55f, 0.855f, 0.94f, 0.91f), "Read on demand • no dashboard polling", 9, _config.UI.Theme.MutedText, "MiddleRight");

        var cards = new (string Title, string Value, bool Warning)[]
        {
            ("KERNEL", Rogue.Lifecycle.State.ToString(), Rogue.Dependencies.UnsatisfiedRequiredCount > 0),
            ("SERVICES", Rogue.Registry.Count.ToString(), false),
            ("CAPABILITIES", Rogue.Capabilities.Count.ToString(), false),
            ("MODULES", Rogue.Modules.Count.ToString(), false),
            ("DEPENDENCIES", Rogue.Dependencies.UnsatisfiedRequiredCount + " missing", Rogue.Dependencies.UnsatisfiedRequiredCount > 0),
            ("DLL UPTIME", FormatDuration((float)dllUptime.TotalSeconds), false),
            ("UI DOCUMENTS", Rogue.Ui.ActiveDocumentCount.ToString(), Rogue.Ui.ShowFailures > 0),
            ("UI FAILURES", Rogue.Ui.ShowFailures.ToString(), Rogue.Ui.ShowFailures > 0),
            ("SCHEDULER", Rogue.Scheduler.Count + " active", Rogue.Scheduler.Failures > 0),
            ("HTTP", http.ActiveRequests + " active / " + http.QueuedRequests + " queued", http.Failures > 0),
            ("DATABASE", db.RegisteredConnections + " conn / " + db.Failures + " failed", db.Failures > 0),
            ("WORLD SCANS", Rogue.World.EntityScans + " / " + Rogue.World.EntityScanFailures + " failed", Rogue.World.EntityScanFailures > 0),
            ("CACHE", Rogue.Cache.Count + " entries", false),
            ("EVENTS", Rogue.Events.SubscriptionCount + " subscriptions", RogueRustDiagnostics.EventFailures > 0),
            ("COMMANDS", Rogue.Commands.Count + " registered", Rogue.Commands.Failures > 0),
            ("ADAPTERS", Rogue.Adapters.AvailableCount + "/" + Rogue.Adapters.All.Count + " available", Rogue.Adapters.Failures > 0),
            ("PROFILED", Rogue.Profiler.Operations + " / " + Rogue.Profiler.SlowOperations + " slow", Rogue.Profiler.Failures > 0),
            ("CIRCUITS", Rogue.CircuitBreakers.RejectedCalls + " rejected", Rogue.CircuitBreakers.RejectedCalls > 0)
        };

        for (int i = 0; i < cards.Length; i++)
        {
            int col = i % 3, row = i / 3;
            float left = 0.052f + col * 0.296f;
            float top = 0.815f - row * 0.105f;
            string card = "rram.diag.card." + i;
            ui.Panel(card, "rram.body", Rect(left, top - 0.082f, left + 0.280f, top), _config.UI.Theme.SurfaceAlt);
            ui.Label(card + ".title", card, Rect(0.04f, 0.58f, 0.96f, 0.91f), cards[i].Title, 8, _config.UI.Theme.MutedText, "MiddleLeft");
            ui.Label(card + ".value", card, Rect(0.04f, 0.10f, 0.96f, 0.60f), cards[i].Value, 11, cards[i].Warning ? _config.UI.Theme.Warning : _config.UI.Theme.Text, "MiddleLeft");
        }

        string refresh = UiActionCallback(s.Player, "diag.refresh", () => Draw(s));
        ui.Button("rram.diag.refresh", "rram.body", Rect(0.052f, 0.075f, 0.20f, 0.125f), "REFRESH SNAPSHOT", refresh, _config.UI.Theme.AccentMuted, 9);
        ui.Label("rram.diag.footer", "rram.body", Rect(0.22f, 0.075f, 0.94f, 0.125f),
            $"Integration calls {RogueRustDiagnostics.IntegrationCalls} • failures {RogueRustDiagnostics.IntegrationFailures} • cache {RogueRustDiagnostics.CacheHits} hits / {RogueRustDiagnostics.CacheMisses} misses",
            8, _config.UI.Theme.MutedText, "MiddleLeft");
    }

    #endregion

    #region Convars

    private void DrawConvars(RogueUiDocument ui, Session s)
    {
        // Console descriptors are static for the running Rust build; cache the list once.
        // Values are still read live from each command object while drawing.
        List<ConsoleSystem.Command> vars = Filter(_serverConvarCache, s, x => x.FullName + " " + x.Description).ToList();
        if (s.SubMenu == 1) vars = vars.Where(x => s.FavouriteConvars.Contains(x.FullName)).ToList();
        string allVars = UiActionCallback(s.Player, "convar.filter.all", () => { s.SubMenu = 0; s.Page = 0; Draw(s); });
        string favVars = UiActionCallback(s.Player, "convar.filter.fav", () => { s.SubMenu = 1; s.Page = 0; Draw(s); });
        ui.Button("rram.convar.filter.all", "rram.body", Rect(0.052f, 0.858f, 0.135f, 0.900f), "ALL", allVars, s.SubMenu == 0 ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
        ui.Button("rram.convar.filter.fav", "rram.body", Rect(0.140f, 0.858f, 0.245f, 0.900f), "FAVOURITES", favVars, s.SubMenu == 1 ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
        const int pageSize = 21;
        DrawCharacterFilter(ui, s, vars, x => x.FullName); List<ConsoleSystem.Command> page = PageWithSize(vars, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            ConsoleSystem.Command variable = page[i]; int col = i % 3, row = i / 3; float left = 0.052f + col * 0.296f; float top = 0.825f - row * 0.105f;
            string cb = UiCallback(s.Player, $"convar.{s.Page}.{i}", arg => { string value = UiCallbackArgument(arg, 0); ConsoleSystem.Run(ConsoleSystem.Option.Server, variable.FullName, value); Audit(s.Player, $"Set convar {variable.FullName} to {value}"); Draw(s); });
            string card = "rram.convar." + i;
            ui.Panel(card, "rram.body", Rect(left, top - 0.084f, left + 0.280f, top), _config.UI.Theme.SurfaceAlt);
            ui.Label("rram.convar.name." + i, card, Rect(0.025f, 0.52f, 0.66f, 0.92f), variable.FullName, 8, _config.UI.Theme.Text, "MiddleLeft");
            ui.Label("rram.convar.desc." + i, card, Rect(0.025f, 0.08f, 0.66f, 0.50f), variable.Description ?? string.Empty, 7, _config.UI.Theme.MutedText, "MiddleLeft");
            string fav = UiActionCallback(s.Player, $"convar.fav.{s.Page}.{i}", () => { if (!s.FavouriteConvars.Add(variable.FullName)) s.FavouriteConvars.Remove(variable.FullName); Draw(s); });
            ui.Button("rram.convar.fav." + i, card, Rect(0.64f, 0.18f, 0.71f, 0.82f), s.FavouriteConvars.Contains(variable.FullName) ? "★" : "☆", fav, "0 0 0 0", 11);
            ui.Input("rram.convar.input." + i, card, Rect(0.72f, 0.18f, 0.97f, 0.82f), variable.String ?? string.Empty, cb, 9, _config.UI.Theme.Text, 64, false, "MiddleCenter");
        }
        DrawPagerWithSize(ui, s, vars.Count, pageSize);
    }

    #endregion

    #region Plugins

    private void DrawPlugins(RogueUiDocument ui, Session s)
    {
        List<PluginInfo> infos = GetPluginInfos(); infos = Filter(infos, s, x => x.Title + " " + x.Name + " " + x.Description).ToList();
        if (s.SubMenu == 1) infos = infos.Where(x => x.Loaded).ToList();
        else if (s.SubMenu == 2) infos = infos.Where(x => !x.Loaded).ToList();
        string allPlugins = UiActionCallback(s.Player, "plugin.filter.all", () => { s.SubMenu = 0; s.Page = 0; Draw(s); });
        string loadedPlugins = UiActionCallback(s.Player, "plugin.filter.loaded", () => { s.SubMenu = 1; s.Page = 0; Draw(s); });
        string unloadedPlugins = UiActionCallback(s.Player, "plugin.filter.unloaded", () => { s.SubMenu = 2; s.Page = 0; Draw(s); });
        ui.Button("rram.plugin.filter.all", "rram.body", Rect(0.052f, 0.858f, 0.125f, 0.900f), "ALL", allPlugins, s.SubMenu == 0 ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
        ui.Button("rram.plugin.filter.loaded", "rram.body", Rect(0.130f, 0.858f, 0.225f, 0.900f), "LOADED", loadedPlugins, s.SubMenu == 1 ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
        ui.Button("rram.plugin.filter.unloaded", "rram.body", Rect(0.230f, 0.858f, 0.335f, 0.900f), "UNLOADED", unloadedPlugins, s.SubMenu == 2 ? _config.UI.Theme.Accent : _config.UI.Theme.SurfaceAlt, 8);
        const int pageSize = 21;
        DrawCharacterFilter(ui, s, infos, x => x.Title); List<PluginInfo> page = PageWithSize(infos, s.Page, pageSize);
        for (int i = 0; i < page.Count; i++)
        {
            PluginInfo p = page[i]; int col = i % 3, row = i / 3; float left = 0.052f + col * 0.296f; float top = 0.825f - row * 0.105f;
            string card = "rram.plugin." + i; ui.Panel(card, "rram.body", Rect(left, top - 0.084f, left + 0.280f, top), _config.UI.Theme.SurfaceAlt);
            string pluginTitle = p.Loaded ? $"{p.Title} • v{p.Version}" : $"{p.Title} • {p.Error}";
            ui.Label("rram.plugin.name." + i, card, Rect(0.025f, 0.55f, 0.64f, 0.92f), pluginTitle, 8, _config.UI.Theme.Text, "MiddleLeft");
            ui.Label("rram.plugin.desc." + i, card, Rect(0.025f, 0.10f, 0.56f, 0.52f), p.Loaded ? p.Author + " • " + p.HookTime.ToString("0.000") + "s" : p.Description, 7, _config.UI.Theme.MutedText, "MiddleLeft");
            ui.Badge("rram.plugin.state." + i, card, Rect(0.58f, 0.57f, 0.69f, 0.88f), p.Loaded ? "ON" : "OFF", p.Loaded ? _config.UI.Theme.Success : _config.UI.Theme.Danger);
            if (p.Loaded)
            {
                string reload = UiActionCallback(s.Player, $"plugin.reload.{i}", () => { Interface.Oxide.ReloadPlugin(p.FileName); _pluginInfoCacheDirty = true; Audit(s.Player, "Reloaded plugin " + p.Name); Draw(s); });
                string unload = UiActionCallback(s.Player, $"plugin.unload.{i}", () => Confirm(s, "UNLOAD PLUGIN", "Unload " + p.Title + "?", () => { Interface.Oxide.UnloadPlugin(p.FileName); _pluginInfoCacheDirty = true; Audit(s.Player, "Unloaded plugin " + p.Name); Draw(s); }));
                ui.Button("rram.plugin.reload." + i, card, Rect(0.71f, 0.54f, 0.97f, 0.90f), "RELOAD", reload, _config.UI.Theme.Warning, 8);
                ui.Button("rram.plugin.unload." + i, card, Rect(0.71f, 0.10f, 0.97f, 0.46f), "UNLOAD", unload, _config.UI.Theme.Danger, 8);
            }
            else
            {
                string load = UiActionCallback(s.Player, $"plugin.load.{i}", () => { Interface.Oxide.LoadPlugin(p.FileName); _pluginInfoCacheDirty = true; Audit(s.Player, "Loaded plugin " + p.Name); Draw(s); });
                ui.Button("rram.plugin.load." + i, card, Rect(0.71f, 0.20f, 0.97f, 0.80f), "LOAD", load, _config.UI.Theme.Success, 8);
            }
        }
        DrawPagerWithSize(ui, s, infos.Count, pageSize);
    }

    private sealed class PluginInfo
    {
        public string Name = string.Empty, Title = string.Empty, Version = string.Empty, Description = string.Empty, FileName = string.Empty, Author = string.Empty, Error = string.Empty;
        public double HookTime;
        public bool Loaded;
    }

    private List<PluginInfo> GetPluginInfos()
    {
        // Plugin discovery/scanning is one of the more expensive admin views. Reuse the
        // structural snapshot for a few seconds and invalidate immediately on load/unload.
        float now = Time.realtimeSinceStartup;
        if (!_pluginInfoCacheDirty && now < _pluginInfoCacheExpiresAt)
            return _pluginInfoCache;

        _pluginInfoCache.Clear();
        var loadedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Plugin plugin in Interface.Oxide.RootPluginManager.GetPlugins().Where(p => p != null && !p.IsCorePlugin && p.Filename != null))
        {
            loadedNames.Add(plugin.Name);
            _pluginInfoCache.Add(new PluginInfo
            {
                Name = plugin.Name,
                Title = plugin.Title,
                Version = plugin.Version.ToString(),
                Description = plugin.Description ?? string.Empty,
                FileName = System.IO.Path.GetFileNameWithoutExtension(plugin.Filename),
                Author = plugin.Author ?? string.Empty,
                #if CARBON
                    HookTime = plugin.TotalHookTime.TotalMilliseconds,
                #else
                    HookTime = plugin.TotalHookTime,
                #endif
                Loaded = true
            });
        }

#if !CARBON
        foreach (PluginLoader loader in Interface.Oxide.GetPluginLoaders())
        {
            foreach (string name in loader.ScanDirectory(Interface.Oxide.PluginDirectory).Except(loadedNames, StringComparer.OrdinalIgnoreCase))
            {
                string error = loader.PluginErrors.TryGetValue(name, out string message) ? "Failed to compile" : "Unloaded";
                _pluginInfoCache.Add(new PluginInfo { Name = name, Title = name, FileName = name, Error = error, Loaded = false });
            }
        }
#endif
        _pluginInfoCache.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        _pluginInfoCacheDirty = false;
        _pluginInfoCacheExpiresAt = now + 5f;
        return _pluginInfoCache;
    }

    #endregion


    private RogueUiDocument CreateOverlayUi(bool cursorEnabled = true)
    {
        RogueUiDocument ui = CreateUi(Overlay);
        ApplyTheme(ui);
        ui.Panel(Overlay, "Overlay", RogueUiRect.Full, "0 0 0 0", cursorEnabled);
        return ui;
    }

    private RogueUiDocument CreatePopupUi()
    {
        RogueUiDocument ui = CreateUi(Popup);
        ApplyTheme(ui);
        ui.Panel(Popup, "Overlay", RogueUiRect.Full, "0 0 0 0", false);
        return ui;
    }

    #region Vehicles

    private static readonly string[] VehicleCategories = { "ALL", "ANIMALS", "BIKES", "BOATS", "CARS", "HELICOPTERS", "MISC", "SIEGE", "TRAINS" };

    private void DrawVehicles(RogueUiDocument ui, Session s)
    {
        if (!HasAdminPermission(s.Player, VehiclePermission) || _vehicles == null) return;

        // Native Rust F1-style vehicle browser: compact category ribbon, dense 12-column
        // artwork grid and the vehicle name directly underneath each image.
        for (int i = 0; i < VehicleCategories.Length; i++)
        {
            string category = VehicleCategories[i];
            float left = 0.035f + i * 0.101f;
            string cb = UiActionCallback(s.Player, "vehicles.cat." + i, () =>
            {
                s.VehicleCategory = category; s.Page = 0; Draw(s);
            });
            ui.Tab("rram.vehicles.cat." + i, "rram.body", Rect(left, 0.865f, left + 0.094f, 0.905f),
                category, cb, string.Equals(s.VehicleCategory, category, StringComparison.OrdinalIgnoreCase));
        }

        const int columns = 12;
        const int vehiclePageSize = 36;
        IReadOnlyList<RogueVehicleDescriptor> vehicles =
            _vehicles.Filter(s.VehicleCategory, s.Search, s.Page * vehiclePageSize, vehiclePageSize);

        for (int i = 0; i < vehicles.Count; i++)
        {
            RogueVehicleDescriptor vehicle = vehicles[i];
            int col = i % columns, row = i / columns;
            float left = 0.020f + col * 0.0803f;
            float top = 0.820f - row * 0.218f;
            string card = "rram.vehicle." + i;
            bool selected = string.Equals(s.SelectedVehicleId, vehicle.Id, StringComparison.OrdinalIgnoreCase);

            // The tile itself stays visually quiet like Rust F1; selection uses the
            // muted olive treatment rather than a heavy bordered admin card.
            ui.Panel(card, "rram.body", Rect(left, top - 0.190f, left + 0.074f, top),
                selected ? _config.UI.Theme.AccentMuted : "0 0 0 0");

            string imagePanel = card + ".art";
            ui.Panel(imagePanel, card, Rect(0.02f, 0.30f, 0.98f, 0.98f),
                selected ? _config.UI.Theme.AccentMuted : _config.UI.Theme.SurfaceAlt);

            // Native Rust artwork is authoritative. Only consult ImageLibrary for vehicles
            // that have no exact/approved ItemDefinition representation.
            bool hasNativeVehicleIcon = HasNativeVehicleIcon(vehicle);
            if (!hasNativeVehicleIcon && TryGetVehicleImage(vehicle, out string vehiclePng))
                ui.Image(card + ".image", imagePanel, Rect(0.06f, 0.06f, 0.94f, 0.94f), vehiclePng);
            else if (!hasNativeVehicleIcon)
                ui.Label(card + ".fallback", imagePanel, Rect(0.06f, 0.12f, 0.94f, 0.88f),
                    vehicle.DisplayName, 8, _config.UI.Theme.MutedText, "MiddleCenter");

            // F1-style caption: artwork first, clean centered name directly underneath.
            ui.Label(card + ".name", card, Rect(0.01f, 0.10f, 0.99f, 0.285f),
                vehicle.DisplayName.ToUpperInvariant(), 7, _config.UI.Theme.Text, "UpperCenter");

            // Match Items: clicking the artwork performs the primary action immediately.
            string spawn = UiActionCallback(s.Player, "vehicles.spawn." + i, () =>
            {
                s.SelectedVehicleId = vehicle.Id;
                SpawnVehicleForAdmin(s, vehicle);
            });
            ui.Button(card + ".spawn", imagePanel, RogueUiRect.Full, string.Empty, spawn, "0 0 0 0", 1);
        }

        int total = _vehicles.Filter(s.VehicleCategory, s.Search, 0, 120).Count;
        DrawVehiclePager(ui, s, total, vehiclePageSize);
    }

    private void DrawVehiclePager(RogueUiDocument ui, Session s, int count, int pageSize)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(count / (double)pageSize));
        if (s.Page >= pages) s.Page = pages - 1;
        string prev = UiActionCallback(s.Player, "vehicles.prev", () => { if (s.Page > 0) s.Page--; Draw(s); });
        string next = UiActionCallback(s.Player, "vehicles.next", () => { if (s.Page + 1 < pages) s.Page++; Draw(s); });
        ui.Button("rram.vehicles.prev", "rram.body", Rect(0.405f, 0.018f, 0.455f, 0.052f), "‹", prev, _config.UI.Theme.SurfaceAlt, 11);
        ui.Label("rram.vehicles.page", "rram.body", Rect(0.46f, 0.018f, 0.54f, 0.052f), $"{s.Page + 1} / {pages}", 8, _config.UI.Theme.MutedText, "MiddleCenter");
        ui.Button("rram.vehicles.next", "rram.body", Rect(0.545f, 0.018f, 0.595f, 0.052f), "›", next, _config.UI.Theme.SurfaceAlt, 11);
    }

    private void SpawnVehicleForAdmin(Session s, RogueVehicleDescriptor vehicle)
    {
        if (s?.Player == null || vehicle == null) return;

        // Resolve against Rust's server prefab catalogue at action time. This avoids
        // hard-coding build-sensitive prefab paths into the lightweight AdminMenu.
        string prefabPath = FindVehiclePrefabPath(vehicle);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Toast(s.Player, "Vehicles", "Vehicle prefab is not available on this Rust build: " + vehicle.DisplayName, _config.UI.Theme.Warning);
            return;
        }

        Vector3 forward = s.Player.eyes != null ? s.Player.eyes.BodyForward() : s.Player.transform.forward;
        Vector3 requested = s.Player.transform.position + forward * 6f + Vector3.up * 1.5f;
        Vector3 destination = requested;
        if (_teleport != null)
            _teleport.TryResolveSafeDestination(requested, out destination, new RogueSafePositionOptions
            {
                SearchRadius = 8f, Attempts = 48, GroundClearance = 1.0f, WaterClearance = 0.1f, RequireEntityClearance = true
            });

        BaseEntity entity = GameManager.server.CreateEntity(prefabPath, destination, Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z).normalized));
        if (entity == null)
        {
            Toast(s.Player, "Vehicles", "Rust could not create " + vehicle.DisplayName + ".", _config.UI.Theme.Warning);
            return;
        }

        entity.OwnerID = s.Player.userID;
        entity.Spawn();
        Audit(s.Player, "Spawned vehicle " + vehicle.DisplayName + " at " + SafeGridReference(destination));
        Toast(s.Player, "Vehicles", "Spawned " + vehicle.DisplayName, _config.UI.Theme.Success);
    }

    private string FindVehiclePrefabPath(RogueVehicleDescriptor vehicle)
    {
        if (vehicle == null) return null;
        if (!string.IsNullOrWhiteSpace(vehicle.Prefab))
            return vehicle.Prefab;
        if (GameManifest.Current == null || GameManifest.Current.entities == null)
            return null;

        // GameManifest already stores the exact resource paths accepted by
        // GameManager.server.CreateEntity(string,...). Return that path directly;
        // ResourceRef<GameObject>.resourcePath is read-only and must not be fabricated.
        string[] terms = (vehicle.SearchText + " " + vehicle.Id)
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 4)
            .Select(term => term.ToLowerInvariant())
            .Distinct()
            .ToArray();

        string bestPath = null;
        int bestScore = 0;
        foreach (string pathValue in GameManifest.Current.entities)
        {
            if (string.IsNullOrEmpty(pathValue)) continue;
            string path = pathValue.ToLowerInvariant();
            if (!path.EndsWith(".prefab", StringComparison.Ordinal)) continue;
            if (!path.Contains("vehicle") && !path.Contains("horse") && !path.Contains("boat") &&
                !path.Contains("helicopter") && !path.Contains("train") && !path.Contains("snowmobile") &&
                !path.Contains("kayak") && !path.Contains("submarine")) continue;

            int score = 0;
            for (int i = 0; i < terms.Length; i++)
                if (path.Contains(terms[i])) score++;

            if (score <= bestScore) continue;
            bestScore = score;
            bestPath = pathValue;
        }

        return bestScore > 0 ? bestPath : null;
    }

    private bool TryGetVehicleImage(RogueVehicleDescriptor vehicle, out string png)
    {
        png = string.Empty;
        if (!_config.UI.UseItemImages || vehicle == null) return false;

        string cacheKey = string.IsNullOrWhiteSpace(vehicle.ImageKey) ? "vehicle:" + vehicle.Id : vehicle.ImageKey;
        if (_vehicleImageCache.TryGetValue(cacheKey, out string cached) && !string.IsNullOrWhiteSpace(cached))
        {
            png = cached;
            return true;
        }

        string existing;
        if (TryGetImage(cacheKey, out existing, 0) && !string.IsNullOrWhiteSpace(existing))
        {
            _vehicleImageCache[cacheKey] = existing;
            _vehicleImageMissUntil.Remove(cacheKey);
            png = existing;
            return true;
        }

        if (_vehicleImageMissUntil.TryGetValue(cacheKey, out float retryAt) && Time.realtimeSinceStartup < retryAt)
            return false;

        // Native ItemId rendering is attempted before this method. If it is unavailable,
        // use Carbon's current Rust item-reference artwork for the exact same vehicle identity.
        string imageShortname = !string.IsNullOrWhiteSpace(vehicle.ItemShortname)
            ? vehicle.ItemShortname
            : VehicleCarbonImageShortname(vehicle.Id);
        if (string.IsNullOrWhiteSpace(imageShortname))
        {
            _vehicleImageMissUntil[cacheKey] = Time.realtimeSinceStartup + 300f;
            return false;
        }

        string sourceUrl = "https://cdn.carbonmod.gg/items/" + Uri.EscapeDataString(imageShortname) + ".png";
        string queued = GetOrQueueImage(cacheKey, sourceUrl, 0, result =>
        {
            if (string.IsNullOrWhiteSpace(result))
            {
                _vehicleImageMissUntil[cacheKey] = Time.realtimeSinceStartup + 60f;
                return;
            }

            _vehicleImageCache[cacheKey] = result;
            _vehicleImageMissUntil.Remove(cacheKey);
            QueueVehicleImageRedraw();
        });

        if (!string.IsNullOrWhiteSpace(queued))
        {
            _vehicleImageCache[cacheKey] = queued;
            _vehicleImageMissUntil.Remove(cacheKey);
            png = queued;
            return true;
        }

        _vehicleImageMissUntil[cacheKey] = Time.realtimeSinceStartup + 2f;
        return false;
    }

    private bool HasNativeVehicleIcon(RogueVehicleDescriptor vehicle)
    {
        return GetVehicleIconItem(vehicle) != null;
    }

    private ItemDefinition GetVehicleIconItem(RogueVehicleDescriptor vehicle)
    {
        if (vehicle == null || string.IsNullOrWhiteSpace(vehicle.Id)) return null;
        if (_vehicleIconItemCache.TryGetValue(vehicle.Id, out ItemDefinition cached))
            return cached;

        ItemDefinition resolved = null;
        if (vehicle.ItemId != 0)
            resolved = ItemManager.FindItemDefinition(vehicle.ItemId);
        if (resolved == null && !string.IsNullOrWhiteSpace(vehicle.ItemShortname))
            resolved = ItemManager.FindItemDefinition(vehicle.ItemShortname);
        if (resolved == null)
        {
            string[] candidates = VehicleIconShortnames(vehicle.Id);
            for (int i = 0; i < candidates.Length; i++)
            {
                resolved = ItemManager.FindItemDefinition(candidates[i]);
                if (resolved != null) break;
            }
        }

        _vehicleIconItemCache[vehicle.Id] = resolved;
        return resolved;
    }

    private static string[] VehicleIconShortnames(string id)
    {
        // Rust/Carbon item reference shortnames. Several vehicles are hidden/admin spawn
        // items but still have a real ItemDefinition + native Rust icon, so use those before
        // any network image. Do not substitute repair parts, armour or ammunition.
        switch ((id ?? string.Empty).ToLowerInvariant())
        {
            case "horse": return new[] { "horse.saddle" };
            case "bicycle": return new[] { "bicycle" };
            case "motorbike": return new[] { "motorbike" };
            case "trike": return new[] { "trike" };
            case "rowboat": return new[] { "rowboat" };
            case "rhib": return new[] { "rhib" };
            case "kayak": return new[] { "kayak" };
            case "tugboat": return new[] { "tugboat" };
            case "diverpropulsionvehicle": return new[] { "skidoo" };
            case "ptboat": return new[] { "ptboat" };
            case "sedan": return new[] { "sedan" };

            // Current RogueVehicleService exposes one generic modular-car entry. Prefer the
            // actual 4-module spawn item, then the chassis definitions as compatibility fallbacks.
            case "modularcar": return new[] { "4module car", "vehicle.chassis.4mod", "vehicle.chassis.3mod", "vehicle.chassis.2mod" };

            case "minicopter": return new[] { "minicopter" };
            case "scraptransport": return new[] { "scraptransportheli" };
            case "attackhelicopter": return new[] { "attackhelicopter" };
            case "submarine": return new[] { "submarinesolo", "submarineduo" };
            case "hotairballoon": return new[] { "hab" };
            case "snowmobile": return new[] { "snowmobile" };
            case "mlrs": return new[] { "mlrs" };
            case "soccerball": return new[] { "soccerball" };
            case "batteringram": return new[] { "batteringram" };
            case "catapult": return new[] { "catapult" };
            case "mountedballista": return new[] { "ballista.mounted", "ballista" };
            case "siegetower": return new[] { "siegetower" };
            case "locomotive": return new[] { "locomotive" };
            case "workcart": return new[] { "workcart" };
            case "caboose": return new[] { "caboose" };
            case "wagon": return new[] { "wagon" };

            default:
                return string.IsNullOrWhiteSpace(id) ? Array.Empty<string>() : new[] { id };
        }
    }

    private static string VehicleCarbonImageShortname(string id)
    {
        // Network fallback uses the same exact item identity as Carbon's Rust Items reference.
        // It is only reached when the server cannot resolve the corresponding native ItemDefinition.
        switch ((id ?? string.Empty).ToLowerInvariant())
        {
            case "modularcar": return "4module car";
            case "submarine": return "submarinesolo";
            case "scraptransport": return "scraptransportheli";
            case "diverpropulsionvehicle": return "skidoo";
            default:
            {
                string[] candidates = VehicleIconShortnames(id);
                return candidates.Length == 0 ? string.Empty : candidates[0];
            }
        }
    }

    private void QueueVehicleImageRedraw()
    {
        if (_vehicleImageRedrawQueued) return;
        _vehicleImageRedrawQueued = true;
        NextTick(() =>
        {
            _vehicleImageRedrawQueued = false;
            foreach (Session open in _sessions.Values)
            {
                if (open?.Player != null && open.Player.IsConnected && open.Menu == MenuType.Vehicles)
                    Draw(open);
            }
        });
    }

    private void ShowNativeVehicleIcons(Session s)
    {
        if (s?.Player == null || !s.Player.IsConnected || _vehicles == null) return;
        try
        {
            const int pageSize = 36;
            IReadOnlyList<RogueVehicleDescriptor> page = _vehicles.Filter(s.VehicleCategory, s.Search, s.Page * pageSize, pageSize);
            CuiElementContainer elements = new CuiElementContainer();
            for (int i = 0; i < page.Count; i++)
            {
                RogueVehicleDescriptor vehicle = page[i];
                ItemDefinition item = GetVehicleIconItem(vehicle);
                if (item == null) continue;

                string card = "rram.vehicle." + i;
                string imagePanel = card + ".art";
                elements.Add(new CuiElement
                {
                    Name = card + ".nativeicon",
                    Parent = imagePanel,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            ItemId = item.itemid,
                            SkinId = 0,
                            Color = "1 1 1 1",
                            BlocksRaycast = false
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.06 0.06",
                            AnchorMax = "0.94 0.94",
                            SetTransformIndex = 0
                        }
                    }
                });
            }
            if (elements.Count > 0) CuiHelper.AddUi(s.Player, elements);
        }
        catch (Exception ex)
        {
            LogWarning("UI", "Native vehicle icons could not be rendered: " + ex.Message);
        }
    }

    #endregion

    #region Give

    private void DrawGive(RogueUiDocument ui, Session s)
    {
        s.NativeGiveIcons.Clear();
        if (!HasAdminPermission(s.Player, GivePermission)) return;
        if (HasAdminPermission(s.Player, GiveSelfPermission))
        {
            s.SelectedPlayerId = s.Player.UserIDString; s.SelectedPlayerName = StripName(s.Player.displayName);
        }
        else if (string.IsNullOrEmpty(s.SelectedPlayerId))
        {
            s.Selection = SelectionPurpose.GiveTarget; DrawPlayerSelection(ui, s); return;
        }

        string targetCb = UiActionCallback(s.Player, "give.target", () => { if (!HasAdminPermission(s.Player, GiveSelfPermission)) { s.Selection = SelectionPurpose.GiveTarget; Draw(s); } });
        ui.Button("rram.give.target", "rram.body", Rect(0.055f, 0.845f, 0.25f, 0.895f), "TARGET  •  " + s.SelectedPlayerName, targetCb, _config.UI.Theme.Accent, 10);

        // Rust-style compact category ribbon.
        ItemCategory[] cats = { ItemCategory.Weapon, ItemCategory.Construction, ItemCategory.Items, ItemCategory.Resources, ItemCategory.Attire, ItemCategory.Tool, ItemCategory.Medical, ItemCategory.Food, ItemCategory.Ammunition, ItemCategory.Traps, ItemCategory.Misc, ItemCategory.Component, ItemCategory.Electrical, ItemCategory.Fun };
        string allCb = UiActionCallback(s.Player, "give.cat.all", () => { s.GiveCategory = -1; s.GiveRecentOnly = false; s.GiveFavouritesOnly = false; s.Page = 0; Draw(s); });
        ui.Tab("rram.give.cat.all", "rram.body", Rect(0.27f, 0.851f, 0.325f, 0.892f), "ALL", allCb, s.GiveCategory == -1 && !s.GiveRecentOnly && !s.GiveFavouritesOnly);
        string recentCb = UiActionCallback(s.Player, "give.cat.recent", () => { s.GiveRecentOnly = true; s.GiveFavouritesOnly = false; s.Page = 0; Draw(s); });
        ui.Tab("rram.give.cat.recent", "rram.body", Rect(0.33f, 0.851f, 0.405f, 0.892f), "RECENT", recentCb, s.GiveRecentOnly);
        string favsCb = UiActionCallback(s.Player, "give.cat.favs", () => { s.GiveFavouritesOnly = true; s.GiveRecentOnly = false; s.Page = 0; Draw(s); });
        ui.Tab("rram.give.cat.favs", "rram.body", Rect(0.41f, 0.851f, 0.475f, 0.892f), "FAVS", favsCb, s.GiveFavouritesOnly);
        for (int i = 0; i < cats.Length; i++)
        {
            ItemCategory cat = cats[i];
            int row = i / 7, col = i % 7;
            float left = 0.485f + col * 0.067f;
            float bottom = row == 0 ? 0.868f : 0.823f;
            float top = bottom + 0.034f;
            string cb = UiActionCallback(s.Player, "give.cat." + i, () => { s.GiveCategory = (int)cat; s.GiveRecentOnly = false; s.GiveFavouritesOnly = false; s.Page = 0; Draw(s); });
            ui.Tab("rram.give.cat." + i, "rram.body", Rect(left, bottom, left + 0.061f, top), ShortCategory(cat), cb, !s.GiveRecentOnly && !s.GiveFavouritesOnly && s.GiveCategory == (int)cat);
        }

        IEnumerable<ItemDefinition> source = _allItems;
        if (s.GiveFavouritesOnly)
            source = source.Where(x => s.FavouriteGiveItems.Contains(x.shortname));
        else if (s.GiveRecentOnly)
        {
            HashSet<string> recent = new HashSet<string>(s.RecentGiveItems, StringComparer.OrdinalIgnoreCase);
            source = source.Where(x => recent.Contains(x.shortname));
        }
        else if (s.GiveCategory >= 0)
            source = source.Where(x => (int)x.category == s.GiveCategory);

        IEnumerable<ItemDefinition> filteredItems = source;
        if (!string.IsNullOrWhiteSpace(s.Search))
        {
            string search = s.Search.Trim().ToLowerInvariant();
            filteredItems = filteredItems.Where(x =>
                _itemSearchKeys.TryGetValue(x.itemid, out string key) && key.IndexOf(search, StringComparison.Ordinal) >= 0);
        }
        if (!string.IsNullOrEmpty(s.Character) && s.Character != "~")
            filteredItems = filteredItems.Where(x => (x.displayName?.english ?? string.Empty).StartsWith(s.Character, StringComparison.OrdinalIgnoreCase));
        List<ItemDefinition> items = filteredItems.ToList();
        DrawCharacterFilter(ui, s, items, x => x.displayName.english);
        const int givePageSize = 60; // Native screenshot density: 12 columns x 5 rows.
        List<ItemDefinition> page = items.Skip(Math.Max(0, s.Page) * givePageSize).Take(givePageSize).ToList();
        for (int i = 0; i < page.Count; i++)
        {
            ItemDefinition item = page[i]; int col = i % 12, row = i / 12;
            // Native F1-style item browser: twelve compact columns by five rows, matching Rust's own item browser proportions without oversized cards.
            float left = 0.046f + col * 0.0745f; float top = 0.775f - row * 0.135f;
            string card = "rram.give.item." + i;
            string itemCb = UiActionCallback(s.Player, $"give.item.{s.Page}.{i}", () => GiveItemAmount(s, item, 1));
            string give100Cb = UiActionCallback(s.Player, $"give.100.{s.Page}.{i}", () => GiveItemAmount(s, item, 100));
            string give1000Cb = UiActionCallback(s.Player, $"give.1000.{s.Page}.{i}", () => GiveItemAmount(s, item, 1000));
            string giveStackCb = UiActionCallback(s.Player, $"give.stack.{s.Page}.{i}", () => GiveItemAmount(s, item, Math.Max(1, item.stackable)));
            string bpCb = UiActionCallback(s.Player, $"give.bp.{s.Page}.{i}", () => DrawGiveOverlay(s, item, true));
            string favCb = UiActionCallback(s.Player, $"give.fav.{s.Page}.{i}", () => { if (!s.FavouriteGiveItems.Add(item.shortname)) s.FavouriteGiveItems.Remove(item.shortname); Draw(s); });
            ui.Panel(card, "rram.body", Rect(left, top - 0.116f, left + 0.069f, top), _config.UI.Theme.SurfaceAlt);
            // Keep Give visually consistent with RogueRustSkinBox: prefer the cached
            // ImageLibrary PNG when available, then fall back to Rust's native ItemId icon.
            // Native Rust ItemId artwork is the fast/default path. ImageLibrary is only
            // consulted when native icons are disabled, avoiding adapter lookups and HTTP/cache
            // work for the normal Items browser.
            if (_config.UI.UseNativeItemIcons)
            {
                s.NativeGiveIcons.Add(item);
            }
            else if (_config.UI.UseItemImages && TryGetItemImage(item, out string png))
            {
                ui.Image(card + ".image", card, Rect(0.12f, 0.38f, 0.88f, 0.92f), png);
            }
            else
            {
                ui.Label(card + ".fallback", card, Rect(0.08f, 0.38f, 0.92f, 0.84f), item.displayName.english, 10, _config.UI.Theme.Text, "MiddleCenter");
            }
            ui.Button(card + ".select", card, Rect(0.01f, 0.36f, 0.99f, 0.99f), string.Empty, itemCb, "0 0 0 0", 1);

            // Dedicated F1 caption band. BP/FAV are kept on the artwork corners so they
            // can never cover the item name, regardless of category/search/filter.
            ui.Panel(card + ".caption", card, Rect(0.01f, 0.225f, 0.99f, 0.355f), _config.UI.Theme.Surface);
            ui.Label(card + ".name", card + ".caption", Rect(0.02f, 0.02f, 0.98f, 0.98f),
                item.displayName.english.ToUpperInvariant(), 7, _config.UI.Theme.Text, "MiddleCenter");
            ui.Button(card + ".bp", card, Rect(0.015f, 0.80f, 0.17f, 0.965f), "BP", bpCb, _config.UI.Theme.AccentMuted, 6);
            ui.Button(card + ".fav", card, Rect(0.83f, 0.80f, 0.985f, 0.965f),
                s.FavouriteGiveItems.Contains(item.shortname) ? "★" : "☆", favCb, "0 0 0 0", 9);
            // CUI has no native mouse-enter callback, so keep the native F1 fast-action
            // workflow as a persistent compact action strip: card=1, then 100/1K/STACK.
            ui.Button(card + ".100", card, Rect(0.01f, 0.01f, 0.25f, 0.22f), "100", give100Cb, _config.UI.Theme.Surface, 6);
            ui.Button(card + ".1000", card, Rect(0.255f, 0.01f, 0.50f, 0.22f), "1K", give1000Cb, _config.UI.Theme.Surface, 6);
            ui.Button(card + ".stack", card, Rect(0.505f, 0.01f, 0.99f, 0.22f), "STACK", giveStackCb, _config.UI.Theme.AccentMuted, 6);
        }
        DrawGivePager(ui, s, items.Count);
        if (_config.UI.UseItemImages || _config.UI.UseNativeItemIcons)
            ui.Label("rram.give.imagehint", "rram.body", Rect(0.66f, 0.018f, 0.94f, 0.052f), "Native Rust icons • ImageLibrary fallback", 8, _config.UI.Theme.MutedText, "MiddleRight");
    }

    private void ShowNativeGiveIcons(Session s)
    {
        if (s.Player == null || !s.Player.IsConnected || s.NativeGiveIcons.Count == 0) return;

        try
        {
            CuiElementContainer elements = new CuiElementContainer();
            int count = Math.Min(60, s.NativeGiveIcons.Count);
            for (int i = 0; i < count; i++)
            {
                ItemDefinition item = s.NativeGiveIcons[i];
                if (item == null) continue;

                string card = "rram.give.item." + i;
                string name = card + ".nativeicon";
                elements.Add(new CuiElement
                {
                    Name = name,
                    Parent = card,
                    Components =
                    {
                        new CuiImageComponent
                        {
                            ItemId = item.itemid,
                            SkinId = 0,
                            Color = "1 1 1 1",
                            BlocksRaycast = false
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.12 0.38",
                            AnchorMax = "0.88 0.92",
                            SetTransformIndex = 0
                        }
                    }
                });
            }

            if (elements.Count > 0)
                CuiHelper.AddUi(s.Player, elements);
        }
        catch (Exception ex)
        {
            LogWarning("UI", "Native Give item icons could not be rendered: " + ex.Message);
        }
    }

    private static string ShortCategory(ItemCategory category) => category switch
    {
        ItemCategory.Construction => "BUILD",
        ItemCategory.Resources => "RES",
        ItemCategory.Ammunition => "AMMO",
        ItemCategory.Component => "COMP",
        ItemCategory.Electrical => "ELEC",
        _ => category.ToString().ToUpperInvariant()
    };

    private void DrawGivePager(RogueUiDocument ui, Session s, int count)
    {
        int pages = Math.Max(1, (int)Math.Ceiling(count / 60d));
        s.Page = Math.Max(0, Math.Min(s.Page, pages - 1));
        if (s.Page > 0)
        {
            string prev = UiActionCallback(s.Player, "give.page.prev", () => { s.Page--; Draw(s); });
            ui.Button("rram.give.prev", "rram.body", Rect(0.40f, 0.018f, 0.47f, 0.055f), "‹ PREV", prev, _config.UI.Theme.SurfaceAlt, 9);
        }
        ui.Label("rram.give.page", "rram.body", Rect(0.475f, 0.018f, 0.555f, 0.055f), $"{s.Page + 1} / {pages}", 9, _config.UI.Theme.MutedText);
        if (s.Page + 1 < pages)
        {
            string next = UiActionCallback(s.Player, "give.page.next", () => { s.Page++; Draw(s); });
            ui.Button("rram.give.next", "rram.body", Rect(0.56f, 0.018f, 0.63f, 0.055f), "NEXT ›", next, _config.UI.Theme.SurfaceAlt, 9);
        }
    }

    private void GiveItemAmount(Session s, ItemDefinition definition, int amount)
    {
        if (s == null || definition == null || s.Player == null || !s.Player.IsConnected) return;

        BasePlayer? target = FindBasePlayer(s.SelectedPlayerId);
        if (target == null || !target.IsConnected)
        {
            Toast(s.Player, "Give", "Target must be online.", _config.UI.Theme.Warning);
            return;
        }

        int givenAmount = Math.Max(1, amount);
        Item? created = ItemManager.Create(definition, givenAmount);
        if (created == null)
        {
            Toast(s.Player, "Give", "Item could not be created.", _config.UI.Theme.Danger);
            return;
        }

        // Quick-give is deliberately non-modal. Do not rebuild the item browser here:
        // admins must be able to press 1 / 100 / 1K / STACK repeatedly without the
        // originating CUI buttons being destroyed and recreated between clicks.
        target.GiveItem(created, BaseEntity.GiveItemReason.PickedUp);
        RecordRecentGive(s, definition);
        Audit(s.Player, $"Quick-gave {givenAmount} x {definition.displayName.english} to {TargetText(target)}");
        Toast(s.Player, "Give", string.Format(GetLang("Notice.Given", s.Player), givenAmount, definition.displayName.english, target.displayName), _config.UI.Theme.Success);
    }

    private static void RecordRecentGive(Session s, ItemDefinition definition)
    {
        s.RecentGiveItems.RemoveAll(x => string.Equals(x, definition.shortname, StringComparison.OrdinalIgnoreCase));
        s.RecentGiveItems.Insert(0, definition.shortname);
        if (s.RecentGiveItems.Count > 24)
            s.RecentGiveItems.RemoveRange(24, s.RecentGiveItems.Count - 24);
    }

    private void DrawGiveOverlay(Session s, ItemDefinition item, bool blueprint)
    {
        s.PendingItem = item;
        string amountCb = UiCallback(s.Player, "give.amount", arg => { if (int.TryParse(UiCallbackArgument(arg, 0), out int value)) s.GiveAmount = Math.Max(1, value); });
        string skinCb = UiCallback(s.Player, "give.skin", arg => { if (ulong.TryParse(UiCallbackArgument(arg, 0), out ulong value)) s.SkinId = value; });
        string cancel = UiActionCallback(s.Player, "give.cancel", () => CloseGiveOverlay(s, redraw: false));
        string give = UiActionCallback(s.Player, "give.go", () => GiveItem(s, item, blueprint));
        RogueUiDocument ui = CreateOverlayUi(); ui.Modal("rram.give.modal", Overlay, RogueUiRect.Centered(0.34f, 0.40f));
        ui.Title("rram.give.title", "rram.give.modal", Rect(0.07f, 0.82f, 0.93f, 0.94f), (blueprint ? "BLUEPRINT • " : "ITEM • ") + item.displayName.english, "MiddleCenter");
        ui.Label("rram.give.target.label", "rram.give.modal", Rect(0.08f, 0.68f, 0.92f, 0.76f), "Target: " + s.SelectedPlayerName, 11, _config.UI.Theme.MutedText, "MiddleCenter");
        ui.Input("rram.give.amount", "rram.give.modal", Rect(0.08f, 0.50f, 0.92f, 0.59f), s.GiveAmount.ToString(), amountCb, 12, _config.UI.Theme.Text, 8, false, "MiddleCenter");
        ui.Input("rram.give.skin", "rram.give.modal", Rect(0.08f, 0.37f, 0.92f, 0.46f), s.SkinId.ToString(), skinCb, 12, _config.UI.Theme.Text, 24, false, "MiddleCenter");
        ui.Button("rram.give.cancel", "rram.give.modal", Rect(0.08f, 0.09f, 0.43f, 0.20f), "CANCEL", cancel, _config.UI.Theme.SurfaceAlt);
        ui.Button("rram.give.go", "rram.give.modal", Rect(0.57f, 0.09f, 0.92f, 0.20f), "GIVE", give, _config.UI.Theme.Success);
        ShowUi(s.Player, ui, true);
    }

    private void GiveItem(Session s, ItemDefinition definition, bool blueprint)
    {
        BasePlayer? target = FindBasePlayer(s.SelectedPlayerId);
        if (target == null || !target.IsConnected) { Toast(s.Player, "Give", "Target must be online.", _config.UI.Theme.Warning); return; }
        Item? item = ItemManager.Create(definition, Math.Max(1, s.GiveAmount), s.SkinId);
        if (item == null) { Toast(s.Player, "Give", "Item could not be created.", _config.UI.Theme.Danger); return; }
        if (blueprint) item.blueprintTarget = definition.itemid;
        target.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
        RecordRecentGive(s, definition);
        int givenAmount = s.GiveAmount; ulong givenSkin = s.SkinId;
        Audit(s.Player, $"Gave {givenAmount} x {(blueprint ? "blueprint " : "")}{definition.displayName.english} to {TargetText(target)} skin={givenSkin}");
        s.GiveAmount = 1;
        s.SkinId = 0;
        CloseGiveOverlay(s, redraw: true);
        Toast(s.Player, "Give", string.Format(GetLang("Notice.Given", s.Player), givenAmount, definition.displayName.english, target.displayName), _config.UI.Theme.Success);
    }

    private void CloseGiveOverlay(Session s, bool redraw)
    {
        s.PendingItem = null;
        DestroyUi(s.Player, Overlay);
        if (redraw) Draw(s);

        // Carbon can finish processing the originating CUI callback after the first destroy.
        // Repeating the cleanup on the next tick prevents a completed Give modal from
        // remaining visually attached to the player's screen.
        NextTick(() =>
        {
            if (s.Player == null || !s.Player.IsConnected) return;
            DestroyUi(s.Player, Overlay);
            if (redraw) Draw(s);
        });
    }

    #endregion

    private string SafeGridReference(Vector3 position)
    {
        try
        {
            return GridReference(position);
        }
        catch (Exception ex)
        {
            LogWarning("World", $"Grid reference unavailable; using local fallback. {ex.GetType().Name}: {ex.Message}");
            return LocalGridReference(position);
        }
    }

    private static string LocalGridReference(Vector3 position)
    {
        const float defaultWorldSize = 4500f;
        const float gridCellSize = 146.3f;
        float worldSize = defaultWorldSize;

        try
        {
            Type serverType = Type.GetType("ConVar.Server, Assembly-CSharp", false);
            if (serverType != null)
            {
                object value = null;
                var field = serverType.GetField("worldsize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (field != null) value = field.GetValue(null);
                if (value == null)
                {
                    var property = serverType.GetProperty("worldsize", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (property != null) value = property.GetValue(null, null);
                }
                if (value != null)
                {
                    float candidate = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    if (candidate > 0f) worldSize = candidate;
                }
            }
        }
        catch { }

        float half = worldSize * 0.5f;
        int column = Math.Max(0, (int)Math.Floor((position.x + half) / gridCellSize));
        int row = Math.Max(0, (int)Math.Floor((half - position.z) / gridCellSize));
        return GridColumnName(column) + row.ToString(CultureInfo.InvariantCulture);
    }

    private static string GridColumnName(int index)
    {
        index++;
        string result = string.Empty;
        while (index > 0)
        {
            index--;
            result = (char)('A' + (index % 26)) + result;
            index /= 26;
        }
        return result;
    }

    // Prefer the ImageLibrary adapter for crisp cached PNG item artwork. This is deliberately
    // non-blocking: if ImageLibrary has no cached entry, Give immediately falls back to ItemId.
    private bool TryGetItemImage(ItemDefinition item, out string png)
    {
        png = string.Empty;
        if (!_config.UI.UseItemImages || item == null) return false;

        // ImageLibrary v2 consumer path: cache successful adapter resolutions and briefly
        // negative-cache misses. The DLL owns bounded download/dedupe/retry work; AdminMenu
        // avoids repeatedly asking the adapter for the same unresolved item every redraw.
        if (_itemImageCache.TryGetValue(item.itemid, out string cached) && !string.IsNullOrWhiteSpace(cached))
        {
            png = cached;
            return true;
        }
        if (_itemImageMissUntil.TryGetValue(item.itemid, out float retryAt) && Time.realtimeSinceStartup < retryAt)
            return false;

        try
        {
            string shortname = item.shortname ?? string.Empty;
            string? image = null;
            if ((!string.IsNullOrWhiteSpace(shortname) && Adapters.Images.TryGetImage(shortname, out image, 0) && !string.IsNullOrWhiteSpace(image)) ||
                (Adapters.Images.TryGetImage(item.itemid.ToString(CultureInfo.InvariantCulture), out image, 0) && !string.IsNullOrWhiteSpace(image)))
            {
                png = image!;
                _itemImageCache[item.itemid] = png;
                _itemImageMissUntil.Remove(item.itemid);
                return true;
            }
        }
        catch (Exception ex)
        {
            if (!_config.UI.UseNativeItemIcons)
                LogWarning("UI", "ImageLibrary v2 item lookup failed and native Give icons are disabled: " + ex.Message);
        }

        _itemImageMissUntil[item.itemid] = Time.realtimeSinceStartup + 8f;
        return false;
    }

    #region Shared helpers

    private void DrawSubTabs(RogueUiDocument ui, Session s, string[] names, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int index = i; float left = 0.055f + i * 0.155f;
            string cb = UiActionCallback(s.Player, "sub." + i, () => { s.SubMenu = index; s.ResetListState(); Draw(s); });
            ui.Tab("rram.sub." + i, "rram.body", Rect(left, 0.882f, left + 0.145f, 0.925f), names[i], cb, s.SubMenu == i);
        }
    }

    private bool CanUsePlayerAction(BasePlayer player, string label, string permissionName)
    {
        // Match the original Chaos AdminMenu behaviour: section permissions are optional,
        // but permission viewing always requires the permissions capability.
        if (string.Equals(label, "VIEW PERMISSIONS", StringComparison.OrdinalIgnoreCase))
            return HasAdminPermission(player, permissionName);
        return !_config.Permissions.UsePlayerAdminPermissions || HasAdminPermission(player, permissionName);
    }

    private string GetServerAdminBadge(string userId)
    {
        if (!ulong.TryParse(userId, out ulong uid)) return string.Empty;

        if (DeveloperList.Contains(uid))
            return "★ DEV";

        try
        {
            string group = ServerUsers.Get(uid)?.group.ToString() ?? string.Empty;
            if (string.Equals(group, "Owner", StringComparison.OrdinalIgnoreCase))
                return "★ OWNER";
            if (string.Equals(group, "Moderator", StringComparison.OrdinalIgnoreCase))
                return "★ MOD";
        }
        catch { }

        BasePlayer player = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
        return player != null && player.IsAdmin ? "★ ADMIN" : string.Empty;
    }

    private bool IsServerAdmin(BasePlayer player)
    {
        return player != null && player.IsAdmin;
    }

    private static string CanonicalAdminPermission(string perm)
    {
        if (string.IsNullOrWhiteSpace(perm)) return perm;
        const string legacyPrefix = "adminmenu.";
        return perm.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
            ? "roguerustadminmenu." + perm.Substring(legacyPrefix.Length)
            : perm;
    }

    private bool HasAdminPermission(BasePlayer player, string perm)
    {
        if (_config.Permissions.AutoAdminAccess && IsServerAdmin(player)) return true;

        string canonical = CanonicalAdminPermission(perm);
        if (base.HasPermission(player, canonical)) return true;

        // Preserve grants from pre-RogueRust permission names without registering
        // the legacy names (which causes Oxide prefix warnings).
        const string canonicalPrefix = "roguerustadminmenu.";
        if (canonical != null && canonical.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
            return base.HasPermission(player, "adminmenu." + canonical.Substring(canonicalPrefix.Length));

        return false;
    }

    private bool CanAccess(BasePlayer player, MenuType menu) => menu switch
    {
        MenuType.Dashboard => true,
        MenuType.Players => HasAdminPermission(player, PlayerPermission),
        MenuType.Teleport => HasAdminPermission(player, TeleportPermission),
        MenuType.Commands => HasAdminPermission(player, CommandsPermission),
        MenuType.Permissions => HasAdminPermission(player, PermissionPermission),
        MenuType.Groups => HasAdminPermission(player, GroupPermission),
        MenuType.Convars => HasAdminPermission(player, ConvarPermission),
        MenuType.Plugins => HasAdminPermission(player, PluginPermission),
        MenuType.Give => HasAdminPermission(player, GivePermission),
        MenuType.Vehicles => HasAdminPermission(player, VehiclePermission),
        MenuType.Diagnostics => HasAdminPermission(player, DiagnosticsPermission),
        _ => false
    };

    private void RegisterConfiguredPermissions()
    {
        if (_config.Commands.PlayerInfoCommands == null) return;

        for (int groupIndex = 0; groupIndex < _config.Commands.PlayerInfoCommands.Count; groupIndex++)
        {
            List<PlayerInfoCommandEntry> commands = _config.Commands.PlayerInfoCommands[groupIndex]?.Commands;
            if (commands == null) continue;

            for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
            {
                string perm = CanonicalAdminPermission(commands[commandIndex]?.RequiredPermission);
                if (string.IsNullOrWhiteSpace(perm) ||
                    !perm.StartsWith("roguerustadminmenu.", StringComparison.OrdinalIgnoreCase) ||
                    permission.PermissionExists(perm, this))
                    continue;

                permission.RegisterPermission(perm, this);
            }
        }
    }

    private void AutoGrantAdminPermissions()
    {
        if (!_config.Permissions.AutoGrantAdminGroupPermissions) return;

        const string adminGroup = "admin";
        if (!permission.GroupExists(adminGroup))
        {
            LogWarning("Permissions", "Oxide admin group was not found; automatic AdminMenu permission grants were skipped.");
            return;
        }

        int granted = 0;
        foreach (string perm in CorePermissions)
        {
            if (permission.GroupHasPermission(adminGroup, perm)) continue;
            permission.GrantGroupPermission(adminGroup, perm, this);
            granted++;
        }

        // Also grant custom AdminMenu permissions owned by this plugin.
        if (_config.Commands.PlayerInfoCommands != null)
        {
            for (int groupIndex = 0; groupIndex < _config.Commands.PlayerInfoCommands.Count; groupIndex++)
            {
                List<PlayerInfoCommandEntry> commands = _config.Commands.PlayerInfoCommands[groupIndex]?.Commands;
                if (commands == null) continue;

                for (int commandIndex = 0; commandIndex < commands.Count; commandIndex++)
                {
                    string perm = CanonicalAdminPermission(commands[commandIndex]?.RequiredPermission);
                    if (string.IsNullOrWhiteSpace(perm) ||
                        !perm.StartsWith("roguerustadminmenu.", StringComparison.OrdinalIgnoreCase) ||
                        permission.GroupHasPermission(adminGroup, perm))
                        continue;

                    permission.GrantGroupPermission(adminGroup, perm, this);
                    granted++;
                }
            }
        }

        if (granted > 0)
            LogInformation("Permissions", $"Granted {granted} AdminMenu permission(s) to the Oxide admin group.");
    }

    private IEnumerable<T> Filter<T>(IEnumerable<T> source, Session s, Func<T, string> selector)
    {
        IEnumerable<T> result = source;
        if (!string.IsNullOrWhiteSpace(s.Search)) result = result.Where(x => (selector(x) ?? string.Empty).IndexOf(s.Search, StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrEmpty(s.Character) && s.Character != "~") result = result.Where(x => (selector(x) ?? string.Empty).StartsWith(s.Character, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private List<T> Page<T>(List<T> source, int page) => source.Skip(Math.Max(0, page) * _config.UI.PageSize).Take(_config.UI.PageSize).ToList();

    private List<RecentPlayer> GetVisiblePlayers(Session s, bool forceBoth = false)
    {
        foreach (BasePlayer player in BasePlayer.activePlayerList) RememberPlayer(player, true);
        IEnumerable<RecentPlayer> players = _recentPlayers.Values;
        if (!forceBoth)
        {
            players = players.Where(x => (s.ShowOnline && IsOnline(x.Id)) || (s.ShowOffline && !IsOnline(x.Id)));
        }
        players = Filter(players, s, x => x.Name + " " + x.Id);
        return players.OrderByDescending(x => IsOnline(x.Id)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private bool IsOnline(string id) => ulong.TryParse(id, out ulong uid) && BasePlayer.FindByID(uid) != null;

    private BasePlayer? FindBasePlayer(string id)
    {
        if (!ulong.TryParse(id, out ulong uid)) return null;
        return BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
    }

    private bool PluginRequirementMet(string pluginName) => string.IsNullOrWhiteSpace(pluginName) || plugins.Exists(pluginName);
    private bool PermissionRequirementMet(BasePlayer player, string perm) => string.IsNullOrWhiteSpace(perm) || HasAdminPermission(player, perm);
    private static string StripName(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var builder = new System.Text.StringBuilder(name.Length);
        bool insideTag = false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '<')
            {
                insideTag = true;
                continue;
            }

            if (c == '>' && insideTag)
            {
                insideTag = false;
                continue;
            }

            if (!insideTag) builder.Append(c);
        }

        string value = builder.ToString();
        return string.IsNullOrWhiteSpace(value) ? name : value;
    }
    private static string Quote(string text) => "\"" + (text ?? string.Empty).Replace("\"", "") + "\"";
    private static string TargetText(BasePlayer target) => target.displayName + " (" + target.userID + ")";
    private static bool TrySeconds(object? raw, out double value)
    {
        value = 0d;
        if (raw == null) return false;
        try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return true; } catch { return false; }
    }
    private static string FormatDuration(double seconds)
    {
        TimeSpan span = TimeSpan.FromSeconds(Math.Max(0d, seconds));
        int hours = span.Hours + span.Days * 24;
        return hours > 0 ? $"{hours:00}h {span.Minutes:00}m {span.Seconds:00}s" : span.Minutes > 0 ? $"{span.Minutes:00}m {span.Seconds:00}s" : $"{span.Seconds:00}s";
    }
    private static RogueUiRect Rect(float minX, float minY, float maxX, float maxY) => new(minX.ToString("0.###", CultureInfo.InvariantCulture) + " " + minY.ToString("0.###", CultureInfo.InvariantCulture), maxX.ToString("0.###", CultureInfo.InvariantCulture) + " " + maxY.ToString("0.###", CultureInfo.InvariantCulture));

    private void Toast(BasePlayer player, string title, string message, string color)
        => Toast(player, title, message, color, 1.6);

    private void Toast(BasePlayer player, string title, string message, string color, double durationSeconds)
    {
        if (player == null || !player.IsConnected) return;

        // Notifications are visual-only. Build them directly as non-raycasting CUI instead
        // of using a full-screen RogueUI popup document, so rapid admin actions remain clickable.
        DestroyUi(player, Popup);
        CuiElementContainer toast = new CuiElementContainer();
        toast.Add(new CuiElement
        {
            Name = Popup,
            Parent = "Overlay",
            Components =
            {
                new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
            }
        });
        toast.Add(new CuiElement
        {
            Name = "rram.toast",
            Parent = Popup,
            Components =
            {
                new CuiImageComponent { Color = color, BlocksRaycast = false },
                new CuiRectTransformComponent { AnchorMin = "0.655 0.035", AnchorMax = "0.965 0.095" }
            }
        });
        toast.Add(new CuiElement
        {
            Name = "rram.toast.title",
            Parent = "rram.toast",
            Components =
            {
                new CuiTextComponent { Text = title.ToUpperInvariant(), FontSize = 10, Color = _config.UI.Theme.Text, Align = TextAnchor.MiddleLeft },
                new CuiRectTransformComponent { AnchorMin = "0.035 0.52", AnchorMax = "0.965 0.90" }
            }
        });
        toast.Add(new CuiElement
        {
            Name = "rram.toast.message",
            Parent = "rram.toast",
            Components =
            {
                new CuiTextComponent { Text = message, FontSize = 9, Color = _config.UI.Theme.Text, Align = TextAnchor.MiddleLeft },
                new CuiRectTransformComponent { AnchorMin = "0.035 0.10", AnchorMax = "0.965 0.56" }
            }
        });
        CuiHelper.AddUi(player, toast);

        double lifetime = Math.Max(0.5, Math.Min(30.0, durationSeconds));
        string delayKey = "toast-close-" + player.userID;
        Delay(TimeSpan.FromSeconds(lifetime), () =>
        {
            if (player != null && player.IsConnected)
                DestroyUi(player, Popup);
        }, delayKey);
    }

    private void Confirm(Session s, string title, string message, Action action)
    {
        string yes = UiActionCallback(s.Player, "confirm.yes", () => { DestroyUi(s.Player, Overlay); action(); }, true);
        RogueUiDocument ui = CreateOverlayUi();
        ui.ConfirmationModal("rram.confirm", Overlay, title, message, yes, "roguerust.ui.close " + Overlay, "CONFIRM", "CANCEL");
        ShowUi(s.Player, ui, true);
    }

    private void QueueAuditSave()
    {
        if (_auditSaveQueued) return;
        _auditSaveQueued = true;
        Delay(TimeSpan.FromSeconds(2), () =>
        {
            _auditSaveQueued = false;
            SaveData(AuditHistoryDataKey, _auditHistory);
        }, "adminmenu-audit-save");
    }

    private async void Audit(BasePlayer player, string message)
    {
        LogInformation("AdminMenu", player.displayName + " (" + player.userID + "): " + message);
        _auditHistory.Add(new AuditEntry { Utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), AdminId = player.UserIDString, AdminName = StripName(player.displayName), Message = message });
        while (_auditHistory.Count > _config.Data.MaxAuditEntries) _auditHistory.RemoveAt(0);
        QueueAuditSave();
        if (string.IsNullOrWhiteSpace(_config.Integrations.LogWebhook)) return;
        try
        {
            string content = $"**RogueRust Admin Menu**\n**{StripName(player.displayName)}** (`{player.userID}`)\n{message}\n<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:f>";
            await SendDiscordWebhookAsync(_config.Integrations.LogWebhook, content, "RogueRust Admin Menu");
        }
        catch (Exception ex) { LogWarning("AdminMenu", "Discord audit failed: " + ex.Message); }
    }

    #endregion
}