using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Services.Inventory;

namespace PopupSystem.Tests.EditMode.Fakes
{
    /// <summary>Three items covering the stacking shapes: never stacks, stacks by 10, stacks by 50.</summary>
    public static class TestItems
    {
        public const string Sword = "sword";
        public const string Potion = "potion";
        public const string Arrows = "arrows";

        public static ItemCatalog Catalog { get; } = new(new[]
        {
            new ItemDefinition(Sword, "Sword", null, 1, ItemCategory.Equipment),
            new ItemDefinition(Potion, "Potion", null, 10, ItemCategory.Consumable),
            new ItemDefinition(Arrows, "Arrows", null, 50, ItemCategory.Material),
        });

        public static InventoryConfig Config(int slotLimit)
        {
            return new InventoryConfig(slotLimit, Catalog);
        }

        public static InventoryManager Manager(int slotLimit, FakeInventoryStorage storage = null, string playerId = "p1")
        {
            var manager = new InventoryManager(storage ?? new FakeInventoryStorage());
            manager.Initialize(Config(slotLimit));
            manager.Load(playerId, InventorySnapshot.Empty);
            return manager;
        }

        public static ItemAmount[] Of(string itemId, int count)
        {
            return new[] { new ItemAmount(itemId, count) };
        }
    }
}
