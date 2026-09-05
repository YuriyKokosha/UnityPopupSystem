using System;
using System.Collections.Generic;
using PopupSystem.Game.Domain.Inventory;

namespace PopupSystem.Game.Domain.Offer
{
    public sealed class OfferData
    {
        public string OfferId { get; }
        public IReadOnlyList<InventoryResource> Rewards { get; }
        public DateTime StartsAtUtc { get; }
        public DateTime EndsAtUtc { get; }

        public OfferData(
            string offerId,
            IReadOnlyList<InventoryResource> rewards,
            DateTime startsAtUtc,
            DateTime endsAtUtc)
        {
            OfferId = offerId;
            Rewards = rewards;
            StartsAtUtc = startsAtUtc;
            EndsAtUtc = endsAtUtc;
        }

        public bool IsActiveAt(DateTime utcNow)
        {
            return utcNow >= StartsAtUtc && utcNow <= EndsAtUtc;
        }
    }
}
