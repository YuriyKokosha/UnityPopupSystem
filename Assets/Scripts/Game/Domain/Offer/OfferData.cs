using System;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;

namespace PopupSystem.Game.Domain.Offer
{
    /// <summary>The economic terms of an offer: what it costs, what it grants, and when it is on sale. Copy and
    /// art are <see cref="OfferRemoteContent"/>, fetched separately. <see cref="Price"/> is null for a free
    /// offer — a claim, in effect.</summary>
    public sealed class OfferData
    {
        public string OfferId { get; }
        public CurrencyAmount Price { get; }
        public RewardBundle Reward { get; }
        public DateTime StartsAtUtc { get; }
        public DateTime EndsAtUtc { get; }

        public OfferData(
            string offerId,
            CurrencyAmount price,
            RewardBundle reward,
            DateTime startsAtUtc,
            DateTime endsAtUtc)
        {
            if (price != null && price.Amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(price), price.Amount, "A price cannot be negative.");
            }

            OfferId = offerId;
            Price = price;
            Reward = reward ?? RewardBundle.Empty;
            StartsAtUtc = startsAtUtc;
            EndsAtUtc = endsAtUtc;
        }

        public bool IsFree => Price == null || Price.Amount == 0;

        public bool IsActiveAt(DateTime utcNow)
        {
            return utcNow >= StartsAtUtc && utcNow <= EndsAtUtc;
        }
    }
}
