using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using Oxide.Game.Rust.Cui;
namespace Oxide.Plugins
{
    [Info("RogueRustServerInfo", "RogueAssassin", "1.1.3")]
    [Description("In-game server information and command reference panel. Players see player commands; admins also see the admin list.")]
    public sealed class RogueRustServerInfo : RogueRustPlugin
    {
        [RoguePermission]
        private const string AdminPermission = "roguerustserverinfo.admin";
        private const string PluginVersion = "1.1.3";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(1, 1, 3);

        // The panel is three documents so that only the part that changed is resent:
        // the shell is drawn once when the panel opens, the navigation on a tab change,
        // and the content on a tab or page change.
        private const string UiRoot = "RogueRustServerInfo.Main";
        private const string UiNav = "RogueRustServerInfo.Nav";
        private const string UiContent = "RogueRustServerInfo.Content";
        private const string UiCursor = "RogueRustServerInfo.Cursor";
        private const string NavHost = "rrsi.nav";
        private const string BodyHost = "rrsi.body";
        private const string Transparent = "0 0 0 0";

        private const string AudienceDefault = "Default";
        private const string AudienceAdmin = "Admin";
        private const int TabInfo = 0;
        private const int TabCommands = 1;
        private const int TabAdmin = 2;
        private const int MaximumInfoLines = 16;

        private Configuration _config;
        private readonly Dictionary<ulong, ViewState> _views = new Dictionary<ulong, ViewState>();
        private readonly List<CommandEntry> _defaultCommands = new List<CommandEntry>();
        private readonly List<CommandEntry> _adminCommands = new List<CommandEntry>();
        private readonly HashSet<string> _listedCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<ulong> _tipShown = new HashSet<ulong>();
        private readonly HashSet<ulong> _tipVisible = new HashSet<ulong>();

        #region Configuration

        private sealed class Configuration
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("Game Tip", Order = 15)]
            public GameTipSettings GameTip = new GameTipSettings();

            [JsonProperty("Theme (Rust CUI colours: red green blue alpha, each 0-1)", Order = 18)]
            public ThemeSettings Theme = new ThemeSettings();

            [JsonProperty("Information Lines (start a line with # for a heading)", Order = 20)]
            public List<string> InfoLines = DefaultInfoLines();

