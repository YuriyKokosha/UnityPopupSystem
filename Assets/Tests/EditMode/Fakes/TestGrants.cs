using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Wallet;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>A wallet + inventory + grant service wired together on fakes, the way AppInstaller wires them
    /// on production types. The catalog is the production fake's, because the daily reward and the offer name
    /// production item ids.</summary>
    public sealed class TestGrants
    {
        public TestGrants(int slotLimit, string playerId = "p1")
        {
            Storage = new FakeInventoryStorage();
            Wallet = new WalletManager();
            Inventory = new InventoryManager(Storage);
            Inventory.Initialize(new InventoryConfig(slotLimit, ProductionCatalog.Catalog));
            Inventory.Load(playerId, InventorySnapshot.Empty);
            Grants = new RewardGrantService(Wallet, Inventory);
        }

        public FakeInventoryStorage Storage { get; }
        public WalletManager Wallet { get; }
        public InventoryManager Inventory { get; }
        public RewardGrantService Grants { get; }

        /// <summary>Fills every slot with non-stackable items so nothing else fits.</summary>
        public void FillInventory()
        {
            var free = Inventory.FreeSlots ?? 0;
            if (free > 0)
            {
                Inventory.TryAdd(TestItems.Of(FakeRemoteConfigApi.ChestItemId, free));
            }
        }
    }
}
