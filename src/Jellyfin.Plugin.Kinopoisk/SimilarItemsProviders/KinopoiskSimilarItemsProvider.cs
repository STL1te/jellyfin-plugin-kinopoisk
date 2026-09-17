using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk.SimilarItemsProviders
{
    /// <summary>
    /// Feeds Kinopoisk's "похожие фильмы" list into Jellyfin's similar/recommended items (Jellyfin 12+).
    /// </summary>
    public class KinopoiskSimilarItemsProvider : IRemoteSimilarItemsProvider
    {
        private readonly IKinopoiskApiClient _apiClient;
        private readonly ILogger<KinopoiskSimilarItemsProvider> _logger;

        public KinopoiskSimilarItemsProvider(IKinopoiskApiClient kinopoiskApiClient, ILogger<KinopoiskSimilarItemsProvider> logger)
        {
            _apiClient = kinopoiskApiClient ?? throw new ArgumentNullException(nameof(kinopoiskApiClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => Constants.ProviderName;

        public MetadataPluginType Type => MetadataPluginType.SimilarityProvider;

        public TimeSpan? CacheDuration => TimeSpan.FromDays(7);

        public bool Supports(System.Type itemType)
            => typeof(Movie).IsAssignableFrom(itemType) || typeof(Series).IsAssignableFrom(itemType);

        public async IAsyncEnumerable<SimilarItemReference> GetSimilarItemsAsync(
            BaseItem item,
            SimilarItemsQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!item.TryGetProviderId(Constants.ProviderId, out var kinopoiskIdStr)
                || !int.TryParse(kinopoiskIdStr, CultureInfo.InvariantCulture, out var kinopoiskId))
                yield break;

            // Sequels, prequels and spin-offs are a far stronger recommendation than "viewers also
            // watched", so they go first and the plain similars list fills in behind them.
            var seen = new HashSet<int>();
            foreach (var filmId in await GetRelatedFilmIds(kinopoiskId, cancellationToken))
            {
                if (!seen.Add(filmId))
                    continue;

                yield return new SimilarItemReference
                {
                    ProviderName = Constants.ProviderId,
                    ProviderId = filmId.ToString(CultureInfo.InvariantCulture)
                };
            }
        }

        private async Task<IReadOnlyList<int>> GetRelatedFilmIds(int kinopoiskId, CancellationToken cancellationToken)
        {
            try
            {
                var relations = await _apiClient.GetRelations(kinopoiskId, cancellationToken);
                var similars = await _apiClient.GetSimilars(kinopoiskId, cancellationToken);

                return (relations?.Items ?? (ICollection<RelatedFilmResponse_items>)Array.Empty<RelatedFilmResponse_items>())
                    .Select(i => i.KinopoiskId)
                    .Concat((similars?.Items ?? (ICollection<SimilarFilmResponse_items>)Array.Empty<SimilarFilmResponse_items>())
                        .Select(i => i.FilmId))
                    .Where(id => id > 0)
                    .ToArray();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to get related films from Kinopoisk for {KinopoiskId}", kinopoiskId);
                return Array.Empty<int>();
            }
        }
    }
}
