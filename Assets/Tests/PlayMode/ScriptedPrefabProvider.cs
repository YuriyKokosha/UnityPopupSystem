using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using PopupSystem.UI.Runtime.Content;
using UnityEngine;

namespace PopupSystem.Tests.PlayMode
{
    /// <summary>Wraps the real <see cref="AddressablesUiPrefabProvider"/> and, by default, behaves exactly like
    /// it. Two knobs script the one thing the real thing will not do on demand: <see cref="DeferNextLoad"/> holds
    /// the next <see cref="LoadAsync"/> for a frame so a test can observe the manager mid-open, and
    /// <see cref="FailNextLoad"/> makes the next load of one address throw the way a bad Addressables address
    /// does.</summary>
    public sealed class ScriptedPrefabProvider : IUiPrefabProvider
    {
        private readonly IUiPrefabProvider _inner;

        private bool _deferNext;
        private string _failNextAddress;
        private Exception _failNextWith;

        public ScriptedPrefabProvider(IUiPrefabProvider inner)
        {
            _inner = inner;
        }

        public void DeferNextLoad()
        {
            _deferNext = true;
        }

        public void FailNextLoad(string address, Exception exception)
        {
            _failNextAddress = address;
            _failNextWith = exception;
        }

        public async UniTask<GameObject> LoadAsync(string address, CancellationToken cancellationToken)
        {
            if (_deferNext)
            {
                _deferNext = false;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            if (_failNextAddress == address)
            {
                _failNextAddress = null;
                var exception = _failNextWith;
                _failNextWith = null;
                throw exception;
            }

            return await _inner.LoadAsync(address, cancellationToken);
        }

        public GameObject GetLoaded(string address) => _inner.GetLoaded(address);

        public GameObject LoadBlocking(string address) => _inner.LoadBlocking(address);
    }
}
