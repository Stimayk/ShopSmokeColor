using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;
using System.Drawing;
using static CounterStrikeSharp.API.Core.Listeners;

namespace ShopSmokeColor
{
    public class ShopSmokeColor : BasePlugin
    {
        public override string ModuleName => "[SHOP] Smoke Color";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.0";

        private IShopApi? SHOP_API;
        private const string CategoryName = "SmokeColor";
        public static JObject? JsonSmokeColor { get; private set; }
        private readonly PlayerSmokeColor[] playerSmokeColor = new PlayerSmokeColor[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/SmokeColor.json");
            if (File.Exists(configPath))
            {
                JsonSmokeColor = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonSmokeColor == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "÷ветной дым");

            var sortedItems = JsonSmokeColor.Properties()
                .Where(p => p.Value is JObject)
                .Select(p => new { Key = p.Name, Value = (JObject)p.Value })
                .ToList();

            int teamIndex = sortedItems.FindIndex(p => p.Key == "TeamSmoke");
            if (teamIndex != -1)
            {
                var teamItem = sortedItems[teamIndex];
                sortedItems.RemoveAt(teamIndex);
                sortedItems.Insert(0, teamItem);
            }

            int randomIndex = sortedItems.FindIndex(p => p.Key == "RandomSmoke");
            if (randomIndex != -1)
            {
                var randomItem = sortedItems[randomIndex];
                sortedItems.RemoveAt(randomIndex);

                int insertIndex = teamIndex != -1 ? 1 : 0;
                sortedItems.Insert(insertIndex, randomItem);
            }

            foreach (var item in sortedItems)
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Key,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<OnClientDisconnect>(playerSlot => playerSmokeColor[playerSlot] = null!);
            RegisterListener<OnEntitySpawned>(OnEntitySpawned);
        }

        public void OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName, int buyPrice, int sellPrice, int duration, int count)
        {
            string color;
            if (uniqueName == "Team")
            {
                color = "team";
            }
            else if (uniqueName == "Random")
            {
                color = "random";
            }
            else if (!TryGetItemColor(uniqueName, out color))
            {
                Logger.LogError($"{uniqueName} has invalid or missing 'color' in config!");
                return;
            }

            playerSmokeColor[player.Slot] = new PlayerSmokeColor(color, itemId);
        }

        public void OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1)
            {
                string color;
                if (uniqueName == "TeamSmoke")
                {
                    color = "team";
                }
                else if (uniqueName == "RandomSmoke")
                {
                    color = "random";
                }
                else if (!TryGetItemColor(uniqueName, out color))
                {
                    Logger.LogError($"{uniqueName} has invalid or missing 'color' in config!");
                    return;
                }

                playerSmokeColor[player.Slot] = new PlayerSmokeColor(color, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
        }

        public void OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerSmokeColor[player.Slot] = null!;
        }

        private void OnEntitySpawned(CEntityInstance entity)
        {
            if (entity.DesignerName != "smokegrenade_projectile") return;

            var smokeGrenade = new CSmokeGrenadeProjectile(entity.Handle);
            if (smokeGrenade.Handle == IntPtr.Zero) return;

            Server.NextFrame(() =>
            {
                var throwerValue = smokeGrenade.Thrower.Value;
                if (throwerValue == null) return;
                var throwerValueController = throwerValue.Controller.Value;
                if (throwerValueController == null) return;
                var controller = new CCSPlayerController(throwerValueController.Handle);

                if (playerSmokeColor[controller.Slot] == null) return;

                var smokeColor = GetSmokeColor(controller);

                smokeGrenade.SmokeColor.X = smokeColor.R;
                smokeGrenade.SmokeColor.Y = smokeColor.G;
                smokeGrenade.SmokeColor.Z = smokeColor.B;
            });
        }

        private Color GetSmokeColor(CCSPlayerController player)
        {
            string colorString = playerSmokeColor[player.Slot].Color;

            if (colorString == "random")
            {
                return GetRandomColor();
            }
            else if (colorString == "team")
            {
                return player.TeamNum == 3 ? Color.Blue : Color.Yellow;
            }
            else
            {
                try
                {
                    return ColorTranslator.FromHtml(colorString);
                }
                catch (Exception)
                {
                    return Color.White;
                }
            }
        }

        private static Color GetRandomColor()
        {
            Random rnd = new();
            return Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256));
        }

        private static bool TryGetItemColor(string uniqueName, out string color)
        {
            color = "";
            if (JsonSmokeColor != null && JsonSmokeColor.TryGetValue(uniqueName, out JToken? obj) && obj is JObject jsonItem && jsonItem["color"] != null && jsonItem["color"]!.Type != JTokenType.Null)
            {
                color = jsonItem["color"]!.ToString();
                return true;
            }
            return false;
        }

        public record PlayerSmokeColor(string Color, int ItemID);
    }
}