using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vcr;
using Xunit;

namespace KinopoiskUnofficialInfo.ApiClient.Tests
{
    public class KinopoiskApiClientTests
    {
        private const string ApiToken = "85d30ae5-d875-4c5f-900d-8e37bb20625e";
        private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private readonly VCR _vcr;
        private readonly Mock<IHttpClientFactory> _clientFactoryMock;
        public KinopoiskApiClientTests()
        {
            var dirInfo = new System.IO.DirectoryInfo("../../../cassettes"); //3 levels up to get to the root of the test project
            _vcr = new VCR(new FileSystemCassetteStorage(dirInfo));

            _clientFactoryMock = new Mock<IHttpClientFactory>();
            _clientFactoryMock
                .Setup(f => f.CreateClient(It.IsAny<string>()))
                .Returns(() =>
                {
                    var httpHandler = _vcr.GetVcrHandler();
                    httpHandler.InnerHandler = new HttpClientHandler();
                    return new HttpClient(httpHandler);
                });
        }

        private string GetMethodName([CallerMemberName] string memberName = "") => memberName;

        [Theory]
        [InlineData(1142206, "Пальма", null)]
        [InlineData(1044982, "Шпион, который меня кинул", "The Spy Who Dumped Me")]
        [InlineData(1445243, "Будь моим Кириллом", null)]
        [InlineData(251733, "Аватар", "Avatar")]
        public async Task GetSingleFilm_ShouldParseFilm(int filmId, string nameRu, string nameOriginal)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetSingleFilm(filmId);

                Assert.NotNull(res);
                Assert.Equal(FilmType.FILM, res.Type);
                Assert.Equal(nameRu, res.NameRu);
                Assert.Equal(nameOriginal, res.NameOriginal);
            }
        }

        [Theory]
        [InlineData(4416198, "В активном поиске", null, FilmType.TV_SERIES)]
        [InlineData(77298, "Вавилон 5", "Babylon 5", FilmType.TV_SERIES)]
        public async Task GetSingleFilm_ShouldParseTvShow(int filmId, string nameRu, string nameOriginal, FilmType type)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetSingleFilm(filmId);

                Assert.NotNull(res);
                Assert.Equal(type, res.Type);
                Assert.Equal(nameRu, res.NameRu);
                Assert.Equal(nameOriginal, res.NameOriginal);
            }
        }

        [Theory]
        [InlineData(89540, 81, StaffResponseProfessionKey.PRODUCER_USSR)]
        public async Task GetSingleFilm_ShouldParseProducerUssr(int filmId, int index, StaffResponseProfessionKey profession)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetStaff(filmId);

                Assert.NotNull(res);
                Assert.Equal(profession, res.ToArray()[index].ProfessionKey);
            }
        }

        [Theory]
        [InlineData(3873197, "Ирина Старшенбаум", "")]
        public async Task GetPerson_ShouldParseName(int personId, string nameRu, string nameEn)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{personId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetPerson(personId);

                Assert.NotNull(res);
                Assert.Equal(nameRu, res.NameRu);
                Assert.Equal(nameEn, res.NameEn);
            }
        }

        [Theory]
        [InlineData(1395460, "Интернет-трейлер (сезон 1)")]
        public async Task GetTrailers_ShouldParseName(int filmId, string name)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetTrailers(filmId);

                Assert.NotNull(res);
                Assert.Contains(res.Items, t => name.Equals(t.Name));
            }
        }

        [Theory]
        [InlineData(948870)]
        public async Task GetTrailers_ShouldReturnEmptyResponse(int filmId)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetTrailers(filmId);

                Assert.NotNull(res);
                Assert.Empty(res.Items);
            }
        }

        [Theory]
        [InlineData(77298, 5, 2, "Points of Departure")]
        public async Task GetSeasons_ShouldParseEpisodes(int filmId, int seasonCount, int seasonNumber, string firstEpisodeName)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetSeasons(filmId);

                Assert.NotNull(res);
                Assert.Equal(seasonCount, res.Items.Count);
                var season = res.Items.Single(s => s.Number == seasonNumber);
                Assert.Equal(firstEpisodeName, season.Episodes.Single(e => e.EpisodeNumber == 1).NameEn);
            }
        }

        [Theory]
        [InlineData(251733)]
        public async Task GetImages_ShouldParsePosters(int filmId)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetImages(filmId, KinopoiskImageType.POSTER);

                Assert.NotNull(res);
                Assert.NotEmpty(res.Items);
                Assert.All(res.Items, i => Assert.StartsWith("http", i.ImageUrl));
            }
        }

        [Theory]
        [InlineData(251733)]
        public async Task GetSimilars_ShouldParseItems(int filmId)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetSimilars(filmId);

                Assert.NotNull(res);
                Assert.NotEmpty(res.Items);
                Assert.All(res.Items, i => Assert.True(i.FilmId > 0));
            }
        }

        [Theory]
        [InlineData(1044982)]
        public async Task GetDistributions_ShouldParseDatesAndCompanies(int filmId)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{filmId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.GetDistributions(filmId);

                Assert.NotNull(res);
                Assert.NotEmpty(res.Items);
                Assert.Contains(res.Items, i => !string.IsNullOrEmpty(i.Date));
            }
        }

        [Theory]
        [InlineData("Ирина Старшенбаум", 3873197)]
        public async Task SearchPersonByName_ShouldFindPerson(string name, int personId)
        {
            using (_vcr.UseCassette($"{GetMethodName()}_{personId}", RecordMode.NewEpisodes))
            {
                var apiClient = new KinopoiskApiClient(ApiToken, _loggerFactory.CreateLogger<KinopoiskApiClient>(), _clientFactoryMock.Object);

                var res = await apiClient.SearchPersonByName(name);

                Assert.NotNull(res);
                Assert.Contains(res.Items, i => i.KinopoiskId == personId);
            }
        }
    }
}
