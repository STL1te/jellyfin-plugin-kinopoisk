using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Kinopoisk.ProviderIdResolvers;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk.MetadataProviders
{
    public class VideoImageProvider : BaseImageProvider
    {
        /// <summary>
        /// Kinopoisk serves images one type per request. These are the types that map onto something
        /// Jellyfin actually displays - the rest (PROMO, CONCEPT, STILL, SHOOTING, SCREENSHOT) are
        /// frame grabs and production stills of no use as artwork.
        /// </summary>
        private static readonly (KinopoiskImageType KinopoiskType, ImageType JellyfinType)[] _imageTypes =
        {
            (KinopoiskImageType.POSTER, ImageType.Primary),
            (KinopoiskImageType.COVER, ImageType.Backdrop),
            (KinopoiskImageType.FAN_ART, ImageType.Backdrop),
            (KinopoiskImageType.WALLPAPER, ImageType.Backdrop),
        };

        private readonly ILogger<VideoImageProvider> _logger;
        private readonly IKinopoiskApiClient _apiClient;
        private readonly IProviderIdResolver<BaseItem> _providerIdResolver;

        public override string Name => Constants.ProviderName;

        public VideoImageProvider(IKinopoiskApiClient kinopoiskApiClient, IProviderIdResolver<BaseItem> providerIdResolver, ILogger<VideoImageProvider> logger, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiClient = kinopoiskApiClient ?? throw new ArgumentNullException(nameof(kinopoiskApiClient));
            _providerIdResolver = providerIdResolver ?? throw new ArgumentNullException(nameof(providerIdResolver));
        }

        public override bool Supports(BaseItem item)
            => item is Movie || item is Series;

        public override IEnumerable<ImageType> GetSupportedImages(BaseItem item)
            => _imageTypes.Select(t => t.JellyfinType)
                .Append(ImageType.Logo)
                .Distinct();

        public override async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var (resolveResult, kinopoiskId) = await _providerIdResolver.TryResolve(item, cancellationToken);
            if (!resolveResult)
                return Enumerable.Empty<RemoteImageInfo>();

            var film = await _apiClient.GetSingleFilm(kinopoiskId, cancellationToken);

            var galleries = await Task.WhenAll(_imageTypes.Select(async t =>
                (await _apiClient.GetImages(kinopoiskId, t.KinopoiskType, cancellationToken)).ToRemoteImageInfos(t.JellyfinType)));

            // Only the film's own poster/cover/logo can be a "no-poster" placeholder and are worth a
            // round-trip each; gallery entries are plain CDN links, and there are up to 80 of them.
            var primary = await FilterEmptyImages(film.ToRemoteImageInfos());

            // The film's own images go first so they stay the default choice.
            return primary
                .Concat(galleries.SelectMany(g => g))
                .GroupBy(i => i.Url)
                .Select(g => g.First());
        }
    }
}
