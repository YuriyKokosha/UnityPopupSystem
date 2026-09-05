using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.WindowQueue;
using PopupSystem.Game.Services.Time;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Runtime.Registry;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.DailyReward;
using PopupSystem.UI.Windows.Offer;
using PopupSystem.UI.Windows.RewardPopup;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zenject;

namespace PopupSystem.Tests.PlayMode
{
    [TestFixture]
    public sealed class WindowFlowPlayModeTests
    {
        private const int PollTimeoutMs = 5000;
        private const int PollStepMs = 50;

        private static readonly DateTime StartUtc = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private TestUiHierarchy _ui;
        private DiContainer _container;
        private AddressablesUiPrefabProvider _prefabProvider;
        private WindowFactory _windowFactory;
        private WindowsManager _windowsManager;
        private TestTimeProvider _time;
        private FakeFlowRpcManager _rpc;
        private FakeRemoteImageLoader _imageLoader;

        [SetUp]
        public void SetUp()
        {
            _ui = new TestUiHierarchy();
            _container = new DiContainer();

            _time = new TestTimeProvider(StartUtc);
            _rpc = new FakeFlowRpcManager();
            _imageLoader = new FakeRemoteImageLoader();

            _container.Bind<ITimeProvider>().FromInstance(_time);
            _container.Bind<IRpcManager>().FromInstance(_rpc);
            _container.Bind<IRemoteImageLoader>().FromInstance(_imageLoader);
            _container.Bind<PlayerInventoryManager>().AsSingle();
            _container.Bind<DailyRewardManager>().AsSingle();
            _container.Bind<OfferManager>().AsSingle();

            _container.BindIFactory<DailyRewardWindowController>().To<DailyRewardWindowController>();
            _container.BindIFactory<OfferWindowController>().To<OfferWindowController>();
            _container.BindIFactory<RewardPopupController>().To<RewardPopupController>();

            var modules = new List<IWindowModule>
            {
                _container.Instantiate<DailyRewardWindowModule>(),
                _container.Instantiate<OfferWindowModule>(),
                _container.Instantiate<RewardPopupModule>(),
            };

            _prefabProvider = new AddressablesUiPrefabProvider();

            _prefabProvider.LoadBlocking(ModalBackdropPresenter.BackdropPrefabAddress);

            var registry = new WindowRegistry(modules);
            _windowFactory = new WindowFactory(
                registry,
                new WindowControllerResolver(modules),
                _ui,
                _prefabProvider,
                _container.Instantiate<PrefabFactory<WindowView>>());

            _windowsManager = new WindowsManager(_windowFactory, registry, _prefabProvider);

            _container.Bind<IWindowsManager>().FromInstance(_windowsManager);
        }

        [TearDown]
        public void TearDown()
        {
            _rpc.RemoteConfigEndpoint.CancelIfPending();

            _windowsManager.Dispose();
            _windowFactory.Dispose();
            _imageLoader.Dispose();
            _prefabProvider.Dispose();
            _ui.Dispose();
        }

        [Test]
        public async Task DailyReward_Claim_OpensTheRewardPopupWhileTheClaimIsStillInFlight()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.DailyReward);
            var view = FindView<DailyRewardWindowView>(UILayerType.Windows);

            Assert.That(view, Is.Not.Null);
            Assert.That(HasText(view, "Daily Reward"), Is.True, "The window should show its own copy.");

            var claimButton = ButtonLabelled(view, "Claim");
            Assert.That(claimButton, Is.Not.Null, "The claim button is the one whose label SetContent wrote.");
            Assert.That(claimButton.interactable, Is.True);

            claimButton.onClick.Invoke();

            var popupOpened = await WaitUntilAsync(() => FindView<RewardPopupView>(UILayerType.Popups) != null);
            Assert.That(popupOpened, Is.True, "The reward popup opens before the claim resolves.");
            Assert.That(
                claimButton.interactable,
                Is.False,
                "The claim button must be disabled for the duration of the claim, or a second tap starts another one.");

