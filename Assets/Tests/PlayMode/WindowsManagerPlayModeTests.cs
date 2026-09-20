using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.Contracts;
using PopupSystem.Game.Services.Inventory;
using PopupSystem.Game.Services.Profile;
using PopupSystem.UI.Definitions;
using PopupSystem.UI.Runtime;
using PopupSystem.UI.Runtime.Backdrop;
using PopupSystem.UI.Runtime.Content;
using PopupSystem.UI.Runtime.ControllerResolver;
using PopupSystem.UI.Runtime.Factory;
using PopupSystem.UI.Runtime.Manager;
using PopupSystem.UI.Runtime.Registry;
using PopupSystem.UI.Windows.MainGame;
using PopupSystem.UI.Windows.RewardPopup;
using PopupSystem.UI.Windows.Settings;
using UnityEngine;
using Zenject;

namespace PopupSystem.Tests.PlayMode
{
    [TestFixture]
    public sealed class WindowsManagerPlayModeTests
    {
        private TestUiHierarchy _ui;
        private DiContainer _container;
        private AddressablesUiPrefabProvider _prefabProvider;
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

            _container.Bind<PlayerInventoryManager>().AsSingle();
            _container.Bind<PlayerProfileManager>().AsSingle();

            var modules = new List<IWindowModule>
            {
                _container.Instantiate<SettingsWindowModule>(),
                _container.Instantiate<RewardPopupModule>(),
                _container.Instantiate<MainGameWindowModule>(),
            };

            _prefabProvider = new AddressablesUiPrefabProvider();

            _prefabProvider.LoadBlocking(ModalBackdropPresenter.BackdropPrefabAddress);

            var registry = new WindowRegistry(modules);
            var resolver = new WindowControllerResolver(modules);
            _windowFactory = new WindowFactory(
                registry,
                resolver,
                _ui,
                _prefabProvider,
                _container.Instantiate<PrefabFactory<WindowView>>());

            _windowsManager = new WindowsManager(_windowFactory, registry, _prefabProvider);

            _container.Bind<IWindowsManager>().FromInstance(_windowsManager);
        }

        [TearDown]
        public void TearDown()
        {
            _windowsManager.Dispose();
            _windowFactory.Dispose();
            _prefabProvider.Dispose();
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
                "The pool used to be unbounded and never emptied - every view it held outlived the session.");
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
    }
}
