namespace PopupSystem.Game.Domain.Inventory
{
    public enum InventoryFailureReason
    {
        None = 0,
        UnknownItem = 1,
        NotEnoughSlots = 2,
        NotEnoughItems = 3,
        QuantityLimitExceeded = 4,
    }

    /// <summary>The answer to an add/remove without an exception: the UI shows the reason, tests check the
    /// numbers. A rejected operation changes nothing.</summary>
    public sealed class InventoryOperationResult
    {
        public static readonly InventoryOperationResult Ok = new(true, InventoryFailureReason.None, 0, 0, null);

        public bool Success { get; }
        public InventoryFailureReason Reason { get; }
        public int SlotsRequired { get; }
        public int SlotsFree { get; }
        public string ItemId { get; }

        private InventoryOperationResult(bool success, InventoryFailureReason reason, int slotsRequired, int slotsFree, string itemId)
        {
            Success = success;
            Reason = reason;
            SlotsRequired = slotsRequired;
            SlotsFree = slotsFree;
            ItemId = itemId;
        }

        public static InventoryOperationResult NotEnoughSlots(int slotsRequired, int slotsFree)
        {
            return new InventoryOperationResult(false, InventoryFailureReason.NotEnoughSlots, slotsRequired, slotsFree, null);
        }

        public static InventoryOperationResult UnknownItem(string itemId)
        {
            return new InventoryOperationResult(false, InventoryFailureReason.UnknownItem, 0, 0, itemId);
        }

        public static InventoryOperationResult NotEnoughItems(string itemId)
        {
            return new InventoryOperationResult(false, InventoryFailureReason.NotEnoughItems, 0, 0, itemId);
        }

        /// <summary>The add would take <paramref name="itemId"/> past <see cref="InventoryConfig.MaxUnitsPerItem"/>.</summary>
        public static InventoryOperationResult QuantityLimitExceeded(string itemId)
        {
            return new InventoryOperationResult(false, InventoryFailureReason.QuantityLimitExceeded, 0, 0, itemId);
        }
    }
}
