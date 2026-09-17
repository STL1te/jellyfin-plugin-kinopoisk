using System;
using System.Collections.Generic;
using System.Linq;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class ApiModelExtensionsTests
    {
        [Fact]
        public void ToPersonInfosShouldSkipPersonsWithoutNames()
        {
            var staff = new List<StaffResponse>
            {
                CreateStaff(1, "Иван Иванов", string.Empty),
                CreateStaff(2, string.Empty, string.Empty),
                CreateStaff(3, "   ", "   "),
                CreateStaff(4, string.Empty, "John Smith")
            };

            var result = staff.ToPersonInfos().ToArray();

            Assert.Equal(2, result.Length);
            Assert.Equal("Иван Иванов", result[0].Name);
            Assert.Equal(1, result[0].SortOrder);
            Assert.Equal("John Smith", result[1].Name);
            Assert.Equal(2, result[1].SortOrder);
        }

        [Theory]
        [InlineData("1992-03-30", 1992, 3, 30)]
        [InlineData("2019-02-13T00:00:00.0000000Z", 2019, 2, 13)]
        public void ParseDateShouldUnderstandBothKinopoiskDateShapes(string src, int year, int month, int day)
        {
            var result = src.ParseDate();

            Assert.NotNull(result);
            Assert.Equal(new DateTime(year, month, day), result.Value.Date);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("не дата")]
        public void ParseDateShouldReturnNullForGarbage(string src)
            => Assert.Null(src.ParseDate());

        [Fact]
        public void ApplyDistributionsShouldUseEarliestOriginalReleaseAndDistributors()
        {
            var item = new Movie();

            item.ApplyDistributions(new DistributionResponse
            {
                Items = new List<Distribution>
                {
                    CreateDistribution(DistributionType.WORLD_PREMIER, "2018-08-02", false, "Lionsgate"),
                    CreateDistribution(DistributionType.PREMIERE, "2018-08-16", false, "Вольга"),
                    CreateDistribution(DistributionType.LOCAL, "2016-01-01", true, "Ре-релиз"),
                    CreateDistribution(DistributionType.LOCAL, "2018-11-20", false)
                }
            });

            Assert.Equal(new DateTime(2018, 8, 2), item.PremiereDate.Value.Date);
            Assert.Equal(2018, item.ProductionYear);
            Assert.Equal(new[] { "Lionsgate", "Вольга" }, item.Studios);
        }

        [Fact]
        public void ApplyDistributionsShouldLeaveItemAloneWhenThereAreNoDates()
        {
            var item = new Movie { PremiereDate = new DateTime(2018, 1, 1), ProductionYear = 2018 };

            item.ApplyDistributions(new DistributionResponse
            {
                Items = new List<Distribution> { CreateDistribution(DistributionType.LOCAL, null, false) }
            });

            Assert.Equal(new DateTime(2018, 1, 1), item.PremiereDate);
            Assert.Empty(item.Studios);
        }

        [Theory]
        [InlineData(FilmProductionStatus.FILMING, null, null, SeriesStatus.Unreleased)]
        [InlineData(FilmProductionStatus.COMPLETED, null, null, SeriesStatus.Ended)]
        [InlineData(FilmProductionStatus.UNKNOWN, null, 2015, SeriesStatus.Ended)]
        [InlineData(FilmProductionStatus.UNKNOWN, true, null, SeriesStatus.Ended)]
        [InlineData(FilmProductionStatus.UNKNOWN, false, null, SeriesStatus.Continuing)]
        public void GetSeriesStatusShouldFallBackToYearRange(FilmProductionStatus status, bool? completed, int? endYear, SeriesStatus expected)
        {
            var film = new Film { ProductionStatus = status, Completed = completed, EndYear = endYear };

            Assert.Equal(expected, film.GetSeriesStatus());
        }

        [Fact]
        public void FindEpisodeShouldMapNumbersNameAndAirDate()
        {
            var seasons = new SeasonResponse
            {
                Items = new List<Season>
                {
                    new Season { Number = 1, Episodes = new List<Episode> { CreateEpisode(1, 1, "Первая") } },
                    new Season { Number = 2, Episodes = new List<Episode> { CreateEpisode(2, 7, "Седьмая") } }
                }
            };

            Assert.Null(seasons.FindEpisode(2, 1));

            var result = seasons.FindEpisode(2, 7).ToEpisode();

            Assert.Equal("Седьмая", result.Name);
            Assert.Equal(2, result.ParentIndexNumber);
            Assert.Equal(7, result.IndexNumber);
            Assert.Equal(new DateTime(1998, 5, 6), result.PremiereDate.Value.Date);
            Assert.Equal(1998, result.ProductionYear);
        }

        [Fact]
        public void ToRemoteImageInfosShouldMapCoverToBackdropAndLogoToLogo()
        {
            var film = new Film
            {
                PosterUrl = "https://kp/poster.jpg",
                CoverUrl = "https://kp/cover.jpg",
                LogoUrl = "https://kp/logo.png"
            };

            var result = film.ToRemoteImageInfos().ToArray();

            Assert.Equal(ImageType.Primary, result[0].Type);
            Assert.Equal(ImageType.Backdrop, result[1].Type);
            Assert.Equal(ImageType.Logo, result[2].Type);
        }

        private static Episode CreateEpisode(int seasonNumber, int episodeNumber, string nameRu)
            => new Episode
            {
                SeasonNumber = seasonNumber,
                EpisodeNumber = episodeNumber,
                NameRu = nameRu,
                ReleaseDate = "1998-05-06"
            };

        private static Distribution CreateDistribution(DistributionType type, string date, bool reRelease, params string[] companies)
            => new Distribution
            {
                Type = type,
                Date = date,
                ReRelease = reRelease,
                Companies = companies.Select(c => new Company { Name = c }).ToList()
            };

        private static StaffResponse CreateStaff(
            int id,
            string nameRu,
            string nameEn)
        {
            return new StaffResponse
            {
                StaffId = id,
                NameRu = nameRu,
                NameEn = nameEn,
                PosterUrl = string.Empty,
                ProfessionText = "Актёр",
                ProfessionKey = StaffResponseProfessionKey.ACTOR
            };
        }
    }
}
