namespace PopupSystem.Game.Domain.Core
{
    public sealed class PlayerProfile
    {
        public string PlayerId { get; }
        public string DisplayName { get; }
        public int Level { get; }

        public PlayerProfile(string playerId, string displayName, int level)
        {
            PlayerId = playerId;
            DisplayName = displayName;
            Level = level;
        }
    }
}
