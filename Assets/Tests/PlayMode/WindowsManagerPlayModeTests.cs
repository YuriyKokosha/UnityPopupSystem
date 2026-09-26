using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Profile;
using PopupSystem.Game.Services.Wallet;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.Controller;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Runtime.Registry;
using PopupSystem.UI.Services;
using PopupSystem.UI.Windows.MainGame;
using PopupSystem.UI.Windows.RewardPopup;
using PopupSystem.UI.Windows.Settings;
using UnityEngine;
using UnityEngine.TestTools;
using Zenject;

namespace PopupSystem.Tests.PlayMode
{
    [TestFixture]
    public sealed class WindowsManagerPlayModeTests
    {
        private TestUiHierarchy _ui;
        private DiContainer _container;
        private AddressablesUiPrefabProvider _prefabProvider;
        private ScriptedPrefabProvider _prefabs;
        private ScriptedControllerResolver _controllers;
        private AddressablesUiIconProvider _iconProvider;
        private WindowFactory _windowFactory;

        private WindowsManager _windowsManager;

        [SetUp]
        public void SetUp()
        {
            _ui = new TestUiHierarchy();
            _container = new DiContainer();

            _container.BindIFactory<SettingsWindowController>().To<SettingsWindowController>();
            _container.BindIFactory<RewardPopupController>().To<RewardPopupController>();
            _container.BindIFactory<MainGameWindowController>().To<MainGameWindowController>();

            _container.Bind<WalletManager>().AsSingle();
            _container.Bind<IInventoryStorage>().To<InMemoryInventoryStorage>().AsSingle();
            _container.Bind<InventoryManager>().AsSingle();
            _container.Bind<PlayerProfileManager>().AsSingle();
            _iconProvider = new AddressablesUiIconProvider();
            _container.Bind<IUiIconProvider>().FromInstance(_iconProvider);
            _container.Bind<RewardIcons>().AsSingle();

            var modules = new List<IWindowModule>
            {
                _container.Instantiate<SettingsWindowModule>(),
                _container.Instantiate<RewardPopupModule>(),
                _container.Instantiate<MainGameWindowModule>(),
            };

            _prefabProvider = new AddressablesUiPrefabProvider();
            // Real loads by default; the two tests that need a slow or a failing load script it through this wrapper.
            _prefabs = new ScriptedPrefabProvider(_prefabProvider);

            _prefabProvider.LoadBlocking(ModalBackdropPresenter.BackdropPrefabAddress);

            var registry = new WindowRegistry(modules);
            // Real controllers by default; the tests that need a failing create or dispose script it through this.
            _controllers = new ScriptedControllerResolver(new WindowControllerResolver(modules));
            _windowFactory = new WindowFactory(
                registry,
                _controllers,
                _ui,
                _prefabs,
                _container.Instantiate<PrefabFactory<WindowView>>());

            _windowsManager = new WindowsManager(_windowFactory, registry, _prefabs);

            _container.Bind<IWindowsManager>().FromInstance(_windowsManager);
        }

        [TearDown]
        public void TearDown()
        {
            _windowsManager.Dispose();
            _windowFactory.Dispose();
            _prefabProvider.Dispose();
            _iconProvider.Dispose();
            _ui.Dispose();
        }

