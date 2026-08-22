using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;

namespace Zenject
{
    public class DisposableManager : IDisposable
    {
        readonly List<DisposableInfo> _disposables = new List<DisposableInfo>();
        bool _disposed;

        public DisposableManager(
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<IDisposable> disposables,
            [Inject(Optional = true, Source = InjectSources.Local)]
            List<ModestTree.Util.Tuple<Type, int>> priorities)
        {
            foreach (var disposable in disposables)
            {
                // Note that we use zero for unspecified priority
                // This is nice because you can use negative or positive for before/after unspecified
                var matches = priorities.Where(x => disposable.GetType().DerivesFromOrEqual(x.First)).Select(x => x.Second).ToList();
                int priority = matches.IsEmpty() ? 0 : matches.Single();

                _disposables.Add(new DisposableInfo(disposable, priority));
            }

            Log.Debug("Loaded {0} IDisposables to DisposablesHandler", _disposables.Count());
        }

        public void Add(IDisposable disposable)
        {
            Add(disposable, 0);
        }

        public void Add(IDisposable disposable, int priority)
        {
            _disposables.Add(
                new DisposableInfo(disposable, priority));
        }

        public void Remove(IDisposable disposable)
        {
            _disposables.RemoveWithConfirm(
                _disposables.Where(x => x.Disposable == disposable).Single());
        }

        public void Dispose()
        {
            // ProjectContext binds TickableManager/InitializableManager/DisposableManager with
            // InheritInSubContainers() (see ProjectContext.InstallBindings), which - per
            // DiContainer's constructor - copies those exact same BindInfo/provider entries into
            // every SceneContext's own container rather than creating fresh per-scene instances.
            // The practical effect: ProjectContext's own Kernel and this scene's Kernel end up
            // holding a reference to the literal same DisposableManager. Unity calls
            // OnApplicationQuit/OnDestroy on both independently at shutdown (MonoKernel's own
            // _isDisposed flag only guards a single Kernel instance calling Dispose twice on
            // itself, not two different Kernels sharing one manager), so whichever runs second
            // used to hit this assert even though nothing is actually wrong - the manager was
            // already correctly disposed once by the other Kernel. Treat that as a no-op instead
            // of a hard failure.
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Dispose in the reverse order that they are initialized in
            var disposablesOrdered = _disposables.OrderBy(x => x.Priority).Reverse().ToList();

            foreach (var disposable in disposablesOrdered.Select(x => x.Disposable).GetDuplicates())
            {
                Assert.That(false, "Found duplicate IDisposable with type '{0}'".Fmt(disposable.GetType()));
            }

            foreach (var disposable in disposablesOrdered)
            {
                Log.Debug("Disposing '" + disposable.Disposable.GetType() + "'");

                try
                {
                    disposable.Disposable.Dispose();
                }
                catch (Exception e)
                {
                    throw Assert.CreateException(
                        e, "Error occurred while disposing IDisposable with type '{0}'", disposable.Disposable.GetType().Name());
                }
            }

            Log.Debug("Disposed of {0} disposables in DisposablesHandler", disposablesOrdered.Count());
        }

        class DisposableInfo
        {
            public IDisposable Disposable;
            public int Priority;

            public DisposableInfo(IDisposable disposable, int priority)
            {
                Disposable = disposable;
                Priority = priority;
            }
        }
    }
}
