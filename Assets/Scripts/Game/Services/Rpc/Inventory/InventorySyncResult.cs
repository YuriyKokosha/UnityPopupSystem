using System;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Services.Rpc.Inventory
{
    /// <summary>The server's answer to "here is my hash": either it matches (keep what you have) or here is the
    /// authoritative snapshot (replace yours). Exactly one of the two.</summary>
    public sealed class InventorySyncResult
    {
        public static readonly InventorySyncResult Valid = new(true, null);

        public bool IsValid { get; }
        public InventorySnapshot Snapshot { get; }

        private InventorySyncResult(bool isValid, InventorySnapshot snapshot)
        {
            IsValid = isValid;
            Snapshot = snapshot;
        }

        public static InventorySyncResult Replace(InventorySnapshot snapshot)
        {
            return new InventorySyncResult(false, snapshot ?? throw new ArgumentNullException(nameof(snapshot)));
        }
    }
}
