using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;

namespace PopupSystem.Game.Services.Rpc.RemoteConfig
{
    public sealed class FakeRemoteConfigApi : IRemoteConfigApi
    {
        public const string SwordItemId = DemoItemIds.Sword;
        public const string HealthPotionItemId = DemoItemIds.HealthPotion;
        public const string ArrowsItemId = DemoItemIds.Arrows;
        public const string ChestItemId = DemoItemIds.Chest;
        public const string OreItemId = DemoItemIds.Ore;

        // 12 slots on purpose: small enough that the demo scene reaches "inventory full" within a few claims.
        public const int DemoSlotLimit = 12;

        public async UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken)
        {
            await UniTask.Delay(700, cancellationToken: cancellationToken);

            return new OfferRemoteContent(
                "Starter Offer",
                "A limited-time bundle at a discounted price.",
                "Buy",
                "offers/starter-offer-banner.png");
        }

        public async UniTask<InventoryConfig> GetInventoryConfigAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(500, cancellationToken: cancellationToken);
            return CreateDemoConfig();
        }

        /// <summary>The demo config without the simulated latency, for tests that need the production item ids.</summary>
        public static InventoryConfig CreateDemoConfig()
        {
            return new InventoryConfig(DemoSlotLimit, new ItemCatalog(new[]
            {
                new ItemDefinition(SwordItemId, "Sword", "UI/Items/Sword", 1, ItemCategory.Equipment),
                new ItemDefinition(HealthPotionItemId, "Health Potion", "UI/Items/HealthPotion", 10, ItemCategory.Consumable),
                new ItemDefinition(ArrowsItemId, "Arrows", "UI/Items/Arrows", 50, ItemCategory.Material),
                new ItemDefinition(ChestItemId, "Chest", "UI/Items/Chest", 1, ItemCategory.Container),
                new ItemDefinition(OreItemId, "Ore", "UI/Items/Ore", 20, ItemCategory.Material),
            }));
        }
    }
}