            var rewardShown = await WaitUntilAsync(
                () => HasText(FindView<RewardPopupView>(UILayerType.Popups), "Daily Reward Claimed"));
            Assert.That(rewardShown, Is.True, "The popup fills itself in once the claim resolves.");

            Assert.That(handle.IsClosed, Is.False, "The window stays open while its own popup is up.");

            await _windowsManager.CloseTopPopupAsync();

            var windowClosed = await WaitUntilAsync(() => handle.IsClosed);
            Assert.That(windowClosed, Is.True, "Dismissing the reward popup closes the window that opened it.");
        }

        [Test]
        public async Task DailyReward_ReopenedFromThePool_HasItsClaimButtonEnabledAgain()
        {
            var firstHandle = await _windowsManager.OpenAsync(WindowType.DailyReward);
            var firstView = FindView<DailyRewardWindowView>(UILayerType.Windows);
            ButtonLabelled(firstView, "Claim").onClick.Invoke();

            Assert.That(await WaitUntilAsync(() => FindView<RewardPopupView>(UILayerType.Popups) != null), Is.True);
            Assert.That(
                await WaitUntilAsync(() => HasText(FindView<RewardPopupView>(UILayerType.Popups), "Daily Reward Claimed")),
                Is.True);

            await _windowsManager.CloseTopPopupAsync();
            Assert.That(await WaitUntilAsync(() => firstHandle.IsClosed), Is.True);

            await _windowsManager.OpenAsync(WindowType.DailyReward);
            var secondView = FindView<DailyRewardWindowView>(UILayerType.Windows);

            Assert.That(secondView, Is.SameAs(firstView), "This test is only meaningful on a pooled view.");
            Assert.That(
                ButtonLabelled(secondView, "Claim").interactable,
                Is.True,
                "A reopened window must start from a clean baseline, not from where the last one left off.");
        }

        [Test]
        public async Task Offer_OpensWithAPlaceholder_ThenFillsInTheRemoteCopy()
        {
            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            Assert.That(HasText(view, "Loading offer..."), Is.True);

            _rpc.RemoteConfigEndpoint.Resolve(
                new OfferRemoteContent("Starter Offer", "500 gems and 50 energy.", "Buy", bannerImageUrl: null));

            var filled = await WaitUntilAsync(() => HasText(view, "500 gems and 50 energy."));
            Assert.That(filled, Is.True, "The remote copy replaces the placeholder once it arrives.");
            Assert.That(ButtonLabelled(view, "Buy"), Is.Not.Null, "The CTA label is remote-sourced too.");
        }

        [Test]
        public async Task Offer_FallsBackToSafeCopy_WhenTheRemoteConfigEndpointFails()
        {
            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            _rpc.RemoteConfigEndpoint.Fail(new InvalidOperationException("remote config unreachable"));

            var fellBack = await WaitUntilAsync(() => HasText(view, "Special Offer"));
            Assert.That(fellBack, Is.True, "A failed content fetch must degrade, not blank the window.");
            Assert.That(ButtonLabelled(view, "Buy"), Is.Not.Null, "The offer is still purchasable on fallback copy.");
        }

        [Test]
        public async Task Offer_ShowsNoActiveOffer_OnceItsWindowHasClosed()
        {
            _container.Resolve<OfferManager>().GetActiveOfferData();
            _time.AdvanceDays(8);

            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            Assert.That(HasText(view, "No active offer."), Is.True);
            Assert.That(
                _imageLoader.LoadCount,
                Is.Zero,
                "With no offer there is nothing to fetch - it must not go to the network anyway.");
        }

        [Test]
        public async Task Offer_ClosedWhileItsContentIsInFlight_NeverWritesIntoTheTornDownView()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            await handle.CloseAsync();
            Assert.That(handle.IsClosed, Is.True);

            _rpc.RemoteConfigEndpoint.Resolve(
                new OfferRemoteContent("Too late", "Too late", "Too late", bannerImageUrl: null));

            await UniTask.Delay(200);

            Assert.That(
                HasText(view, "Too late"),
                Is.False,
                "Content arriving after the window closed must not reach the view.");
        }

        [Test]
        public async Task RewardPopup_ShowsAnError_WhenTheOperationItIsObservingFails()
        {
            var operation = new UniTaskCompletionSource<RewardPopupData>();
            await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(operation.Task));
            var view = FindView<RewardPopupView>(UILayerType.Popups);

            operation.TrySetException(new InvalidOperationException("the grant was rejected"));

            var reported = await WaitUntilAsync(
                () => HasText(view, "Couldn't claim your reward. Please try again."));
            Assert.That(reported, Is.True, "A rejected operation is worth telling the player about.");
        }

        [Test]
        public async Task RewardPopup_SaysNothing_WhenTheOperationItIsObservingIsCancelled()
        {
            var operation = new UniTaskCompletionSource<RewardPopupData>();
            await _windowsManager.OpenAsync(WindowType.RewardPopup, new RewardPopupRequest(operation.Task));
            var view = FindView<RewardPopupView>(UILayerType.Popups);

            operation.TrySetCanceled();
            await UniTask.Delay(200);

            Assert.That(
                HasText(view, "Couldn't claim your reward. Please try again."),
                Is.False);
        }

        private TView FindView<TView>(UILayerType layerType) where TView : WindowView
        {
            return _ui.GetLayer(layerType).GetComponentsInChildren<TView>(true).FirstOrDefault();
        }

        private static bool HasText(WindowView view, string expected)
        {
            return view != null &&
                   view.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text == expected);
        }

        private static Button ButtonLabelled(WindowView view, string label)
        {
            return view == null
                ? null
                : view.GetComponentsInChildren<Button>(true)
                    .FirstOrDefault(button => button.GetComponentsInChildren<TMP_Text>(true)
                        .Any(text => text.text == label));
        }

        private static async UniTask<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = PollTimeoutMs)
        {
            var elapsed = Stopwatch.StartNew();

            while (!condition())
            {
                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                {
                    return false;
                }

                await UniTask.Delay(PollStepMs);
            }

            return true;
        }

        private sealed class TestTimeProvider : ITimeProvider
        {
            public TestTimeProvider(DateTime utcNow) => UtcNow = utcNow;

            public DateTime UtcNow { get; private set; }

            public void AdvanceDays(double days) => UtcNow = UtcNow.AddDays(days);
        }

        private sealed class FakeFlowRpcManager : IRpcManager
        {
            public GatedRemoteConfigEndpoint RemoteConfigEndpoint { get; } = new();

            public ICoreRpcApi Core => throw new NotSupportedException("These flows never call Core.");
            public IInventoryRpcApi Inventory => throw new NotSupportedException("These flows never call Inventory.");
            public IWindowQueueRpcApi WindowQueue => throw new NotSupportedException("These flows never call WindowQueue.");
            public IRemoteConfigApi RemoteConfig => RemoteConfigEndpoint;
        }

        private sealed class GatedRemoteConfigEndpoint : IRemoteConfigApi
        {
            private readonly UniTaskCompletionSource<OfferRemoteContent> _pending = new();

            public UniTask<OfferRemoteContent> GetOfferContentAsync(string offerId, CancellationToken cancellationToken)
            {
                return _pending.Task;
            }

            public void Resolve(OfferRemoteContent content) => _pending.TrySetResult(content);

            public void Fail(Exception exception) => _pending.TrySetException(exception);

            public void CancelIfPending() => _pending.TrySetCanceled();
        }

        private sealed class FakeRemoteImageLoader : IRemoteImageLoader, IDisposable
        {
            private Texture2D _texture;

            public int LoadCount { get; private set; }

            public UniTask<Texture2D> LoadAsync(string relativeOrAbsoluteUrl, CancellationToken cancellationToken)
            {
                LoadCount++;

                if (_texture == null)
                {
                    _texture = new Texture2D(2, 2);
                }

                return UniTask.FromResult(_texture);
            }

            public void Dispose()
            {
                if (_texture != null)
                {
                    UnityEngine.Object.Destroy(_texture);
                    _texture = null;
                }
            }
        }
    }
}
