using Cysharp.Threading.Tasks;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.UI.Core;

namespace PopupSystem.UI.Windows.RewardPopup
{
    /// <summary>
    /// Payload for opening RewardPopup while the reward itself is still being resolved (a claim
    /// or purchase call in flight), rather than already-known data. RewardPopupController awaits
    /// RewardTask itself and drives the popup's own loading state from it - this is what makes
    /// RewardPopupView.SetLoading a real signal instead of a stub that's flipped off immediately.
    /// Callers that already have the final data (nothing left to wait for) can still use this by
    /// wrapping it in <see cref="UniTask.FromResult{T}"/>.
    ///
    /// Lives next to RewardPopupController (not in Game/Domain/Rewards, where it started) because
    /// it implements IWindowData - a UI-layer concept. Leaving it in Game/Domain would make the
    /// domain layer depend on the UI layer, backwards from the dependency direction everywhere
    /// else in the project (UI depends on Game, never the reverse). RewardPopupData below it -
    /// the resolved reward itself, with no UI dependency - correctly stays in Game/Domain/Rewards.
    /// </summary>
    public sealed class RewardPopupRequest : IWindowData
    {
        public UniTask<RewardPopupData> RewardTask { get; }

        public RewardPopupRequest(UniTask<RewardPopupData> rewardTask)
        {
            RewardTask = rewardTask;
        }
    }
}
