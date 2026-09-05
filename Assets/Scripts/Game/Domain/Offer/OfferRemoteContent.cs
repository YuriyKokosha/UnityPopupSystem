namespace PopupSystem.Game.Domain.Offer
{
    public sealed class OfferRemoteContent
    {
        public string Title { get; }
        public string Description { get; }
        public string ActionText { get; }

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
