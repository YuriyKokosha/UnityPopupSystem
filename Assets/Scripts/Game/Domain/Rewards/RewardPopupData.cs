namespace PopupSystem.Game.Domain.Rewards
{
    public sealed class RewardPopupData
    {
        public string Title { get; }
        public RewardBundle Reward { get; }

        public RewardPopupData(string title, RewardBundle reward)
        {
            Title = title;
            Reward = reward ?? RewardBundle.Empty;
        }
    }
}
