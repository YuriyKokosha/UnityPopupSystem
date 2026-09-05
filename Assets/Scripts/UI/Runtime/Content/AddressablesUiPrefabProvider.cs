using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PopupSystem.UI.Runtime.Content
{
    public sealed class AddressablesUiPrefabProvider : IUiPrefabProvider, IDisposable
    {
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> _handles = new();

        public async UniTask<GameObject> LoadAsync(string address, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new ArgumentException("A UI prefab address is required.", nameof(address));
            }

            var handle = GetOrStartLoad(address);

            if (!handle.IsDone)
            {
                await handle.ToUniTask(cancellationToken: cancellationToken);
            }

            return ResultOrThrow(address, handle);
        }

        public GameObject GetLoaded(string address)
        {
            if (!_handles.TryGetValue(address, out var handle) || !handle.IsDone)
            {
                return null;
            }

            return handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
        }

        public GameObject LoadBlocking(string address)
        {
            var handle = GetOrStartLoad(address);

            if (!handle.IsDone)
            {
                handle.WaitForCompletion();
            }

            return ResultOrThrow(address, handle);
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

        private AsyncOperationHandle<GameObject> GetOrStartLoad(string address)
        {
            if (_handles.TryGetValue(address, out var existing))
            {
                // A finished handle that failed is a cached error, not a cached answer - drop it and let this caller retry.
                if (existing.IsDone && existing.Status != AsyncOperationStatus.Succeeded)
                {
                    _handles.Remove(address);

                    if (existing.IsValid())
                    {
                        Addressables.Release(existing);
                    }
                }
                else
                {
                    return existing;
                }
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(address);
            _handles[address] = handle;
            return handle;
        }

        private static GameObject ResultOrThrow(string address, AsyncOperationHandle<GameObject> handle)
        {
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                throw new InvalidOperationException(
                    $"No UI prefab found at Addressables address '{address}'. Check that the prefab is " +
                    "marked Addressable and that its address matches the one in its WindowDefinition.");
            }

            return handle.Result;
        }
    }
}
