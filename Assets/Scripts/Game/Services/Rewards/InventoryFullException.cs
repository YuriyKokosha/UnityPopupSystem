using System;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Rewards
{
    /// <summary>A grant was refused because its items do not fit. Thrown before anything is applied, so the
    /// wallet is untouched and the claim/purchase can be retried once space is freed.</summary>
    public sealed class InventoryFullException : InvalidOperationException
    {
        public InventoryOperationResult Result { get; }

        public InventoryFullException(InventoryOperationResult result)
            : base(Describe(result))
        {
            Result = result;
        }

        private static string Describe(InventoryOperationResult result)
        {
            if (result == null)
            {
                return "The inventory refused the items.";
            }

            return result.Reason switch
            {
                InventoryFailureReason.NotEnoughSlots =>
                    $"Not enough inventory slots: {result.SlotsRequired} needed, {result.SlotsFree} free.",
                InventoryFailureReason.UnknownItem =>
                    $"Item '{result.ItemId}' is not in the catalog.",
                InventoryFailureReason.QuantityLimitExceeded =>
                    $"Item '{result.ItemId}' would exceed {InventoryConfig.MaxUnitsPerItem} units.",
                _ => "The inventory refused the items.",
            };
        }
    }
}
