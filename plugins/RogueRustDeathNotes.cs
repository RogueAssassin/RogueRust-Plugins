using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using System.IO;
using Rust;
using Rust.Ai.Gen2;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
	[Info("RogueRustDeathNotes", "RogueAssassin", "2.2.0")]
	[Description("RogueRustDeathNotes - RogueRust death notification engine, based on the original Death Notes concept, with integrated native GUI output and per-player controls.")]
	public sealed class RogueRustDeathNotes : RogueRustPlugin
	{
		[PluginReference]
		private Plugin UINotify;
		
		[PluginReference]
		private Plugin Notify;
		
		#region Fields

		private const string WildcardCharacter = "*";
		[RoguePermission]
		private const string CanSeePermission = "roguerustdeathnotes.cansee";
		[RoguePermission]
		private const string CantSeePermission = "roguerustdeathnotes.cantsee";
        [RoguePermission]
        private const string SuppressPermission = "roguerustdeathnotes.suppress";
        [RoguePermission]
        private const string TeamOnlyPermission = "roguerustdeathnotes.seeteamonly";

		private const string PluginVersion = "2.2.0";

		private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 2, 0);

		private static RogueRustDeathNotes _instance;

		private PluginConfiguration _configuration;
        private const string PlayerSettingsDataKey = "RogueRustDeathNotes/player-settings";
        private const string LegacyPlayerSettingsDataKey = "RogueRust/RogueRustDeathNotes/player-settings";
        private const string TranslationDataKey = "RogueRustDeathNotes/killer-data";
        private const string LegacyTranslationDataKey = "RogueRust/RogueRustDeathNotes/killer-data";
        private const string GuiName = "RogueRustDeathNotes.GUI";
        private const ulong NativeChatAvatarSteamId = 76561198640726735UL;
        private const string EmbeddedIconOwner = "RogueRustDeathNotes";
        private const string EmbeddedIconKey = "notification-icon";
        private const string EmbeddedIconResource = "Oxide.Ext.RogueRust.Icons.RogueRustDeathNotes.png";
        private string _embeddedDeathNotesIcon;
        private PlayerSettingsData _playerSettings = new PlayerSettingsData();
        private PluginConfiguration.Translation _translationData = new PluginConfiguration.Translation();

        private sealed class PlayerSettingsData
        {
            public Dictionary<ulong, PlayerNoticeSettings> Players = new Dictionary<ulong, PlayerNoticeSettings>();
        }

        private sealed class PlayerNoticeSettings
        {
            public bool Enabled = true;
            public bool TeamOnly;
        }

		private readonly Dictionary<string, string> _enemyPrefabs = new()
		{
			["spikes.floor"] = "Wooden Floor Spike Cluster",
			["spikes_static"] = "Wooden Floor Spike Cluster",
			["barricade.woodwire"] = "Barbed Wooden Barricade",
			["barricade.metal"] = "Metal Barricade",
			["wall.external.high.wood"] = "High External Wooden Wall",
			["wall.external.high.stone"] = "High External Stone Wall",
			["gates.external.high.stone"] = "High External Stone Gate",
			["campfire"] = "Campfire",
			["skull_fire_pit"] = "Skull Fire Pit",
			["heavyscientist"] = "Heavy Scientist"
		};
		private readonly Dictionary<string, string> _weaponPrefabs = new()
		{
			["rocket_basic"] = "Rocket",
			["rocket_hv"] = "High Velocity Rocket",
			["rocket_fire"] = "Incendiary Rocket",
			["grenade.f1.deployed"] = "F1 Grenade",
			["grenade.beancan.deployed"] = "Beancan Grenade",
			["survey_charge.deployed"] = "Survey Charge",
			["explosive.satchel.deployed"] = "Satchel Charge",
			["explosive.timed.deployed"] = "Timed Explosive Charge",
			["rock.entity"] = "Rock",
			["longsword.entity"] = "Longsword",
			["mace.entity"] = "Mace",
			["spear_stone.entity"] = "Stone Spear",
			["spear_wooden.entity"] = "Wooden Spear",
			["machete.weapon"] = "Machete",
			["knife_bone.entity"] = "Bone Knife",
			["bone_club.entity"] = "Bone Club",
			["salvaged_cleaver.entity"] = "Salvaged Cleaver",
			["salvaged_sword.entity"] = "Salvaged Sword",
			["candy_cane.entity"] = "Candy Cane Club",
			["flamethrower.entity"] = "Flame Thrower",
			["snowball.entity"] = "Snowball",
			["combat.knife.entity"] = "Combat Knife"
		};
		private readonly Dictionary<string, CombatEntityType> _combatEntityTypes = new()
		{
			["GunTrap"]  = CombatEntityType.Trap,
			["FlameTurret"]  = CombatEntityType.Turret,
			["AutoTurret"]  = CombatEntityType.Turret,
			["BaseHelicopter"]  = CombatEntityType.Helicopter,
			["BradleyAPC"]  = CombatEntityType.Bradley,
			["BasePlayer"]  = CombatEntityType.Player,
			["NPCMurderer"]  = CombatEntityType.Murderer,
			["CodeLock"]  = CombatEntityType.Lock,
			["Scientist"]  = CombatEntityType.Scientist,
			["ScientistNPC"]  = CombatEntityType.Scientist,
			["HTNPlayer"]  = CombatEntityType.Scientist,
			["NPCAutoTurret"]  = CombatEntityType.Sentry,
			["FireBall"]  = CombatEntityType.Fire,
			["scarecrow"]  = CombatEntityType.ScarecrowNPC,
			["ScientistNPCNew"]  = CombatEntityType.Scientist,
			["tunneldweller"]  = CombatEntityType.TunnelDweller,
			["underwaterdweller"]  = CombatEntityType.UnderwaterDweller
		};

		private readonly Regex _colorTagRegex =
			new Regex(@"<color=.{0,7}>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private readonly Regex _sizeTagRegex =
			new Regex(@"<size=\d*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		private readonly List<string> _richTextLiterals = new List<string>
		{
			"</color>", "</size>", "<b>", "</b>", "<i>", "</i>"
		};

		private readonly Dictionary<ulong, AttackInfo> _previousAttack = new Dictionary<ulong, AttackInfo>();

        private Dictionary<ulong, HitInfo> _patrolHeliTagTracker = new Dictionary<ulong, HitInfo>();
        private HashSet<ulong> _bradleyApcTagTracker = new HashSet<ulong>();
        private readonly Dictionary<string, string> _localWeaponCatalog =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _translationDataDirty;


        private void LogOutput(string text)
        {
	        //Puts(text);
	        string logText = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss") + " : " + text;
	        LogToFile("DeathNotes-log", logText, this);
        }
        
		private readonly Func<PluginConfiguration.DeathMessage, DeathData, bool>[] _messageMatchingStages =
		{
			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && MatchesCombatEntityType(d.KillerEntityType, m.KillerType) &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          MatchesCombatEntityType(d.VictimEntityType, m.VictimType) &&
			          m.DamageType == WildcardCharacter,

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          MatchesDamageType(d.DamageType, m.DamageType),

			(m, d) => m.Enabled == true && m.KillerType == WildcardCharacter &&
			          m.VictimType == WildcardCharacter &&
			          m.DamageType == WildcardCharacter
		};

		#endregion

		#region Hooks

		private void Init()
		{
			_instance = this;


            LoadPlayerSettings();
            EnsureRogueRustLanguageFile();
            MigrateLegacyConfigKeys();
			_configuration = Config.ReadObject<PluginConfiguration>();
            if (_configuration == null)
                _configuration = new PluginConfiguration();

            LoadTranslationData();
            _configuration.Translations = _translationData;
			_configuration.LoadDefaults();
            _translationData = _configuration.Translations;
            SaveTranslationData();
			Config.WriteObject(_configuration);

            string tmpPerm;
            foreach (string perm in _configuration.PatrolHeliDisplayPerms)
            {
                tmpPerm = perm.Trim().ToLowerInvariant();
                if (!tmpPerm.StartsWith("roguerustdeathnotes."))
                    continue;
                Rogue.Permissions.Register(tmpPerm, this);
            }

            foreach (string perm in _configuration.BradleyDisplayPerms)
            {
                tmpPerm = perm.Trim().ToLowerInvariant();
                if (!tmpPerm.StartsWith("roguerustdeathnotes."))
                    continue;
                Rogue.Permissions.Register(tmpPerm, this);
            }

            foreach (var msg in _configuration.Translations.Messages)
			{
                foreach (string perm in msg.DisplayPerms)
                {
                    tmpPerm = perm.Trim().ToLowerInvariant();
                    if (!tmpPerm.StartsWith("roguerustdeathnotes."))
                        continue;
                    Rogue.Permissions.Register(tmpPerm, this);
                }
            }

			LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustDeathNotes/; Lang=<language>/RogueRust/RogueRustDeathNotes/messages.json");
		}

        private void MigrateLegacyConfigKeys()
        {
            try
            {
                JObject root = Config.ReadObject<JObject>();
                if (root == null || root["Formatting Settings"] != null) return;

                PluginConfiguration c = new PluginConfiguration();

                JToken Token(string numbered, string original) => root[numbered] ?? root[original];
                T Read<T>(string numbered, string original, T fallback)
                {
                    JToken token = Token(numbered, original);
                    if (token == null) return fallback;
                    try { return token.ToObject<T>(); } catch { return fallback; }
                }

                c.Formatting.VariableFormats = Read("01 - Formatting - Variable Formats","Variable Formats",c.Formatting.VariableFormats);
                c.Formatting.VariableColors = Read("01 - Formatting - Variable Colors","Variable Colors",c.Formatting.VariableColors);
                c.Formatting.ChatFormat = Read("01 - Formatting - Chat Message Format ({message} is replaced by the death notification)","Chat - Message format ({message} is replaced by the death notification)",c.Formatting.ChatFormat);
                c.Output.Modules = Read("02 - Output - Integrated Output Modules","Integrated Output Modules",c.Output.Modules);
                c.Output.NotifyMessageType = Read("02 - Output - Notify Message Type","Notify Message Type",c.Output.NotifyMessageType);
                c.Output.UINotifyMessageType = Read("02 - Output - UINotify Message Type","UINotify Message Type",c.Output.UINotifyMessageType);
                c.PlayerControls.CanPlayerUseDNCommand = Read("03 - Player Controls - Can Player Use /dn Command","Can Player Use /dn Command",c.PlayerControls.CanPlayerUseDNCommand);
                c.PatrolHelicopter.DisplayPerms = Read("04 - Patrol Helicopter - Display Permissions (Display For All Players If Empty)","Patrol Heli Tags Display Permissions (Display For All Players If Empty)",c.PatrolHelicopter.DisplayPerms);
                c.PatrolHelicopter.ShowTagsInConsole = Read("04 - Patrol Helicopter - Show Tags in Console","Show Patrol Heli Tags in Console",c.PatrolHelicopter.ShowTagsInConsole);
                c.PatrolHelicopter.ShowTagsInChat = Read("04 - Patrol Helicopter - Show Tags in Chat","Show Patrol Heli Tags in Chat",c.PatrolHelicopter.ShowTagsInChat);
                c.PatrolHelicopter.ShowTagsInNotify = Read("04 - Patrol Helicopter - Show Tags in Notify","Show Patrol Heli Tags in Notify",c.PatrolHelicopter.ShowTagsInNotify);
                c.PatrolHelicopter.ShowTagsInUINotify = Read("04 - Patrol Helicopter - Show Tags in UINotify","Show Patrol Heli Tags in UINotify",c.PatrolHelicopter.ShowTagsInUINotify);
                c.PatrolHelicopter.TagMessage = Read("04 - Patrol Helicopter - Tag Message","Patrol Helicopter Tag Message",c.PatrolHelicopter.TagMessage);
                c.BradleyApc.DisplayPerms = Read("05 - Bradley APC - Display Permissions (Display For All Players If Empty)","Bradley APC Tags Display Permissions (Display For All Players If Empty)",c.BradleyApc.DisplayPerms);
                c.BradleyApc.ShowTagsInConsole = Read("05 - Bradley APC - Show Tags in Console","Show Bradley APC Tags in Console",c.BradleyApc.ShowTagsInConsole);
                c.BradleyApc.ShowTagsInChat = Read("05 - Bradley APC - Show Tags in Chat","Show Bradley APC Tags in Chat",c.BradleyApc.ShowTagsInChat);
                c.BradleyApc.ShowTagsInNotify = Read("05 - Bradley APC - Show Tags in Notify","Show Bradley APC Tags in Notify",c.BradleyApc.ShowTagsInNotify);
                c.BradleyApc.ShowTagsInUINotify = Read("05 - Bradley APC - Show Tags in UINotify","Show Bradley APC Tags in UINotify",c.BradleyApc.ShowTagsInUINotify);
                c.BradleyApc.TagMessage = Read("05 - Bradley APC - Tag Message","Bradley Tag Message",c.BradleyApc.TagMessage);
                c.General.MessageRadius = Read("06 - General - Message Broadcast Radius (in meters)","Message Broadcast Radius (in meters)",c.General.MessageRadius);
                c.General.UseMetricDistance = Read("06 - General - Use Metric Distance","Use Metric Distance",c.General.UseMetricDistance);
                c.General.RequirePermission = Read("06 - General - Require Permission (roguerustdeathnotes.cansee)","Require Permission (deathnotes.cansee)",c.General.RequirePermission);
                c.Developer.DebugMode = Read("07 - Developer - Debug Mode Enabled","Debug Mode Enabled",c.Developer.DebugMode);
                c.Version = CurrentVersion;
                Config.WriteObject(c, true);
                LogInformation("Configuration", "Migrated RogueRustDeathNotes configuration to the grouped v2.2.0 family layout.");
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Legacy configuration migration could not be completed: " + exception.Message);
            }
        }

        private void OnServerInitialized()
        {
            BuildLocalWeaponCatalog();
            string embeddedIcon = GetEmbeddedDeathNotesIcon();
            if (!string.IsNullOrWhiteSpace(embeddedIcon))
                Puts("RogueRustDeathNotes embedded NativeGUI icon ready with FileStorage CRC " + embeddedIcon + ".");
            else
                PrintWarning("RogueRustDeathNotes embedded NativeGUI icon was not available during server initialization; NativeGUI will use text-only fallback.");
        }

        private void BuildLocalWeaponCatalog()
        {
            _localWeaponCatalog.Clear();

            if (ItemManager.itemList == null)
                return;

            foreach (ItemDefinition definition in ItemManager.itemList)
            {
                if (definition == null || string.IsNullOrEmpty(definition.shortname))
                    continue;

                string displayName = definition.displayName?.english;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = HumanizePascalCase(definition.shortname.Replace(".", " "));

                _localWeaponCatalog[definition.shortname] = displayName;
                _localWeaponCatalog[definition.shortname + ".entity"] = displayName;
            }

            LogInformation("Definitions",
                "Loaded " + _localWeaponCatalog.Count +
                " local Rust item/prefab aliases from the running server. No remote definitions are used.");
        }

        private void MarkTranslationDataDirty()
        {
            _translationDataDirty = true;
            Debounce("deathnotes-translation-save", TimeSpan.FromSeconds(2), delegate
            {
                if (!_translationDataDirty)
                    return;

                _translationDataDirty = false;
                SaveTranslationData();
            });
        }

		private void OnPluginLoaded(Plugin plugin)
		{
			if (plugin.Name == "Notify")
				Notify = plugin;
			else if (plugin.Name == "UINotify")
				UINotify = plugin;
		}

		private void OnPluginUnloaded(Plugin plugin)
		{
			if (plugin.Name == "Notify")
				Notify = null;
			else if (plugin.Name == "UINotify")
				UINotify = null;
		}
		
		private void Unload()
		{
			SavePlayerSettings();
            if (_translationDataDirty)
            {
                _translationDataDirty = false;
                SaveTranslationData();
            }
            DestroyGuiForAll();
            LogInformation("Lifecycle", "Unloading RogueRustDeathNotes.");
			_instance = null;
		}

		private void OnEntityTakeDamage(BasePlayer victimEntity, HitInfo hitInfo)
		{
			if (victimEntity == null || hitInfo == null)
            	return;

            BaseEntity attacker = victimEntity.lastAttacker ?? hitInfo?.Initiator;

            var userId = victimEntity.ToPlayer().userID;

            AttackInfo currentAttack;
            if (_previousAttack.TryGetValue(userId, out currentAttack))
            {
                if (attacker != null)
                    currentAttack.Attacker = attacker;
                
                if (hitInfo != null)
                    currentAttack.HitInfo = hitInfo;

                if (victimEntity.lastDamage != null)
                    currentAttack.DamageType = victimEntity.lastDamage;
            }
            else
            {
                _previousAttack[userId] = new AttackInfo
                {
                    HitInfo = hitInfo,
                    Attacker = victimEntity.lastAttacker ?? hitInfo?.Initiator,
                    DamageType = victimEntity.lastDamage
                };
            }
		}

        //This method tracks Bradley APC damage and reports whoever tagged it.
        private void OnEntityTakeDamage(BradleyAPC victimEntity, HitInfo hitInfo) => HandleEntityDamage(victimEntity, hitInfo);
        
        //This method tracks Patrol Helicopter damage and reports whoever tagged it.
		private void OnPatrolHelicopterTakeDamage(PatrolHelicopter victimEntity, HitInfo hitInfo) => HandleEntityDamage(victimEntity, hitInfo);

		private void HandleEntityDamage( BaseCombatEntity victimEntity, HitInfo hitInfo )
		{
			if (victimEntity == null || hitInfo == null)
				return;

			bool isPatrolHeli = victimEntity is PatrolHelicopter;

			HitInfo storedHitInfo;
			ulong netID = victimEntity.net.ID.Value;

			if (isPatrolHeli)
			{
				bool wasHeliIdFound = _patrolHeliTagTracker.TryGetValue(netID, out storedHitInfo);
				if (!wasHeliIdFound)
				{
					storedHitInfo = new HitInfo();
					_patrolHeliTagTracker.Add(netID, storedHitInfo);
				}

				if (hitInfo.WeaponPrefab != null)
					storedHitInfo.WeaponPrefab = hitInfo.WeaponPrefab;

				if (hitInfo.Weapon != null)
					storedHitInfo.Weapon = hitInfo.Weapon;

				if (wasHeliIdFound || (victimEntity.lastAttackedTime != float.NegativeInfinity))
					return;
				
				if (storedHitInfo.Initiator == null && hitInfo.Initiator != null)
					storedHitInfo.Initiator = hitInfo.Initiator;
			}
			else //If it's a Bradley APC
			{
				if ((victimEntity.lastAttackedTime != float.NegativeInfinity) || (_bradleyApcTagTracker.Contains(netID)))
					return;
				_bradleyApcTagTracker.Add(netID);
				storedHitInfo = hitInfo;
			}

			var attackerPlayer = hitInfo.Initiator?.ToPlayer();
			if( attackerPlayer?.displayName == null )
			{
				return;
			}

			storedHitInfo.Initiator = hitInfo.Initiator;

			if( (isPatrolHeli ? !ShowPatrolHeliTags() : !ShowBradleyTags()) || Rogue.Permissions.Has( attackerPlayer.UserIDString, SuppressPermission ) )
			{
				return;
			}

			var data = new DeathData
			{
				VictimEntity = victimEntity,
				KillerEntity = victimEntity.lastAttacker ?? hitInfo.Initiator,
				VictimEntityType = GetCombatEntityType(victimEntity),
				KillerEntityType = GetCombatEntityType(victimEntity.lastAttacker),
				DamageType = victimEntity.lastDamage,
				HitInfo = storedHitInfo
			};

			if (data.KillerEntity != null)
				data.KillerEntityType = CombatEntityType.Player;
				
			string message = PopulateMessageVariables(isPatrolHeli ? _configuration.PatrolHeliTagMessage : _configuration.BradleyTagMessage, data);

			object hookResult = false;
			try
			{
				hookResult = Interface.Call("OnDeathNotice", data.ToDictionary(), message);
			}
			catch (NullReferenceException)
			{
				return;
			}
			
			if (hookResult?.Equals(false) ?? false)
				return;

			//Prints to console
			if (isPatrolHeli ? _configuration.ShowPatrolHeliTagsInConsole : _configuration.ShowBradleyTagsInConsole)
				Puts(StripRichText(message));
				
			//Prints to chat, the Notify plugin, and/or the UINotify plugin
			foreach (var player in BasePlayer.activePlayerList)
			{
				if ((_configuration.RequirePermission &&
				     !Rogue.Permissions.Has(player.UserIDString, CanSeePermission)) ||
				    Rogue.Permissions.Has(player.UserIDString, CantSeePermission) ||
                    (Rogue.Permissions.Has(player.UserIDString, TeamOnlyPermission) && !ArePlayersOnSameTeam(player, data)))
					continue;

                if (isPatrolHeli ? !PlayersPassesPermsCheck(player.UserIDString, _configuration.PatrolHeliDisplayPerms) : !PlayersPassesPermsCheck(player.UserIDString, _configuration.BradleyDisplayPerms))
                        continue;

				if (_configuration.MessageRadius != -1 &&
				    player.Distance(data.VictimEntity) > _configuration.MessageRadius)
					continue;

				if (isPatrolHeli ? _configuration.ShowPatrolHeliTagsInChat : _configuration.ShowBradleyTagsInChat)
				{
					Player.Reply(
						player,
						_configuration.ChatFormat.Replace("{message}", message),
						NativeChatAvatarSteamId
					);
				}

                    if (_configuration.OutputModules.EnableNativeGui)
                        ShowNativeGui(player, GetNativeGuiMessage(message));
					
				if ((isPatrolHeli ? _configuration.ShowPatrolHeliTagsInNotify : _configuration.ShowBradleyTagsInNotify) && Notify != null)
					Notify.Call("SendNotify", player, _configuration.NotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
					
				if ((isPatrolHeli ? _configuration.ShowPatrolHeliTagsInUINotify : _configuration.ShowBradleyTagsInUINotify) && UINotify != null)
					UINotify.Call("SendNotify", player, _configuration.UINotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
			}
		}
		
		//This method tracks when Patrol Helicopters are killed or despawns naturally, and cleans them from the tag trackers.
		private void OnEntityKill(PatrolHelicopter entity)
		{
			_patrolHeliTagTracker.Remove(entity.net.ID.Value);
		}

		//This method tracks when Bradley APCs are killed or despawns naturally, and cleans them from the tag trackers.
		private void OnEntityKill(BradleyAPC entity)
		{
			_bradleyApcTagTracker.Remove(entity.net.ID.Value);
		}
		
		private void OnEntityDeath(BaseCombatEntity victimEntity, HitInfo hitInfo)
		{
            using (Measure("RogueRustDeathNotes", "OnEntityDeath"))
            {
			//There is no victim information for some reason.
			// Try to avoid error when entity was destroyed
			//Note: If someone is wounded and dies, there will be null hitInfo
			if ((victimEntity == null) || (victimEntity.gameObject == null))
				return;

			//Snakes produce two death messages, one when it is killed and one when it is skinned.
			//The below is to prevent a death note related to skinning.
			if ((victimEntity is SnakeHazard) && (((SnakeHazard)victimEntity).IsCorpse))
				return;
			
			//Some corpses produce two death messages, one when it is killed and one when it is skinned.
			//The below is to prevent a death note related to skinning.
			if (victimEntity is BaseCorpse)
				return;
			
			if (hitInfo == null)
			{
				hitInfo = new HitInfo();
				hitInfo.Initiator = null;
			}

			//Attempts to get the NetID. This was occasionally resulting in a NullReferenceException, so we're being careful.
			ulong netID = 0;
			try
			{
				netID = victimEntity.net.ID.Value;
			}
			catch (Exception e)
			{
				LogOutput($"Exception in OnEntityDeath while retrieving victimEntity.net: {e.ToString()}");
				netID = 0;
			}
			
			if ((victimEntity is PatrolHelicopter || victimEntity is BradleyAPC) && netID == 0)
				return;

			DeathData data = new DeathData
			{
				VictimEntity = victimEntity,
				KillerEntity = victimEntity.lastAttacker ?? hitInfo?.Initiator,
				VictimEntityType = GetCombatEntityType(victimEntity),
				KillerEntityType = GetCombatEntityType(victimEntity.lastAttacker),
				DamageType = victimEntity.lastDamage,
				HitInfo = hitInfo
			};

			if (victimEntity is PatrolHelicopter && _patrolHeliTagTracker.TryGetValue( netID, out var value ))
				data.HitInfo = value;

			// Handle inconsistencies/exceptions
			HandleInconsistencies(ref data);

            if (data.VictimEntityType == CombatEntityType.Player)
                _previousAttack.Remove(data.VictimEntity.ToPlayer().userID);
            
			if (_configuration.DebugMode)
			{
				LogOutput("[DEATHNOTES DEBUG]");
				LogOutput(
					$"\tKillerEntity: {data.KillerEntity?.GetType().Name ?? "NULL"} / {data.KillerEntity?.ShortPrefabName ?? "NULL"} / {data.KillerEntity?.PrefabName ?? "NULL"}");
				if (data.KillerEntity != null)
					LogOutput(
						$"\tKiller's Owner: {covalence.Players.FindPlayerById(data.KillerEntity?.OwnerID.ToString())?.Name}");
				LogOutput(
					$"\tVictimEntity: {data.VictimEntity?.GetType().Name ?? "NULL"} / {data.VictimEntity?.ShortPrefabName ?? "NULL"} / {data.VictimEntity?.PrefabName ?? "NULL"}");
				LogOutput($"\tInitiator: {data.HitInfo?.Initiator?.ShortPrefabName}");
				LogOutput($"\tLastAttacker: {data.VictimEntity?.lastAttacker?.ShortPrefabName}");
				LogOutput($"\tKillerEntityType: {data.KillerEntityType}");
				LogOutput($"\tVictimEntityType: {data.VictimEntityType}");
				LogOutput($"\tDamageType: {data.DamageType}");
				LogOutput($"\tBodypart: {GetCustomizedBodypartName(data.HitInfo)}");
				LogOutput($"\tWeapon: {hitInfo?.WeaponPrefab?.ShortPrefabName ?? "NULL"}");
                if (data.HitInfo != null)
                    LogOutput($"\tHitPositionWorld: {GetPositionString(data.HitInfo.HitPositionWorld)}");
			}
			

			// Change entity type for dwellers
			RepairEntityTypes(ref data);

			// Need do this before we cancel out of the method, as we need to track all these entities dying. Even if it's not a player killing them.
			if (victimEntity is PatrolHelicopter)
				_patrolHeliTagTracker.Remove(netID);
			else if (victimEntity is BradleyAPC)
				_bradleyApcTagTracker.Remove(netID);

			// Ignore deaths of other entities
			if (data.KillerEntityType == CombatEntityType.Other || data.VictimEntityType == CombatEntityType.Other)
				return;
			
            PluginConfiguration.DeathMessage deathMessage = GetDeathMessage(data);

			// Populate the variables in the message
			string message = PopulateMessageVariables(
				// Find the best matching death message for this death
				GetDeathMessageString(deathMessage),
				data
			);

			if ((message == null) || (deathMessage == null))
				return;
	
			object hookResult = false;
			try
			{ 
				hookResult = Interface.Call("OnDeathNotice", data.ToDictionary(), message);
				if (hookResult?.Equals(false) ?? false)
					return;
			}
			catch (Exception e)
			{
				LogOutput($"Exception in OnEntityDeath while calling OnDeathNotice hook: {e.ToString()}");
				return;
			}
			
			if (
					(deathMessage.ShowKillsInChat == true || deathMessage.ShowKillsInConsole == true || deathMessage.ShowKillsInNotify == true || deathMessage.ShowKillsInUINotify == true) &&
					(data.KillerEntityType != CombatEntityType.Player || !Rogue.Permissions.Has(data.KillerEntity?.ToPlayer()?.UserIDString, SuppressPermission)) &&
					(data.VictimEntityType != CombatEntityType.Player || !Rogue.Permissions.Has(data.VictimEntity?.ToPlayer()?.UserIDString, SuppressPermission))
			    )
			{

				//Prints to console
				if (deathMessage.ShowKillsInConsole == true)
					Puts(StripRichText(message));
				
				//Prints to chat, the Notify plugin, and/or the UINotify plugin
				foreach (var player in BasePlayer.activePlayerList)
				{
					if ((_configuration.RequirePermission &&
						!Rogue.Permissions.Has(player.UserIDString, CanSeePermission)) ||
					    Rogue.Permissions.Has(player.UserIDString, CantSeePermission) ||
                        !IsPlayerNoticeEnabled(player) ||
                        ((Rogue.Permissions.Has(player.UserIDString, TeamOnlyPermission) || IsPlayerTeamOnly(player)) && !ArePlayersOnSameTeam(player, data)))
						continue;
                    
                    if (!PlayersPassesPermsCheck(player.UserIDString, deathMessage.DisplayPerms))
                        continue;

					if (_configuration.MessageRadius != -1 &&
						player.Distance(data.VictimEntity) > _configuration.MessageRadius)
						continue;

					if (deathMessage.ShowKillsInChat == true)
					{
						Player.Reply(
							player,
							_configuration.ChatFormat.Replace("{message}", message),
							NativeChatAvatarSteamId
						);
					}

                    if (_configuration.OutputModules.EnableNativeGui)
                        ShowNativeGui(player, GetNativeGuiMessage(message));
					
					if ((deathMessage.ShowKillsInNotify == true) && Notify != null)
						Notify.Call("SendNotify", player, _configuration.NotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));

					if ((deathMessage.ShowKillsInUINotify == true) && UINotify != null)
						UINotify.Call("SendNotify", player, _configuration.UINotifyMessageType, _configuration.ChatFormat.Replace("{message}", message));
				}
			}
            }
		}

		private void RepairEntityTypes(ref DeathData data)
		{
			if (data.VictimEntity != null)
			{
				string victimPrefabName = data.VictimEntity.ShortPrefabName.ToLower();
				if (victimPrefabName.Contains("corpse"))
				{
					data.VictimEntityType = CombatEntityType.Other;
				}
				else if (victimPrefabName.Contains("underwaterdweller"))
				{
					data.VictimEntityType = CombatEntityType.UnderwaterDweller;
				}
				else if (victimPrefabName.Contains("tunneldweller"))
				{
					data.VictimEntityType = CombatEntityType.TunnelDweller;
				}
				else if (victimPrefabName.Contains("bradleyapc"))
				{
					data.VictimEntityType = CombatEntityType.Bradley;
				}
				else if (victimPrefabName.Contains("shark"))
				{
					data.VictimEntityType = CombatEntityType.Shark;
				}
			}

			if (data.KillerEntity != null)
			{
				string killerPrefabName = data.KillerEntity.ShortPrefabName.ToLower();

				if (killerPrefabName.Contains("corpse"))
				{
					data.KillerEntityType = CombatEntityType.Other;
				}
				else if (killerPrefabName.Contains("underwaterdweller"))
				{
					data.KillerEntityType = CombatEntityType.UnderwaterDweller;
				}
				else if (killerPrefabName.StartsWith("excavator"))
				{
					data.KillerEntityType = CombatEntityType.ExcavatorArm;
				}
				else if (killerPrefabName.StartsWith("carshredder"))
				{
					data.KillerEntityType = CombatEntityType.CarShredder;
				}
				else if (killerPrefabName.Contains("tunneldweller"))
				{
					data.KillerEntityType = CombatEntityType.TunnelDweller;
				}
				else if (killerPrefabName.Contains("bradleyapc"))
				{
					data.KillerEntityType = CombatEntityType.Bradley;
				}
				else if (killerPrefabName.Contains("shark"))
				{
					data.KillerEntityType = CombatEntityType.Shark;
				}
				else if (killerPrefabName.Contains("cactus"))
				{
					data.KillerEntityType = CombatEntityType.Cactus;
				}
				else if (killerPrefabName.Contains("locomotive"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}
				else if (killerPrefabName.Contains("workcart"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}
				else if (killerPrefabName.Contains("train"))
				{
					data.KillerEntityType = CombatEntityType.Train;
				}

			}
		}

		private void OnFlameThrowerBurn(FlameThrower flameThrower, BaseEntity baseEntity)
		{
			if (flameThrower == null || baseEntity == null) return;

			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.Flamethrower;
			flame.SourceEntity = flameThrower;
			flame.Initiator = flameThrower.GetOwnerPlayer();
		}

		private void OnFlameExplosion(FlameExplosive explosive, BaseEntity baseEntity)
		{
			if (explosive == null || baseEntity == null) return;

			var flame = baseEntity.gameObject.AddComponent<Flame>();
			flame.Source = Flame.FlameSource.IncendiaryProjectile;
			flame.SourceEntity = explosive;
			flame.Initiator = explosive.creatorEntity;
		}

		private void OnFireBallSpread(FireBall fireBall, BaseEntity newFire)
		{
			if (fireBall == null) return;

			var flame = fireBall.GetComponent<Flame>();
			if (flame == null) return;

			var newFlame = newFire.gameObject.AddComponent<Flame>();
			newFlame.Source = flame.Source;
			newFlame.SourceEntity = flame.SourceEntity;
			newFlame.Initiator = flame.Initiator;
		}

		private void OnFireBallDamage(FireBall fireBall, BaseCombatEntity target, HitInfo hitInfo)
        {
            if (hitInfo.Initiator == null)
			    hitInfo.Initiator = fireBall;
        }

		#endregion

		#region Death Messages

		private PluginConfiguration.DeathMessage GetDeathMessage(DeathData data)
		{
			foreach (var matchingStage in _messageMatchingStages)
			{
				var match = _configuration.Translations.Messages.Find(m => matchingStage.Invoke(m, data));

				if (match != null)
					return match;
			}

			return null;
		}

		private string GetDeathMessageString(PluginConfiguration.DeathMessage deathMessage)
		{
            if (deathMessage != null)
                return deathMessage.Messages.GetRandom((uint) DateTime.UtcNow.Millisecond);
            else return null;
        }

		private string PopulateMessageVariables(string message, DeathData data)
		{
			if (string.IsNullOrEmpty(message))
				return null;

			var replacements = new Dictionary<string, string>
			{
				["victim"] = GetCustomizedEntityName(data.VictimEntity, data.VictimEntityType),
                ["position"] = GetPositionString(data.HitInfo.HitPositionWorld),
                ["mapgrid"] = MapHelper.GridToString(MapHelper.PositionToGrid(data.HitInfo.HitPositionWorld))
			};

			if (data.KillerEntityType != CombatEntityType.None)
			{
				replacements.Add("killer", GetCustomizedEntityName(data.KillerEntity, data.KillerEntityType));
				replacements.Add("bodypart", GetCustomizedBodypartName(data.HitInfo));

				if (data.KillerEntity != null)
				{
					var distance = data.KillerEntity.Distance(data.VictimEntity);
					replacements.Add("distance", GetDistance(distance, _configuration.UseMetricDistance));
				}

				if ((data.KillerEntityType == CombatEntityType.Player) || (data.KillerEntityType == CombatEntityType.CustomNPC) || (data.KillerEntityType == CombatEntityType.Drone))
				{
					replacements.Add("hp", data.KillerEntity.Health().ToString("#0.#"));
					replacements.Add("weapon", GetCustomizedWeaponName(data));
					replacements.Add("attachments", string.Join(", ", GetCustomizedAttachmentNames(data.HitInfo)));
				}
				else if (data.KillerEntityType == CombatEntityType.Turret
						 || data.KillerEntityType == CombatEntityType.Lock
						 || data.KillerEntityType == CombatEntityType.Trap
						 || data.KillerEntityType == CombatEntityType.SAM)
				{
					replacements.Add("owner",
						covalence.Players.FindPlayerById(data.KillerEntity.OwnerID.ToString())?.Name ?? "Unknown Owner"
					);
				}
			}

			message = InsertPlaceholderValues(message, replacements);

			replacements = null;
			return message;
		}

		private struct DeathData
		{
			public CombatEntityType VictimEntityType { get; set; }
			[JsonIgnore] public BaseCombatEntity VictimEntity { get; set; }

			public CombatEntityType KillerEntityType { get; set; }
			[JsonIgnore] public BaseEntity KillerEntity { get; set; }

			public DamageType DamageType { get; set; }
			[JsonIgnore] public HitInfo HitInfo { get; set; }

			public Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
			{
				["VictimEntityType"] = VictimEntityType,
				["VictimEntity"] = VictimEntity,
				["KillerEntityType"] = KillerEntityType,
				["KillerEntity"] = KillerEntity,
				["DamageType"] = DamageType,
				["HitInfo"] = HitInfo
			};
		}

		#endregion

		#region Entity Identification

		private CombatEntityType GetCombatEntityType(BaseEntity entity)
		{
			
			if (entity == null)
				return CombatEntityType.None;
			
			string entityTypeName = entity.GetType().Name;
			if (entityTypeName != "ScientistNPC" && _combatEntityTypes.TryGetValue( entityTypeName, out var entityType ))
				return entityType;

			if (_combatEntityTypes.TryGetValue( entity.ShortPrefabName, out var type ))
				return type;

			//For those plugins that do not correctly type ZombieNPCs as Zombies.
			if (entityTypeName.StartsWith("Zombie"))
				return CombatEntityType.ZombieNPC;
			
            //For a plugin that defined a CustomPet entity name, we redefine it as a CustonNPC.
			if (entityTypeName.StartsWith("CustomPet"))
				return CombatEntityType.CustomNPC;

 			switch (entity)
			{
				case ScientistNPC:
                case ScientistNPC2:
				{
					if (entity.ShortPrefabName.EndsWith("heavy"))
						return CombatEntityType.HeavyScientist;
					return CombatEntityType.Scientist;
				}

				case Zombie:
					return CombatEntityType.ZombieNPC;
				
				case PatrolHelicopter:
					return CombatEntityType.Helicopter;
				
				case BaseAnimalNPC:
				case Wolf2:
				case Panther:
				case Tiger:
				case Crocodile:
				case SnakeHazard:
				case RidableHorse:
				case FarmableAnimal:
					return CombatEntityType.Animal;
				
				case SamSite:
					return CombatEntityType.SAM;
				
				case BaseOven:
					return CombatEntityType.HeatSource;
				
				case SimpleBuildingBlock:
					return CombatEntityType.ExternalWall;
				
				case Barricade:
					return CombatEntityType.Barricade;
				
				case BaseTrap:
				case IOEntity:
				case GunTrap:
					return CombatEntityType.Trap;

				case GingerbreadNPC:
					return CombatEntityType.GingerbreadNPC;

				case ScarecrowNPC:
					return CombatEntityType.ScarecrowNPC;
				
				case Minicopter:
					return CombatEntityType.Minicopter;
				
				case ScrapTransportHelicopter:
					return CombatEntityType.ScrapTransportHelicopter;
				
				case AttackHelicopter:
					return CombatEntityType.AttackHelicopter;
				
				case BeeSwarmAI:
					return CombatEntityType.BeeSwarm;
				
				case BaseFishNPC:
					if (entity.ShortPrefabName.ToLower().Contains("shark"))
						return CombatEntityType.Shark;
					else return CombatEntityType.Animal;
					
				case SimpleShark:
					return CombatEntityType.Shark;

				case NPCShopKeeper:
					return CombatEntityType.NPCShopKeeper;

				case ExcavatorArm:
					return CombatEntityType.ExcavatorArm;

				case LargeShredder:
					return CombatEntityType.CarShredder;

				case Drone:
					return CombatEntityType.Drone;

				case ResourceEntity:
				{
					if (entity.ShortPrefabName.EndsWith("cactus"))
						return CombatEntityType.Cactus;
					else return CombatEntityType.Other;
				}

				case TrainEngine:
				case TrainCar:
					return CombatEntityType.Train;

				case MagnetCrane:
					return CombatEntityType.MagnetCrane;

				case ElevatorLift:
					return CombatEntityType.Elevator;
				
				case HotAirBalloon:
					return CombatEntityType.HotAirBalloon;

				case BatteringRam:
					return CombatEntityType.BatteringRam;

                case Tugboat:
                    return CombatEntityType.Tugboat;

                case CargoShip:
                    return CombatEntityType.CargoShip;

                case DigitSendCodeLock:
                    return CombatEntityType.Lock;

				default:
					return CombatEntityType.Other;
			}
		}

		private string GetCustomizedEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			var name = GetEntityName(entity, combatEntityType);

			if (string.IsNullOrEmpty(name))
				return null;

			// Don't load player names into config
			if (combatEntityType == CombatEntityType.Player)
				return name;

			if (!_configuration.Translations.Names.ContainsKey(name))
			{
				_configuration.Translations.Names.Add(name, name);
                _translationData = _configuration.Translations;
                MarkTranslationDataDirty();
			}

			return _configuration.Translations.Names[name];
		}

		private string GetEntityName(BaseEntity entity, CombatEntityType combatEntityType)
		{
			// Entity may be null for helicopter or bradley, see HandleExceptions(...)
			if (entity == null &&
				combatEntityType != CombatEntityType.Helicopter &&
				combatEntityType != CombatEntityType.Bradley)
				return null;

			switch (combatEntityType)
			{
				case CombatEntityType.Player:
					return StripRichText(entity.ToPlayer().displayName);
				
				case CombatEntityType.Murderer:
				case CombatEntityType.ScarecrowNPC:
				case CombatEntityType.Scientist:
				case CombatEntityType.HeavyScientist:
				case CombatEntityType.ZombieNPC:
				case CombatEntityType.GingerbreadNPC:
				case CombatEntityType.NPCShopKeeper:
					var name = entity.ToPlayer()?.displayName;
					
					if ((combatEntityType == CombatEntityType.HeavyScientist) && (name == "Scientist"))
						name = "Heavy Scientist";
					
					if (!string.IsNullOrEmpty(name) && name != entity.ToPlayer()?.userID.ToString())
					{
						return name;
					}

					if (!_enemyPrefabs.ContainsKey(entity.ShortPrefabName))
					{
						return combatEntityType.ToString();
					}

					break;

				case CombatEntityType.TunnelDweller:
					return "Tunnel Dweller";

				case CombatEntityType.UnderwaterDweller:
					return "Underwater Dweller";

				case CombatEntityType.Helicopter:
					return "Patrol Helicopter";

				case CombatEntityType.Bradley:
					return "Bradley APC";

				case CombatEntityType.Sentry:
					return "Sentry";
				
				case CombatEntityType.Fire:
					return entity.creatorEntity?.ToPlayer()?.displayName ?? "Fire";
					
				case CombatEntityType.Minicopter:
					return "Minicopter";
				
				//NOTE: Scrappy reporting can't work because a death results in two death notices. One is where the Scrappy is both the killer and the victim,
				//and the other is where it shows the player as having committed suicide, even though they did not. There's no apparent way to tie
				//the two together.
				case CombatEntityType.ScrapTransportHelicopter:
					return "Scrap Transport Helicopter";
				
				case CombatEntityType.AttackHelicopter:
					return "AttackHelicopter";
				
				case CombatEntityType.BeeSwarm:
					return "BeeSwarm";
				
				case CombatEntityType.Shark:
					return "Shark";

				case CombatEntityType.ExcavatorArm:
					return "Giant Excavator Arm";

				case CombatEntityType.CarShredder:
					return "Car Shredder";

				case CombatEntityType.Drone:
					return "Drone";

				case CombatEntityType.Cactus:
					return "Cactus";

				case CombatEntityType.Train:
					return "Train";

				case CombatEntityType.MagnetCrane:
					return "Magnet Crane";

				case CombatEntityType.Elevator:
					return "Elevator";

				case CombatEntityType.HotAirBalloon:
					return "Hot Air Balloon";

				case CombatEntityType.BatteringRam:
					return "Battering Ram";

                case CombatEntityType.Tugboat:
                    return "Tugboat";

                case CombatEntityType.CargoShip:
                    return "CargoShip";

                case CombatEntityType.CustomNPC:
                    if (entity != null && entity is BasePlayer)
                        return entity.ToPlayer().displayName;
                    else return "CustomNPC";
			}

			if (_enemyPrefabs.TryGetValue( entity.ShortPrefabName, out string entityName ))
				return entityName;

			return GetFriendlyRuntimeName(entity);
		}

        private string GetFriendlyRuntimeName(BaseEntity entity)
        {
            if (entity == null)
                return "Unknown";

            string prefab = entity.ShortPrefabName;
            if (!string.IsNullOrWhiteSpace(prefab))
            {
                string normalized = prefab
                    .Replace(".entity", string.Empty)
                    .Replace(".deployed", string.Empty)
                    .Replace("_", " ")
                    .Replace(".", " ");
                string friendly = HumanizePascalCase(normalized);
                if (!string.IsNullOrWhiteSpace(friendly))
                    return friendly;
            }

            return HumanizePascalCase(entity.GetType().Name);
        }

		internal enum CombatEntityType
		{
			Helicopter = 0,
			Bradley = 1,
			Animal = 2,
			Murderer = 3,
			Scientist = 4,
			Player = 5,
			Trap = 6,
			Turret = 7,
			Barricade = 8,
			ExternalWall = 9,
			HeatSource = 10,
			Fire = 11,
			Lock = 12,
			Sentry = 13,
			Other = 14,
			None = 15,
			ScarecrowNPC = 16,
			TunnelDweller = 17,
			UnderwaterDweller = 18,
			ZombieNPC = 19,
			GingerbreadNPC = 20,
			HeavyScientist = 21,
			Minicopter = 22,
			ScrapTransportHelicopter = 23,
			AttackHelicopter = 24,
			BeeSwarm = 25,
			SAM = 26,
			Shark = 27,
			NPCShopKeeper = 28,
			ExcavatorArm = 29,
			Drone = 30,
			CarShredder = 31,
			Cactus = 32,
			Train = 33,
			MagnetCrane = 34,
			Elevator = 35,
			HotAirBalloon = 36,
			BatteringRam = 37,
            Tugboat = 38,
            CargoShip = 39,
            CustomNPC = 40
		}

		#endregion

		#region Workarounds and Inconsistency Handling

		private void HandleInconsistencies(ref DeathData data)
		{
			// Deaths of other entity types are not of interest and might cause errors
			if (data.VictimEntityType == CombatEntityType.Other)
				return;

			if (data.KillerEntity is FireBall)
			{
				data.DamageType = DamageType.Heat;
				if (data.KillerEntity.ShortPrefabName.StartsWith("flameturret"))
					data.KillerEntityType = CombatEntityType.Turret;
			}

			// If the killer entity is null, but a weapon is given, we might be able to fall back to the parent entity of that weapon
			// Notably for the auto turret after the changes it has had
			if (data.KillerEntity == null && data.HitInfo?.Weapon != null)
			{
				data.KillerEntity = data.HitInfo.Weapon.GetParentEntity();
				data.KillerEntityType = GetCombatEntityType(data.KillerEntity);
			}

			//If the killer is a SAM Site
			if (data.KillerEntity == null && data.HitInfo != null && data.HitInfo.Initiator != null &&
			    data.HitInfo.Initiator.ShortPrefabName.StartsWith("sam_site_turret_deployed"))
			{
				data.KillerEntity = data.VictimEntity.lastAttacker ?? data.HitInfo.Initiator;
				// data.KillerEntity = data.HitInfo.Initiator;
				data.KillerEntityType = CombatEntityType.SAM;
			}

			// Get previous attacker when bleeding out
			if (data.VictimEntityType == CombatEntityType.Player &&
				(data.DamageType == DamageType.Bleeding || data.HitInfo == null))
			{
				var userId = data.VictimEntity.ToPlayer().userID;

				if (_previousAttack.ContainsKey(userId))
				{
					var attack = _previousAttack[userId];
					data.KillerEntity = attack.Attacker;
					data.KillerEntityType = GetCombatEntityType(data.KillerEntity);

					// Restore previous hitInfo for weapon determination
					if (attack.HitInfo != null)
						data.HitInfo = attack.HitInfo;

					// Use previous damagetype if this is a self inflicted death,
					// so falling to death etc. is also shown when wounded and bleeding out
					if (data.KillerEntity == null || data.KillerEntity == data.VictimEntity)
						data.DamageType = attack.DamageType;
					else
						data.DamageType = DamageType.Bleeding;
				}
			}

			if (data.KillerEntityType != CombatEntityType.None && data.KillerEntity != null)
			{
				// Workaround for deaths caused by flamethrower or rocket fire 
				var flame = data.KillerEntity.gameObject.GetComponent<Flame>();
				if (flame != null && flame.Initiator != null)
				{
					data.KillerEntity = flame.Initiator;
					data.KillerEntityType = CombatEntityType.Player;
					return;
				}
			}

			// Bradley kill with main cannon
			if (data.HitInfo?.WeaponPrefab?.ShortPrefabName == "maincannonshell")
			{
				data.KillerEntityType = CombatEntityType.Bradley;
				return;
			}

			if (data.HitInfo?.WeaponPrefab?.ShortPrefabName?.StartsWith("rocket_heli") ?? false)
			{
				data.KillerEntityType = CombatEntityType.Helicopter;
				return;
			}
			
			// Vehicle Kills
			if ((data.KillerEntityType == CombatEntityType.Player
				&& data.DamageType == DamageType.Generic
				&& data.KillerEntity.ToPlayer().isMounted) ||
				(data.KillerEntityType == CombatEntityType.Train &&
				 data.DamageType == DamageType.Generic &&
				 data.VictimEntityType == CombatEntityType.Player))
			{
				data.DamageType = DamageType.Collision;
				return;
			}
		}

		private struct AttackInfo
		{
			public HitInfo HitInfo { get; set; }
			public DamageType DamageType { get; set; }
			public BaseEntity Attacker { get; set; }
		}

		private class Flame : MonoBehaviour
		{
			public FlameSource Source { get; set; }
			public BaseEntity SourceEntity { get; set; }
			public BaseEntity Initiator { get; set; }

			public enum FlameSource
			{
				Flamethrower,
				IncendiaryProjectile
			}
		}

		#endregion

		#region Weapons

		private string GetCustomizedWeaponName(DeathData deathData)
		{
			var name = GetWeaponName(deathData);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Weapons.ContainsKey(name))
			{
				_configuration.Translations.Weapons.Add(name, name);
                _translationData = _configuration.Translations;
                MarkTranslationDataDirty();
			}

			return _configuration.Translations.Weapons[name];
		}

		private string GetWeaponName(DeathData deathData)
		{
			if (deathData.HitInfo == null)
				return null;

			Item item = deathData.HitInfo.Weapon?.GetItem();
			/*var parentEntity = hitInfo.Weapon?.GetParentEntity();
			Item item = null;

			if (parentEntity is BasePlayer)
			{
				(parentEntity as BasePlayer).inventory.FindItemUID(hitInfo.Weapon.ownerItemUID);
			}
			else if (parentEntity is ContainerIOEntity)
			{
				(parentEntity as ContainerIOEntity).inventory.FindItemByUID(hitInfo.Weapon.ownerItemUID);
			}*/

			if (item != null)
				return item.info.displayName.english;

			string prefab = null;
            try {
                prefab = deathData.HitInfo.Initiator?.GetComponent<Flame>()?.SourceEntity?.ShortPrefabName ??
						 deathData.HitInfo.WeaponPrefab?.ShortPrefabName;
            }
            catch (Exception e)
            {
                LogOutput($"Exception in GetWeaponName: {e.ToString()}");
                prefab = null;
            }

			if (prefab != null)
			{
				if (_weaponPrefabs.TryGetValue(prefab, out string weaponName))
					return weaponName;

                if (_localWeaponCatalog.TryGetValue(prefab, out string localWeaponName))
                    return localWeaponName;

                string normalizedPrefab = prefab
                    .Replace(".entity", string.Empty)
                    .Replace(".deployed", string.Empty);
                if (_localWeaponCatalog.TryGetValue(normalizedPrefab, out localWeaponName))
                    return localWeaponName;

                normalizedPrefab = normalizedPrefab.Replace("_", " ").Replace(".", " ");
				return HumanizePascalCase(normalizedPrefab);
			}
			
			//Added by Terceran 20250215. Ballistas are a special case because they are considered a vehicle and
			//not a weapon.
			if (deathData.HitInfo.ProjectilePrefab?.name?.StartsWith("ballista") ?? false)
			{
				return "Ballista";
			}

			// Vehicles are the only thing we classify as a weapon, while not being classified as such by the game.
			if (deathData.DamageType == DamageType.Collision)
			{
				return "Vehicle";
			}

			return null;
		}

		private string[] GetCustomizedAttachmentNames(HitInfo info)
		{
			var items = info?.Weapon?.GetItem()?.contents?.itemList;

			if (items == null)
			{
				return Array.Empty<string>();
			}

			return items.Select(i => GetCustomizedAttachmentName(i.info.displayName.english)).ToArray();
		}

		private string GetCustomizedAttachmentName(string name)
		{
			if (!_configuration.Translations.Attachments.ContainsKey(name))
			{
				_configuration.Translations.Attachments.Add(name, name);
                _translationData = _configuration.Translations;
                MarkTranslationDataDirty();
			}

			return _configuration.Translations.Attachments[name];
		}

		#endregion

		#region Bodyparts

		private string GetCustomizedBodypartName(HitInfo hitInfo)
		{
			var name = GetBodypartName(hitInfo);

			if (string.IsNullOrEmpty(name))
				return null;

			if (!_configuration.Translations.Bodyparts.ContainsKey(name))
			{
				_configuration.Translations.Bodyparts.Add(name, name);
                _translationData = _configuration.Translations;
                MarkTranslationDataDirty();
			}

			return _configuration.Translations.Bodyparts[name];
		}

		private string GetBodypartName(HitInfo hitInfo)
		{
			var hitArea = hitInfo?.boneArea ?? (HitArea) (-1);
			return (int) hitArea == -1 ? "Body" : hitArea.ToString();
		}

		#endregion

        private void LoadTranslationData()
        {
            _translationData =
                LoadData(TranslationDataKey, delegate { return new PluginConfiguration.Translation(); }) ??
                new PluginConfiguration.Translation();

            if (IsTranslationDataEmpty(_translationData))
            {
                PluginConfiguration.Translation legacy =
                    LoadData(LegacyTranslationDataKey, delegate { return new PluginConfiguration.Translation(); });
                if (!IsTranslationDataEmpty(legacy))
                {
                    _translationData = legacy;
                    SaveTranslationData();
                    LogInformation("Data", "Migrated translation data to RogueRust/RogueRustDeathNotes/killer-data.json.");
                }
            }
        }

        private static bool IsTranslationDataEmpty(PluginConfiguration.Translation data)
        {
            return data == null ||
                   ((data.Messages == null || data.Messages.Count == 0) &&
                    (data.Names == null || data.Names.Count == 0) &&
                    (data.Bodyparts == null || data.Bodyparts.Count == 0) &&
                    (data.Weapons == null || data.Weapons.Count == 0) &&
                    (data.Attachments == null || data.Attachments.Count == 0));
        }

        private void SaveTranslationData()
        {
            if (_translationData == null)
                _translationData = new PluginConfiguration.Translation();

            SaveData(TranslationDataKey, _translationData);
        }

        private void EnsureRogueRustLanguageFile()
        {
            try
            {
                Dictionary<string, string> defaults = new Dictionary<string, string>
                {
                    ["Distance Unit Singular"] = "meter",
                    ["Distance Unit Plural"] = "meters",
                    ["Toggle On"] = "RogueRustDeathNotes enabled.",
                    ["Toggle Off"] = "RogueRustDeathNotes disabled."
                };

                string langRoot = Interface.Oxide.LangDirectory;
                HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };

                if (Directory.Exists(langRoot))
                {
                    string[] dirs = Directory.GetDirectories(langRoot, "*", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        string language = Path.GetFileName(dirs[i]);
                        if (!string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                            languages.Add(language);
                    }
                }

                string oldRoot = Path.Combine(langRoot, "RogueRust", "RogueRustDeathNotes");
                if (Directory.Exists(oldRoot))
                {
                    string[] oldFiles = Directory.GetFiles(oldRoot, "*.json", SearchOption.TopDirectoryOnly);
                    for (int i = 0; i < oldFiles.Length; i++)
                        languages.Add(Path.GetFileNameWithoutExtension(oldFiles[i]));
                }

                foreach (string language in languages)
                {
                    string root = Path.Combine(langRoot, language, "RogueRust", "RogueRustDeathNotes");
                    Directory.CreateDirectory(root);
                    string target = Path.Combine(root, "messages.json");
                    if (File.Exists(target))
                        continue;

                    string oldRogue = Path.Combine(oldRoot, language + ".json");
                    string legacy = Path.Combine(langRoot, language, Name + ".json");

                    if (File.Exists(oldRogue))
                        File.Copy(oldRogue, target, false);
                    else if (File.Exists(legacy))
                        File.Copy(legacy, target, false);
                    else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(target, JsonConvert.SerializeObject(defaults, Formatting.Indented));
                }
            }
            catch (Exception exception)
            {
                LogWarning("Localization", "Could not initialize RogueRust language files: " + exception.Message);
            }
        }

        private void LoadPlayerSettings()
        {
            _playerSettings =
                LoadData(PlayerSettingsDataKey, delegate { return new PlayerSettingsData(); }) ??
                new PlayerSettingsData();

            if (_playerSettings.Players == null)
                _playerSettings.Players = new Dictionary<ulong, PlayerNoticeSettings>();

            if (_playerSettings.Players.Count == 0)
            {
                PlayerSettingsData legacy =
                    LoadData(LegacyPlayerSettingsDataKey, delegate { return new PlayerSettingsData(); });
                if (legacy != null && legacy.Players != null && legacy.Players.Count > 0)
                {
                    _playerSettings = legacy;
                    SavePlayerSettings();
                    LogInformation("Data", "Migrated player settings to RogueRust/RogueRustDeathNotes/player-settings.json.");
                }
            }
        }

        private void SavePlayerSettings()
        {
            SaveData(PlayerSettingsDataKey, _playerSettings);
        }

        private PlayerNoticeSettings GetPlayerSettings(ulong userId)
        {
            PlayerNoticeSettings settings;
            if (!_playerSettings.Players.TryGetValue(userId, out settings))
            {
                settings = new PlayerNoticeSettings();
                _playerSettings.Players[userId] = settings;
            }
            return settings;
        }

        private bool IsPlayerNoticeEnabled(BasePlayer player)
        {
            return player == null || GetPlayerSettings(player.userID).Enabled;
        }

        private bool IsPlayerTeamOnly(BasePlayer player)
        {
            return player != null && GetPlayerSettings(player.userID).TeamOnly;
        }

        private string GetNativeGuiMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            string chatFormat = _configuration?.ChatFormat;
            if (!string.IsNullOrEmpty(chatFormat) && chatFormat.Contains("{message}"))
            {
                string prefix = chatFormat.Substring(0, chatFormat.IndexOf("{message}", StringComparison.Ordinal));
                string suffix = chatFormat.Substring(chatFormat.IndexOf("{message}", StringComparison.Ordinal) + "{message}".Length);

                if (!string.IsNullOrEmpty(prefix) && message.StartsWith(prefix, StringComparison.Ordinal))
                    message = message.Substring(prefix.Length);
                if (!string.IsNullOrEmpty(suffix) && message.EndsWith(suffix, StringComparison.Ordinal))
                    message = message.Substring(0, message.Length - suffix.Length);
            }

            // Compatibility cleanup for older/custom branded chat formats.
            message = Regex.Replace(message,
                @"^\s*\[?\s*(?:<color=[^>]+>)?RogueRustDeathNotes(?:</color>)?\s*[\]|:\-]*\s*",
                string.Empty,
                RegexOptions.IgnoreCase);

            return message.Trim();
        }

        private int GetNativeGuiFontSize(string message, int configuredSize)
        {
            int length = StripRichText(message ?? string.Empty).Length;
            if (length > 220) return Mathf.Clamp(configuredSize - 3, 9, 30);
            if (length > 165) return Mathf.Clamp(configuredSize - 2, 9, 30);
            if (length > 110) return Mathf.Clamp(configuredSize - 1, 9, 30);
            return configuredSize;
        }

        private string GetNativeGuiAnchorMin(string message, string configuredAnchorMin, string configuredAnchorMax)
        {
            string[] minParts = configuredAnchorMin.Split(' ');
            string[] maxParts = configuredAnchorMax.Split(' ');
            if (minParts.Length != 2 || maxParts.Length != 2)
                return configuredAnchorMin;

            if (!float.TryParse(minParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float minX) ||
                !float.TryParse(maxParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float maxY))
                return configuredAnchorMin;

            int length = StripRichText(message ?? string.Empty).Length;
            float height = length > 220 ? 0.145f :
                           length > 165 ? 0.125f :
                           length > 110 ? 0.105f :
                           length > 70  ? 0.085f : 0.070f;
            float minY = Mathf.Clamp01(maxY - height);

            return minX.ToString("0.###", CultureInfo.InvariantCulture) + " " +
                   minY.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private string GetEmbeddedDeathNotesIcon()
        {
            if (!string.IsNullOrWhiteSpace(_embeddedDeathNotesIcon))
                return _embeddedDeathNotesIcon;

            try
            {
                if (RogueServices.Instance.ImageLibrary.TryGetEmbeddedImage(
                    EmbeddedIconOwner, EmbeddedIconKey, EmbeddedIconResource, out string png) &&
                    !string.IsNullOrWhiteSpace(png))
                {
                    _embeddedDeathNotesIcon = png;
                    return _embeddedDeathNotesIcon;
                }
            }
            catch (Exception ex)
            {
                PrintWarning("RogueRustDeathNotes could not initialize its embedded NativeGUI icon: " + ex.Message);
            }

            return null;
        }

        private void ShowNativeGui(BasePlayer player, string message)
        {
            if (player == null || !player.IsConnected || !_configuration.OutputModules.EnableNativeGui)
                return;

            CuiHelper.DestroyUi(player, GuiName);

            PluginConfiguration.OutputModuleSettings ui = _configuration.OutputModules;
            string embeddedIcon = GetEmbeddedDeathNotesIcon();
            bool hasEmbeddedIcon = !string.IsNullOrWhiteSpace(embeddedIcon);
            CuiElementContainer container = new CuiElementContainer();
            string panel = container.Add(new CuiPanel
            {
                Image = { Color = ui.GuiBackgroundColor },
                RectTransform =
                {
                    AnchorMin = GetNativeGuiAnchorMin(message, ui.GuiAnchorMin, ui.GuiAnchorMax),
                    AnchorMax = ui.GuiAnchorMax
                },
                CursorEnabled = false
            }, "Overlay", GuiName);

            container.Add(new CuiPanel
            {
                Image = { Color = ui.GuiAccentColor },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.012 1" }
            }, panel);

            if (hasEmbeddedIcon)
            {
                container.Add(new CuiElement
                {
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent { Png = embeddedIcon, Color = "1 1 1 1" },
                        new CuiRectTransformComponent { AnchorMin = "0.028 0.18", AnchorMax = "0.145 0.82" }
                    }
                });
            }

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = ui.GuiTitle,
                    FontSize = ui.GuiTitleFontSize,
                    Align = TextAnchor.MiddleLeft,
                    Color = ui.GuiTitleColor,
                    Font = "robotocondensed-bold.ttf"
                },
                RectTransform = { AnchorMin = hasEmbeddedIcon ? "0.165 0.74" : "0.035 0.74", AnchorMax = "0.98 0.96" }
            }, panel);

            container.Add(new CuiLabel
            {
                Text =
                {
                    Text = message,
                    FontSize = GetNativeGuiFontSize(message, ui.GuiFontSize),
                    Align = TextAnchor.MiddleLeft,
                    Color = ui.GuiTextColor,
                    Font = "robotocondensed-regular.ttf"
                },
                RectTransform = { AnchorMin = hasEmbeddedIcon ? "0.165 0.08" : "0.035 0.08", AnchorMax = "0.98 0.75" }
            }, panel);

            CuiHelper.AddUi(player, container);
            ulong userId = player.userID;
            Debounce("deathnotes-gui-" + userId,
                TimeSpan.FromSeconds(ui.GuiDuration),
                delegate
                {
                    BasePlayer target = BasePlayer.FindByID(userId);
                    if (target != null)
                        CuiHelper.DestroyUi(target, GuiName);
                });
        }

        private void DestroyGuiForAll()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
                if (player != null) CuiHelper.DestroyUi(player, GuiName);
        }

		#region Helper

        public bool ShowPatrolHeliTags()
        {
            return (_configuration.ShowPatrolHeliTagsInConsole || _configuration.ShowPatrolHeliTagsInChat || _configuration.ShowPatrolHeliTagsInNotify || _configuration.ShowPatrolHeliTagsInUINotify);
        }


        public bool ShowBradleyTags()
        {
            return (_configuration.ShowBradleyTagsInConsole || _configuration.ShowBradleyTagsInChat || _configuration.ShowBradleyTagsInNotify || _configuration.ShowBradleyTagsInUINotify);
        }


        [RogueCommand("dn",
            Aliases = new[] { "dnt", "roguerust.deathnotes" },
            Description = "Toggles Death Notes visibility or team-only mode.",
            Usage = "/dn [on|off|toggle|teamonly|teamoff]", Category = "DeathNotes",
            CooldownSeconds = 0.5, AllowConsole = false)]
        private RogueCommandResult CmdDeathNotes(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail("This command is player-only.");
            if (!_configuration.CanPlayerUseDNCommand)
                return RogueCommandResult.Fail("The Death Notes toggle command is disabled.");

            PlayerNoticeSettings settings = GetPlayerSettings(player.userID);
            string[] args = context.Arguments ?? Array.Empty<string>();
            string arg = args.Length == 0 ? "toggle" : args[0].ToLowerInvariant();

            switch (arg)
            {
                case "toggle":
                    settings.Enabled = !settings.Enabled;
                    break;
                case "on":
                    settings.Enabled = true;
                    break;
                case "off":
                    settings.Enabled = false;
                    break;
                case "teamonly":
                    settings.TeamOnly = true;
                    settings.Enabled = true;
                    break;
                case "teamoff":
                    settings.TeamOnly = false;
                    break;
                default:
                    return RogueCommandResult.Fail("Usage: /dn [on|off|toggle|teamonly|teamoff]");
            }

            Debounce("deathnotes-player-settings", TimeSpan.FromSeconds(2), SavePlayerSettings);
            string status = settings.Enabled ? "ENABLED" : "DISABLED";
            string team = settings.TeamOnly ? " (TEAM ONLY)" : string.Empty;
            return RogueCommandResult.Ok("<color=#80D000>Death Notes</color> are <color=#00FF00>" +
                status + "</color>" + team + ".");
        }

		//Useful for debugging
		private static void LogDebug(string text)
		{
			if (_instance._configuration.DebugMode)
			{
				if (BasePlayer.activePlayerList.Count >= 1)
				{
					BasePlayer.activePlayerList[0].ConsoleMessage($"<color=orange>{text}</color>");
				}
			}
		}


        //Checks to see if the player has the specified Death Message permissions 
        private bool PlayersPassesPermsCheck(string playerIDString, HashSet<string> displayPerms)
        {
            if (displayPerms == null || displayPerms.Count <= 0)
                return true;

            string tmpPerm;
            foreach (string perm in displayPerms)
            {
                tmpPerm = perm.Trim().ToLower();
                if (!tmpPerm.StartsWith("deathnotes.") || tmpPerm.Equals("roguerustdeathnotes.cansee") || tmpPerm.Equals("roguerustdeathnotes.cantsee") || tmpPerm.Equals("roguerustdeathnotes.suppress") || tmpPerm.Equals("roguerustdeathnotes.seeteamonly"))
                    continue;
                
                if (Rogue.Permissions.Has(playerIDString, tmpPerm))
                    return true;
            }
            return false;
        }


        //Indicates if either the attacker or the victim in the DeathData parameter are on the same team as the supplied player
        private bool ArePlayersOnSameTeam(BasePlayer player, DeathData data)
        {
            if (player == null)
                return false;

            if ((data.KillerEntity is BasePlayer killer && killer != null) && (killer.userID == player.userID || (killer.currentTeam != 0 && player.currentTeam == killer.currentTeam)))
                return true;

            if ((data.VictimEntity is BasePlayer victim && victim != null) && ((victim.userID == player.userID) || (victim.currentTeam != 0 && player.currentTeam == victim.currentTeam)))
                return true;

            return false;
        }

		//Primarily for Rustcord interoperability, this indicates whether or not the death was PVP caused, or not.
		private bool IsDeathPVP(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Player || 
										 killerType == CombatEntityType.Trap || killerType == CombatEntityType.Turret ||
										 killerType == CombatEntityType.Barricade ||
										 killerType == CombatEntityType.ExternalWall ||
										 killerType == CombatEntityType.HeatSource ||
 									     killerType == CombatEntityType.Cactus ||
 									     killerType == CombatEntityType.Fire || killerType == CombatEntityType.Lock ||
                                         killerType == CombatEntityType.Other || killerType == CombatEntityType.None ||
										 killerType == CombatEntityType.SAM)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was animal caused, or not.
		private bool IsDeathAnimal(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return (((victimType == CombatEntityType.Animal || victimType == CombatEntityType.BeeSwarm ||
						 victimType == CombatEntityType.Shark) && killerType == CombatEntityType.Player) ||
                         (victimType == CombatEntityType.Player && (killerType == CombatEntityType.Animal ||
						 killerType == CombatEntityType.BeeSwarm || killerType == CombatEntityType.Shark)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was vehicle caused, or not.
		private bool IsDeathVehicle(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Helicopter ||
										      killerType == CombatEntityType.Bradley || killerType == CombatEntityType.Minicopter ||
                                              killerType == CombatEntityType.ScrapTransportHelicopter ||
										      killerType == CombatEntityType.AttackHelicopter ||
											  killerType == CombatEntityType.Drone ||
											  killerType == CombatEntityType.ExcavatorArm ||
											  killerType == CombatEntityType.Train ||
											  killerType == CombatEntityType.CarShredder ||
											  killerType == CombatEntityType.MagnetCrane ||
											  killerType == CombatEntityType.Elevator ||
											  killerType == CombatEntityType.HotAirBalloon ||
											  killerType == CombatEntityType.BatteringRam ||
                                              killerType == CombatEntityType.Tugboat ||
                                              killerType == CombatEntityType.CargoShip)) ||
                         					  ((victimType == CombatEntityType.Minicopter ||
											  victimType == CombatEntityType.ScrapTransportHelicopter ||
											  victimType == CombatEntityType.AttackHelicopter ||
 											  victimType == CombatEntityType.ExcavatorArm ||
										      victimType == CombatEntityType.CarShredder ||
											  victimType == CombatEntityType.MagnetCrane ||
											  victimType == CombatEntityType.Elevator ||
                          					  victimType == CombatEntityType.HotAirBalloon ||
											  victimType == CombatEntityType.BatteringRam ||
                                              victimType == CombatEntityType.Tugboat ||
                          					  victimType == CombatEntityType.CargoShip ||
                          					  victimType == CombatEntityType.Helicopter ||
											  victimType == CombatEntityType.Drone ||
											  victimType == CombatEntityType.Bradley) && (killerType == CombatEntityType.Player)));
		}

		//Primarily for Rustcord interoperability, this indicates whether or not the death was NPC caused, or not.
		private bool IsDeathNPC(int inVictimType, int inKillerType)
        {
			CombatEntityType victimType = (CombatEntityType)inVictimType;
			CombatEntityType killerType = (CombatEntityType)inKillerType;
			return ((victimType == CombatEntityType.Player && (killerType == CombatEntityType.Murderer ||
											  killerType == CombatEntityType.Scientist ||
											  killerType == CombatEntityType.Sentry ||
                                              killerType == CombatEntityType.ScarecrowNPC ||
											  killerType == CombatEntityType.TunnelDweller ||
											  killerType == CombatEntityType.UnderwaterDweller ||
                                              killerType == CombatEntityType.ZombieNPC ||
											  killerType == CombatEntityType.GingerbreadNPC ||
  											  killerType == CombatEntityType.HeavyScientist ||
  											  killerType == CombatEntityType.CustomNPC ||
                                              killerType == CombatEntityType.NPCShopKeeper)) ||
											  ((victimType == CombatEntityType.Murderer ||
											  victimType == CombatEntityType.Scientist ||
                                              victimType == CombatEntityType.Sentry ||
											  victimType == CombatEntityType.ScarecrowNPC ||
                                              victimType == CombatEntityType.TunnelDweller ||
											  victimType == CombatEntityType.UnderwaterDweller ||
                                              victimType == CombatEntityType.ZombieNPC ||
											  victimType == CombatEntityType.GingerbreadNPC ||
                                              victimType == CombatEntityType.HeavyScientist ||
                                              victimType == CombatEntityType.CustomNPC ||
											  victimType == CombatEntityType.NPCShopKeeper) &&
                                              (killerType == CombatEntityType.Player)));
		}

        private static string GetPositionString(Vector3 position)
        {
            if (position == null)
                return "NULL";
            return $"({position.x:F2},{position.y:F2},{position.z:F2})";
        }

		private static string GetDistance(float meters, bool useMetric)
		{
			double value = Math.Round(useMetric ? meters : meters * 3.28f, 1);
			string unit = value == 1 ? "meter" : "meters";

			return $"{value} {unit}";
		}

		private static string ApplyVariableFormat(string text, string variableName)
		{
			if (_instance._configuration.VariableFormats.ContainsKey(variableName))
			{
				var format = _instance._configuration.VariableFormats[variableName];
				text = format.Replace("{value}", text);
			}

			return text;
		}

		private static string InsertPlaceholderValues(string text, Dictionary<string, string> values)
		{
			foreach (var kvp in values)
			{
				string value = ApplyVariableFormat(kvp.Value, kvp.Key);
				if (string.IsNullOrEmpty(kvp.Value))
				{
					text = text.Replace($"{{{kvp.Key}}}", string.Empty);
				}
				else if (_instance._configuration.VariableColors.ContainsKey(kvp.Key))
				{
					var color = _instance._configuration.VariableColors[kvp.Key];
					text = text.Replace($"{{{kvp.Key}}}", $"<color={color}>{value}</color>");
					color = null;
				}
				else
				{
					text = text.Replace($"{{{kvp.Key}}}", value);
				}
			}

			return text;
		}

		private static string HumanizePascalCase(string text)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			var sb = new StringBuilder();

			foreach (char c in text)
			{
				if (char.IsUpper(c) && sb.Length != 0 && !char.IsUpper(sb[sb.Length - 1]))
					sb.Append(" ");

				sb.Append(c);
			}

			return sb.ToString();
		}

		private string StripRichText(string text)
		{
			if (string.IsNullOrEmpty(text))
				return string.Empty;

			text = _colorTagRegex.Replace(text, string.Empty);
			text = _sizeTagRegex.Replace(text, string.Empty);

			foreach (var richTextLiteral in _richTextLiterals)
				text = text.Replace(richTextLiteral, string.Empty);

			return text;
		}

		private static bool MatchesCombatEntityType(CombatEntityType combatEntityType, string text)
		{
			if (combatEntityType == CombatEntityType.None && text == "-")
				return true;

			return combatEntityType.ToString().Equals(text);
		}

		private static bool MatchesDamageType(DamageType damageType, string text)
		{
			return damageType.ToString().Equals(text);
		}

		#endregion

		#region Configuration

		protected override void LoadDefaultMessages()
		{
            // Runtime language catalog: lang/<language>/RogueRust/RogueRustDeathNotes/messages.json.
		}

		protected override void LoadDefaultConfig() => PrintWarning("Generating new configuration file...");

		private sealed class PluginConfiguration
		{
			[JsonIgnore] public Translation Translations = new Translation();

            [JsonProperty("Formatting Settings", Order = 100)] public FormattingSettings Formatting = new FormattingSettings();
            [JsonProperty("Output Settings", Order = 200)] public OutputSettings Output = new OutputSettings();
            [JsonProperty("Player Control Settings", Order = 300)] public PlayerControlSettings PlayerControls = new PlayerControlSettings();
            [JsonProperty("Patrol Helicopter Settings", Order = 400)] public PatrolHelicopterSettings PatrolHelicopter = new PatrolHelicopterSettings();
            [JsonProperty("Bradley APC Settings", Order = 500)] public BradleyApcSettings BradleyApc = new BradleyApcSettings();
            [JsonProperty("General Settings", Order = 600)] public GeneralSettings General = new GeneralSettings();
            [JsonProperty("Developer Settings", Order = 700)] public DeveloperSettings Developer = new DeveloperSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            public sealed class FormattingSettings
            {
                [JsonProperty("Variable Formats")] public Dictionary<string, string> VariableFormats = new Dictionary<string, string> { ["attachments"] = " ({value})" };
                [JsonProperty("Variable Colors")] public Dictionary<string, string> VariableColors = new Dictionary<string, string>
                {
                    ["killer"]="#C4FF00", ["victim"]="#C4FF00", ["weapon"]="#C4FF00", ["attachments"]="#C4FF00",
                    ["distance"]="#C4FF00", ["owner"]="#C4FF00", ["position"]="#C4FF00", ["mapgrid"]="#C4FF00"
                };
                [JsonProperty("Chat Message Format ({message} is replaced by the death notification)")]
                public string ChatFormat = "<color=#838383>[<color=#80D000>RogueRustDeathNotes</color>] {message}</color>";
            }

            public sealed class OutputSettings
            {
                [JsonProperty("Integrated Output Modules")] public OutputModuleSettings Modules = new OutputModuleSettings();
                [JsonProperty("Notify Message Type")] public int NotifyMessageType;
                [JsonProperty("UINotify Message Type")] public int UINotifyMessageType;
            }

            public sealed class PlayerControlSettings
            {
                [JsonProperty("Can Player Use /dn Command")] public bool CanPlayerUseDNCommand = true;
            }

            public sealed class PatrolHelicopterSettings
            {
                [JsonProperty("Display Permissions (Display For All Players If Empty)")] public HashSet<string> DisplayPerms = new HashSet<string>();
                [JsonProperty("Show Tags in Console")] public bool ShowTagsInConsole = true;
                [JsonProperty("Show Tags in Chat")] public bool ShowTagsInChat = true;
                [JsonProperty("Show Tags in Notify")] public bool ShowTagsInNotify;
                [JsonProperty("Show Tags in UINotify")] public bool ShowTagsInUINotify;
                [JsonProperty("Tag Message")] public string TagMessage = "{killer} has tagged the {victim} with their {weapon} over a distance of {distance}.";
            }

            public sealed class BradleyApcSettings
            {
                [JsonProperty("Display Permissions (Display For All Players If Empty)")] public HashSet<string> DisplayPerms = new HashSet<string>();
                [JsonProperty("Show Tags in Console")] public bool ShowTagsInConsole = true;
                [JsonProperty("Show Tags in Chat")] public bool ShowTagsInChat = true;
                [JsonProperty("Show Tags in Notify")] public bool ShowTagsInNotify;
                [JsonProperty("Show Tags in UINotify")] public bool ShowTagsInUINotify;
                [JsonProperty("Tag Message")] public string TagMessage = "{killer} has tagged the {victim} with their {weapon} over a distance of {distance}.";
            }

            public sealed class GeneralSettings
            {
                [JsonProperty("Message Broadcast Radius (in meters)")] public int MessageRadius = -1;
                [JsonProperty("Use Metric Distance")] public bool UseMetricDistance = true;
                [JsonProperty("Require Permission (roguerustdeathnotes.cansee)")] public bool RequirePermission;
            }

            public sealed class DeveloperSettings
            {
                [JsonProperty("Debug Mode Enabled")] public bool DebugMode;
            }

            public sealed class OutputModuleSettings
            {
                [JsonProperty("Enable native GUI notifications")] public bool EnableNativeGui;
                [JsonProperty("GUI - Notification display duration in seconds")] public float GuiDuration = 6f;
                [JsonProperty("GUI - Background RGBA color (Rust CUI format: red green blue alpha, each 0-1)")] public string GuiBackgroundColor = "0.055 0.06 0.07 0.94";
                [JsonProperty("GUI - Death message text RGBA color (Rust CUI format: red green blue alpha, each 0-1)")] public string GuiTextColor = "0.90 0.92 0.94 1";
                [JsonProperty("GUI - Screen position minimum anchor (x y, each 0-1)")] public string GuiAnchorMin = "0.665 0.825";
                [JsonProperty("GUI - Screen position maximum anchor (x y, each 0-1)")] public string GuiAnchorMax = "0.985 0.945";
                [JsonProperty("GUI - Death message font size")] public int GuiFontSize = 12;
                [JsonProperty("GUI - Title text shown above the death message")] public string GuiTitle = "ROGUERUSTDEATHNOTES";
                [JsonProperty("GUI - Title font size")] public int GuiTitleFontSize = 10;
                [JsonProperty("GUI - Title RGBA color (Rust CUI format: red green blue alpha, each 0-1)")] public string GuiTitleColor = "0.72 0.78 0.82 1";
                [JsonProperty("GUI - Left accent bar RGBA color (Rust CUI format: red green blue alpha, each 0-1)")] public string GuiAccentColor = "0.50 0.82 0.00 1";
            }

            // Compatibility accessors keep the existing death engine untouched.
            [JsonIgnore] public Dictionary<string,string> VariableFormats { get => Formatting.VariableFormats; set => Formatting.VariableFormats=value; }
            [JsonIgnore] public Dictionary<string,string> VariableColors { get => Formatting.VariableColors; set => Formatting.VariableColors=value; }
            [JsonIgnore] public string ChatFormat { get => Formatting.ChatFormat; set => Formatting.ChatFormat=value; }
            [JsonIgnore] public OutputModuleSettings OutputModules { get => Output.Modules; set => Output.Modules=value; }
            [JsonIgnore] public int NotifyMessageType { get => Output.NotifyMessageType; set => Output.NotifyMessageType=value; }
            [JsonIgnore] public int UINotifyMessageType { get => Output.UINotifyMessageType; set => Output.UINotifyMessageType=value; }
            [JsonIgnore] public bool CanPlayerUseDNCommand { get => PlayerControls.CanPlayerUseDNCommand; set => PlayerControls.CanPlayerUseDNCommand=value; }
            [JsonIgnore] public HashSet<string> PatrolHeliDisplayPerms { get => PatrolHelicopter.DisplayPerms; set => PatrolHelicopter.DisplayPerms=value; }
            [JsonIgnore] public bool ShowPatrolHeliTagsInConsole { get => PatrolHelicopter.ShowTagsInConsole; set => PatrolHelicopter.ShowTagsInConsole=value; }
            [JsonIgnore] public bool ShowPatrolHeliTagsInChat { get => PatrolHelicopter.ShowTagsInChat; set => PatrolHelicopter.ShowTagsInChat=value; }
            [JsonIgnore] public bool ShowPatrolHeliTagsInNotify { get => PatrolHelicopter.ShowTagsInNotify; set => PatrolHelicopter.ShowTagsInNotify=value; }
            [JsonIgnore] public bool ShowPatrolHeliTagsInUINotify { get => PatrolHelicopter.ShowTagsInUINotify; set => PatrolHelicopter.ShowTagsInUINotify=value; }
            [JsonIgnore] public string PatrolHeliTagMessage { get => PatrolHelicopter.TagMessage; set => PatrolHelicopter.TagMessage=value; }
            [JsonIgnore] public HashSet<string> BradleyDisplayPerms { get => BradleyApc.DisplayPerms; set => BradleyApc.DisplayPerms=value; }
            [JsonIgnore] public bool ShowBradleyTagsInConsole { get => BradleyApc.ShowTagsInConsole; set => BradleyApc.ShowTagsInConsole=value; }
            [JsonIgnore] public bool ShowBradleyTagsInChat { get => BradleyApc.ShowTagsInChat; set => BradleyApc.ShowTagsInChat=value; }
            [JsonIgnore] public bool ShowBradleyTagsInNotify { get => BradleyApc.ShowTagsInNotify; set => BradleyApc.ShowTagsInNotify=value; }
            [JsonIgnore] public bool ShowBradleyTagsInUINotify { get => BradleyApc.ShowTagsInUINotify; set => BradleyApc.ShowTagsInUINotify=value; }
            [JsonIgnore] public string BradleyTagMessage { get => BradleyApc.TagMessage; set => BradleyApc.TagMessage=value; }
            [JsonIgnore] public int MessageRadius { get => General.MessageRadius; set => General.MessageRadius=value; }
            [JsonIgnore] public bool UseMetricDistance { get => General.UseMetricDistance; set => General.UseMetricDistance=value; }
            [JsonIgnore] public bool RequirePermission { get => General.RequirePermission; set => General.RequirePermission=value; }
            [JsonIgnore] public bool DebugMode { get => Developer.DebugMode; set => Developer.DebugMode=value; }

			public void LoadDefaults()
			{
                Formatting ??= new FormattingSettings();
                Output ??= new OutputSettings();
                PlayerControls ??= new PlayerControlSettings();
                PatrolHelicopter ??= new PatrolHelicopterSettings();
                BradleyApc ??= new BradleyApcSettings();
                General ??= new GeneralSettings();
                Developer ??= new DeveloperSettings();
                Formatting.VariableFormats ??= new Dictionary<string, string> { ["attachments"] = " ({value})" };
                Formatting.VariableColors ??= new Dictionary<string, string>();
                PatrolHelicopter.DisplayPerms ??= new HashSet<string>();
                BradleyApc.DisplayPerms ??= new HashSet<string>();
                Version = CurrentVersion;
                OutputModules ??= new OutputModuleSettings();
                OutputModules.GuiDuration = Mathf.Clamp(OutputModules.GuiDuration, 1f, 30f);
                OutputModules.GuiFontSize = Mathf.Clamp(OutputModules.GuiFontSize, 10, 30);
                OutputModules.GuiTitleFontSize = Mathf.Clamp(OutputModules.GuiTitleFontSize, 8, 24);
                OutputModules.GuiTitle ??= "ROGUERUSTDEATHNOTES";
                OutputModules.GuiTitleColor ??= "0.72 0.78 0.82 1";
                OutputModules.GuiAccentColor ??= "0.50 0.82 0.00 1";
                if ((OutputModules.GuiAnchorMin == "0.675 0.865" ||
                     OutputModules.GuiAnchorMin == "0.625 0.790") &&
                    OutputModules.GuiAnchorMax == "0.985 0.945")
                {
                    OutputModules.GuiAnchorMin = "0.665 0.825";
                    OutputModules.GuiAnchorMax = "0.985 0.945";
                }
                if (OutputModules.GuiFontSize == 14)
                    OutputModules.GuiFontSize = 12;
                if (OutputModules.GuiTitleFontSize == 11)
                    OutputModules.GuiTitleFontSize = 10;
				Translations.Names ??= new Dictionary<string, string>();
				Translations.Bodyparts ??= new Dictionary<string, string>();
				Translations.Weapons ??= new Dictionary<string, string>();
				Translations.Attachments ??= new Dictionary<string, string>();
				Translations.Messages ??= new List<DeathMessage>();
				
                // Display names are resolved locally from the running Rust server and persisted
                // to RogueRust/RogueRustDeathNotes/killer-data.json. Keep the plugin source focused
                // on classification, special cases and death-message behavior.

				var defaultMessage = new DeathMessage("Player", "Player", "Generic", "{victim} died due to damage deflection while attempting to murder {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Player", "Bullet", "{killer} shot {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Player", "Arrow", "{victim} was shot by {killer} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Heat", "{killer} inflamed {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "*", "{killer} killed {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Slash", "{killer} slashed {victim} into pieces with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "*", "{killer} killed a {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Shark", "*", "The waters are a tad safer now that {killer} killed a {victim} using their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "Bullet", "{killer} shot a {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "Animal", "Arrow", "{killer} shot a {victim} using their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ZombieNPC", "*", "{killer} fought off a spooky {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ZombieNPC", "Player", "*", "{killer} killed {victim} and ate their brains!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ZombieNPC", "Bullet", "{killer} shot a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ScarecrowNPC", "*", "{killer} fought off a spooky {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ScarecrowNPC", "Player", "*", "{killer} scared {victim} to death!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "ScarecrowNPC", "Bullet", "{killer} did some scaring of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "GingerbreadNPC", "*", "{killer} fought off a deliciously deadly {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("GingerbreadNPC", "Player", "*", "Death was sweet for {victim}, who was killed by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "GingerbreadNPC", "Bullet", "{killer} took a bite out of a deliciously deadly {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "*", "{killer} did not want to be a part of the {victim}'s experiments, and did some science of their own with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "Bullet", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Scientist", "Arrow", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "NPCShopKeeper", "*", "{killer} was not interested in {victim}'s inventory, and sold them pain with their {weapon} instead.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CustomNPC", "Player", "*", "{killer} demonstrated NPC superiority to {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "CustomNPC", "*", "{killer} conveyed their opinion of NPCs to {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("NPCShopKeeper", "Player", "*", "{killer} became unhappy enough to end {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
				
				defaultMessage = new DeathMessage("Player", "HeavyScientist", "*", "{killer} did not want to be a part of the {victim}'s experiments, and did some science of their own with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "HeavyScientist", "Bullet", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "HeavyScientist", "Arrow", "{killer} did some research of their own against a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Bradley", "*", "{killer} blew up the {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Helicopter", "*", "The {victim} was destroyed. Good riddance.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Helicopter", "*", "The world is a safer place now that {killer} shot down the {victim} with their {weapon}. Good riddance.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("SAM", "Helicopter", "*", "A {killer} brought down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Cactus", "Player", "*", "{victim} got in a sticky situation with a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("SAM", "Player", "*", "A {killer} brought down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Animal", "Player", "*", "{victim} couldn't run away from the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Shark", "Player", "*", "Baby {killer}, doo-doo, doo-doo, doo-doo ate {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("BeeSwarm", "Player", "*", "A {killer} just reminded {victim} that they are allergic to bee stings.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "BeeSwarm", "*", "{killer} just swatted a {victim} out of existence.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Bradley", "Player", "*", "{victim} was blasted by the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Helicopter", "Player", "*", "{victim} had no chance against the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "Player", "*", "{victim} was reckless and ran into a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "Scientist", "*", "{victim} fell victim to {owner}'s well placed {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Trap", "HeavyScientist", "*", "{victim} fell victim to {owner}'s well placed {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Barricade", "Player", "*", "{victim} was impaled by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "Player", "*", "{owner}'s {killer} did its job, killing intruder {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "Scientist", "*", "{owner}'s {killer} did its job, killing a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Turret", "HeavyScientist", "*", "{owner}'s {killer} did its job, killing a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Murderer", "Player", "*", "A {killer} haunted down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("TunnelDweller", "Player", "*", "{victim} was taken out by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "TunnelDweller", "*", "{killer} took out a {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "TunnelDweller", "Bullet", "{killer} took out a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("UnderwaterDweller", "Player", "*", "{victim} was taken out by an {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "UnderwaterDweller", "*", "{killer} took out an {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "UnderwaterDweller", "Bullet", "{killer} took out a {victim} with their {weapon} over a distance of {distance}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ExcavatorArm", "Player", "*", "A {killer} excavated {victim} for trace amounts of precious materials. None were found.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Elevator", "Player", "*", "We all have our ups and downs, but {victim} was just squashed by an {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HotAirBalloon", "Player", "*", "{victim} learned the hard way that a {killer} can come down as well.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("BatteringRam", "Player", "*", "{victim} forgot to dodge the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Tugboat", "Player", "*", "{victim} forgot that a {killer} is not, in fact, friendly.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CargoShip", "Player", "*", "{victim} was suddently introduced to industrial tonnage by the {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("CarShredder", "Player", "*", "{victim} was torn to pieces in a {killer}. It turns out they consisted of 40% recyclable materials and 60% regret.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("MagnetCrane", "Player", "*", "{victim} was magnetized and traumatized by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Scientist", "Player", "*", "A {killer} shot down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Scientist", "*", "*", "{killer} shot down a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeavyScientist", "Player", "*", "A {killer} shot down {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeavyScientist", "*", "*", "{killer} shot down a {victim}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Sentry", "Player", "*", "{victim} broke the rules in a safezone and was killed by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("-", "Player", "Fall", "{victim} fell to their death. Splat!");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("HeatSource", "Player", "*", "{victim} was grilled on a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Lock", "Player", "*", "{victim} was electrocuted by {owner}'s {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Heat", "{victim} burned to death.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Hunger", "{victim} forgot to eat and hungers for life.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Thirst", "{victim} died of immense thirst.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Radiation", "{victim} had a cheery, radioactive glow. Fatal, but cheery nonetheless.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Cold", "{victim} turned into an ice statue.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Player", "Drowned", "As {victim} just found out, breathing underwater is rather difficult.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("*", "Animal", "Drowned", "As the {victim} just found out, breathing underwater is rather difficult.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Bleeding", "{victim} bled out after being attacked by {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Animal", "Collision", "{killer} ran over a {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Collision", "{killer} ran over {victim} with their {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Train", "Player", "Collision", "{victim} was stopped in their tracks as they were run over by a {killer}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Minicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("ScrapTransportHelicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("AttackHelicopter", "Player", "*", "{victim} tragically perished in a {killer} crash.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Drone", "Player", "*", "A {killer} killed {victim} with its {weapon}.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);

				defaultMessage = new DeathMessage("Player", "Player", "Suicide", "{victim} had enough of life.");
				if (!Translations.Messages.Contains(defaultMessage)) Translations.Messages.Add(defaultMessage);
			
				foreach (var msg in Translations.Messages)
				{
					if (msg.Enabled == null)
						msg.Enabled = true;
				}
			}

			public class DeathMessage
			{
				public string KillerType { get; set; }
				public string VictimType { get; set; }
				public string DamageType { get; set; }
				
				[JsonProperty("Enabled", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? Enabled { get; set; }

				[JsonIgnore]
				public bool EnabledValue => Enabled ?? true;

                [JsonProperty("Display Permissions (Display For All Players If Empty)")] public HashSet<string> DisplayPerms = new HashSet<string>();
				
				[JsonProperty("Show Kills in Console", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInConsole { get; set; }

				[JsonIgnore]
				public bool ShowKillsInConsoleValue => ShowKillsInConsole ?? true;

				[JsonProperty("Show Kills in Chat", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInChat { get; set; }

				[JsonIgnore]
				public bool ShowKillsInChatValue => ShowKillsInChat ?? true;

				[JsonProperty("Show Kills in Notify", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInNotify { get; set; }

				[JsonIgnore]
				public bool ShowKillsInNotifyValue => ShowKillsInNotify ?? false;

				[JsonProperty("Show Kills in UINotify", DefaultValueHandling = DefaultValueHandling.Include, NullValueHandling = NullValueHandling.Include)]
				public bool? ShowKillsInUINotify { get; set; }

				[JsonIgnore]
				public bool ShowKillsInUINotifyValue => ShowKillsInUINotify ?? false;

				public string[] Messages { get; set; }
				
				public override bool Equals(object obj)
				{
					if (obj is DeathMessage other)
					{
						return string.Equals(KillerType, other.KillerType) &&
						       string.Equals(VictimType, other.VictimType) &&
						       string.Equals(DamageType, other.DamageType);
					}
					return false;
				}
				
				public override int GetHashCode()
				{
					return (KillerType + VictimType + DamageType).GetHashCode();
				}

				public DeathMessage(string killerType, string victimType, string damageType, string message)
				{
					KillerType = killerType;
					VictimType = victimType;
					DamageType = damageType;
					Enabled = true;
                    ShowKillsInConsole = true;
                    ShowKillsInChat = true;
                    ShowKillsInNotify = false;
                    ShowKillsInUINotify = false;
					Messages = new[] {message};
				}
			}

			public class Translation
			{
				[JsonProperty("Death Messages")] public List<DeathMessage> Messages = new List<DeathMessage>();

				[JsonProperty("Names")] public Dictionary<string, string> Names = new Dictionary<string, string>();

				[JsonProperty("Bodyparts")]
				public Dictionary<string, string> Bodyparts = new Dictionary<string, string>();

				[JsonProperty("Weapons")] public Dictionary<string, string> Weapons = new Dictionary<string, string>();

				[JsonProperty("Attachments")]
				public Dictionary<string, string> Attachments = new Dictionary<string, string>();
			}
		}

		#endregion
	}
}