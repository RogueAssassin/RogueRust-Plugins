using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Oxide.Ext.RogueRust.Plugins;
using Oxide.Ext.RogueRust.SDK;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("RogueRustFurnaceSplitter", "RogueAssassin", "2.1.0")]
    [Description("Optimized RogueRust furnace splitting with automatic fuel management, configurable stack distribution, ETA display, and responsive UI.")]
    public sealed class RogueRustFurnaceSplitter : RogueRustPlugin
    {
        private const string PluginVersion = "2.1.0";
        private static readonly VersionNumber CurrentVersion = new VersionNumber(2, 1, 0);

        [PluginReference]
        private Plugin UIScaleManager;

        private class OvenSlot
        {
            /// <summary>The item in this slot. May be null.</summary>
            public Item Item;

            /// <summary>The slot position</summary>
            public int? Position;

            /// <summary>The slot's index in the itemList list.</summary>
            public int Index;

            /// <summary>How much should be added/removed from stack</summary>
            public int DeltaAmount;
        }

        public class OvenInfo
        {
            public float ETA;
            public float FuelNeeded;
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerOptions> AllPlayerOptions { get; private set; } = new Dictionary<ulong, PlayerOptions>();
        }

        private class PlayerOptions
        {
            public bool Enabled;
            public Dictionary<string, int> TotalStacks = new Dictionary<string, int>();
        }

        public enum MoveResult
        {
            Ok,
            SlotsFilled,
            NotEnoughSlots
        }

        private StoredData storedData = new StoredData();
        private Dictionary<ulong, PlayerOptions> allPlayerOptions => storedData.AllPlayerOptions;
        private Dictionary<string, int> initialStackOptions = new Dictionary<string, int>();
        private PluginConfig config;

        [RoguePermission]
        private const string permUse = "roguerustfurnacesplitter.use";
        private const string legacyPermUse = "furnacesplitter.use";
        private const string PlayerDataKey = "RogueRustFurnaceSplitter/player-options";

        private readonly Dictionary<ulong, string> openUis = new Dictionary<ulong, string>();
        private readonly Dictionary<BaseOven, List<BasePlayer>> looters = new Dictionary<BaseOven, List<BasePlayer>>();
        private readonly Queue<BaseOven> queuedUiUpdates = new Queue<BaseOven>();
        private readonly HashSet<BaseOven> queuedUiUpdateSet = new HashSet<BaseOven>();

        private void Init()
        {
            LoadLanguageFiles();
            LogInformation("Lifecycle",
            $"{Name} {PluginVersion} initialized. " +
            $"Config={Name}.json; Data=RogueRust/RogueRustFurnaceSplitter/; Lang=<language>/RogueRust/RogueRustFurnaceSplitter/messages.json");
        }

        private void OnServerInitialized()
        {
            var saveCfg = false;
            foreach (var prefab in GameManifest.Current.entities)
            {
                var gameObj = GameManager.server.FindPrefab(prefab);
                if (gameObj == null) continue;

                var oven = gameObj.GetComponent<BaseOven>();
                if (oven != null && oven.allowByproductCreation) // ignore pumpkins, lanterns, etc
                {
                    if (!initialStackOptions.ContainsKey(oven.ShortPrefabName))
                    {
                        //Puts($"Add oven [{oven.ShortPrefabName}] - fuelSlots: {oven.fuelSlots}, inputSlots: {oven.inputSlots}, outputSlots: {oven.outputSlots}");
                        initialStackOptions[oven.ShortPrefabName] = oven.inputSlots;
                    }

                    if (!config.ovens.ContainsKey(oven.ShortPrefabName))
                    {
                        config.ovens[oven.ShortPrefabName] = new PluginConfig.OvenConfig {
                            enabled = false, // global '*' is enabled by default, disable all oven configs
                            fuelMultiplier = 1.0f
                        };
                        saveCfg = true;
                    }
                }
            }

            if (saveCfg)
                SaveConfig();

            Unsubscribe(nameof(OnServerSave));
            if (config.savePlayerData)
            {
                storedData = LoadData(PlayerDataKey, delegate { return new StoredData(); }) ?? new StoredData();
                MigrateLegacyPlayerData();
                Subscribe(nameof(OnServerSave));
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            DestroyUI(player);
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void SaveData()
        {
            if (!config.savePlayerData) return;

            SaveData(PlayerDataKey, storedData);
        }

        private void InitPlayer(BasePlayer player)
        {
            PlayerOptions options;
            if (!allPlayerOptions.TryGetValue(player.userID, out options))
            {
                options = new PlayerOptions
                {
                    Enabled = true,
                    TotalStacks = new Dictionary<string, int>()
                };
                allPlayerOptions[player.userID] = options;
            }

            foreach (var kv in initialStackOptions)
            {
                if (!options.TotalStacks.ContainsKey(kv.Key))
                    options.TotalStacks.Add(kv.Key, kv.Value);
            }
        }

        private bool _uiUpdateScheduled;

        private void ProcessQueuedUiUpdates()
        {
            _uiUpdateScheduled = false;
            using (Measure("RogueRustFurnaceSplitter", "UiUpdateBatch"))
            {
                int processed = 0;
                const int batchSize = 12;
                while (queuedUiUpdates.Count > 0 && processed < batchSize)
                {
                    BaseOven oven = queuedUiUpdates.Dequeue();
                    queuedUiUpdateSet.Remove(oven);

                    if (!oven || oven.IsDestroyed)
                    {
                        processed++;
                        continue;
                    }

                    OvenInfo ovenInfo = GetOvenInfo(oven);
                    List<BasePlayer> ovenLooters = GetLooters(oven);
                    if (ovenLooters != null)
                    {
                        for (int i = 0; i < ovenLooters.Count; i++)
                        {
                            BasePlayer player = ovenLooters[i];
                            if (player != null && !player.IsDestroyed && HasPermission(player) && GetEnabled(player))
                                CreateUi(player, oven, ovenInfo);
                        }
                    }
                    processed++;
                }
            }

            if (queuedUiUpdates.Count > 0)
                ScheduleUiUpdateProcessing();
        }

        private void ScheduleUiUpdateProcessing()
        {
            if (_uiUpdateScheduled) return;
            _uiUpdateScheduled = true;
            Delay(TimeSpan.Zero, ProcessQueuedUiUpdates, "furnacesplitter-ui-update");
        }

        public OvenInfo GetOvenInfo(BaseOven oven)
        {
            OvenInfo result = new OvenInfo();
            PluginConfig.OvenConfig ovenCfg = GetOvenConfig(oven.ShortPrefabName);
            float ETA = GetTotalSmeltTime(oven) / oven.smeltSpeed;

            if (oven.fuelType != null)
            {
                float fuelMultiplier = ovenCfg != null ? ovenCfg.fuelMultiplier : 1.0f;
                float fuelUnits = oven.fuelType.GetComponent<ItemModBurnable>().fuelAmount;
                float neededFuel = (float)Math.Ceiling(ETA * (oven.cookingTemperature / 200.0f) / fuelUnits);

                result.FuelNeeded = neededFuel * fuelMultiplier;
            }
            result.ETA = ETA;

            return result;
        }

        private void Unload()
        {
            _uiUpdateScheduled = false;
            queuedUiUpdates.Clear();
            queuedUiUpdateSet.Clear();
            looters.Clear();
            SaveData();
            if (openUis.Count > 0)
            {
                ulong[] playerIds = openUis.Keys.ToArray();
                for (int i = 0; i < playerIds.Length; i++)
                    DestroyUI(BasePlayer.FindByID(playerIds[i]));
            }
        }

        private bool GetEnabled(BasePlayer player)
        {
            PlayerOptions options;
            if (!allPlayerOptions.TryGetValue(player.userID, out options))
            {
                InitPlayer(player);
                options = allPlayerOptions[player.userID];
            }
            return options.Enabled;
        }

        private void SetEnabled(BasePlayer player, bool enabled)
        {
            PlayerOptions options;
            if (!allPlayerOptions.TryGetValue(player.userID, out options))
            {
                InitPlayer(player);
                options = allPlayerOptions[player.userID];
            }
            options.Enabled = enabled;
            CreateUiIfFurnaceOpen(player);
            Debounce("furnacesplitter-data-save", TimeSpan.FromSeconds(2), SaveData);
        }

        private bool IsSlotCompatible(Item item, BaseOven oven, ItemDefinition itemDefinition)
        {
            ItemModCookable cookable = item.info.GetComponent<ItemModCookable>();

            if (item.amount < item.info.stackable && item.info == itemDefinition)
                return true;

            if (oven.allowByproductCreation && oven.fuelType.GetComponent<ItemModBurnable>().byproductItem == item.info)
                return true;

            if (cookable == null || cookable.becomeOnCooked == itemDefinition)
                return true;

            if (CanCook(cookable, oven))
                return true;

            return false;
        }

        private void OnFuelConsume(BaseOven oven, Item fuel, ItemModBurnable burnable)
        {
            if (IsOvenCompatible(oven))
                QueueUiUpdate(oven);
        }

        private void QueueUiUpdate(BaseOven oven)
        {
            if (oven == null || oven.IsDestroyed || !queuedUiUpdateSet.Add(oven)) return;
            queuedUiUpdates.Enqueue(oven);
            ScheduleUiUpdateProcessing();
        }

        private List<BasePlayer> GetLooters(BaseOven oven)
        {
            List<BasePlayer> players;
            return looters.TryGetValue(oven, out players) ? players : null;
        }

        private void AddLooter(BaseOven oven, BasePlayer player)
        {
            List<BasePlayer> list;
            if (!looters.TryGetValue(oven, out list))
            {
                list = new List<BasePlayer>();
                looters[oven] = list;
            }
            if (!list.Contains(player))
                list.Add(player);
        }

        private void RemoveLooter(BaseOven oven, BasePlayer player)
        {
            List<BasePlayer> list;
            if (!looters.TryGetValue(oven, out list))
                return;

            list.Remove(player);
            if (list.Count == 0)
                looters.Remove(oven);
        }

        private object CanMoveItem(Item item, PlayerInventory inventory, ItemContainerId targetContainerId, int targetSlotIndex, int splitAmount)
        {
            if (item == null || inventory == null)
                return null;

            BasePlayer player = inventory.GetComponent<BasePlayer>();
            if (player == null)
                return null;

            BaseOven oven = inventory.loot.entitySource as BaseOven;
            if (oven == null)
                return null;

            ItemContainer targetContainer = inventory.FindContainer(targetContainerId);
            if (targetContainer != null && !(targetContainer?.entityOwner is BaseOven))
                return null; // ignore moving items within player inventory or container that is not an oven

            ItemContainer container = oven.inventory;
            ItemContainer originalContainer = item.GetRootContainer();
            if (container == null || originalContainer == null || originalContainer?.entityOwner is BaseOven)
                return null; // ignore invalid container or moving items within oven

            BaseOven.MinMax? allowedSlots = oven.GetAllowedSlots(item);
            if (allowedSlots == null)
                return null;

            for (int i = allowedSlots.Value.Min; i <= allowedSlots.Value.Max; i++)
            {
                Item slot = oven.inventory.GetSlot(i);
                if (slot != null && slot.info.shortname != item.info.shortname)
                    return null; // ignore splitting for different item types but allow item to be added to oven
            }

            Func<object> splitFunc = () =>
            {
                if (player == null || !HasPermission(player) || !GetEnabled(player))
                    return null;

                PlayerOptions playerOptions = allPlayerOptions[player.userID];

                if (container == null || originalContainer == null || container == item.GetRootContainer())
                    return null;

                ItemModCookable cookable = item.info.GetComponent<ItemModCookable>();

                if (oven == null || cookable == null || oven.IsOutputItem(item))
                    return null;

                int totalSlots;
                if (!playerOptions.TotalStacks.TryGetValue(oven.ShortPrefabName, out totalSlots))
                    totalSlots = oven.inputSlots;

                if (cookable.lowTemp > oven.cookingTemperature || cookable.highTemp < oven.cookingTemperature)
                    return null;

                MoveSplitItem(item, oven, totalSlots, splitAmount);
                return true;
            };

            object returnValue = splitFunc();

            if (HasPermission(player) && GetEnabled(player))
            {
                if (oven != null && IsOvenCompatible(oven))
                {
                    if (returnValue is bool && (bool)returnValue)
                        AutoAddFuel(inventory, oven);

                    QueueUiUpdate(oven);
                }
            }

            return returnValue;
        }

        private MoveResult MoveSplitItem(Item item, BaseOven oven, int totalSlots, int splitAmount)
        {
            ItemContainer container = oven.inventory;
            int numOreSlots = totalSlots;
            int totalMoved = 0;
            int itemAmount = item.amount > splitAmount ? splitAmount : item.amount;
            int existingAmount = 0;
            int matchingStacks = 0;
            List<Item> containerItems = container.itemList;
            for (int i = 0; i < containerItems.Count && matchingStacks < numOreSlots; i++)
            {
                Item slotItem = containerItems[i];
                if (slotItem == null || slotItem.info != item.info) continue;
                existingAmount += slotItem.amount;
                matchingStacks++;
            }

            int totalAmount = Math.Min(itemAmount + existingAmount, Math.Abs(item.info.stackable * numOreSlots));

            if (numOreSlots <= 0)
            {
                return MoveResult.NotEnoughSlots;
            }

            //Puts("---------------------------");

            int totalStackSize = Math.Min(totalAmount / numOreSlots, item.info.stackable);
            int remaining = totalAmount - totalAmount / numOreSlots * numOreSlots;

            List<int> addedSlots = new List<int>();

            //Puts("total: {0}, remaining: {1}, totalStackSize: {2}", totalAmount, remaining, totalStackSize);

            List<OvenSlot> ovenSlots = new List<OvenSlot>();

            for (int i = 0; i < numOreSlots; ++i)
            {
                Item existingItem;
                int slot = FindMatchingSlotIndex(oven, container, out existingItem, item.info, addedSlots);

                if (slot == -1) // full
                {
                    return MoveResult.NotEnoughSlots;
                }

                addedSlots.Add(slot);

                OvenSlot ovenSlot = new OvenSlot
                {
                    Position = existingItem?.position,
                    Index = slot,
                    Item = existingItem
                };

                int currentAmount = existingItem?.amount ?? 0;
                int missingAmount = totalStackSize - currentAmount + (i < remaining ? 1 : 0);
                ovenSlot.DeltaAmount = missingAmount;

                //Puts("[{0}] current: {1}, delta: {2}, total: {3}", slot, currentAmount, ovenSlot.DeltaAmount, currentAmount + missingAmount);

                if (currentAmount + missingAmount <= 0)
                    continue;

                ovenSlots.Add(ovenSlot);
            }

            foreach (OvenSlot slot in ovenSlots)
            {
                if (slot.Item == null)
                {
                    Item newItem = ItemManager.Create(item.info, slot.DeltaAmount, item.skin);
                    slot.Item = newItem;
                    newItem.MoveToContainer(container, slot.Position ?? slot.Index);
                }
                else
                {
                    slot.Item.amount += slot.DeltaAmount;
                }

                totalMoved += slot.DeltaAmount;
            }

            container.MarkDirty();

            if (totalMoved >= item.amount)
            {
                item.Remove();
                item.GetRootContainer()?.MarkDirty();
                return MoveResult.Ok;
            }
            else
            {
                item.amount -= totalMoved;
                item.GetRootContainer()?.MarkDirty();
                return MoveResult.SlotsFilled;
            }
        }

        private void AutoAddFuel(PlayerInventory playerInventory, BaseOven oven)
        {
            if (oven.fuelType == null) return;
            int neededFuel = (int)Math.Ceiling(GetOvenInfo(oven).FuelNeeded);
            
            neededFuel -= oven.inventory.GetAmount(oven.fuelType.itemid, false);			
			List<Item> playerFuel = Facepunch.Pool.Get<List<Item>>();
			try
            {
                playerInventory.FindItemsByItemID(playerFuel, oven.fuelType.itemid);
                int fuelSlotIndex = 0;

                if (neededFuel <= 0 || playerFuel.Count <= 0)
                    return;

                foreach (Item fuelItem in playerFuel)
                {
                    var existingFuel = oven.inventory.GetSlot(fuelSlotIndex);
                    if (existingFuel != null && existingFuel.amount >= existingFuel.info.stackable) // fuel slot full
                    {
                        if (fuelSlotIndex < oven.fuelSlots) // check fuel slots
                            fuelSlotIndex++; // move to next fuel slot
                        else
                            break; // break if no fuel slots available
                    }

                    int largestFuelAmount = 0;
                    List<Item> ovenItems = oven.inventory.itemList;
                    for (int i = 0; i < ovenItems.Count; i++)
                    {
                        Item ovenItem = ovenItems[i];
                        if (ovenItem != null && ovenItem.info == oven.fuelType && ovenItem.amount > largestFuelAmount)
                            largestFuelAmount = ovenItem.amount;
                    }

                    int toTake = Math.Min(neededFuel,
                        (oven.fuelType.stackable * oven.fuelSlots) - largestFuelAmount);

                    if (toTake > fuelItem.amount)
                        toTake = fuelItem.amount;

                    if (toTake <= 0)
                        break;

                    neededFuel -= toTake;

                    int currentFuelAmount = oven.inventory.GetAmount(oven.fuelType.itemid, false);
                    if (currentFuelAmount >= oven.fuelType.stackable * oven.fuelSlots)
                        break; // Break if oven is full

                    if (toTake >= fuelItem.amount)
                    {
                        fuelItem.MoveToContainer(oven.inventory, fuelSlotIndex);
                    }
                    else
                    {
                        Item splitItem = fuelItem.SplitItem(toTake);
                        if (!splitItem.MoveToContainer(oven.inventory, fuelSlotIndex)) // Break if oven is full
                            break;
                    }

                    if (neededFuel <= 0)
                        break;
                }
			}
			finally
			{
				Facepunch.Pool.Free(ref playerFuel);
			}
        }

        private int FindMatchingSlotIndex(BaseOven oven, ItemContainer container, out Item existingItem, ItemDefinition itemType, List<int> indexBlacklist)
        {
            existingItem = null;
            int firstIndex = -1;
            int inputSlotsMin = oven._inputSlotIndex;
            int inputSlotsMax = oven._inputSlotIndex + oven.inputSlots;
            Item largestExisting = null;

            for (int i = inputSlotsMin; i < inputSlotsMax; ++i)
            {
                if (indexBlacklist.Contains(i))
                    continue;

                Item itemSlot = container.GetSlot(i);
                if (itemSlot == null || (itemType != null && itemSlot.info == itemType))
                {
                    if (firstIndex == -1)
                    {
                        existingItem = itemSlot;
                        firstIndex = i;
                    }

                    if (itemSlot != null && (largestExisting == null || itemSlot.amount > largestExisting.amount))
                        largestExisting = itemSlot;
                }
            }

            if (largestExisting != null)
            {
                existingItem = largestExisting;
                return largestExisting.position;
            }

            if (firstIndex != -1)
                return firstIndex;

            existingItem = null;
            return -1;
        }

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            BaseOven oven = entity as BaseOven;

            if (oven == null || !HasPermission(player) || !IsOvenCompatible(oven))
                return;

            AddLooter(oven, player);
            if (GetEnabled(player))
                QueueUiUpdate(oven); // queue ui updates
            else
                CreateUi(player, oven, new OvenInfo()); // create ui without updates
        }

        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            BaseOven oven = entity as BaseOven;

            if (oven == null || !IsOvenCompatible(oven))
                return;

            DestroyUI(player);
            RemoveLooter(oven, player);
        }

        private void OnEntityKill(BaseNetworkable networkable)
        {
            BaseOven oven = networkable as BaseOven;

            if (oven != null)
            {
                DestroyOvenUI(oven);
            }
        }

        private void OnOvenToggle(BaseOven oven, BasePlayer player)
        {
            if (IsOvenCompatible(oven))
                QueueUiUpdate(oven);
        }

        private void CreateUiIfFurnaceOpen(BasePlayer player)
        {
            BaseOven oven = player.inventory.loot?.entitySource as BaseOven;

            if (oven != null && IsOvenCompatible(oven))
                QueueUiUpdate(oven);
        }

        private CuiElementContainer CreateUi(BasePlayer player, BaseOven oven, OvenInfo ovenInfo)
        {
            PlayerOptions options = allPlayerOptions[player.userID];
            int totalSlots = GetTotalStacksOption(player, oven) ?? oven.inputSlots;
            string remainingTimeStr = "0s";
            string neededFuelStr = " (0)";

            if (ovenInfo.ETA > 0)
            {
                remainingTimeStr = FormatTime(ovenInfo.ETA);
                neededFuelStr = oven.fuelType == null ? "" : " (" + ovenInfo.FuelNeeded.ToString("##,###") +  " " + oven.fuelType.displayName.english.ToLower() + ")";
            }

            float uiScale = 1.0f;
            float[] playerUiInfo = UIScaleManager?.Call<float[]>("API_CheckPlayerUIInfo", player.UserIDString);
            if (playerUiInfo?.Length > 0)
            {
                uiScale = playerUiInfo[2];
            }
            string contentColor = "0.7 0.7 0.7 1.0";
            int contentSize = Convert.ToInt32(10 * uiScale);
            string toggleStateStr = (!options.Enabled).ToString();
            string toggleButtonColor = !options.Enabled
                    ? "0.415 0.5 0.258 0.4"
                    : "0.8 0.254 0.254 0.4";
            string toggleButtonTextColor = !options.Enabled
                    ? "0.607 0.705 0.431"
                    : "0.705 0.607 0.431";
            string buttonColor = "0.75 0.75 0.75 0.1";
            string buttonTextColor = "0.77 0.68 0.68 1";

            int nextDecrementSlot = totalSlots - 1;
            int nextIncrementSlot = totalSlots + 1;

            DestroyUI(player);

            Vector2 uiPosition = new Vector2(
                ((((config.UiPosition.x) - 0.5f) * uiScale) + 0.5f),
                (config.UiPosition.y - 0.02f) + 0.02f * uiScale);
            Vector2 uiSize = new Vector2(0.1785f * uiScale, 0.111f * uiScale);

            CuiElementContainer result = new CuiElementContainer();
            string rootPanelName = result.Add(new CuiPanel
            {
                Image = new CuiImageComponent
                {
                    Color = "0 0 0 0"
                },
                RectTransform =
                {
                    AnchorMin = uiPosition.x + " " + uiPosition.y,
                    AnchorMax = uiPosition.x + uiSize.x + " " + (uiPosition.y + uiSize.y)
                    //AnchorMin = "0.6505 0.022",
                    //AnchorMax = "0.829 0.133"
                }
            }, "Hud.Menu");

            string headerPanel = result.Add(new CuiPanel
            {
                Image = new CuiImageComponent
                {
                    Color = "0.75 0.75 0.75 0.1"
                },
                RectTransform =
                {
                    AnchorMin = "0 0.775",
                    AnchorMax = "1 1"
                }
            }, rootPanelName);

            // Header label
            result.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.051 0",
                    AnchorMax = "1 0.95"
                },
                Text =
                {
                    Text = GetMessage("title", player),
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.77 0.7 0.7 1",
                    FontSize = Convert.ToInt32(13 * uiScale)
                }
            }, headerPanel);

            string contentPanel = result.Add(new CuiPanel
            {
                Image = new CuiImageComponent
                {
                    Color = "0.65 0.65 0.65 0.06"
                },
                RectTransform =
                {
                    AnchorMin = "0 0",
                    AnchorMax = "1 0.74"
                }
            }, rootPanelName);

            // ETA label
            result.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.02 0.7",
                    AnchorMax = "0.98 1"
                },
                Text =
                {
                    Text = string.Format("{0}: " + (ovenInfo.ETA > 0 ? "~" : "") + remainingTimeStr + neededFuelStr, GetMessage("eta", player)),
                    Align = TextAnchor.MiddleLeft,
                    Color = contentColor,
                    FontSize = contentSize
                }
            }, contentPanel);

            // Toggle button
            result.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0.02 0.4",
                    AnchorMax = "0.25 0.7"
                },
                Button =
                {
                    Command = "furnacesplitter.enabled " + toggleStateStr,
                    Color = toggleButtonColor
                },
                Text =
                {
                    Align = TextAnchor.MiddleCenter,
                    Text = options.Enabled ? GetMessage("turnoff", player) : GetMessage("turnon", player),
                    Color = toggleButtonTextColor,
                    FontSize = Convert.ToInt32(11 * uiScale)
                }
            }, contentPanel);

            if (oven.fuelType != null)
            {
                // Trim button
                result.Add(new CuiButton
                {
                    RectTransform =
                    {
                        AnchorMin = "0.27 0.4",
                        AnchorMax = "0.52 0.7"
                    },
                    Button =
                    {
                        Command = "furnacesplitter.trim",
                        Color = buttonColor
                    },
                    Text =
                    {
                        Align = TextAnchor.MiddleCenter,
                        Text = GetMessage("trim", player),
                        Color = contentColor,
                        FontSize = Convert.ToInt32(11 * uiScale)
                    }
                }, contentPanel);
            }

            // Decrease stack button
            result.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0.02 0.05",
                    AnchorMax = "0.07 0.35"
                },
                Button =
                {
                    Command = "furnacesplitter.totalstacks " + nextDecrementSlot,
                    Color = buttonColor
                },
                Text =
                {
                    Align = TextAnchor.MiddleCenter,
                    Text = "<",
                    Color = buttonTextColor,
                    FontSize = contentSize
                }
            }, contentPanel);

            // Empty slots label
            result.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.08 0.05",
                    AnchorMax = "0.19 0.35"
                },
                Text =
                {
                    Align = TextAnchor.MiddleCenter,
                    Text = totalSlots.ToString(),
                    Color = contentColor,
                    FontSize = contentSize
                }
            }, contentPanel);

            // Increase stack button
            result.Add(new CuiButton
            {
                RectTransform =
                {
                    AnchorMin = "0.19 0.05",
                    AnchorMax = "0.25 0.35"
                },
                Button =
                {
                    Command = "furnacesplitter.totalstacks " + nextIncrementSlot,
                    Color = buttonColor
                },
                Text =
                {
                    Align = TextAnchor.MiddleCenter,
                    Text = ">",
                    Color = buttonTextColor,
                    FontSize = contentSize
                }
            }, contentPanel);

            // Stack itemType label
            result.Add(new CuiLabel
            {
                RectTransform =
                {
                    AnchorMin = "0.27 0.05",
                    AnchorMax = "1 0.35"
                },
                Text =
                {
                    Align = TextAnchor.MiddleLeft,
                    Text = string.Format("({0})", GetMessage("totalstacks", player)),
                    Color = contentColor,
                    FontSize = contentSize
                }
            }, contentPanel);

            openUis.Add(player.userID, rootPanelName);
            CuiHelper.AddUi(player, result);
            return result;
        }

        private string FormatTime(float totalSeconds)
        {
            int hours = (int)Math.Floor(totalSeconds / 3600);
            int minutes = (int)Math.Floor(totalSeconds / 60 % 60);
            int seconds = (int)Math.Floor(totalSeconds % 60);

            if (hours <= 0 && minutes <= 0)
                return seconds + "s";
            if (hours <= 0)
                return minutes + "m" + seconds + "s";
            return hours + "h" + minutes + "m" + seconds + "s";
        }

        private float GetTotalSmeltTime(BaseOven oven)
        {
            float ETA = 0f;
            for (int i = oven._inputSlotIndex; i < oven._inputSlotIndex + oven.inputSlots; i++)
            {
                Item inputItem = oven.inventory.GetSlot(i);
                if (inputItem == null) continue;

                ItemModCookable cookable = inputItem.info.GetComponent<ItemModCookable>();
                if (cookable == null) continue;

                ETA += GetSmeltTime(cookable, inputItem.amount);
            }
            return ETA;
        }

        private bool CanCook(ItemModCookable cookable, BaseOven oven)
        {
            return oven.cookingTemperature >= cookable.lowTemp && oven.cookingTemperature <= cookable.highTemp;
        }

        private float GetSmeltTime(ItemModCookable cookable, int amount)
        {
            float smeltTime = cookable.cookTime * amount;
            return smeltTime;
        }

        private int? GetTotalStacksOption(BasePlayer player, BaseOven oven)
        {
            PlayerOptions options = allPlayerOptions[player.userID];

            if (options.TotalStacks.ContainsKey(oven.ShortPrefabName))
                return options.TotalStacks[oven.ShortPrefabName];

            return null;
        }

        private void DestroyUI(BasePlayer player)
        {
            if (!openUis.ContainsKey(player.userID))
                return;

            string uiName = openUis[player.userID];

            if (openUis.Remove(player.userID))
                CuiHelper.DestroyUi(player, uiName);
        }

        private void DestroyOvenUI(BaseOven oven)
        {
            if (oven == null) return;

            List<BasePlayer> players;
            if (!looters.TryGetValue(oven, out players) || players == null)
                return;

            for (int i = players.Count - 1; i >= 0; i--)
            {
                BasePlayer player = players[i];
                if (player != null && !player.IsDestroyed)
                    DestroyUI(player);
            }

            looters.Remove(oven);
            queuedUiUpdateSet.Remove(oven);
        }

        private PluginConfig.OvenConfig GetOvenConfig(string ovenShortname)
        {
            PluginConfig.OvenConfig ovenCfg;
            if (config.ovens.TryGetValue(ovenShortname, out ovenCfg) && ovenCfg.enabled)
                return ovenCfg;

            if (config.ovens.TryGetValue("*", out ovenCfg) && ovenCfg.enabled)
                return ovenCfg;

            return null;
        }

        private bool IsOvenCompatible(BaseOven oven)
        {
            if (oven == null || !oven.allowByproductCreation)
                return false;

            return GetOvenConfig(oven.ShortPrefabName) != null;
        }

        [RogueCommand("fs",
            Aliases = new[] { "roguerust.fs" },
            Description = "Shows or toggles Furnace Splitter for the player.",
            Usage = "/fs [on|off]", Category = "Furnace",
            CooldownSeconds = 0.25, AllowConsole = false)]
        private RogueCommandResult ToggleCommand(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null)
                return RogueCommandResult.Fail("This command is player-only.");
            if (!HasPermission(player))
                return RogueCommandResult.Fail(GetMessage("nopermission", player));

            string[] args = context.Arguments ?? Array.Empty<string>();
            string statusOn = GetMessage("StatusONColor", player);
            string statusOff = GetMessage("StatusOFFColor", player);

            if (args.Length == 0)
            {
                string status = GetEnabled(player) ? statusOn : statusOff;
                StringBuilder help = new StringBuilder();
                help.Append("<size=22><color=green>RogueRust Furnace Splitter</color></size>\\n");
                help.Append(GetMessage("StatusMessage", player) + status + "\\n");
                help.Append("<color=orange>/fs on</color> - Toggles Furnace Splitter to ON\\n");
                help.Append("<color=orange>/fs off</color> - Toggles Furnace Splitter to OFF");
                return RogueCommandResult.Ok(help.ToString());
            }

            if (args[0].Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                SetEnabled(player, true);
                CreateUiIfFurnaceOpen(player);
            }
            else if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                SetEnabled(player, false);
                DestroyUI(player);
            }
            else
            {
                return RogueCommandResult.Fail("Invalid syntax!");
            }

            return RogueCommandResult.Ok(GetMessage("StatusMessage", player) +
                (GetEnabled(player) ? statusOn : statusOff));
        }

        [RogueCommand("furnacesplitter.enabled",
            Aliases = new[] { "roguerustfurnacesplitter.enabled" },
            Description = "Gets or sets Furnace Splitter enabled state for the invoking player.",
            Usage = "furnacesplitter.enabled [true|false]", Category = "Furnace",
            CooldownSeconds = 0.1, AllowConsole = true)]
        private RogueCommandResult EnabledCommand(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null) return RogueCommandResult.Fail("This command is player-only.");
            if (!HasPermission(player)) return RogueCommandResult.Fail(GetMessage("nopermission", player));

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length == 0)
                return RogueCommandResult.Ok(GetEnabled(player).ToString());

            bool enabled;
            if (!bool.TryParse(args[0], out enabled))
                return RogueCommandResult.Fail("Expected true or false.");

            SetEnabled(player, enabled);
            if (enabled)
                CreateUiIfFurnaceOpen(player);
            else
            {
                BaseOven oven = player.inventory.loot?.entitySource as BaseOven;
                if (oven != null)
                    CreateUi(player, oven, new OvenInfo());
            }
            return RogueCommandResult.Ok(enabled.ToString());
        }

        [RogueCommand("furnacesplitter.totalstacks",
            Aliases = new[] { "roguerustfurnacesplitter.totalstacks" },
            Description = "Gets or sets total input stacks for the currently looted furnace.",
            Usage = "furnacesplitter.totalstacks [count]", Category = "Furnace",
            CooldownSeconds = 0.05, AllowConsole = true)]
        private RogueCommandResult TotalStacksCommand(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null) return RogueCommandResult.Fail("This command is player-only.");
            if (!HasPermission(player)) return RogueCommandResult.Fail(GetMessage("nopermission", player));
            if (!GetEnabled(player)) return RogueCommandResult.Ok();

            BaseOven lootSource = player.inventory.loot?.entitySource as BaseOven;
            if (lootSource == null || !IsOvenCompatible(lootSource))
                return RogueCommandResult.Fail(GetMessage("lootsource_invalid", player));

            string ovenName = lootSource.ShortPrefabName;
            PlayerOptions options = allPlayerOptions[player.userID];
            if (!options.TotalStacks.ContainsKey(ovenName))
            {
                LogWarning("Furnace", "Unsupported furnace '" + ovenName + "'.");
                return RogueCommandResult.Fail(GetMessage("unsupported_furnace", player));
            }

            string[] args = context.Arguments ?? Array.Empty<string>();
            if (args.Length == 0)
                return RogueCommandResult.Ok(options.TotalStacks[ovenName].ToString());

            int value;
            if (!int.TryParse(args[0], out value))
                return RogueCommandResult.Fail("Expected a stack count.");

            options.TotalStacks[ovenName] = Mathf.Clamp(value, 1, lootSource.inputSlots);
            CreateUiIfFurnaceOpen(player);
            Debounce("furnacesplitter-data-save", TimeSpan.FromSeconds(2), SaveData);
            return RogueCommandResult.Ok(options.TotalStacks[ovenName].ToString());
        }

        [RogueCommand("furnacesplitter.trim",
            Aliases = new[] { "roguerustfurnacesplitter.trim" },
            Description = "Returns excess fuel from the currently looted furnace.",
            Usage = "furnacesplitter.trim", Category = "Furnace",
            CooldownSeconds = 0.25, AllowConsole = true)]
        private RogueCommandResult TrimCommand(RogueCommandContext context)
        {
            BasePlayer player = context.NativePlayer;
            if (player == null) return RogueCommandResult.Fail("This command is player-only.");
            if (!HasPermission(player)) return RogueCommandResult.Fail(GetMessage("nopermission", player));
            if (!GetEnabled(player)) return RogueCommandResult.Ok();

            BaseOven lootSource = player.inventory.loot?.entitySource as BaseOven;
            if (lootSource == null || !IsOvenCompatible(lootSource))
                return RogueCommandResult.Fail(GetMessage("lootsource_invalid", player));

            OvenInfo ovenInfo = GetOvenInfo(lootSource);
            List<Item> fuelSlots = Facepunch.Pool.Get<List<Item>>();
            try
            {
                int totalFuel = 0;
                List<Item> ovenItems = lootSource.inventory.itemList;
                for (int i = 0; i < ovenItems.Count; i++)
                {
                    Item ovenItem = ovenItems[i];
                    if (ovenItem == null || ovenItem.info != lootSource.fuelType)
                        continue;

                    fuelSlots.Add(ovenItem);
                    totalFuel += ovenItem.amount;
                }

                int toRemove = (int)Math.Floor(totalFuel - ovenInfo.FuelNeeded);
                if (toRemove <= 0)
                    return RogueCommandResult.Ok();

                Vector3 dropPosition = player.GetDropPosition();
                Vector3 dropVelocity = player.GetDropVelocity();
                for (int i = 0; i < fuelSlots.Count && toRemove > 0; i++)
                {
                    Item fuelItem = fuelSlots[i];
                    int toTake = Math.Min(fuelItem.amount, toRemove);
                    toRemove -= toTake;

                    if (toTake >= fuelItem.amount)
                    {
                        if (!player.inventory.GiveItem(fuelItem))
                            fuelItem.Drop(dropPosition, dropVelocity, Quaternion.identity);
                    }
                    else
                    {
                        Item splitItem = fuelItem.SplitItem(toTake);
                        if (!player.inventory.GiveItem(splitItem))
                            splitItem.Drop(dropPosition, dropVelocity, Quaternion.identity);
                    }
                }

                return RogueCommandResult.Ok();
            }
            finally
            {
                Facepunch.Pool.Free(ref fuelSlots);
            }
        }

        private bool HasPermission(BasePlayer player)
        {
            return Rogue.Permissions.Has(player.UserIDString, permUse) ||
                   Rogue.Permissions.Has(player.UserIDString, legacyPermUse);
        }

        #region Configuration

        private readonly Dictionary<string, Dictionary<string, string>> _messages =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        protected override void LoadDefaultMessages()
        {
            // RogueRust localization is stored under lang/<language>/RogueRust/RogueRustFurnaceSplitter/messages.json.
        }

        private static Dictionary<string, string> CreateDefaultMessages()
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["turnon"] = "Turn On",
                ["turnoff"] = "Turn Off",
                ["title"] = "Furnace Splitter",
                ["eta"] = "ETA",
                ["totalstacks"] = "Total stacks",
                ["trim"] = "Trim fuel",
                ["lootsource_invalid"] = "Current loot source invalid",
                ["unsupported_furnace"] = "Unsupported furnace.",
                ["nopermission"] = "You don't have permission to use this.",
                ["StatusONColor"] = "<color=green>ON</color>",
                ["StatusOFFColor"] = "<color=red>OFF</color>",
                ["StatusMessage"] = "Furnace Splitter status set to: "
            };
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
            HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };

            if (Directory.Exists(langRoot))
            {
                string[] languageDirs = Directory.GetDirectories(langRoot, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < languageDirs.Length; i++)
                {
                    string language = Path.GetFileName(languageDirs[i]);
                    if (!string.Equals(language, "RogueRust", StringComparison.OrdinalIgnoreCase))
                        languages.Add(language);
                }
            }

            string oldRogueRoot = Path.Combine(langRoot, "RogueRust", "RogueRustFurnaceSplitter");
            if (Directory.Exists(oldRogueRoot))
            {
                string[] oldFiles = Directory.GetFiles(oldRogueRoot, "*.json", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < oldFiles.Length; i++)
                    languages.Add(Path.GetFileNameWithoutExtension(oldFiles[i]));
            }

            foreach (string language in languages)
            {
                string root = Path.Combine(langRoot, language, "RogueRust", "RogueRustFurnaceSplitter");
                Directory.CreateDirectory(root);
                string target = Path.Combine(root, "messages.json");

                if (!File.Exists(target))
                {
                    string oldRogue = Path.Combine(oldRogueRoot, language + ".json");
                    string legacyFamily = Path.Combine(langRoot, language, "RogueRustFurnaceSplitter.json");
                    string legacyOriginal = Path.Combine(langRoot, language, "FurnaceSplitter.json");

                    if (File.Exists(oldRogue))
                        File.Copy(oldRogue, target, false);
                    else if (File.Exists(legacyFamily))
                        File.Copy(legacyFamily, target, false);
                    else if (File.Exists(legacyOriginal))
                        File.Copy(legacyOriginal, target, false);
                    else if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(target, JsonConvert.SerializeObject(CreateDefaultMessages(), Formatting.Indented));
                }

                if (!File.Exists(target))
                    continue;

                try
                {
                    Dictionary<string, string> catalog =
                        JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(target));
                    if (catalog != null)
                        _messages[language] = catalog;
                }
                catch (Exception exception)
                {
                    LogWarning("Localization", "Could not load '" + target + "': " + exception.Message);
                }
            }

            if (!_messages.ContainsKey("en"))
                _messages["en"] = CreateDefaultMessages();
        }

        private void MigrateLegacyConfig()
        {
            try
            {
                string current = Path.Combine(Interface.Oxide.ConfigDirectory, Name + ".json");
                if (File.Exists(current)) return;

                string legacy = Path.Combine(Interface.Oxide.ConfigDirectory, "FurnaceSplitter.json");
                if (!File.Exists(legacy)) return;

                File.Copy(legacy, current, false);
                LogInformation("Configuration", "Migrated FurnaceSplitter.json to " + Name + ".json.");
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Legacy config migration failed: " + exception.Message);
            }
        }

        private void MigrateLegacyPlayerData()
        {
            if (storedData != null && storedData.AllPlayerOptions.Count > 0)
                return;

            try
            {
                string legacy = Path.Combine(Interface.Oxide.DataDirectory, "FurnaceSplitter.json");
                if (!File.Exists(legacy)) return;

                StoredData migrated = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("FurnaceSplitter");
                if (migrated == null || migrated.AllPlayerOptions.Count == 0) return;

                storedData = migrated;
                SaveData(PlayerDataKey, storedData);
                LogInformation("Data", "Migrated legacy FurnaceSplitter player data into RogueRust storage.");
            }
            catch (Exception exception)
            {
                LogWarning("Data", "Legacy player-data migration failed: " + exception.Message);
            }
        }

        protected override void LoadDefaultConfig()
        {
            LogWarning("Configuration", "Creating default config for RogueRustFurnaceSplitter.");
            config = GetDefaultConfig();
        }

        private PluginConfig GetDefaultConfig()
        {
            return new PluginConfig();
        }

        protected override void LoadConfig()
        {
            MigrateLegacyConfig();
            base.LoadConfig();
            Config.Settings.Converters.Add(new Vector2Converter());

            try
            {
                JObject raw = Config.ReadObject<JObject>();
                bool legacyFlat = raw != null &&
                                  raw["General Settings"] == null &&
                                  (raw["UiPosition"] != null || raw["savePlayerData"] != null || raw["ovens"] != null);

                if (legacyFlat)
                {
                    LegacyPluginConfig legacy = raw.ToObject<LegacyPluginConfig>() ?? new LegacyPluginConfig();
                    config = GetDefaultConfig();
                    config.UiPosition = legacy.UiPosition;
                    config.savePlayerData = legacy.savePlayerData;
                    if (legacy.ovens != null && legacy.ovens.Count > 0)
                        config.ovens = legacy.ovens;

                    LogInformation("Configuration", "Migrated flat FurnaceSplitter configuration to RogueRust grouped settings.");
                }
                else
                {
                    config = raw == null ? GetDefaultConfig() : raw.ToObject<PluginConfig>();
                }
            }
            catch (Exception exception)
            {
                LogWarning("Configuration", "Invalid configuration; using defaults. " + exception.Message);
                config = GetDefaultConfig();
            }

            if (config == null)
                config = GetDefaultConfig();
            if (config.General == null)
                config.General = new PluginConfig.GeneralSettings();
            if (config.UI == null)
                config.UI = new PluginConfig.UiSettings();
            if (config.Furnaces == null)
                config.Furnaces = new PluginConfig.FurnaceSettings();
            if (config.ovens == null)
                config.ovens = new SortedDictionary<string, PluginConfig.OvenConfig>();

            if (!config.ovens.ContainsKey("*"))
            {
                config.ovens["*"] = new PluginConfig.OvenConfig {
                    enabled = true,
                    fuelMultiplier = 1.0f
                };
            }

            config.Version = CurrentVersion;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        private class PluginConfig
        {
            [JsonProperty("General Settings", Order = 10)]
            public GeneralSettings General = new GeneralSettings();

            [JsonProperty("UI Settings", Order = 20)]
            public UiSettings UI = new UiSettings();

            [JsonProperty("Furnace Settings", Order = 30)]
            public FurnaceSettings Furnaces = new FurnaceSettings();

            [JsonProperty("Version (DO NOT CHANGE)", Order = int.MaxValue)]
            public VersionNumber Version = CurrentVersion;

            [JsonIgnore]
            public Vector2 UiPosition
            {
                get { return UI.Position; }
                set { UI.Position = value; }
            }

            [JsonIgnore]
            public bool savePlayerData
            {
                get { return General.SavePlayerData; }
                set { General.SavePlayerData = value; }
            }

            [JsonIgnore]
            public SortedDictionary<string, OvenConfig> ovens
            {
                get { return Furnaces.Ovens; }
                set { Furnaces.Ovens = value; }
            }

            public class GeneralSettings
            {
                [JsonProperty("Save Player Data")]
                public bool SavePlayerData = true;
            }

            public class UiSettings
            {
                [JsonProperty("Position")]
                public Vector2 Position = new Vector2(0.6505f, 0.022f);
            }

            public class FurnaceSettings
            {
                [JsonProperty("Ovens")]
                public SortedDictionary<string, OvenConfig> Ovens =
                    new SortedDictionary<string, OvenConfig>
                    {
                        { "*", new OvenConfig { enabled = true, fuelMultiplier = 1.0f } }
                    };
            }

            public class OvenConfig
            {
                [JsonProperty("Enabled")]
                public bool enabled;

                [JsonProperty("Fuel Multiplier")]
                public float fuelMultiplier = 1.0f;
            }
        }

        private class LegacyPluginConfig
        {
            public Vector2 UiPosition;
            public bool savePlayerData;
            public SortedDictionary<string, PluginConfig.OvenConfig> ovens =
                new SortedDictionary<string, PluginConfig.OvenConfig>();
        }

        private class Vector2Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                Vector2 vec = (Vector2)value;
                serializer.Serialize(writer, new { vec.x, vec.y });
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                Vector2 result = new Vector2();
                JObject jVec = JObject.Load(reader);

                result.x = jVec["x"].ToObject<float>();
                result.y = jVec["y"].ToObject<float>();

                return result;
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector2);
            }
        }

        #endregion Configuration

        #region Exposed plugin methods

        [HookMethod("MoveSplitItem")]
        public string Hook_MoveSplitItem(Item item, BaseOven oven, int totalSlots, int splitAmount)
        {
            MoveResult result = MoveSplitItem(item, oven, totalSlots, splitAmount);
            return result.ToString();
        }

        [HookMethod("GetOvenInfo")]
        public JObject Hook_GetOvenInfo(BaseOven oven)
        {
            OvenInfo ovenInfo = GetOvenInfo(oven);
            return JObject.FromObject(ovenInfo);
        }

        #endregion Exposed plugin methods
    }
}