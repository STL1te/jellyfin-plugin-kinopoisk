using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Season = MediaBrowser.Controller.Entities.TV.Season;

namespace Jellyfin.Plugin.Kinopoisk.MetadataProviders
{
    /// <summary>
    /// Kinopoisk has no season entity of its own - a season is just a numbered bucket of episodes -
    /// so all this can contribute is the air date of the season's first episode. That is still enough
    /// for Jellyfin to sort and date seasons without reaching for another provider.
    /// </summary>
    public class SeasonMetadataProvider : BaseMetadataProvider, IRemoteMetadataProvider<Season, SeasonInfo>
    {
        private readonly IKinopoiskApiClient _apiClient;
        private readonly ILogger<SeasonMetadataProvider> _logger;

        public SeasonMetadataProvider(IKinopoiskApiClient kinopoiskApiClient, ILogger<SeasonMetadataProvider> logger, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory)
        {
            _apiClient = kinopoiskApiClient ?? throw new System.ArgumentNullException(nameof(kinopoiskApiClient));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>()
            {
                QueriedById = true,
                Provider = Constants.ProviderName,
                ResultLanguage = Constants.ProviderMetadataLanguage
            };

            if (!info.SeriesProviderIds.TryGetValue(Constants.ProviderId, out var seriesIdStr)
                || !int.TryParse(seriesIdStr, out var seriesId)
                || !info.IndexNumber.HasValue)
                return result;

            var seasons = await _apiClient.GetSeasons(seriesId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var season = seasons?.Items?.FirstOrDefault(s => s.Number == info.IndexNumber.Value);
            if (season is null)
            {
                _logger.LogDebug("Kinopoisk has no season {SeasonNumber} for series {SeriesId}", info.IndexNumber, seriesId);
                return result;
            }

            result.Item = season.ToSeason();
            result.HasMetadata = true;

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<RemoteSearchResult>()); // Not supported
    }
}
