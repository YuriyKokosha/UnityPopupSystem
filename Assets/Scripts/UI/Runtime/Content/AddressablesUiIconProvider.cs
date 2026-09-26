using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PopupSystem.UI.Runtime.Content
{
    public sealed class AddressablesUiIconProvider : IUiIconProvider, IDisposable
    {
        private readonly Dictionary<string, AsyncOperationHandle<Sprite>> _handles = new();

        public async UniTask<Sprite> LoadAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("An icon address is required.", nameof(address));
            }

            var handle = GetOrStartLoad(address);

            if (!handle.IsDone)
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                throw new InvalidOperationException(
                    $"No icon sprite found at Addressables address '{address}'. Run Tools/UI Kit/Icons/Mark icon " +
                    "sprites addressable, or check the item catalog's IconAddress.");
            }

            return handle.Result;
        }

        public Sprite GetLoaded(string address)
        {
            if (string.IsNullOrEmpty(address) || !_handles.TryGetValue(address, out var handle) || !handle.IsDone)
            {
                return null;
            }

            return handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
        }

        public void Dispose()
        {
            foreach (var handle in _handles.Values)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            _handles.Clear();
        }

        private AsyncOperationHandle<Sprite> GetOrStartLoad(string address)
        {
            if (_handles.TryGetValue(address, out var existing))
            {
                // Same rule as the prefab provider: a failed handle is a cached error, not a cached answer.
                if (!existing.IsDone || existing.Status == AsyncOperationStatus.Succeeded)
                {
                    return existing;
                }

                _handles.Remove(address);

                if (existing.IsValid())
                {
                    Addressables.Release(existing);
                }
            }

            var handle = Addressables.LoadAssetAsync<Sprite>(address);
            _handles[address] = handle;
            return handle;
        }
    }
}
