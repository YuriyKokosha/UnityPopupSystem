using System.Collections.Generic;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Domain.Rewards
{
    public sealed class RewardPopupData
    {
        public string Title { get; }
        public IReadOnlyList<InventoryResource> Rewards { get; }

        public RewardPopupData(string title, IReadOnlyList<InventoryResource> rewards)
        {
            Title = title;
            Rewards = rewards;
        }
    }
}
