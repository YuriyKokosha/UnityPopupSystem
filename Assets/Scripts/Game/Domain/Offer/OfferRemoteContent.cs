namespace PopupSystem.Game.Domain.Offer
{
    /// <summary>
    /// The presentation half of an offer (copy + banner asset) - fetched from a remote
    /// config/CMS endpoint at display time rather than baked into the client, unlike
    /// <see cref="OfferData"/> which holds the offer's economic terms.
    /// </summary>
    public sealed class OfferRemoteContent
    {
        public string Title { get; }
        public string Description { get; }
        public string ActionText { get; }

        /// <summary>
        /// May be null/empty, meaning this offer has no banner - callers must handle that case
        /// instead of assuming a URL is always present.
        /// </summary>
        public string BannerImageUrl { get; }

        public OfferRemoteContent(string title, string description, string actionText, string bannerImageUrl)
        {
            Title = title;
            Description = description;
            ActionText = actionText;
            BannerImageUrl = bannerImageUrl;
        }
    }
}
