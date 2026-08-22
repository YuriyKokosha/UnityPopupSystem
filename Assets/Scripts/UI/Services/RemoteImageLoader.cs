using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PopupSystem.UI.Services
{
    /// <summary>
    /// Downloads textures via <see cref="UnityWebRequest"/> for popups whose visual content is
    /// not bundled with the app. Relative keys are resolved against a configurable base URL, so
    /// this same class serves both a local demo source (StreamingAssets, wired up in
    /// AppInstaller) and a real CDN in production - only the base URL changes.
    /// </summary>
    public sealed class RemoteImageLoader : IRemoteImageLoader
    {
        private readonly string _baseUrl;

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

            using var request = UnityWebRequestTexture.GetTexture(url);

            // Throws UnityWebRequestException on HTTP/transport failure, or OperationCanceledException
            // if the window closes (and its lifetime token is cancelled) before the download finishes -
            // both are expected and handled by the caller, not swallowed here.
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            var texture = ((DownloadHandlerTexture)request.downloadHandler).texture;
            if (texture == null)
            {
                // The request itself succeeded (no exception above), but the response body
                // wasn't a decodable image - wrong content-type, corrupted file, etc. Treat that
                // as a load failure too, rather than silently handing the caller a null texture
                // it wasn't expecting.
                throw new InvalidOperationException($"Downloaded image at '{url}' could not be decoded.");
            }

            return texture;
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
