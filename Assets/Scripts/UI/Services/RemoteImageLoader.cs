using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PopupSystem.UI.Services
{
    public sealed class RemoteImageLoader : IRemoteImageLoader, IDisposable
    {
        private const int RequestTimeoutSeconds = 15;

        private readonly string _baseUrl;
        private readonly Dictionary<string, Texture2D> _cache = new();

        public RemoteImageLoader(string baseUrl)
        {
            _baseUrl = baseUrl;
        }

        public async UniTask<Texture2D> LoadAsync(string relativeOrAbsoluteUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(relativeOrAbsoluteUrl))
            {
                throw new ArgumentException("Image URL must not be empty.", nameof(relativeOrAbsoluteUrl));
            }

            var url = ResolveUrl(relativeOrAbsoluteUrl);

            if (_cache.TryGetValue(url, out var cached) && cached != null)
            {
                return cached;
            }

            using var request = UnityWebRequestTexture.GetTexture(url, nonReadable: true);
            request.timeout = RequestTimeoutSeconds;

            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            var texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            if (texture == null)
            {
                throw new InvalidOperationException($"Downloaded image at '{url}' could not be decoded.");
            }

            if (_cache.TryGetValue(url, out var published) && published != null)
            {
                UnityEngine.Object.Destroy(texture);
                return published;
            }

            _cache[url] = texture;
            return texture;
        }

        public void Dispose()
        {
            foreach (var texture in _cache.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            _cache.Clear();
        }

        private string ResolveUrl(string relativeOrAbsoluteUrl)
        {
            if (relativeOrAbsoluteUrl.Contains("://"))
            {
                return relativeOrAbsoluteUrl;
            }

            return _baseUrl.TrimEnd('/') + "/" + relativeOrAbsoluteUrl.TrimStart('/');
        }
    }
}
