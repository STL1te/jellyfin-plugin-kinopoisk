using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KinopoiskUnofficialInfo.ApiClient;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk.MetadataProviders
{
    public class EpisodeMetadataProvider : BaseMetadataProvider, IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        private readonly IKinopoiskApiClient _apiClient;
        private readonly ILogger<EpisodeMetadataProvider> _logger;

        public EpisodeMetadataProvider(IKinopoiskApiClient kinopoiskApiClient, ILogger<EpisodeMetadataProvider> logger, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory)
        {
            _apiClient = kinopoiskApiClient ?? throw new System.ArgumentNullException(nameof(kinopoiskApiClient));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>()
            {
                QueriedById = true,
                Provider = Constants.ProviderName,
                ResultLanguage = Constants.ProviderMetadataLanguage
            };

            // Episodes have no Kinopoisk id of their own - they are addressed by series id + numbers.
            if (!info.SeriesProviderIds.TryGetValue(Constants.ProviderId, out var seriesIdStr)
                || !int.TryParse(seriesIdStr, out var seriesId)
                || !info.ParentIndexNumber.HasValue
                || !info.IndexNumber.HasValue)
                return result;

            var seasons = await _apiClient.GetSeasons(seriesId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var episode = seasons.FindEpisode(info.ParentIndexNumber.Value, info.IndexNumber.Value);
            if (episode is null)
            {
                _logger.LogDebug("Kinopoisk has no S{Season}E{Episode} for series {SeriesId}", info.ParentIndexNumber, info.IndexNumber, seriesId);
                return result;
            }

            result.Item = episode.ToEpisode();
            result.HasMetadata = true;

            return result;
        }

        public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
            => Task.FromResult(Enumerable.Empty<RemoteSearchResult>()); // Not supported
    }
}
