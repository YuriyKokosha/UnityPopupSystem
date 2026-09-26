using System;
using Cysharp.Threading.Tasks;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.Rewards;
using UnityEngine;

namespace PopupSystem.UI.Windows.RewardPopup
{
    /// <summary>Opens the reward popup over a transaction that is already running. A popup that fails to open
    /// is logged and reported as <c>null</c> rather than thrown, so the caller still awaits the transaction and
    /// lets its outcome decide the button.</summary>
    public static class RewardPopupLauncher
    {
        public static async UniTask<WindowHandle> TryOpenAsync(
            IWindowsManager windowsManager,
            UniTask<RewardPopupData> rewardTask)
        {
            try
            {
                return await windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(rewardTask));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return null;
            }
        }
    }
}
