using PopupSystem.Game.Domain.Core;

namespace PopupSystem.Game.Services.Profile
{
    public sealed class PlayerProfileManager
    {
        public PlayerProfile CurrentProfile { get; private set; }

        public void SetProfile(PlayerProfile profile)
        {
            CurrentProfile = profile;
        }
    }
}
