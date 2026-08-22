namespace PopupSystem.Game.Domain.Inventory
{
    public sealed class InventoryResource
    {
        public string ResourceId { get; }
        public int Amount { get; }

        public InventoryResource(string resourceId, int amount)
        {
            ResourceId = resourceId;
            Amount = amount;
        }
    }
}