            [JsonProperty("Commands", Order = 30)]
            public List<CommandEntry> Commands = DefaultCommands();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;
        }

        private sealed class GeneralSettings
        {
            [JsonProperty("Panel Title")]
            public string Title = "{server.name}";

            [JsonProperty("Panel Subtitle")]
            public string Subtitle = "SERVER INFORMATION  •  COMMANDS";

            [JsonProperty("Panel Footer")]
            public string Footer = "Type /info at any time to open this panel.";

            [JsonProperty("Commands Per Page")]
            public int CommandsPerPage = 10;

            [JsonProperty("Treat Auth Level 2 (Owner) As Admin")]
            public bool AuthLevelIsAdmin = true;

            [JsonProperty("Hide Player Commands The Player Lacks Permission For")]
            public bool HideWithoutPermission = true;

            [JsonProperty("Add Newly Registered RogueRust Commands To The Commands List Automatically")]
            public bool AutoAddCommands = true;

            [JsonProperty("Open Panel When A Player Connects")]
            public bool OpenOnConnect = false;
        }

        private sealed class GameTipSettings
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("Message")]
            public string Message = "Type <color=#C4FF00>/info</color> to open the server menu and see what {server.name} offers.";

            [JsonProperty("Delay After Waking Up (Seconds)")]
            public float DelaySeconds = 4f;

            [JsonProperty("Display Seconds")]
            public float DisplaySeconds = 12f;

            [JsonProperty("Show Again Every (Minutes, 0 = Once Per Connection)")]
            public float RepeatMinutes = 0f;
        }

        // Defaults match the RogueRust administration menu.
        private sealed class ThemeSettings
        {
            [JsonProperty("World Dimming Behind The Panel (0-0.6)")]
            public float WorldDimming = 0.25f;

            [JsonProperty("Window")] public string Window = "0.008 0.020 0.028 0.94";
            [JsonProperty("Header")] public string Header = "0.012 0.035 0.045 0.97";
            [JsonProperty("Navigation")] public string Navigation = "0.012 0.030 0.040 0.94";
            [JsonProperty("Body")] public string Body = "0.010 0.027 0.035 0.88";
            [JsonProperty("Footer")] public string Footer = "0.012 0.027 0.034 0.95";
            [JsonProperty("Surface")] public string Surface = "0.018 0.045 0.056 0.96";
            [JsonProperty("Surface Alternate")] public string SurfaceAlt = "0.025 0.060 0.072 0.98";
            [JsonProperty("Accent")] public string Accent = "0.10 0.68 0.82 1";
            [JsonProperty("Accent Muted")] public string AccentMuted = "0.035 0.095 0.115 0.96";
            [JsonProperty("Brand")] public string Brand = "0.95 0.42 0.12 1";
            [JsonProperty("Danger")] public string Danger = "0.70 0.15 0.13 1";
            [JsonProperty("Text")] public string Text = "0.92 0.95 0.96 1";
            [JsonProperty("Muted Text")] public string MutedText = "0.58 0.68 0.72 1";
            [JsonProperty("Subtitle Text")] public string SubtitleText = "0.50 0.78 0.83 1";
        }

        private sealed class CommandEntry
        {
            [JsonProperty("Command")]
            public string Command = string.Empty;

            [JsonProperty("Description")]
            public string Description = string.Empty;

            [JsonProperty("Usage")]
            public string Usage = string.Empty;

            [JsonProperty("Category")]
            public string Category = "General";

            [JsonProperty("Audience (Default or Admin)")]
            public string Audience = AudienceDefault;

            [JsonProperty("Required Permission (optional)")]
            public string Permission = string.Empty;

            [JsonProperty("Plugin (optional, the entry is hidden while this plugin is not loaded)")]
            public string Plugin = string.Empty;

            [JsonProperty("Show In Panel")]
            public bool Show = true;
        }

        private static CommandEntry Entry(string plugin, string audience, string category, string command, string usage, string description, string permission = "", bool show = true)
        {
            return new CommandEntry
            {
                Command = command,
                Description = description,
                Usage = usage,
                Category = category,
                Audience = audience,
                Permission = permission,
                Plugin = plugin,
                Show = show
            };
        }

        private static List<string> DefaultInfoLines()
        {
            return new List<string>
            {
                "# Welcome, {player.name}",
                "You are playing on {server.name}.",
                "Players online: {players.online} / {players.max}",
                "",
                "# Getting started",
                "Open the Commands tab to see every command available to you.",
                "Type /info at any time to open this panel again.",
                "",
                "# Rules",
                "Edit these lines in oxide/config/RogueRustServerInfo.json."
            };
        }

        private static List<CommandEntry> DefaultCommands()
        {
            const string D = AudienceDefault;
            const string A = AudienceAdmin;
            const string Info = "RogueRustServerInfo";
            const string Restart = "RogueRustServerRestarter";
            const string Messages = "RogueRustCustomPlayerMessages";
            const string Notes = "RogueRustDeathNotes";
            const string Removal = "RogueRustRemovalTool";
            const string Furnace = "RogueRustFurnaceSplitter";
            const string Skins = "RogueRustSkins";
            const string Menu = "RogueRustAdminMenu";
            const string Blueprints = "RogueRustBlueprintManager";
            const string Crafting = "RogueRustCraftingController";
            const string DayNight = "RogueRustDayNightScheduler";
            const string Spawns = "RogueRustRandoSpawns";
            const string Events = "RogueRustEventDirector";
            const string Fuel = "RogueRustFuelStackOptimizer";
            const string Grid = "RogueRustGridPower";
            const string Turrets = "RogueRustTurretLimitOverride";
            const string Crash = "RogueRustHeliCrash";
            const string Building = "RogueRustBuildingManager";
            return new List<CommandEntry>
            {
                // Player commands
                Entry(Info, D, "Server", "info", "/info", "Opens this server information panel."),
                Entry(Info, D, "Server", "commands", "/commands", "Opens this panel on the command list."),
                Entry(Restart, D, "Server", "serverrestart", "/serverrestart", "Shows the next scheduled server restart.", "roguerustserverrestarter.use"),
                Entry(Crash, D, "Events", "helicrash", "/helicrash", "Shows the active helicopter crash site, how far it is and how long it has left."),
                Entry(Messages, D, "Players", "online", "/online", "Shows the number of online players."),
                Entry(Messages, D, "Players", "players", "/players", "Lists online players."),
                Entry(Notes, D, "Combat", "dn", "/dn [on|off|toggle|teamonly|teamoff|meters|feet|yards]", "Controls Death Notes visibility, team mode and distance units."),
                Entry(Removal, D, "Building", "remove", "/remove [seconds|off]", "Enables or disables the removal tool.", "roguerustremovaltool.normal"),
                Entry(Building, D, "Building", "bm", "/bm", "Opens the building manager: target grade, auto grade, hammer grading, skins and wallpaper.", "roguerustbuildingmanager.use"),
                Entry(Building, D, "Building", "up", "/up [wood|stone|metal|hqm]", "Upgrades the whole building you are looking at or standing in.", "roguerustbuildingmanager.bulk"),
                Entry(Building, D, "Building", "down", "/down [twig|wood|stone|metal]", "Downgrades the whole building you are looking at or standing in.", "roguerustbuildingmanager.bulk"),
                Entry(Furnace, D, "Furnace", "fs", "/fs [on|off]", "Shows or toggles Furnace Splitter for you.", "roguerustfurnacesplitter.use"),
                Entry(Skins, D, "Skins", "skin", "/skin", "Opens the skin browser.", "roguerustskins.use"),
                Entry(Skins, D, "Skins", "skins", "/skins", "Opens the skin menu.", "roguerustskins.use"),
                Entry(Skins, D, "Skins", "skinitem", "/skinitem", "Skins the item you are holding or looking at.", "roguerustskins.items"),
                Entry(Skins, D, "Skins", "skincraft", "/skincraft", "Toggles applying your skin set to crafted items.", "roguerustskins.craft"),
                Entry(Skins, D, "Skins", "skininv", "/skininv", "Applies your skin set to your inventory.", "roguerustskins.inventory"),
                Entry(Skins, D, "Skins", "skincon", "/skincon", "Applies your skin set to the container you are looting.", "roguerustskins.container"),
                Entry(Skins, D, "Skins", "skinset", "/skinset", "Selects or edits one of your skin sets.", "roguerustskins.use"),
                Entry(Skins, D, "Skins", "skinsets", "/skinsets", "Lists your skin sets.", "roguerustskins.use"),
                Entry(Skins, D, "Skins", "skinsetname", "/skinsetname <set> <name>", "Renames one of your skin sets.", "roguerustskins.use"),
                Entry(Skins, D, "Skins", "skinauto", "/skinauto", "Toggles automatic skinning of items you pick up.", "roguerustskins.auto"),
                Entry(Skins, D, "Skins", "skinteam", "/skinteam", "Shares your skin set with your team.", "roguerustskins.team"),
                Entry(Skins, D, "Skins", "skinbase", "/skinbase", "Applies your skin set to your base.", "roguerustskins.base"),
                Entry(Skins, D, "Skins", "skinall", "/skinall", "Applies your skin set to everything you own nearby.", "roguerustskins.all"),
                Entry(Skins, D, "Skins", "skinsearch", "/skinsearch <text>", "Searches the skin catalogue."),
                Entry(Skins, D, "Skins", "skinrecent", "/skinrecent", "Shows your recently used skins."),
                Entry(Skins, D, "Skins", "skinfav", "/skinfav", "Shows or edits your favourite skins."),
                Entry(Skins, D, "Skins", "skinrequest", "/skinrequest <workshopID>", "Requests a workshop skin to be added.", "roguerustskins.request"),
                Entry(Skins, D, "Skins", "skincollection", "/skincollection <collectionID>", "Imports a workshop collection.", "roguerustskins.collection"),

                // Console plumbing used by the Furnace Splitter panel. Listed hidden so it is not added automatically.
                Entry(Furnace, D, "Furnace", "furnacesplitter.enabled", "furnacesplitter.enabled [true|false]", "Gets or sets Furnace Splitter for you.", "", false),
                Entry(Furnace, D, "Furnace", "furnacesplitter.totalstacks", "furnacesplitter.totalstacks [count]", "Gets or sets input stacks for the furnace you are looting.", "", false),
                Entry(Furnace, D, "Furnace", "furnacesplitter.trim", "furnacesplitter.trim", "Returns excess fuel from the furnace you are looting.", "", false),

                // Admin commands
                Entry(Menu, A, "Administration", "admin", "/admin", "Opens the RogueRust administration menu."),
                Entry(Menu, A, "Administration", "vanish", "/vanish", "Toggles admin vanish."),
                Entry(Removal, A, "Building", "remove", "/remove [admin|structure|all|external]", "Removal tool admin modes."),
                Entry(Blueprints, A, "Blueprints", "bpreset", "/bpreset <player>", "Resets a player's blueprints."),
                Entry(Blueprints, A, "Blueprints", "bpunlockall", "/bpunlockall <player>", "Unlocks all available blueprints for a player."),
                Entry(Blueprints, A, "Blueprints", "bpunlock", "/bpunlock <player> <item.shortname> [...]", "Unlocks one or more blueprints for a player."),
                Entry(Blueprints, A, "Blueprints", "bpremove", "/bpremove <player> <item.shortname> [...]", "Removes unlocked blueprints from a player."),
                Entry(Blueprints, A, "Blueprints", "bpwipeall", "/bpwipeall", "Resets blueprints for all known players."),
                Entry(Crafting, A, "Crafting", "craftrate", "/craftrate [multiplier]", "View or set the global crafting rate."),
                Entry(Crafting, A, "Crafting", "crafttime", "/crafttime <item> [seconds|default]", "View or set an item's craft time."),
                Entry(Crafting, A, "Crafting", "blockitem", "/blockitem <item>", "Blocks an item from crafting and research."),
                Entry(Crafting, A, "Crafting", "unblockitem", "/unblockitem <item>", "Unblocks an item for crafting and research."),
                Entry(Crafting, A, "Crafting", "benchlvl", "/benchlvl <item> <0-3>", "Sets an item's workbench requirement."),
                Entry(Crafting, A, "Crafting", "setcraftskin", "/setcraftskin <item> <skinId>", "Sets the default crafting skin for an item."),
                Entry(DayNight, A, "World", "tod", "/tod [0-24|HH:MM|freeze|unfreeze]", "Shows or sets world time and freezes or unfreezes it."),
                Entry(DayNight, A, "World", "tod.freeze", "/tod.freeze", "Freezes world time at the current hour."),
                Entry(DayNight, A, "World", "tod.unfreeze", "/tod.unfreeze", "Resumes normal world time progression."),
                Entry(DayNight, A, "World", "daynight.daylength", "/daynight.daylength <1-1440>", "Sets the real-time day duration in minutes."),
                Entry(DayNight, A, "World", "daynight.nightlength", "/daynight.nightlength <1-1440>", "Sets the real-time night duration in minutes."),
                Entry(Spawns, A, "World", "showspawns", "/showspawns", "Displays generated random spawn points for 30 seconds.", "roguerustrandospawns.admin"),
                Entry(Spawns, A, "World", "randospawns.regenerate", "/randospawns.regenerate", "Regenerates random spawn points.", "roguerustrandospawns.admin"),
                Entry(Events, A, "Events", "rred.status", "/rred.status", "Shows event tracking and next-run status.", "roguerusteventdirector.admin"),
                Entry(Events, A, "Events", "rred.next", "/rred.next <plane|patrol|bradley|chinook|ship>", "Shows when an event is next scheduled.", "roguerusteventdirector.admin"),
                Entry(Events, A, "Events", "rred.spawn", "/rred.spawn <plane|patrol|bradley|chinook|ship>", "Immediately spawns a managed world event.", "roguerusteventdirector.admin"),
                Entry(Events, A, "Events", "rred.trigger", "/rred.trigger <plane|patrol|bradley|chinook|ship>", "Immediately triggers a managed world event.", "roguerusteventdirector.admin"),
                Entry(Events, A, "Events", "rred.delay", "/rred.delay <event> <minutes>", "Delays the next managed event.", "roguerusteventdirector.admin"),
                Entry(Events, A, "Events", "rred.reschedule", "/rred.reschedule [event|all]", "Generates a fresh timer for one or all managed events.", "roguerusteventdirector.admin"),
                Entry(Crash, A, "Events", "helicrash", "/helicrash start | here | at <x> <z> | stop | next [minutes]", "Starts, places, stops or reschedules the helicopter crash event.", "roguerusthelicrash.admin"),
                Entry(Fuel, A, "Fuel", "fuelstack.status", "/fuelstack.status", "Shows Fuel Stack Optimizer runtime status.", "roguerustfuelstackoptimizer.admin"),
                Entry(Fuel, A, "Fuel", "fuelstack.audit", "/fuelstack.audit", "Creates the grouped fuel-container audit.", "roguerustfuelstackoptimizer.admin"),
                Entry(Fuel, A, "Fuel", "fuelstack.fuels", "/fuelstack.fuels", "Lists recognised fuel item short names.", "roguerustfuelstackoptimizer.admin"),
                Entry(Fuel, A, "Fuel", "fuelstack.rescan", "/fuelstack.rescan", "Starts a background fuel-container rescan."),
                Entry(Fuel, A, "Fuel", "fuelstackaudit", "/fuelstackaudit", "Writes the grouped fuel audit to your F1 console.", "roguerustfuelstackoptimizer.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.help", "/rrgrid.help", "Shows GridPower administration and diagnostic commands.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.status", "/rrgrid.status", "Shows streetlight and native grid status.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.apply", "/rrgrid.apply", "Reapplies GridPower configuration.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.refresh", "/rrgrid.refresh", "Rebuilds streetlight references and reapplies state.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.grid", "/rrgrid.grid", "Shows the public-grid state and your grid cell.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.native", "/rrgrid.native", "Shows native power-grid access points and output policy.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.native.refresh", "/rrgrid.native.refresh", "Re-indexes native access points.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.native.inspect", "/rrgrid.native.inspect", "Inspects the nearest native access point.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.native.poles", "/rrgrid.native.poles [apply]", "Shows or re-runs pole transformer fitting.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.debug", "/rrgrid.debug", "Draws temporary markers for access points and streetlights.", "roguerustgridpower.admin"),
                Entry(Grid, A, "GridPower", "rrgrid.target", "/rrgrid.target", "Inspects the world object under your cursor.", "roguerustgridpower.admin"),
                Entry(Restart, A, "Server", "serverrestart.reload", "/serverrestart.reload", "Reloads restart configuration and reschedules.", "roguerustserverrestarter.admin"),
                Entry(Restart, A, "Server", "serverrestart.test", "/serverrestart.test", "Runs a 30-second restart countdown test.", "roguerustserverrestarter.admin"),
                Entry(Turrets, A, "Server", "turretlimit", "/turretlimit status | enabled <true|false> | radius <n> | max <n>", "Shows or changes turret interference limits."),
                Entry(Skins, A, "Skins", "skinrequests", "/skinrequests", "Reviews pending skin requests.", "roguerustskins.admin"),
                Entry(Skins, A, "Skins", "skinimport", "/skinimport <workshopID>", "Imports a workshop skin.", "roguerustskins.import"),
                Entry(Skins, A, "Skins", "skinremove", "/skinremove <skinID>", "Removes a skin from the catalogue.", "roguerustskins.import")
            };
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
                // Keep the file that could not be read. The defaults are saved over the config below, and
                // without this copy one typo would cost the owner every setting they had made.
                try { System.IO.File.Copy(Config.Filename, Config.Filename + ".invalid", true); } catch { }
                LogError("Configuration", "Invalid configuration: " + exception.Message);
                _config = new Configuration();
            }

            if (_config == null) _config = new Configuration();
            if (_config.General == null) _config.General = new GeneralSettings();
            if (_config.GameTip == null) _config.GameTip = new GameTipSettings();
            if (_config.Theme == null) _config.Theme = new ThemeSettings();
            _config.Theme.WorldDimming = Math.Max(0f, Math.Min(_config.Theme.WorldDimming, 0.6f));
            _config.GameTip.DelaySeconds = Math.Max(0f, Math.Min(_config.GameTip.DelaySeconds, 120f));
            _config.GameTip.DisplaySeconds = Math.Max(2f, Math.Min(_config.GameTip.DisplaySeconds, 60f));
            _config.GameTip.RepeatMinutes = Math.Max(0f, _config.GameTip.RepeatMinutes);
            if (_config.InfoLines == null) _config.InfoLines = DefaultInfoLines();
            if (_config.Commands == null) _config.Commands = DefaultCommands();
            _config.General.CommandsPerPage = Math.Max(4, Math.Min(_config.General.CommandsPerPage, 14));
            if (_config.Version < new VersionNumber(1, 1, 0)) UpgradeTo110();
            if (_config.Version < CurrentVersion)
            {
                AddNewDefaultCommands();
                _config.Version = CurrentVersion;
            }

            SaveConfig();
            BuildCommandIndex();
        }

        // 1.1.0: the panel opens with /info instead of /help, and entries know which plugin they belong to.
        private void UpgradeTo110()
        {
            if (_config.GameTip.Message != null)
                _config.GameTip.Message = _config.GameTip.Message.Replace("/help", "/info");

            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (CommandEntry entry in DefaultCommands())
                owners[NormalizeCommand(entry.Command)] = entry.Plugin;

            foreach (CommandEntry entry in _config.Commands)
            {
                string owner;
                if (entry != null && string.IsNullOrWhiteSpace(entry.Plugin) &&
                    owners.TryGetValue(NormalizeCommand(entry.Command), out owner))
                    entry.Plugin = owner;
            }
        }

        // Runs once per plugin update: appends the defaults this config does not have yet. An entry an
        // owner deleted by hand comes back here; set "Show In Panel" to false to keep one out for good.
        private void AddNewDefaultCommands()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CommandEntry entry in _config.Commands)
                if (entry != null) existing.Add(EntryKey(entry));

            foreach (CommandEntry entry in DefaultCommands())
                if (existing.Add(EntryKey(entry))) _config.Commands.Add(entry);
        }

        private static string EntryKey(CommandEntry entry)
        {
            bool admin = string.Equals(entry.Audience, AudienceAdmin, StringComparison.OrdinalIgnoreCase);
            return (admin ? "admin:" : "default:") + NormalizeCommand(entry.Command);
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void BuildCommandIndex()
        {
            _defaultCommands.Clear();
            _adminCommands.Clear();
            _listedCommands.Clear();

            foreach (CommandEntry entry in _config.Commands)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Command)) continue;
                entry.Command = NormalizeCommand(entry.Command);
                entry.Description = entry.Description ?? string.Empty;
                entry.Usage = entry.Usage ?? string.Empty;
                entry.Category = string.IsNullOrWhiteSpace(entry.Category) ? "General" : entry.Category.Trim();
                entry.Permission = string.IsNullOrWhiteSpace(entry.Permission) ? string.Empty : entry.Permission.Trim();
                entry.Plugin = string.IsNullOrWhiteSpace(entry.Plugin) ? string.Empty : entry.Plugin.Trim();

                // Hidden entries still count as listed, so a command an owner switched off is not added again.
                _listedCommands.Add(entry.Command);
                if (!entry.Show || !IsPluginLoaded(entry.Plugin)) continue;

                if (string.Equals(entry.Audience, AudienceAdmin, StringComparison.OrdinalIgnoreCase)) _adminCommands.Add(entry);
                else _defaultCommands.Add(entry);
            }

            _defaultCommands.Sort(CompareEntries);
            _adminCommands.Sort(CompareEntries);
        }

        private bool IsPluginLoaded(string pluginName)
        {
            // The plugin manager is not available yet while the config is first read; the list is
            // rebuilt in OnServerInitialized.
            if (pluginName.Length == 0 || plugins == null) return true;
            Plugin plugin = plugins.Find(pluginName);
            return plugin != null && plugin.IsLoaded;
        }

        // Commands registered through RogueRust describe themselves, so they are written into the
        // config where an owner can edit, re-file or hide them. A command only goes on the player list
        // when RogueRust enforces a non-admin permission for it; without one its handler may still
        // restrict access internally, so it starts on the admin list until an owner moves it.
        private bool AddRegisteredCommands()
        {
            bool added = false;
            foreach (RogueCommandDescriptor descriptor in Rogue.Commands.GetHelp(null, RogueCommandSource.Chat))
            {
                bool listed = false;
                foreach (string name in descriptor.Names)
                {
                    if (!_listedCommands.Contains(NormalizeCommand(name))) continue;
                    listed = true;
                    break;
                }
                if (listed) continue;

                RogueCommandAttribute metadata = descriptor.Metadata;
                string command = NormalizeCommand(descriptor.Name);
                string required = (metadata.Permission ?? string.Empty).Trim();
                bool adminOnly = metadata.AdminOnly || required.Length == 0 ||
                                 required.IndexOf("admin", StringComparison.OrdinalIgnoreCase) >= 0;

                _config.Commands.Add(new CommandEntry
                {
                    Command = command,
                    Description = metadata.Description ?? string.Empty,
                    Usage = string.IsNullOrWhiteSpace(metadata.Usage) ? "/" + command : metadata.Usage,
                    Category = string.IsNullOrWhiteSpace(metadata.Category) ? "General" : metadata.Category,
                    Audience = adminOnly ? AudienceAdmin : AudienceDefault,
                    Permission = required,
                    Plugin = descriptor.OwnerName ?? string.Empty
                });
                _listedCommands.Add(command);
                added = true;
            }
            return added;
        }

        private void RefreshCommands()
        {
            if (_config.General.AutoAddCommands && AddRegisteredCommands()) SaveConfig();
            BuildCommandIndex();
        }

        private static int CompareEntries(CommandEntry left, CommandEntry right)
        {
            int category = string.Compare(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);
            return category != 0 ? category : string.Compare(left.Command, right.Command, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCommand(string command)
        {
            return (command ?? string.Empty).Trim().TrimStart('/');
        }

        #endregion

        #region Lifecycle

        private sealed class ViewState
        {
            public bool Open;
            public int Tab = TabInfo;
            public int Page;
        }

        private void Init()
        {
            LogInformation("Lifecycle", Name + " " + PluginVersion + " initialized.");
        }

        private void OnServerInitialized()
        {
            RefreshCommands();
            LogInformation("Commands",
                "Listing " + _defaultCommands.Count + " player and " + _adminCommands.Count + " admin commands for the loaded plugins.");

            bool tips = _config.GameTip.Enabled && !string.IsNullOrWhiteSpace(_config.GameTip.Message);
            // The wake-up hook fires on every respawn; only stay subscribed while tips are in use.
            if (!tips) Unsubscribe(nameof(OnPlayerSleepEnded));
            else if (_config.GameTip.RepeatMinutes > 0f)
                Repeat(TimeSpan.FromMinutes(_config.GameTip.RepeatMinutes), ShowGameTipToAll, "serverinfo-gametip");
        }

        // Which commands exist follows which plugins are loaded. Loads arrive in bursts at startup
        // and on reloads, so the list is rebuilt once things settle.
        private void OnPluginLoaded(Plugin plugin) => QueueCommandRefresh();

        private void OnPluginUnloaded(Plugin plugin) => QueueCommandRefresh();

        private void QueueCommandRefresh()
        {
            Debounce("serverinfo-commands", TimeSpan.FromSeconds(2), RefreshCommands);
        }

        private void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                Close(player);
                if (_tipVisible.Contains(player.userID)) player.SendConsoleCommand("gametip.hidegametip");
            }
            _views.Clear();
            _tipShown.Clear();
            _tipVisible.Clear();
        }

        // Fires once the client has finished loading and the player is in the world, which is the
        // first moment a game tip is actually visible to them.
        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !_tipShown.Add(player.userID)) return;
            timer.Once(_config.GameTip.DelaySeconds, () => ShowGameTip(player));
        }

        private void ShowGameTipToAll()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                ShowGameTip(player);
        }

        private void ShowGameTip(BasePlayer player)
        {
            if (player == null || !player.IsConnected || !_config.GameTip.Enabled) return;
            ulong id = player.userID;
            if (!_tipVisible.Add(id)) return;

            player.SendConsoleCommand("gametip.showgametip", Format(_config.GameTip.Message, player));
            timer.Once(_config.GameTip.DisplaySeconds, () =>
            {
                if (!_tipVisible.Remove(id)) return;
                if (player != null && player.IsConnected) player.SendConsoleCommand("gametip.hidegametip");
            });
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !_config.General.OpenOnConnect) return;
            timer.Once(3f, () =>
            {
                if (player != null && player.IsConnected) Render(player);
            });
        }

        private void OnPlayerDisconnected(BasePlayer player)
        {
            if (player == null) return;
            _views.Remove(player.userID);
            _tipShown.Remove(player.userID);
            _tipVisible.Remove(player.userID);
        }

        #endregion

        #region Commands

        [RogueCommand("info",
            Aliases = new[] { "serverinfo", "commands" },
            Description = "Opens the server information and command panel.",
            Usage = "/info [commands|admin]",
            Category = "Server", CooldownSeconds = 0.5, AllowChat = true, AllowConsole = false)]
        private RogueCommandResult InfoCommand(RogueCommandContext context, string[] args)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail("This command can only be used in game.");

            // /info and /serverinfo open on the information page; /commands goes straight to the list.
            ViewState view = GetView(player);
            view.Page = 0;
            view.Tab = TabInfo;
            if (args != null && args.Length > 0)
            {
                if (args[0].Equals("commands", StringComparison.OrdinalIgnoreCase)) view.Tab = TabCommands;
                else if (args[0].Equals("admin", StringComparison.OrdinalIgnoreCase)) view.Tab = TabAdmin;
            }
            else if (context.Command.TrimStart('/').Equals("commands", StringComparison.OrdinalIgnoreCase))
                view.Tab = TabCommands;

            Render(player);
            return RogueCommandResult.Ok(string.Empty);
        }

        #endregion

        #region Access

        private bool IsAdmin(BasePlayer player)
        {
            if (player == null) return false;
            if (_config.General.AuthLevelIsAdmin && player.net != null && player.net.connection != null &&
                player.net.connection.authLevel >= 2)
                return true;
            return permission.UserHasPermission(player.UserIDString, AdminPermission);
        }

        private List<CommandEntry> GetVisibleCommands(BasePlayer player, int tab, bool admin)
        {
            // The admin tab is only reachable by admins and always shows the complete admin list.
            if (tab == TabAdmin) return admin ? _adminCommands : new List<CommandEntry>();

            var visible = new List<CommandEntry>(_defaultCommands.Count);
            foreach (CommandEntry entry in _defaultCommands)
            {
                if (!admin && _config.General.HideWithoutPermission && entry.Permission.Length != 0 &&
                    !permission.UserHasPermission(player.UserIDString, entry.Permission))
                    continue;
                visible.Add(entry);
            }
            return visible;
        }

        #endregion

        #region UI

        private ViewState GetView(BasePlayer player)
        {
            ViewState view;
            if (!_views.TryGetValue(player.userID, out view))
                _views[player.userID] = view = new ViewState();
            return view;
        }

        private void SelectTab(BasePlayer player, int tab)
        {
            if (player == null || !player.IsConnected) return;
            ViewState view = GetView(player);
            view.Tab = tab;
            view.Page = 0;
            Render(player);
        }

        private void ChangePage(BasePlayer player, int delta)
        {
            if (player == null || !player.IsConnected) return;
            GetView(player).Page += delta;
            Render(player);
        }

        private void Close(BasePlayer player)
        {
            if (player == null) return;
            ViewState view;
            if (!_views.TryGetValue(player.userID, out view) || !view.Open) return;
            view.Open = false;
            DestroyUi(player, UiContent);
            DestroyUi(player, UiNav);
            DestroyUi(player, UiRoot);
            DestroyUi(player, UiCursor);
        }

        private void Render(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            ViewState view = GetView(player);
            bool admin = IsAdmin(player);
            // Access is re-checked on every render, so losing admin while the panel is open drops the tab.
            if (view.Tab == TabAdmin && !admin) view.Tab = TabCommands;
            if (view.Tab < TabInfo || view.Tab > TabAdmin) view.Tab = TabInfo;

            RogueUiDocument nav = BuildNavigation(player, view, admin);
            RogueUiDocument content = BuildContent(player, view, admin);

            if (!view.Open)
            {
                // The shell never changes while the panel is open, so it and the cursor are sent once.
                // Tab and page changes then replace only the small documents inside it, which keeps
                // the window and the mouse cursor steady instead of rebuilding the whole panel.
                EnsureCursor(player);
                ShowUi(player, BuildShell(player));
                ShowUi(player, nav);
                ShowUi(player, content);
                view.Open = true;
                return;
            }

            ShowUiIfChanged(player, nav);
            ShowUiIfChanged(player, content);
        }

        // Holds the mouse cursor for as long as the panel is open. If the cursor were owned by a
        // panel that is redrawn, the client would release and re-capture it on every click.
        private void EnsureCursor(BasePlayer player)
        {
            DestroyUi(player, UiCursor);
            CuiElementContainer cursor = new CuiElementContainer();
            cursor.Add(new CuiElement
            {
                Name = UiCursor,
                Parent = "Overlay",
                Components =
                {
                    new CuiNeedsCursorComponent(),
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                }
            });
            CuiHelper.AddUi(player, cursor);
        }

        private RogueUiDocument NewDocument(string name, string parent)
        {
            ThemeSettings theme = _config.Theme;
            RogueUiDocument ui = CreateUi(name, parent);
            ui.Theme.PanelColor = theme.Window;
            ui.Theme.SurfaceColor = theme.Surface;
            ui.Theme.MutedSurfaceColor = theme.SurfaceAlt;
            ui.Theme.HeaderColor = theme.AccentMuted;
            ui.Theme.ButtonColor = theme.SurfaceAlt;
            ui.Theme.PrimaryColor = theme.Accent;
            ui.Theme.HighlightColor = theme.Accent;
            ui.Theme.DangerColor = theme.Danger;
            ui.Theme.CloseColor = theme.Danger;
            ui.Theme.TextColor = theme.Text;
            ui.Theme.MutedTextColor = theme.MutedText;
            ui.Theme.TitleFontSize = 20;
            ui.Theme.BodyFontSize = 12;
            ui.Theme.ButtonFontSize = 12;
            return ui;
        }

        private RogueUiDocument BuildShell(BasePlayer player)
        {
            ThemeSettings theme = _config.Theme;
            RogueUiDocument ui = NewDocument(UiRoot, "Overlay");
            string dimming = "0 0 0 " + theme.WorldDimming.ToString("0.##", CultureInfo.InvariantCulture);

            ui.Panel(UiRoot, "Overlay", RogueUiRect.Full, dimming, cursorEnabled: false);
            ui.Panel("rrsi.frame", UiRoot, Rect(0.17f, 0.13f, 0.83f, 0.87f), theme.Window);
            ui.Panel("rrsi.header", "rrsi.frame", Rect(0f, 0.905f, 1f, 1f), theme.Header);
            ui.Panel(NavHost, "rrsi.frame", Rect(0f, 0.055f, 0.19f, 0.905f), theme.Navigation);
            ui.Panel(BodyHost, "rrsi.frame", Rect(0.19f, 0.055f, 1f, 0.905f), theme.Body);
            ui.Panel("rrsi.footer", "rrsi.frame", Rect(0f, 0f, 1f, 0.055f), theme.Footer);

            ui.Panel("rrsi.logo", "rrsi.header", Rect(0.014f, 0.16f, 0.062f, 0.84f), "0.045 0.13 0.16 0.94");
            ui.Label("rrsi.logo.text", "rrsi.logo", RogueUiRect.Full, "RR", 15, theme.Brand, "MiddleCenter");
            ui.Label("rrsi.title", "rrsi.header", Rect(0.075f, 0.42f, 0.93f, 0.92f), Format(_config.General.Title, player), 17, theme.Text, "MiddleLeft");
            ui.Label("rrsi.subtitle", "rrsi.header", Rect(0.075f, 0.08f, 0.93f, 0.44f), Format(_config.General.Subtitle, player), 9, theme.SubtitleText, "MiddleLeft");
            ui.Button("rrsi.close", "rrsi.header", Rect(0.952f, 0.22f, 0.988f, 0.78f), "×",
                UiActionCallback(player, "close", () => Close(player)), theme.Danger, 15);

            ui.Label("rrsi.footer.left", "rrsi.footer", Rect(0.015f, 0f, 0.70f, 1f), Format(_config.General.Footer, player), 9, theme.MutedText, "MiddleLeft");
            ui.Label("rrsi.footer.right", "rrsi.footer", Rect(0.70f, 0f, 0.985f, 1f), "RogueRust ServerInfo " + PluginVersion, 9, theme.MutedText, "MiddleRight");
            return ui;
        }

        private RogueUiDocument BuildNavigation(BasePlayer player, ViewState view, bool admin)
        {
            RogueUiDocument ui = NewDocument(UiNav, NavHost);
            ui.Panel(UiNav, NavHost, RogueUiRect.Full, Transparent);

            NavigationButton(ui, player, view, 0, TabInfo, "tab.info", Message("Tab.Info", player));
            NavigationButton(ui, player, view, 1, TabCommands, "tab.commands", Message("Tab.Commands", player));
            if (admin) NavigationButton(ui, player, view, 2, TabAdmin, "tab.admin", Message("Tab.Admin", player));
            return ui;
        }

        private void NavigationButton(RogueUiDocument ui, BasePlayer player, ViewState view, int slot, int tab, string key, string text)
        {
            ThemeSettings theme = _config.Theme;
            float yMax = 0.965f - slot * 0.095f;
            float yMin = yMax - 0.075f;
            bool active = view.Tab == tab;
            if (active) ui.Panel("rrsi.nav.rail." + tab, UiNav, Rect(0.015f, yMin, 0.035f, yMax), theme.Brand);
            ui.Button("rrsi.nav." + tab, UiNav, Rect(0.05f, yMin, 0.94f, yMax), text.ToUpperInvariant(),
                UiActionCallback(player, key, () => SelectTab(player, tab)),
                active ? theme.AccentMuted : "0.012 0.030 0.040 0.42", 10);
        }

        private RogueUiDocument BuildContent(BasePlayer player, ViewState view, bool admin)
        {
            RogueUiDocument ui = NewDocument(UiContent, BodyHost);
            ui.Panel(UiContent, BodyHost, RogueUiRect.Full, Transparent);

            string title = view.Tab == TabInfo ? Message("Tab.Info", player)
                : view.Tab == TabAdmin ? Message("Tab.Admin", player) : Message("Tab.Commands", player);
            ui.Label("rrsi.heading", UiContent, Rect(0.03f, 0.90f, 0.97f, 0.98f), title, 16, _config.Theme.Text, "MiddleLeft");
            ui.Panel("rrsi.heading.rule", UiContent, Rect(0.03f, 0.893f, 0.97f, 0.896f), _config.Theme.AccentMuted);

            if (view.Tab == TabInfo) RenderInfo(ui, player);
            else RenderCommands(ui, player, view, admin);
            return ui;
        }

        private void RenderInfo(RogueUiDocument ui, BasePlayer player)
        {
            ThemeSettings theme = _config.Theme;
            const float top = 0.87f;
            const float lineHeight = 0.052f;
            int count = Math.Min(_config.InfoLines.Count, MaximumInfoLines);
            for (int i = 0; i < count; i++)
            {
                string line = _config.InfoLines[i] ?? string.Empty;
                if (line.Trim().Length == 0) continue;

                bool heading = line.StartsWith("#", StringComparison.Ordinal);
                string text = Format(heading ? line.TrimStart('#').Trim() : line, player);
                float y = top - i * lineHeight;
                ui.Label("rrsi.info." + i, UiContent, Rect(0.03f, y - lineHeight, 0.97f, y), text,
                    heading ? 14 : 12, heading ? theme.Accent : theme.Text, "MiddleLeft");
            }
        }

        private void RenderCommands(RogueUiDocument ui, BasePlayer player, ViewState view, bool admin)
        {
            ThemeSettings theme = _config.Theme;
            List<CommandEntry> commands = GetVisibleCommands(player, view.Tab, admin);
            int perPage = _config.General.CommandsPerPage;
            int pageCount = Math.Max(1, (commands.Count + perPage - 1) / perPage);
            view.Page = Math.Max(0, Math.Min(view.Page, pageCount - 1));

            if (commands.Count == 0)
                ui.Label("rrsi.empty", UiContent, Rect(0.03f, 0.45f, 0.97f, 0.55f), Message("Commands.None", player), 12, theme.MutedText, "MiddleCenter");

            const float top = 0.875f;
            const float bottom = 0.105f;
            float rowHeight = (top - bottom) / perPage;
            int first = view.Page * perPage;
            int last = Math.Min(commands.Count, first + perPage);
            for (int i = first; i < last; i++)
            {
                CommandEntry entry = commands[i];
                int slot = i - first;
                float y = top - slot * rowHeight;
                string row = "rrsi.row." + slot;
                string usage = entry.Usage.Length == 0 ? "/" + entry.Command : entry.Usage;

                ui.Panel(row, UiContent, Rect(0.03f, y - rowHeight + 0.008f, 0.97f, y), slot % 2 == 0 ? theme.Surface : theme.SurfaceAlt)
                  .Label(row + ".command", row, Rect(0.015f, 0f, 0.25f, 1f), "/" + entry.Command, 12, theme.Accent, "MiddleLeft")
                  .Label(row + ".description", row, Rect(0.26f, 0.46f, 0.82f, 1f), entry.Description, 10, theme.Text, "MiddleLeft")
                  .Label(row + ".usage", row, Rect(0.26f, 0f, 0.82f, 0.50f), usage, 9, theme.MutedText, "MiddleLeft")
                  .Label(row + ".category", row, Rect(0.83f, 0f, 0.985f, 1f), entry.Category.ToUpperInvariant(), 8, theme.SubtitleText, "MiddleRight");
            }

            ui.Label("rrsi.count", UiContent, Rect(0.03f, 0.02f, 0.33f, 0.085f),
                string.Format(CultureInfo.InvariantCulture, Message("Commands.Count", player), commands.Count),
                10, theme.MutedText, "MiddleLeft");

            if (pageCount <= 1) return;
            ui.Button("rrsi.page.prev", UiContent, Rect(0.66f, 0.022f, 0.73f, 0.083f), "‹",
                UiActionCallback(player, "page.prev", () => ChangePage(player, -1)), theme.SurfaceAlt, 14);
            ui.Label("rrsi.page", UiContent, Rect(0.74f, 0.02f, 0.89f, 0.085f),
                (view.Page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture),
                10, theme.Text, "MiddleCenter");
            ui.Button("rrsi.page.next", UiContent, Rect(0.90f, 0.022f, 0.97f, 0.083f), "›",
                UiActionCallback(player, "page.next", () => ChangePage(player, 1)), theme.SurfaceAlt, 14);
        }

        private static RogueUiRect Rect(float minX, float minY, float maxX, float maxY)
        {
            return new RogueUiRect(
                minX.ToString("0.####", CultureInfo.InvariantCulture) + " " + minY.ToString("0.####", CultureInfo.InvariantCulture),
                maxX.ToString("0.####", CultureInfo.InvariantCulture) + " " + maxY.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static string Format(string template, BasePlayer player)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? string.Empty;
            return template
                .Replace("{server.name}", ConVar.Server.hostname ?? string.Empty)
                .Replace("{players.online}", BasePlayer.activePlayerList.Count.ToString(CultureInfo.InvariantCulture))
                .Replace("{players.max}", ConVar.Server.maxplayers.ToString(CultureInfo.InvariantCulture))
                .Replace("{player.name}", player != null ? player.displayName ?? string.Empty : string.Empty);
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
                    ["Tab.Info"] = "Information",
                    ["Tab.Commands"] = "Commands",
                    ["Tab.Admin"] = "Admin Commands",
                    ["Commands.None"] = "No commands are available to you.",
                    ["Commands.Count"] = "{0} commands"
                };
            }
        }

        #endregion
    }
}
