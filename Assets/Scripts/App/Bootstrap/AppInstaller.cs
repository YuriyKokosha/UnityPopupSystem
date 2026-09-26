using System;
using PopupSystem.App.Runtime;
using PopupSystem.App.States;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.Game.Services.WindowQueue;
using PopupSystem.Game.Services.WindowQueue.Aggregators;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Infrastructure;
using PopupSystem.UI.Preloader;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Runtime.Registry;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.DailyReward;
using PopupSystem.UI.Windows.Inventory;
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
        private const string PreloaderOverlayAddress = "UI/PreloaderOverlay";

        [SerializeField] private UIRoot _uiRoot;

        public override void InstallBindings()
        {
            Container.Bind<UIRoot>().FromInstance(_uiRoot).AsSingle();
            Container.Bind<IUILayerProvider>().To<UIRoot>().FromResolve();
            // Concrete type first, then the port and IDisposable via FromResolve: anything that owns
            // something releasable has to resolve to one instance rather than one per binding.
            Container.Bind<AddressablesUiPrefabProvider>().AsSingle();
            Container.Bind<IUiPrefabProvider>().To<AddressablesUiPrefabProvider>().FromResolve();
            Container.Bind<IDisposable>().To<AddressablesUiPrefabProvider>().FromResolve();
            Container.Bind<AddressablesUiIconProvider>().AsSingle();
            Container.Bind<IUiIconProvider>().To<AddressablesUiIconProvider>().FromResolve();
            Container.Bind<IDisposable>().To<AddressablesUiIconProvider>().FromResolve();
            Container.Bind<RewardIcons>().AsSingle();

            Container.Bind<PreloaderOverlayView>().FromMethod(context =>
                CreatePreloaderOverlay(context.Container.Resolve<IUiPrefabProvider>())).AsSingle();

            Container.Bind<IRpcManager>().To<FakeRpcManager>().AsSingle();

            Container.Bind<RemoteImageLoader>().AsSingle().WithArguments(GetStreamingAssetsBaseUrl());
            Container.Bind<IRemoteImageLoader>().To<RemoteImageLoader>().FromResolve();
            Container.Bind<IDisposable>().To<RemoteImageLoader>().FromResolve();

            Container.Bind<IWindowModule>().To<MainGameWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<SettingsWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<RewardPopupModule>().AsSingle();
            Container.Bind<IWindowModule>().To<DailyRewardWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<OfferWindowModule>().AsSingle();
            Container.Bind<IWindowModule>().To<InventoryWindowModule>().AsSingle();

            Container.BindIFactory<MainGameWindowController>().To<MainGameWindowController>();
            Container.BindIFactory<InventoryWindowController>().To<InventoryWindowController>();
            Container.BindIFactory<SettingsWindowController>().To<SettingsWindowController>();
            Container.BindIFactory<RewardPopupController>().To<RewardPopupController>();
            Container.BindIFactory<DailyRewardWindowController>().To<DailyRewardWindowController>();
            Container.BindIFactory<OfferWindowController>().To<OfferWindowController>();
            Container.Bind<PrefabFactory<WindowView>>().AsSingle();

            Container.Bind<IWindowRegistry>().To<WindowRegistry>().AsSingle();
            Container.Bind<IWindowControllerResolver>().To<WindowControllerResolver>().AsSingle();

            Container.Bind<WindowFactory>().AsSingle();
            Container.Bind<IWindowFactory>().To<WindowFactory>().FromResolve();
            Container.Bind<IDisposable>().To<WindowFactory>().FromResolve();

            Container.Bind<WindowsManager>().AsSingle();
            Container.Bind<IWindowsManager>().To<WindowsManager>().FromResolve();
            Container.Bind<IDisposable>().To<WindowsManager>().FromResolve();

            Container.Bind<ServerSyncedTimeProvider>().AsSingle();
            Container.Bind<ITimeProvider>().To<ServerSyncedTimeProvider>().FromResolve();

            Container.Bind<ITickable>().To<TimeResyncTicker>().AsSingle();

            Container.Bind<PlayerProfileManager>().AsSingle();
            Container.Bind<WalletManager>().AsSingle();
            Container.Bind<IInventoryStorage>().To<FileInventoryStorage>().AsSingle()
                .WithArguments(Application.persistentDataPath);
            Container.Bind<InventoryManager>().AsSingle();
            Container.Bind<InventorySyncService>().AsSingle();
            Container.Bind<RewardGrantService>().AsSingle();
            Container.Bind<DailyRewardManager>().AsSingle();
            Container.Bind<OfferManager>().AsSingle();
            Container.Bind<WindowQueueManager>().AsSingle();
            Container.Bind<DailyRewardWindowAggregator>().AsSingle();
            Container.Bind<IWindowQueueAggregator>().To<DailyRewardWindowAggregator>().FromResolve();
            Container.Bind<IDisposable>().To<DailyRewardWindowAggregator>().FromResolve();

            Container.Bind<IWindowQueueAggregator>().To<OfferWindowAggregator>().AsSingle();
            Container.Bind<WindowQueueRunner>().AsSingle();
            Container.Bind<IDisposable>().To<WindowQueueRunner>().FromResolve();

            Container.Bind<AppInitState>().AsSingle();
            Container.Bind<AppConnectServerState>().AsSingle();
            Container.Bind<AppMainGameState>().AsSingle();
            Container.Bind<AppStateManager>().AsSingle();
            Container.Bind<AppEntryPoint>().AsSingle();
            Container.Bind<IInitializable>().To<AppEntryPoint>().FromResolve();

            Container.Bind<IDisposable>().To<AppStateManager>().FromResolve();
        }

        private static string GetStreamingAssetsBaseUrl()
        {
            var path = Application.streamingAssetsPath;
            return path.Contains("://") ? path : "file://" + path;
        }

        private PreloaderOverlayView CreatePreloaderOverlay(IUiPrefabProvider prefabProvider)
        {
            var prefab = prefabProvider.LoadBlocking(PreloaderOverlayAddress);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"AppInstaller: nothing at Addressables address '{PreloaderOverlayAddress}'. " +
                    "The preloader overlay is required to boot - fix or restore the prefab asset.");
            }

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