        [Test]
        public async Task OpenAsync_InstantiatesTheRealPrefab_IntoItsOwnLayer_AndReachesActive()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Active));
            Assert.That(handle.IsClosed, Is.False);
            Assert.That(_windowsManager.IsQueueIdle, Is.False);

            var view = FindView(UILayerType.Windows);
            Assert.That(view, Is.Not.Null, "The Settings prefab should have been instantiated into WindowsLayer.");
            Assert.That(view.gameObject.activeInHierarchy, Is.True);
            Assert.That(view, Is.TypeOf<SettingsWindowView>());
        }

        [Test]
        public async Task OpenAsync_PlaysTheRealTransition_LeavingTheWindowFullyVisibleAndInteractive()
        {
            await _windowsManager.OpenAsync(WindowType.Settings);

            var canvasGroup = FindView(UILayerType.Windows).GetComponent<CanvasGroup>();
            Assert.That(canvasGroup, Is.Not.Null, "Window prefabs ship with a CanvasGroup.");
            Assert.That(canvasGroup.alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            Assert.That(FindView(UILayerType.Windows).transform.localScale.x, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public async Task CloseAsync_RunsToDisposed_AndLeavesTheQueueIdle()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);

            await handle.CloseAsync();

            Assert.That(handle.IsClosed, Is.True);
            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(_windowsManager.IsQueueIdle, Is.True, "Nothing is on screen or animating out any more.");

            var view = FindView(UILayerType.Windows);
            Assert.That(view.gameObject.activeSelf, Is.False, "A closed view is hidden and pooled, not destroyed.");
        }

        [Test]
        public async Task ReopeningTheSameWindowType_ReusesThePooledView()
        {
            var first = await _windowsManager.OpenAsync(WindowType.Settings);
            var firstView = FindView(UILayerType.Windows);
            await first.CloseAsync();

            await _windowsManager.OpenAsync(WindowType.Settings);
            var secondView = FindView(UILayerType.Windows);

            Assert.That(secondView, Is.SameAs(firstView));
            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1), "No second instance should have been created.");
        }

        [Test]
        public async Task Popup_OpensOnItsOwnLayer_SortedAboveTheWindowBeneathIt()
        {
            await _windowsManager.OpenAsync(WindowType.Settings);
            await _windowsManager.OpenAsync(WindowType.RewardPopup);

            var windowCanvas = FindView(UILayerType.Windows).GetComponent<Canvas>();
            var popupCanvas = FindView(UILayerType.Popups).GetComponent<Canvas>();

            Assert.That(windowCanvas, Is.Not.Null, "Window prefabs carry their own Canvas.");
            Assert.That(popupCanvas, Is.Not.Null);
            Assert.That(windowCanvas.overrideSorting, Is.True,
                "UILayerSorter turns sibling order into an explicit sortingOrder.");
            Assert.That(popupCanvas.overrideSorting, Is.True);

            Assert.That(windowCanvas.sortingOrder, Is.GreaterThan(_ui.GetLayerSortingOrder(UILayerType.Windows)));
            Assert.That(popupCanvas.sortingOrder, Is.GreaterThan(_ui.GetLayerSortingOrder(UILayerType.Popups)));

            Assert.That(popupCanvas.sortingOrder, Is.GreaterThan(windowCanvas.sortingOrder));
            Assert.That(_windowsManager.HasOpenPopups, Is.True);
        }

        [Test]
        public async Task ModalPopup_GetsABackdrop_SortedBetweenItAndTheLayerBeneath()
        {
            await _windowsManager.OpenAsync(WindowType.RewardPopup);

            var backdrop = FindBackdrop(UILayerType.Popups);
            Assert.That(backdrop, Is.Not.Null, "A modal window gets a dimming/input-blocking backdrop.");
            Assert.That(backdrop.activeInHierarchy, Is.True);

            var backdropOrder = backdrop.GetComponent<Canvas>().sortingOrder;
            var popupOrder = FindView(UILayerType.Popups).GetComponent<Canvas>().sortingOrder;

            Assert.That(backdropOrder, Is.LessThan(popupOrder));
            Assert.That(backdropOrder, Is.GreaterThan(_ui.GetLayerSortingOrder(UILayerType.Popups)));
        }

        [Test]
        public async Task NonModalWindow_GetsNoBackdrop()
        {
            await _windowsManager.OpenAsync(WindowType.Settings);

            var backdrop = FindBackdrop(UILayerType.Windows);
            Assert.That(
                backdrop == null || !backdrop.activeInHierarchy,
                Is.True,
                "Settings is not modal, so nothing should be dimmed behind it.");
        }

        [Test]
        public async Task ClosingTheModalPopup_TakesItsBackdropWithIt()
        {
            var popup = await _windowsManager.OpenAsync(WindowType.RewardPopup);
            Assert.That(FindBackdrop(UILayerType.Popups).activeInHierarchy, Is.True);

            await popup.CloseAsync();

            Assert.That(
                FindBackdrop(UILayerType.Popups).activeInHierarchy,
                Is.False,
                "The backdrop is pooled with SetActive(false), not left dimming an empty layer.");
        }

        [Test]
        public async Task CloseTopPopupAsync_ClosesThePopupAndLeavesTheWindowBeneathAlone()
        {
            var window = await _windowsManager.OpenAsync(WindowType.Settings);
            var popup = await _windowsManager.OpenAsync(WindowType.RewardPopup);

            await _windowsManager.CloseTopPopupAsync();

            Assert.That(popup.IsClosed, Is.True);
            Assert.That(window.IsClosed, Is.False, "Closing the top popup must not touch the window under it.");
            Assert.That(_windowsManager.HasOpenPopups, Is.False);
            Assert.That(_windowsManager.IsQueueIdle, Is.False, "The window beneath is still on screen.");
        }

        [Test]
        public async Task ClosingTheSameHandleTwice_WaitsForTheCloseAlreadyInFlight()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);

            var firstClose = handle.CloseAsync();
            var secondClose = handle.CloseAsync();

            await UniTask.WhenAll(firstClose, secondClose);

            Assert.That(handle.IsClosed, Is.True);
            Assert.That(_windowsManager.IsQueueIdle, Is.True);
        }

        [Test]
        public async Task StateChanged_AnnouncesClosingBeforeDisposed_WithoutSkippingEither()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Active));

            var observed = new List<WindowLifecycleState>();
            void Record(WindowLifecycleState state) => observed.Add(state);

            handle.StateChanged += Record;
            await handle.CloseAsync();
            handle.StateChanged -= Record;

            Assert.That(
                observed,
                Is.EqualTo(new[] { WindowLifecycleState.Closing, WindowLifecycleState.Disposed }),
                "Closing must be announced before Disposed, and neither may be skipped.");
        }

        [Test]
        public async Task BaseScreen_OpenedAgainWhileUp_HandsBackTheSameHandle()
        {
            var first = await _windowsManager.OpenAsync(WindowType.MainGame);
            var second = await _windowsManager.OpenAsync(WindowType.MainGame);

            Assert.That(second, Is.SameAs(first), "The base screen is single-instance.");
            Assert.That(
                CountViews(UILayerType.Windows),
                Is.EqualTo(1),
                "A second copy must not be instantiated behind the first.");
        }

        [Test]
        public async Task BaseScreen_NeverCountsAsQueueBusy_EvenAfterBeingClosedAndOpenedAgain()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.MainGame);

            Assert.That(_windowsManager.IsQueueIdle, Is.True);

            await handle.CloseAsync();
            var reopened = await _windowsManager.OpenAsync(WindowType.MainGame);

            Assert.That(reopened, Is.Not.SameAs(handle), "It really was closed, so this is a new instance.");
            Assert.That(
                _windowsManager.IsQueueIdle,
                Is.True,
                "A reopened base screen goes back into the base slot, not onto the window stack.");
        }

        [Test]
        public async Task Dispose_TearsDownEverythingStillOnScreen()
        {
            var baseScreen = await _windowsManager.OpenAsync(WindowType.MainGame);
            var window = await _windowsManager.OpenAsync(WindowType.Settings);
            var popup = await _windowsManager.OpenAsync(WindowType.RewardPopup);

            _windowsManager.Dispose();

            Assert.That(baseScreen.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(window.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(popup.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(_windowsManager.IsQueueIdle, Is.True);

            await UniTask.WhenAll(
                baseScreen.WaitForCloseAsync(),
                window.WaitForCloseAsync(),
                popup.WaitForCloseAsync());
        }

        [Test]
        public async Task DisposingTheFactory_DestroysWhatThePoolWasHolding()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);
            var view = FindView(UILayerType.Windows);
            await handle.CloseAsync();

            Assert.That(view != null, Is.True, "Closing a window pools its view rather than destroying it.");

            _windowFactory.Dispose();

            await UniTask.DelayFrame(2);

            Assert.That(
                view == null,
                Is.True,
                "The pool must stay bounded: an unbounded pool keeps every view it holds alive for the session.");
        }

        [Test]
        public async Task OpenAsync_CountsTheWindowAsBusy_WhileItsPrefabIsStillLoading()
        {
            _prefabs.DeferNextLoad();

            var opening = _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(
                _windowsManager.IsQueueIdle,
                Is.False,
                "The prefab has not finished loading and nothing is on a stack yet - reading idle here would let " +
                "the queue open its own window on top of the one already on its way.");

            var handle = await opening;

            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Active));
            Assert.That(_windowsManager.IsQueueIdle, Is.False);

            await handle.CloseAsync();

            Assert.That(_windowsManager.IsQueueIdle, Is.True);
        }

        [Test]
        public async Task OpenAsync_ThatFinishesLoadingAfterDispose_IsReleased_NotOpened()
        {
            _prefabs.DeferNextLoad();

            var opening = _windowsManager.OpenAsync(WindowType.Settings);
            _windowsManager.Dispose();

            Exception caught = null;
            try
            {
                await opening;
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null, "An open that outlives the manager has to end as a cancellation.");
            Assert.That(_windowsManager.IsQueueIdle, Is.True, "Nothing is left pending or on a stack.");

            var view = FindView(UILayerType.Windows);
            Assert.That(
                view == null || !view.gameObject.activeInHierarchy,
                Is.True,
                "The late view is released (pooled hidden or destroyed), never shown after Dispose.");
        }

        [Test]
        public async Task OpenAsync_AfterDispose_IsRefused_WithoutLoadingAnything()
        {
            _windowsManager.Dispose();

            Exception caught = null;
            try
            {
                await _windowsManager.OpenAsync(WindowType.Settings);
            }
            catch (OperationCanceledException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
            Assert.That(FindView(UILayerType.Windows), Is.Null);
        }

        [Test]
        public async Task OpenAsync_ThatFailsWhileLoading_LeavesTheQueueIdle_AndSaysSo()
        {
            var idleRaised = 0;
            _windowsManager.QueueBecameIdle += () => idleRaised++;
            _prefabs.FailNextLoad("UI/Windows/SettingsWindow", new InvalidOperationException("[Expected] scripted load failure"));

            // Explicit try/catch rather than Throws: see testing-in-unity.md on async delegates inside constraints.
            Exception caught = null;
            try
            {
                await _windowsManager.OpenAsync(WindowType.Settings);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null, "A load failure has to surface to the caller, not be swallowed.");

            Assert.That(_windowsManager.IsQueueIdle, Is.True, "A failed open must not leave a phantom pending window.");
            Assert.That(idleRaised, Is.EqualTo(1), "The queue was told busy by the pending open; it has to be told idle again.");
            Assert.That(FindView(UILayerType.Windows), Is.Null);
        }

        [Test]
        public async Task MainGame_SettingsButton_ComesBack_AfterAFailedOpen()
        {
            await _windowsManager.OpenAsync(WindowType.MainGame);
            var settingsButton = ((MainGameWindowView)FindView(UILayerType.Windows)).SettingsButton;
            Assert.That(settingsButton, Is.Not.Null, "The MainGame prefab has lost its settings button reference.");

            _prefabs.FailNextLoad("UI/Windows/SettingsWindow", new InvalidOperationException("[Expected] scripted load failure"));
            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted load failure"));

            settingsButton.onClick.Invoke();
            await UniTask.DelayFrame(2);

            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1), "The failed open must not leave a Settings view behind.");

            settingsButton.onClick.Invoke();

            var opened = await WaitUntilAsync(() => FindSettingsView() != null && FindSettingsView().gameObject.activeInHierarchy);

            Assert.That(
                opened,
                Is.True,
                "The 'open requested' flag must be reset when an open throws, or every later tap is swallowed " +
                "for the rest of the session.");
        }

        [Test]
        public async Task StateChanged_ThrowingDisposedObserver_StillCompletesTheClose_ForEveryoneWaiting()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);

            var observed = new List<WindowLifecycleState>();
            handle.StateChanged += state =>
            {
                if (state == WindowLifecycleState.Disposed)
                {
                    throw new InvalidOperationException("[Expected] scripted Disposed observer failure");
                }
            };
            handle.StateChanged += state => observed.Add(state);

            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted Disposed observer failure"));

            await handle.CloseAsync();

            Assert.That(handle.IsClosed, Is.True);
            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(
                handle.WaitForCloseAsync().Status,
                Is.EqualTo(UniTaskStatus.Succeeded),
                "A throwing observer must not leave WaitForCloseAsync pending forever while IsClosed is already true.");
            Assert.That(
                observed,
                Is.EqualTo(new[] { WindowLifecycleState.Closing, WindowLifecycleState.Disposed }),
                "One observer throwing must not stop the next one hearing about it.");
            Assert.That(_windowsManager.IsQueueIdle, Is.True);

            await handle.CloseAsync();
            await handle.WaitForCloseAsync();

            Assert.That(handle.IsClosed, Is.True, "A second close of an already-closed handle is a no-op.");
        }

        [Test]
        public async Task StateChanged_ThrowingClosingObserver_DoesNotStopTheClose()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);
            var view = FindView(UILayerType.Windows);

            handle.StateChanged += state =>
            {
                if (state == WindowLifecycleState.Closing)
                {
                    throw new InvalidOperationException("[Expected] scripted Closing observer failure");
                }
            };

            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted Closing observer failure"));

            var firstClose = handle.CloseAsync();
            var secondClose = handle.CloseAsync();
            await UniTask.WhenAll(firstClose, secondClose);

            Assert.That(handle.IsClosed, Is.True);
            Assert.That(handle.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(handle.WaitForCloseAsync().Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(
                _windowsManager.IsQueueIdle,
                Is.True,
                "A throw must not escape before the cleanup: the handle would stay in _closingHandles for good.");
            Assert.That(view.gameObject.activeSelf, Is.False, "The view still went back to the pool.");
        }

        [Test]
        public async Task Dispose_WithAThrowingObserver_StillTearsDownEveryWindow()
        {
            var window = await _windowsManager.OpenAsync(WindowType.Settings);
            var popup = await _windowsManager.OpenAsync(WindowType.RewardPopup);

            popup.StateChanged += state =>
            {
                if (state == WindowLifecycleState.Closing)
                {
                    throw new InvalidOperationException("[Expected] scripted observer failure during dispose");
                }
            };

            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted observer failure during dispose"));

            _windowsManager.Dispose();

            Assert.That(popup.State, Is.EqualTo(WindowLifecycleState.Disposed));
            Assert.That(
                window.State,
                Is.EqualTo(WindowLifecycleState.Disposed),
                "The popup is disposed first; its observer throwing must not abandon the window beneath it.");

            await UniTask.WhenAll(window.WaitForCloseAsync(), popup.WaitForCloseAsync());
        }

        [Test]
        public async Task ThrowingControllerDispose_StillReleasesTheView_AndCompletesTheClose()
        {
            _controllers.ThrowOnDisposeOfNextCreated(new InvalidOperationException("[Expected] scripted controller dispose failure"));

            var handle = await _windowsManager.OpenAsync(WindowType.Settings);
            var view = FindView(UILayerType.Windows);

            LogAssert.Expect(LogType.Exception, new Regex(@"\[Expected\] scripted controller dispose failure"));

            await handle.CloseAsync();

            Assert.That(handle.IsClosed, Is.True);
            Assert.That(handle.WaitForCloseAsync().Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(_windowsManager.IsQueueIdle, Is.True);
            Assert.That(view.gameObject.activeSelf, Is.False);

            await _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(FindView(UILayerType.Windows), Is.SameAs(view), "The view reached the pool despite the throw.");
            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1));
        }

        [Test]
        public async Task BaseScreen_OpenedTwiceConcurrently_WhileItsPrefabIsLoading_CreatesOneInstance()
        {
            _prefabs.DeferNextLoad();

            var firstOpen = _windowsManager.OpenAsync(WindowType.MainGame);
            var secondOpen = _windowsManager.OpenAsync(WindowType.MainGame);

            var (first, second) = await UniTask.WhenAll(firstOpen, secondOpen);

            Assert.That(second, Is.SameAs(first), "Both callers were racing the same load; they share its handle.");
            Assert.That(first.State, Is.EqualTo(WindowLifecycleState.Active));
            Assert.That(
                CountViews(UILayerType.Windows),
                Is.EqualTo(1),
                "Concurrent opens must share one instance; two copies would leave one orphaned on screen.");
            Assert.That(_windowsManager.IsQueueIdle, Is.True);
        }

        [Test]
        public async Task BaseScreen_OpenedTwiceConcurrently_WhenTheLoadFails_BothSeeTheFailure_AndARetryWorks()
        {
            _prefabs.DeferNextLoad();
            _prefabs.FailNextLoad("UI/Windows/MainGameWindow", new InvalidOperationException("[Expected] scripted base load failure"));

            var firstOpen = _windowsManager.OpenAsync(WindowType.MainGame);
            var secondOpen = _windowsManager.OpenAsync(WindowType.MainGame);

            var firstError = await CaptureAsync(firstOpen);
            var secondError = await CaptureAsync(secondOpen);

            Assert.That(firstError, Is.InstanceOf<InvalidOperationException>());
            Assert.That(secondError, Is.SameAs(firstError), "The concurrent caller shares the one open, failure included.");
            Assert.That(FindView(UILayerType.Windows), Is.Null);

            var retried = await _windowsManager.OpenAsync(WindowType.MainGame);

            Assert.That(retried.State, Is.EqualTo(WindowLifecycleState.Active), "The failed shared open was cleared.");
            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1));
        }

        [Test]
        public async Task ControllerCreateThatThrows_OnAFreshView_HandsTheViewBack_Hidden()
        {
            _controllers.FailNextCreate(new InvalidOperationException("[Expected] scripted controller create failure"));

            var error = await CaptureAsync(_windowsManager.OpenAsync(WindowType.Settings));

            Assert.That(error, Is.InstanceOf<InvalidOperationException>(), "The failure still reaches the caller.");
            Assert.That(_windowsManager.IsQueueIdle, Is.True);

            var orphan = FindView(UILayerType.Windows);
            Assert.That(
                orphan == null || !orphan.gameObject.activeSelf,
                Is.True,
                "A freshly instantiated view must not stay active on screen with no owner.");

            await _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1), "The failed view was pooled and reused.");
            Assert.That(FindView(UILayerType.Windows).gameObject.activeInHierarchy, Is.True);
        }

        [Test]
        public async Task ControllerCreateThatThrows_OnAPooledView_PutsItBackInThePool()
        {
            var handle = await _windowsManager.OpenAsync(WindowType.Settings);
            var pooledView = FindView(UILayerType.Windows);
            await handle.CloseAsync();

            _controllers.FailNextCreate(new InvalidOperationException("[Expected] scripted controller create failure"));

            var error = await CaptureAsync(_windowsManager.OpenAsync(WindowType.Settings));

            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(pooledView.gameObject.activeSelf, Is.False);

            await _windowsManager.OpenAsync(WindowType.Settings);

            Assert.That(
                FindView(UILayerType.Windows),
                Is.SameAs(pooledView),
                "The pooled view must go back to the pool; dropping it makes the next open instantiate another copy.");
            Assert.That(CountViews(UILayerType.Windows), Is.EqualTo(1));
        }

        // Awaits in the body rather than inside a Throws constraint: see testing-in-unity.md.
        private static async UniTask<Exception> CaptureAsync(UniTask<WindowHandle> open)
        {
            try
            {
                await open;
            }
            catch (Exception ex)
            {
                return ex;
            }

            return null;
        }

        private SettingsWindowView FindSettingsView()
        {
            return _ui.GetLayer(UILayerType.Windows).GetComponentInChildren<SettingsWindowView>(true);
        }

        private static async UniTask<bool> WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
        {
            var elapsed = Stopwatch.StartNew();

            while (!condition())
            {
                if (elapsed.ElapsedMilliseconds >= timeoutMs)
                {
                    return false;
                }

                await UniTask.Yield(PlayerLoopTiming.Update);
            }

            return true;
        }

        private WindowView FindView(UILayerType layerType)
        {
            var layer = _ui.GetLayer(layerType);
            for (var i = 0; i < layer.childCount; i++)
            {
                var view = layer.GetChild(i).GetComponent<WindowView>();
                if (view != null)
                {
                    return view;
                }
            }

            return null;
        }

        private int CountViews(UILayerType layerType)
        {
            var layer = _ui.GetLayer(layerType);
            var count = 0;
            for (var i = 0; i < layer.childCount; i++)
            {
                if (layer.GetChild(i).GetComponent<WindowView>() != null)
                {
                    count++;
                }
            }

            return count;
        }

        private GameObject FindBackdrop(UILayerType layerType)
        {
            var layer = _ui.GetLayer(layerType);
            for (var i = 0; i < layer.childCount; i++)
            {
                var child = layer.GetChild(i);
                if (child.GetComponent<WindowView>() == null && child.name.StartsWith("ModalBackdrop"))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        /// <summary>Wraps the real resolver; scripts a failing create, or a controller whose Dispose throws after
        /// doing its real work.</summary>
        private sealed class ScriptedControllerResolver : IWindowControllerResolver
        {
            private readonly IWindowControllerResolver _inner;

            private Exception _failNextCreateWith;
            private Exception _throwOnNextDisposeWith;

            public ScriptedControllerResolver(IWindowControllerResolver inner)
            {
                _inner = inner;
            }

            public void FailNextCreate(Exception exception)
            {
                _failNextCreateWith = exception;
            }

            public void ThrowOnDisposeOfNextCreated(Exception exception)
            {
                _throwOnNextDisposeWith = exception;
            }

            public IWindowController Create(WindowType type)
            {
                if (_failNextCreateWith != null)
                {
                    var exception = _failNextCreateWith;
                    _failNextCreateWith = null;
                    throw exception;
                }

                var controller = _inner.Create(type);

                if (_throwOnNextDisposeWith != null)
                {
                    var exception = _throwOnNextDisposeWith;
                    _throwOnNextDisposeWith = null;
                    return new DisposeThrowingController(controller, exception);
                }

                return controller;
            }
        }

        private sealed class DisposeThrowingController : IWindowController
        {
            private readonly IWindowController _inner;
            private readonly Exception _exception;

            public DisposeThrowingController(IWindowController inner, Exception exception)
            {
                _inner = inner;
                _exception = exception;
            }

            public UniTask InitializeAsync(
                WindowView view,
                WindowHandle handle,
                IWindowData payload,
                CancellationToken cancellationToken)
            {
                return _inner.InitializeAsync(view, handle, payload, cancellationToken);
            }

            public void Dispose()
            {
                _inner.Dispose();
                throw _exception;
            }
        }
    }
}
