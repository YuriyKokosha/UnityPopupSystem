using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Domain.Inventory;
using PopupSystem.Game.Domain.Offer;
using PopupSystem.Game.Domain.Rewards;
using PopupSystem.Game.Domain.Wallet;
using PopupSystem.Game.Services.DailyReward;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Offer;
using PopupSystem.Game.Services.Rpc;
using PopupSystem.Game.Services.Rpc.Core;
using PopupSystem.Game.Services.Rpc.Inventory;
using PopupSystem.Game.Services.Rewards;
using PopupSystem.Game.Services.Rpc.RemoteConfig;
using PopupSystem.Game.Services.Rpc.Wallet;
using PopupSystem.Game.Services.Rpc.WindowQueue;
using PopupSystem.Game.Services.Time;
using PopupSystem.Game.Services.Wallet;
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
using PopupSystem.UI.Windows.Inventory;
using PopupSystem.UI.Windows.Offer;
using PopupSystem.UI.Runtime.Widgets;
using PopupSystem.UI.Windows.RewardPopup;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
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
        private ScriptedPrefabProvider _prefabs;
        private AddressablesUiIconProvider _iconProvider;
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
            _container.Bind<WalletManager>().AsSingle();
            _container.Bind<IInventoryStorage>().To<InMemoryInventoryStorage>().AsSingle();
            _container.Bind<InventoryManager>().AsSingle();
            _container.Bind<RewardGrantService>().AsSingle();
            _container.Bind<DailyRewardManager>().AsSingle();
            _container.Bind<OfferManager>().AsSingle();

            // The real icon provider: the windows draw their rewards from Addressables sprites, and a test that
            // faked them would not notice a missing address.
            _iconProvider = new AddressablesUiIconProvider();
            _container.Bind<IUiIconProvider>().FromInstance(_iconProvider);
            _container.Bind<RewardIcons>().AsSingle();

            _container.BindIFactory<DailyRewardWindowController>().To<DailyRewardWindowController>();
            _container.BindIFactory<OfferWindowController>().To<OfferWindowController>();
            _container.BindIFactory<RewardPopupController>().To<RewardPopupController>();
            _container.BindIFactory<InventoryWindowController>().To<InventoryWindowController>();

            var modules = new List<IWindowModule>
            {
                _container.Instantiate<DailyRewardWindowModule>(),
                _container.Instantiate<OfferWindowModule>(),
                _container.Instantiate<RewardPopupModule>(),
                _container.Instantiate<InventoryWindowModule>(),
            };

            _prefabProvider = new AddressablesUiPrefabProvider();
            // Real loads by default; the popup-failure flows script a failing load through this wrapper.
            _prefabs = new ScriptedPrefabProvider(_prefabProvider);

            _prefabProvider.LoadBlocking(ModalBackdropPresenter.BackdropPrefabAddress);

            var registry = new WindowRegistry(modules);
            _windowFactory = new WindowFactory(
                registry,
                new WindowControllerResolver(modules),
                _ui,
                _prefabs,
                _container.Instantiate<PrefabFactory<WindowView>>());

            _windowsManager = new WindowsManager(_windowFactory, registry, _prefabs);

            _container.Bind<IWindowsManager>().FromInstance(_windowsManager);

            // The controllers gate their buttons on inventory space, so the inventory has to be configured and loaded
            // the way the connect flow leaves it.
            var inventory = _container.Resolve<InventoryManager>();
            inventory.Initialize(FakeRemoteConfigApi.CreateDemoConfig());
            inventory.Load("test-player", InventorySnapshot.Empty);

            // The offer costs gold; the wallet starts the way the connect flow leaves it, so Buy is affordable.
            _container.Resolve<WalletManager>().SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 1500) }));
        }

        [TearDown]
        public void TearDown()
        {
            _rpc.RemoteConfigEndpoint.CancelIfPending();

            _windowsManager.Dispose();
            _windowFactory.Dispose();
            _imageLoader.Dispose();
            _prefabProvider.Dispose();
            _iconProvider.Dispose();
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
        public async Task DailyReward_Claim_ShowsOneIconPerRewardLineInThePopup()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.DailyReward);
            var window = FindView<DailyRewardWindowView>(UILayerType.Windows);
            var reward = _container.Resolve<DailyRewardManager>().Reward;
            var lines = reward.Currencies.Count + reward.Items.Count;

            Assert.That(
                VisibleIcons(window),
                Is.EqualTo(lines),
                "The window previews the reward it is about to grant, one icon per line.");

            ButtonLabelled(window, "Claim").onClick.Invoke();

            var shown = await WaitUntilAsync(
                () => HasText(FindView<RewardPopupView>(UILayerType.Popups), "Daily Reward Claimed"));
            Assert.That(shown, Is.True);

            var popup = FindView<RewardPopupView>(UILayerType.Popups);
            Assert.That(
                VisibleIcons(popup),
                Is.EqualTo(lines),
                "Every currency and item of the bundle is an icon with a sprite - a missing address would be a " +
                "label instead, and this count would drop.");
            Assert.That(HasText(popup, "x2"), Is.True, "An item line carries its count next to its icon.");

            // Let the claim flow run to its end; left waiting on the popup, it would close the window during
            // TearDown and animate a destroyed CanvasGroup into the next test.
            await _windowsManager.CloseTopPopupAsync();
            Assert.That(await WaitUntilAsync(() => handle.IsClosed), Is.True);
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

            // Past the cooldown, so the reopened window is claimable again and the button is what it shows.
            _time.AdvanceSeconds(_container.Resolve<DailyRewardManager>().TimeUntilAvailable.TotalSeconds + 1);
            await _windowsManager.OpenAsync(WindowType.DailyReward);
            var secondView = FindView<DailyRewardWindowView>(UILayerType.Windows);

            Assert.That(secondView, Is.SameAs(firstView), "This test is only meaningful on a pooled view.");
            Assert.That(
                ButtonLabelled(secondView, "Claim").interactable,
                Is.True,
                "A reopened window must start from a clean baseline, not from where the last one left off.");
        }

        [Test]
        public async Task DailyReward_OnCooldown_ShowsATimerInsteadOfTheButton_AndGivesTheButtonBackWhenItEnds()
        {
            var manager = _container.Resolve<DailyRewardManager>();
            await manager.ClaimRewardAsync(CancellationToken.None);

            await _windowsManager.OpenAsync(WindowType.DailyReward);
            var view = FindView<DailyRewardWindowView>(UILayerType.Windows);

            Assert.That(view.CooldownRoot, Is.Not.Null, "The prefab has lost its cooldown timer (Tools/UI Kit/Daily Reward).");
            Assert.That(view.CooldownRoot.activeInHierarchy, Is.True, "On cooldown the slot shows the timer...");
            Assert.That(view.ClaimButton.gameObject.activeInHierarchy, Is.False, "...and not the button.");
            Assert.That(view.CooldownText.text, Is.EqualTo("01:00"), "The full one-minute interval, rounded up to whole seconds.");

            _time.AdvanceSeconds(30);
            Assert.That(await WaitUntilAsync(() => view.CooldownText.text == "00:30"), Is.True, "The countdown follows the clock.");

            _time.AdvanceSeconds(31);
            var buttonBack = await WaitUntilAsync(
                () => view.ClaimButton.gameObject.activeInHierarchy && view.ClaimButton.interactable);

            Assert.That(buttonBack, Is.True, "The button comes back on its own the moment the reward is claimable.");
            Assert.That(view.CooldownRoot.activeInHierarchy, Is.False);
        }

        [Test]
        public async Task DailyReward_Claim_WhenThePopupFailsToOpen_StillGrantsOnce_AndClosesTheWindow()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.DailyReward);
            var view = FindView<DailyRewardWindowView>(UILayerType.Windows);
            _prefabs.FailNextLoad(RewardPopupAddress, new InvalidOperationException("[Expected] scripted popup failure"));
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted popup failure"));

            ButtonLabelled(view, "Claim").onClick.Invoke();

            Assert.That(
                await WaitUntilAsync(() => handle.IsClosed),
                Is.True,
                "The claim went through without its popup; the window must finish the flow, not strand a dead button.");
            Assert.That(FindView<RewardPopupView>(UILayerType.Popups), Is.Null);
            Assert.That(_container.Resolve<WalletManager>().GetBalance("gold"), Is.EqualTo(1500 + 500), "Granted exactly once.");
            Assert.That(_container.Resolve<DailyRewardManager>().IsRewardAvailable(), Is.False);
        }

        [Test]
        public async Task Offer_Buy_WhenThePopupFailsToOpen_ChargesOnce_AndClosesTheWindow()
        {
            var handle = await OpenOfferWithContentAsync();
            var view = FindView<OfferWindowView>(UILayerType.Windows);
            _prefabs.FailNextLoad(RewardPopupAddress, new InvalidOperationException("[Expected] scripted popup failure"));
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted popup failure"));

            ButtonLabelled(view, "Buy for 1 000 gold").onClick.Invoke();

            Assert.That(await WaitUntilAsync(() => handle.IsClosed), Is.True);
            Assert.That(FindView<RewardPopupView>(UILayerType.Popups), Is.Null);
            var wallet = _container.Resolve<WalletManager>();
            Assert.That(wallet.GetBalance("gold"), Is.EqualTo(1500 - 1000), "Charged exactly once.");
            Assert.That(wallet.GetBalance("gems"), Is.EqualTo(500), "Granted exactly once.");
        }

        [Test]
        public async Task Offer_Buy_WhenThePopupAndThePurchaseBothFail_GivesTheButtonBack()
        {
            var handle = await OpenOfferWithContentAsync();
            var view = FindView<OfferWindowView>(UILayerType.Windows);
            var wallet = _container.Resolve<WalletManager>();
            _prefabs.FailNextLoad(RewardPopupAddress, new InvalidOperationException("[Expected] scripted popup failure"));
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted popup failure"));
            // Not injected, so no [Expected] label: the controller logs production's own refusal of the purchase.
            LogAssert.Expect(LogType.Exception, new Regex("Not enough gold"));

            ButtonLabelled(view, "Buy for 1 000 gold").onClick.Invoke();
            // The balance drops while the purchase is in flight, so its re-check refuses it.
            wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));

            Assert.That(await WaitUntilAsync(() => ButtonLabelled(view, "Not enough gold") != null), Is.True);
            Assert.That(handle.IsClosed, Is.False, "A refused purchase leaves the window open.");

            wallet.AddCurrencies(new[] { new CurrencyAmount("gold", 5000) });

            var restored = await WaitUntilAsync(() => ButtonLabelled(view, "Buy for 1 000 gold")?.interactable == true);
            Assert.That(restored, Is.True, "The in-progress flag was released: the button follows the wallet again.");
        }

        [Test]
        public async Task Inventory_GridScrolls_FromAnywhereInTheViewport_NotOnlyFromItsButtons()
        {
            await _windowsManager.OpenAsync(WindowType.Inventory);
            var view = FindView<InventoryWindowView>(UILayerType.Windows);
            var scroll = view.GetComponentInChildren<ScrollRect>(true);
            Assert.That(scroll, Is.Not.Null);

            // A graphic is only raycastable once it has been drawn.
            await UniTask.NextFrame();
            await UniTask.NextFrame();

            var raycaster = view.GetComponent<GraphicRaycaster>();
            var cell = (RectTransform)scroll.content.GetChild(1);
            var points = new[]
            {
                cell.rect.center,                                          // an empty card: nothing on it raycasts
                new Vector2(cell.rect.xMax + 8f, cell.rect.center.y),      // the gap between two cards
            };

            foreach (var local in points)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(null, cell.TransformPoint(local));
                var hits = new List<RaycastResult>();
                raycaster.Raycast(new PointerEventData(null) { position = screen }, hits);

                var dragTarget = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IDragHandler>(hits[0].gameObject) : null;
                Assert.That(
                    dragTarget,
                    Is.SameAs(scroll.gameObject),
                    $"A drag starting at {local} inside a cell must reach the grid's ScrollRect; the hit was " +
                    $"'{(hits.Count > 0 ? hits[0].gameObject.name : "nothing")}'.");
            }
        }

        [Test]
        public async Task Inventory_GridKeepsMoving_AfterAFlingIsReleased()
        {
            var scroll = await OpenInventoryWithOverflowAsync();
            var content = scroll.content;
            var ped = await DragAsync(scroll, stepPixels: 40f, steps: 5);

            scroll.OnEndDrag(ped);
            var atRelease = content.anchoredPosition.y;

            for (var i = 0; i < 5; i++)
            {
                await UniTask.NextFrame();
            }

            Assert.That(scroll.velocity.y, Is.GreaterThan(0f), "Released mid-fling, the grid carries its velocity.");
            Assert.That(
                content.anchoredPosition.y,
                Is.GreaterThan(atRelease + 1f),
                "Inertia: the content keeps travelling after the finger lifts.");
        }

        [Test]
        public async Task Inventory_GridOverscrollsPastTheTop_AndSpringsBack()
        {
            var scroll = await OpenInventoryWithOverflowAsync();
            var content = scroll.content;
            var ped = await DragAsync(scroll, stepPixels: -40f, steps: 4);

            Assert.That(content.anchoredPosition.y, Is.LessThan(-1f), "Elastic: a pull past the top overshoots.");

            scroll.OnEndDrag(ped);
            var settled = await WaitUntilAsync(() => Mathf.Abs(content.anchoredPosition.y) < 0.5f, timeoutMs: 2000);
            Assert.That(settled, Is.True, "Released, it springs back to the top rather than staying pulled.");
        }

        private async UniTask<ScrollRect> OpenInventoryWithOverflowAsync()
        {
            await _windowsManager.OpenAsync(WindowType.Inventory);
            var view = FindView<InventoryWindowView>(UILayerType.Windows);

            // Twice the demo slots: eight rows never fit, whatever the Game view's size, so there is room to fling.
            view.Render(Array.Empty<InventoryWindowView.SlotModel>(), 24);
            await UniTask.NextFrame();

            return view.GetComponentInChildren<ScrollRect>(true);
        }

        private static async UniTask<PointerEventData> DragAsync(ScrollRect scroll, float stepPixels, int steps)
        {
            var viewport = scroll.viewport;
            var start = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(viewport.rect.center));
            var ped = new PointerEventData(null)
            {
                button = PointerEventData.InputButton.Left,
                position = start,
                pressPosition = start,
            };

            scroll.OnInitializePotentialDrag(ped);
            scroll.OnBeginDrag(ped);

            for (var i = 1; i <= steps; i++)
            {
                await UniTask.NextFrame();
                ped.position = start + new Vector2(0f, stepPixels * i);
                scroll.OnDrag(ped);
            }

            // One more frame so LateUpdate turns the last step into velocity.
            await UniTask.NextFrame();
            return ped;
        }

        [Test]
        public async Task Offer_OpensWithAPlaceholder_ThenFillsInTheRemoteCopy()
        {
            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            Assert.That(HasText(view, "Loading offer..."), Is.True);
            var loading = ButtonLabelled(view, "Loading...");
            Assert.That(loading, Is.Not.Null, "While the copy is in flight the CTA says so.");
            Assert.That(loading.interactable, Is.False, "Buy must not be clickable before the player can read what is on sale.");

            _rpc.RemoteConfigEndpoint.Resolve(
                new OfferRemoteContent("Starter Offer", "500 gems and 50 energy.", "Buy", bannerImageUrl: null));

            var filled = await WaitUntilAsync(() => HasText(view, "500 gems and 50 energy."));
            Assert.That(filled, Is.True, "The remote copy replaces the placeholder once it arrives.");

            var buy = ButtonLabelled(view, "Buy for 1 000 gold");
            Assert.That(buy, Is.Not.Null, "The CTA label is remote-sourced too, and carries the local price.");
            Assert.That(buy.interactable, Is.True);
        }

        [Test]
        public async Task Offer_DisablesBuy_WhenTheWalletCannotCoverThePrice_AndReenablesItWhenItCan()
        {
            var wallet = _container.Resolve<WalletManager>();
            wallet.SetWallet(new WalletSnapshot(new[] { new CurrencyAmount("gold", 10) }));

            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);
            _rpc.RemoteConfigEndpoint.Resolve(
                new OfferRemoteContent("Starter Offer", "500 gems and 50 energy.", "Buy", bannerImageUrl: null));

            var disabled = await WaitUntilAsync(() => ButtonLabelled(view, "Not enough gold") != null);
            Assert.That(disabled, Is.True, "An unaffordable offer says why instead of failing on tap.");
            Assert.That(ButtonLabelled(view, "Not enough gold").interactable, Is.False);

            wallet.AddCurrencies(new[] { new CurrencyAmount("gold", 5000) });

            var enabled = await WaitUntilAsync(() => ButtonLabelled(view, "Buy for 1 000 gold") != null);
            Assert.That(enabled, Is.True, "The button comes back the moment the balance covers the price.");
            Assert.That(ButtonLabelled(view, "Buy for 1 000 gold").interactable, Is.True);
        }

        [Test]
        public async Task Offer_FallsBackToSafeCopy_WhenTheRemoteConfigEndpointFails()
        {
            await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);

            _rpc.RemoteConfigEndpoint.Fail(new InvalidOperationException("[Expected] remote config unreachable"));

            var fellBack = await WaitUntilAsync(() => HasText(view, "Special Offer"));
            Assert.That(fellBack, Is.True, "A failed content fetch must degrade, not blank the window.");
            Assert.That(ButtonLabelled(view, "Buy for 1 000 gold"), Is.Not.Null, "The offer is still purchasable on fallback copy.");
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

            operation.TrySetException(new InvalidOperationException("[Expected] the grant was rejected"));

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

        private const string RewardPopupAddress = "UI/Windows/RewardPopupWindow";

        private async UniTask<WindowHandle> OpenOfferWithContentAsync()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Offer);
            var view = FindView<OfferWindowView>(UILayerType.Windows);
            _rpc.RemoteConfigEndpoint.Resolve(
                new OfferRemoteContent("Starter Offer", "500 gems and 50 energy.", "Buy", bannerImageUrl: null));
            Assert.That(await WaitUntilAsync(() => ButtonLabelled(view, "Buy for 1 000 gold")?.interactable == true), Is.True);
            return handle;
        }

        private TView FindView<TView>(UILayerType layerType) where TView : WindowView
        {
            return _ui.GetLayer(layerType).GetComponentsInChildren<TView>(true).FirstOrDefault();
        }

        private static int VisibleIcons(WindowView view)
        {
            return view == null
                ? 0
                : view.GetComponentsInChildren<IconAmountView>(false).Count(entry => entry.Icon != null);
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

            public void AdvanceSeconds(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
        }

        private sealed class FakeFlowRpcManager : IRpcManager
        {
            public GatedRemoteConfigEndpoint RemoteConfigEndpoint { get; } = new();

            public ICoreRpcApi Core => throw new NotSupportedException("These flows never call Core.");
            public IWalletRpcApi Wallet => throw new NotSupportedException("These flows never call Wallet.");
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

            public UniTask<InventoryConfig> GetInventoryConfigAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException("These flows configure the inventory directly.");
            }
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
