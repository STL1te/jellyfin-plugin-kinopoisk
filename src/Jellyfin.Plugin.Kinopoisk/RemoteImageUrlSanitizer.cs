using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Kinopoisk
{
    public class RemoteImageUrlSanitizer
    {
        /// <summary>
        /// Kinopoisk redirects a missing image to a "no-poster" placeholder, normally in one hop.
        /// The cap is only here so a redirect loop cannot hang a library scan forever.
        /// </summary>
        private const int MaxRedirects = 5;

        private readonly HttpClient _httpClient;

        public RemoteImageUrlSanitizer(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new System.ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> SanitizeRemoteImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return null;

            var currentUrl = url;
            for (var redirects = 0; !currentUrl.Contains("no-poster"); redirects++)
            {
                if (redirects > MaxRedirects)
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);

                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
                {
                    // The point of this check is weeding out placeholders, not reachability. If the
                    // CDN would not talk to us, keep the url and let Jellyfin decide later - failing
                    // here would throw away every other image of the item as well.
                    return url;
                }

                using (response)
                {
                    if ((int)response.StatusCode <= 299)
                        return currentUrl;

                    if (response.Headers.Location is null)
                        return null;

                    currentUrl = response.Headers.Location.ToString();
                }
            }

            return null;
        }
    }
}
