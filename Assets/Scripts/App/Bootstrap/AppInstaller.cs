using PopupSystem.App.Runtime;
using PopupSystem.App.States;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.UI.Enum;
using PopupSystem.UI.Infrastructure;
using PopupSystem.UI.Preloader;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Runtime.Registry;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.DailyReward;
using PopupSystem.UI.Windows.MainGame;
using PopupSystem.UI.Windows.Offer;
using PopupSystem.UI.Windows.RewardPopup;
using PopupSystem.UI.Windows.Settings;
using UnityEngine;
using Zenject;

namespace PopupSystem.App.Bootstrap
{
    public sealed class AppInstaller : MonoInstaller
    {
        private const string PreloaderOverlayResourcePath = "UI/PreloaderOverlay";

        [SerializeField] private UIRoot _uiRoot;

        public override void InstallBindings()
        {
            Container.Bind<UIRoot>().FromInstance(_uiRoot).AsSingle();
            Container.Bind<PreloaderOverlayView>().FromMethod(_ => CreatePreloaderOverlay()).AsSingle();

            Container.Bind<IRpcManager>().To<FakeRpcManager>().AsSingle();
            Container.Bind<IRemoteImageLoader>().To<RemoteImageLoader>().AsSingle()
                .WithArguments("file://" + Application.streamingAssetsPath);

            Container.Bind<IWindowModule>().To<MainGameWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<SettingsWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<RewardPopupModule>().AsSingle();
            Container.Bind<IWindowModule>().To<DailyRewardWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<OfferWindowModule>().AsSingle();

            Container.Bind<IWindowRegistry>().To<WindowRegistry>().AsSingle();
            Container.Bind<IWindowControllerResolver>().To<WindowControllerResolver>().AsSingle();
            Container.Bind<IWindowFactory>().To<WindowFactory>().AsSingle();
            Container.Bind<IWindowsManager>().To<WindowsManager>().AsSingle();

            Container.Bind<PlayerProfileManager>().AsSingle();
            Container.Bind<PlayerInventoryManager>().AsSingle();
            Container.Bind<DailyRewardManager>().AsSingle();
            Container.Bind<OfferManager>().AsSingle();
            Container.Bind<WindowQueueManager>().AsSingle();
            Container.Bind<IWindowQueueAggregator>().To<DailyRewardWindowAggregator>().AsSingle();
            Container.Bind<IWindowQueueAggregator>().To<OfferWindowAggregator>().AsSingle();
            Container.Bind<WindowQueueRunner>().AsSingle();

            Container.Bind<AppInitState>().AsSingle();
            Container.Bind<AppConnectServerState>().AsSingle();
            Container.Bind<AppMainGameState>().AsSingle();
            Container.Bind<AppStateManager>().AsSingle();
            Container.Bind<AppEntryPoint>().AsSingle();
            Container.Bind<IInitializable>().To<AppEntryPoint>().FromResolve();
        }

        /// <summary>
        /// The preloader overlay's content lives in a prefab (like every window) rather than the
        /// scene, so it is instantiated here the same way WindowFactory instantiates windows -
        /// nothing about it is built from code.
        /// </summary>
        private PreloaderOverlayView CreatePreloaderOverlay()
        {
            var prefab = Resources.Load<GameObject>(PreloaderOverlayResourcePath);
            var overlay = Container.InstantiatePrefabForComponent<PreloaderOverlayView>(prefab);

            var rectTransform = overlay.GetComponent<RectTransform>();
            rectTransform.SetParent(_uiRoot.GetLayer(UILayerType.System), false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            return overlay;
        }
    }
}
