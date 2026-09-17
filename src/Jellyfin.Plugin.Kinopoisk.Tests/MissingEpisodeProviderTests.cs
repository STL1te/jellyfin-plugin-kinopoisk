using System;
using Jellyfin.Plugin.Kinopoisk.MetadataProviders;
using KinopoiskUnofficialInfo.ApiClient;
using Xunit;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using KpEpisode = KinopoiskUnofficialInfo.ApiClient.Episode;

namespace Jellyfin.Plugin.Kinopoisk.Tests
{
    public class MissingEpisodeProviderTests
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 17);

        [Theory]
        // undated episodes are never importable, whatever is switched on
        [InlineData(null, true, true, false)]
        // already aired -> needs the "missing" toggle
        [InlineData("2020-01-01", false, true, true)]
        [InlineData("2020-01-01", true, false, false)]
        // airing today or later -> needs the "unaired" toggle
        [InlineData("2026-09-17", true, false, true)]
        [InlineData("2026-12-01", true, false, true)]
        [InlineData("2026-12-01", false, true, false)]
        public void ShouldImportFollowsTheAirDate(string airDate, bool importUnaired, bool importMissing, bool expected)
        {
            var result = MissingEpisodeProvider.ShouldImport(
                airDate.ParseDate(), Today, importUnaired, importMissing, isSpecial: false, importSpecials: false);

            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        public void ShouldImportSkipsSpecialsUnlessAskedFor(bool importSpecials, bool expected)
        {
            var result = MissingEpisodeProvider.ShouldImport(
                "2020-01-01".ParseDate(), Today, importUnaired: true, importMissing: true, isSpecial: true, importSpecials: importSpecials);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void UpdateVirtualEpisodeReportsRealChangesOnly()
        {
            var episode = new Episode
            {
                Name = "Старое название",
                Overview = "Старое описание",
                PremiereDate = new DateTime(2020, 1, 1)
            };

            var src = new KpEpisode
            {
                SeasonNumber = 1,
                EpisodeNumber = 1,
                NameRu = "Новое название",
                Synopsis = "Новое описание",
                ReleaseDate = "2020-02-02"
            };

            Assert.True(MissingEpisodeProvider.UpdateVirtualEpisode(episode, src, src.ReleaseDate.ParseDate()));
            Assert.Equal("Новое название", episode.Name);
            Assert.Equal("Новое описание", episode.Overview);
            Assert.Equal(new DateTime(2020, 2, 2), episode.PremiereDate.Value.Date);
            Assert.Equal(2020, episode.ProductionYear);

            // second pass over the same data must be a no-op, or every scan would rewrite the item
            Assert.False(MissingEpisodeProvider.UpdateVirtualEpisode(episode, src, src.ReleaseDate.ParseDate()));
        }

        [Fact]
        public void UpdateVirtualEpisodeKeepsWhatKinopoiskDoesNotKnow()
        {
            var episode = new Episode { Name = "Есть название", Overview = "Есть описание" };
            var src = new KpEpisode { SeasonNumber = 1, EpisodeNumber = 1, NameRu = null, NameEn = null, Synopsis = null };

            Assert.False(MissingEpisodeProvider.UpdateVirtualEpisode(episode, src, null));
            Assert.Equal("Есть название", episode.Name);
            Assert.Equal("Есть описание", episode.Overview);
        }
    }
}
