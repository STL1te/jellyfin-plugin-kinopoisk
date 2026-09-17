using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class RemoteImageUrlSanitizerTests
    {
        [Fact]
        public async Task SanitizeRemoteImageUrlDisposesRequestAndResponse()
        {
            using var handler = new TrackingHandler();
            using var httpClient = new HttpClient(handler);

            var sanitizer = new RemoteImageUrlSanitizer(httpClient);

            var result = await sanitizer.SanitizeRemoteImageUrl(
                "https://example.com/image.jpg");

            Assert.Equal("https://example.com/image.jpg", result);
            Assert.True(handler.RequestContent.IsDisposed);
            Assert.True(handler.ResponseContent.IsDisposed);
        }

        [Fact]
        public async Task SanitizeRemoteImageUrlKeepsTheUrlWhenTheCdnIsUnreachable()
        {
            using var handler = new ThrowingHandler();
            using var httpClient = new HttpClient(handler);

            var result = await new RemoteImageUrlSanitizer(httpClient)
                .SanitizeRemoteImageUrl("https://example.com/image.jpg");

            Assert.Equal("https://example.com/image.jpg", result);
        }

        [Fact]
        public async Task SanitizeRemoteImageUrlGivesUpOnARedirectLoop()
        {
            using var handler = new LoopingRedirectHandler();
            using var httpClient = new HttpClient(handler);

            var result = await new RemoteImageUrlSanitizer(httpClient)
                .SanitizeRemoteImageUrl("https://example.com/image.jpg");

            Assert.Null(result);
            Assert.True(handler.Calls <= 7, $"followed {handler.Calls} redirects");
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new HttpRequestException("Connection reset by peer");
        }

        private sealed class LoopingRedirectHandler : HttpMessageHandler
        {
            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new System.Uri("https://example.com/image.jpg?r=" + Calls);
                return Task.FromResult(response);
            }
        }

        private sealed class TrackingHandler : HttpMessageHandler
        {
            public TrackingContent RequestContent { get; } = new TrackingContent();

            public TrackingContent ResponseContent { get; } = new TrackingContent();

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                request.Content = RequestContent;

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = ResponseContent
                    });
            }
        }

        private sealed class TrackingContent : HttpContent
        {
            public bool IsDisposed { get; private set; }

            protected override Task SerializeToStreamAsync(
                Stream stream,
                TransportContext context)
            {
                return Task.CompletedTask;
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return true;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    IsDisposed = true;
                }

                base.Dispose(disposing);
            }
        }
    }
}
