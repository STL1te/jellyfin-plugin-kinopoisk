using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KinopoiskUnofficialInfo.ApiClient
{
    public class KinopoiskApiClient : IKinopoiskApiClient
    {
        private readonly string _apiToken;
        private readonly ILogger<KinopoiskApiClient> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Client _apiClient;

        public KinopoiskApiClient(string apiToken, ILogger<KinopoiskApiClient> logger, IHttpClientFactory httpClientFactory)
        {
            if (string.IsNullOrEmpty(apiToken))
            {
                throw new System.ArgumentException($"'{nameof(apiToken)}' cannot be null or empty.", nameof(apiToken));
            }

            _apiToken = apiToken;
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _httpClientFactory = httpClientFactory ?? throw new System.ArgumentNullException(nameof(httpClientFactory));

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiToken);
            _apiClient = new Client(httpClient);
        }

        private async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> method, CancellationToken? ct, [CallerMemberName] string memberName = "")
        {
            try
            {
                _logger.LogDebug("{Method} request starting...", memberName);
                var res = await method.Invoke(ct ?? CancellationToken.None);
                _logger.LogDebug("{Method} request complete successfully", memberName);
                return res;
            }
            catch (ApiException e)
            {
                _logger.LogError("Received non-success result status code {StatusCode} from Kinopoisk API, response content is:\n{Response}", e.StatusCode, e.Response);
                throw;
            }
        }

        // Kinopoisk answers 404 for "this film simply has no such data", which is a normal outcome
        // for most of the optional endpoints - don't let it abort the whole metadata refresh.
        private Task<T> InvokeOptional<T>(Func<CancellationToken, Task<T>> method, CancellationToken? ct, Func<T> emptyResult, [CallerMemberName] string memberName = "")
            => Invoke(async (c) =>
            {
                try
                {
                    return await method.Invoke(c);
                }
                catch (ApiException e) when (e.StatusCode == 404)
                {
                    return emptyResult();
                }
            }, ct, memberName);

        public Task<Film> GetSingleFilm(int filmId, CancellationToken? cancellationToken = null)
            => Invoke((ct) => _apiClient.FilmsAsync(filmId, ct), cancellationToken);

        public Task<ICollection<StaffResponse>> GetStaff(int filmId, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.StaffAllAsync(filmId, ct), cancellationToken, () => Array.Empty<StaffResponse>());

        public Task<FilmSearchResponse> SearchByKeyword(string keyword, int page = 1, CancellationToken? cancellationToken = null)
            => Invoke((ct) => _apiClient.SearchByKeywordAsync(keyword, page, ct), cancellationToken);

        public Task<PersonResponse> GetPerson(int personId, CancellationToken? cancellationToken = null)
            => Invoke((ct) => _apiClient.StaffAsync(personId, ct), cancellationToken);

        public Task<VideoResponse> GetTrailers(int filmId, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.VideosAsync(filmId, ct), cancellationToken, () => new VideoResponse());

        public Task<DistributionResponse> GetDistributions(int filmId, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.DistributionsAsync(filmId, ct), cancellationToken, () => new DistributionResponse());

        public Task<SeasonResponse> GetSeasons(int filmId, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.SeasonsAsync(filmId, ct), cancellationToken, () => new SeasonResponse());

        public Task<ImageResponse> GetImages(int filmId, KinopoiskImageType type, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.ImagesAsync(filmId, type, 1, ct), cancellationToken, () => new ImageResponse());

        public Task<SimilarFilmResponse> GetSimilars(int filmId, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.SimilarsAsync(filmId, ct), cancellationToken, () => new SimilarFilmResponse());

        public Task<PersonByNameResponse> SearchPersonByName(string name, int page = 1, CancellationToken? cancellationToken = null)
            => InvokeOptional((ct) => _apiClient.PersonsAsync(name, page, ct), cancellationToken, () => new PersonByNameResponse());
    }
}
