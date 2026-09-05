using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PopupSystem.UI.Services;
using UnityEngine;

namespace PopupSystem.Tests.PlayMode
{
    [TestFixture]
    public sealed class RemoteImageLoaderPlayModeTests
    {
        private const string BannerUrl = "offers/starter-offer-banner.png";

        private RemoteImageLoader _loader;

        [SetUp]
        public void SetUp()
        {
            _loader = new RemoteImageLoader(StreamingAssetsBaseUrl());
        }

        [TearDown]
        public void TearDown()
        {
            _loader.Dispose();
        }

        [Test]
        public async Task LoadAsync_ReturnsTheSameTexture_ForARepeatedUrl()
        {
            var first = await _loader.LoadAsync(BannerUrl, CancellationToken.None);
            var second = await _loader.LoadAsync(BannerUrl, CancellationToken.None);

            Assert.That(first, Is.Not.Null);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public async Task Dispose_DestroysTheCachedTextures()
        {
            var texture = await _loader.LoadAsync(BannerUrl, CancellationToken.None);
            Assert.That(texture != null, Is.True);

            _loader.Dispose();
            await UniTask.DelayFrame(2);

            Assert.That(texture == null, Is.True);
        }

        [Test]
        public async Task LoadAsync_Throws_ForAnAddressThatIsNotThere()
        {
            Exception caught = null;

            try
            {
                await _loader.LoadAsync("offers/there-is-no-such-file.png", CancellationToken.None);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Assert.That(caught, Is.Not.Null);
        }

        private static string StreamingAssetsBaseUrl()
        {
            var path = Application.streamingAssetsPath;
            return path.Contains("://") ? path : "file://" + path;
        }
    }
}
