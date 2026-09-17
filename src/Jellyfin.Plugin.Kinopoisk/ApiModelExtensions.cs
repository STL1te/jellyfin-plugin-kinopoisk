using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;

namespace Jellyfin.Plugin.Kinopoisk
{
    public static class ApiModelExtensions
    {
        public static RemoteSearchResult ToRemoteSearchResult(this Film src)
        {
            if (src is null)
                return null;

            var res = new RemoteSearchResult() {
                Name = src.GetLocalName(),
                ImageUrl = src.PosterUrl,
                PremiereDate = src.GetPremiereDate(),
                ProductionYear = src.GetProductionYear(),
                Overview = src.Description,
                SearchProviderName = Constants.ProviderName
            };
            res.SetProviderId(Constants.ProviderId, Convert.ToString(src.KinopoiskId));
            if (!string.IsNullOrWhiteSpace(src.ImdbId))
                res.SetProviderId(MetadataProvider.Imdb, src.ImdbId);

            return res;
        }

        public static IEnumerable<RemoteSearchResult> ToRemoteSearchResults(this FilmSearchResponse src, ILogger logger)
        {
            if (src?.Films is null)
                return Enumerable.Empty<RemoteSearchResult>();

            return src.Films
                .Select(s => s.ToRemoteSearchResult(logger))
                .Where(s => s != null);
        }

        public static RemoteSearchResult ToRemoteSearchResult(this FilmSearchResponse_films src, ILogger logger)
        {
            try {
                if (src is null)
                    return null;

                var res = new RemoteSearchResult() {
                    Name = src.GetLocalName(),
                    ImageUrl = src.PosterUrl,
                    PremiereDate = src.GetPremiereDate(),
                    ProductionYear = GetFirstYear(src.Year),
                    Overview = src.Description,
                    SearchProviderName = Constants.ProviderName
                };
                res.SetProviderId(Constants.ProviderId, Convert.ToString(src.FilmId));

                return res;
            }
            catch (Exception e) {
                logger.LogError(e, "Exception during parse");
                return null;
            }
        }

        public static IEnumerable<RemoteSearchResult> ToRemoteSearchResults(this PersonByNameResponse src)
        {
            if (src?.Items is null)
                return Enumerable.Empty<RemoteSearchResult>();

            return src.Items
                .Select(s => s.ToRemoteSearchResult())
                .Where(s => s != null);
        }

        public static RemoteSearchResult ToRemoteSearchResult(this PersonByNameResponse_items src)
        {
            var name = src.GetLocalName();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var res = new RemoteSearchResult()
            {
                Name = name,
                ImageUrl = src.PosterUrl,
                SearchProviderName = Constants.ProviderName
            };
            res.SetProviderId(Constants.ProviderId, Convert.ToString(src.KinopoiskId));

            return res;
        }

        public static RemoteSearchResult ToRemoteSearchResult(this PersonResponse src)
        {
            var name = src.GetLocalName();
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var res = new RemoteSearchResult()
            {
                Name = name,
                ImageUrl = src.PosterUrl,
                PremiereDate = src.Birthday.ParseDate(),
                SearchProviderName = Constants.ProviderName
            };
            res.SetProviderId(Constants.ProviderId, Convert.ToString(src.PersonId));

            return res;
        }

        public static Series ToSeries(this Film src)
        {
            if (src is null)
                return null;

            var res = new Series();

            FillCommonFilmInfo(src, res);

            if (src.EndYear > 1900)
                res.EndDate = new DateTime(src.EndYear.Value, 12, 31);
            res.Status = src.GetSeriesStatus();

            return res;
        }

        public static Movie ToMovie(this Film src)
        {
            if (src is null)
                return null;

            var res = new Movie();

            FillCommonFilmInfo(src, res);

            return res;
        }

        public static SeriesStatus? GetSeriesStatus(this Film src)
            => src?.ProductionStatus switch
            {
                FilmProductionStatus.ANNOUNCED
                    or FilmProductionStatus.FILMING
                    or FilmProductionStatus.PRE_PRODUCTION
                    or FilmProductionStatus.POST_PRODUCTION => SeriesStatus.Unreleased,
                FilmProductionStatus.COMPLETED => SeriesStatus.Ended,
                // No usable production status - fall back to what the year range tells us.
                _ => (src?.Completed == true || src?.EndYear > 1900) ? SeriesStatus.Ended : SeriesStatus.Continuing,
            };

        private static void FillCommonFilmInfo(Film src, BaseItem dst)
        {
            dst.SetProviderId(Constants.ProviderId, Convert.ToString(src.KinopoiskId));
            dst.Name = src.GetLocalName();
            dst.OriginalTitle = src.GetOriginalNameIfNotSame();
            dst.PremiereDate = src.GetPremiereDate();
            dst.ProductionYear = src.GetProductionYear();
            if (!string.IsNullOrWhiteSpace(src.Slogan))
                dst.Tagline = src.Slogan;
            dst.Overview = string.IsNullOrWhiteSpace(src.Description) ? src.ShortDescription : src.Description;
            if (src.FilmLength > 0)
                dst.RunTimeTicks = src.FilmLength.Value * TimeSpan.TicksPerMinute;
            if (!string.IsNullOrWhiteSpace(src.WebUrl))
                dst.HomePageUrl = src.WebUrl;
            if (src.Countries != null)
                dst.ProductionLocations = src.Countries.Select(c => c.Country1).ToArray();
            if (src.Genres != null)
                foreach(var genre in src.Genres.Select(c => c.Genre1))
                    dst.AddGenre(genre);
            if (!string.IsNullOrEmpty(src.RatingAgeLimits))
                dst.OfficialRating = $"{src.RatingAgeLimits}+";
            else
                dst.OfficialRating = src.RatingMpaa;

            dst.CommunityRating = (float?)src.RatingKinopoisk;
            if (dst.CommunityRating < 0.1)
                dst.CommunityRating = (float?)src.RatingImdb;
            if (dst.CommunityRating < 0.1)
                dst.CommunityRating = null;
            dst.CriticRating = src.GetCriticRatingAsTenPointBased();

            if (!string.IsNullOrWhiteSpace(src.ImdbId))
                dst.SetProviderId(MetadataProvider.Imdb, src.ImdbId);
        }

        /// <summary>
        /// A film object alone only ever yields "1 January of the production year" - Kinopoisk keeps
        /// the actual release dates and the distributor companies in /distributions.
        /// </summary>
        public static void ApplyDistributions(this BaseItem dst, DistributionResponse src)
        {
            if (dst is null || src?.Items is null)
                return;

            var premiere = src.Items
                .Where(i => i.ReRelease != true)
                .Select(i => i.Date.ParseDate())
                .Where(d => d.HasValue)
                .Min();

            if (premiere.HasValue)
            {
                dst.PremiereDate = premiere;
                dst.ProductionYear = premiere.Value.Year;
            }

            var studios = src.Items
                .Where(i => i.Type == DistributionType.PREMIERE || i.Type == DistributionType.WORLD_PREMIER)
                .SelectMany(i => i.Companies ?? (ICollection<Company>)Array.Empty<Company>())
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToArray();

            if (studios.Length > 0)
                dst.Studios = studios;
        }

        public static float? GetCriticRatingAsTenPointBased(this Film src)
        {
            if (src is null)
                return null;

            if (src.RatingRfCritics > 0.0)
                return (float?)src.RatingRfCritics;

            if (src.RatingFilmCritics > 0.0)
                return (float?)src.RatingFilmCritics;

            return null;
        }

        public static IEnumerable<RemoteImageInfo> ToRemoteImageInfos(this Film src)
        {
            if (src is null)
                yield break;

            if (!string.IsNullOrEmpty(src.PosterUrl))
                yield return CreateRemoteImageInfo(src.PosterUrl, ImageType.Primary);

            // Wide "cover" artwork - the closest thing Kinopoisk has to a Jellyfin backdrop.
            if (!string.IsNullOrEmpty(src.CoverUrl))
                yield return CreateRemoteImageInfo(src.CoverUrl, ImageType.Backdrop);

            if (!string.IsNullOrEmpty(src.LogoUrl))
                yield return CreateRemoteImageInfo(src.LogoUrl, ImageType.Logo);
        }

        public static IEnumerable<RemoteImageInfo> ToRemoteImageInfos(this ImageResponse src, ImageType imageType)
        {
            if (src?.Items is null)
                return Enumerable.Empty<RemoteImageInfo>();

            return src.Items
                .Where(i => !string.IsNullOrEmpty(i.ImageUrl))
                .Select(i =>
                {
                    var res = CreateRemoteImageInfo(i.ImageUrl, imageType);
                    res.ThumbnailUrl = i.PreviewUrl;
                    return res;
                });
        }

        private static RemoteImageInfo CreateRemoteImageInfo(string url, ImageType imageType)
            => new RemoteImageInfo()
            {
                Type = imageType,
                Url = url,
                Language = Constants.ProviderMetadataLanguage,
                ProviderName = Constants.ProviderName
            };

        public static IReadOnlyList<MediaUrl> ToMediaUrls(this VideoResponse src)
        {
            if (src is null || src.Items is null || src.Items.Count < 1)
                return null;

            return src.Items.Select(t => t.ToMediaUrl())
                .Where(mu => mu != null)
                .ToList();
        }

        public static MediaUrl ToMediaUrl(this VideoResponse_items src) {
            if (src is null || !VideoResponse_itemsSite.YOUTUBE.Equals(src.Site) || string.IsNullOrEmpty(src.Url))
                return null;

            return new MediaUrl
            {
                Name = src.Name,
                Url = src.Url.SanitizeYoutubeLink()
            };
        }

        public static string SanitizeYoutubeLink(this string src)
        {
            // Jellyfin web currently recognizes only https://www.youtube.com/watch?v=xxx links
            return src
                .Replace("http://", "https://")
                .Replace("https://youtu.be/", "https://www.youtube.com/watch?v=")
                .Replace("https://www.youtube.com/v/", "https://www.youtube.com/watch?v=");
        }

        public static RemoteImageInfo ToRemoteImageInfo(this PersonResponse src)
        {
            if (src is null || string.IsNullOrEmpty(src.PosterUrl))
                return null;

            return new RemoteImageInfo(){
                Type = ImageType.Primary,
                Url = src.PosterUrl,
                ProviderName = Constants.ProviderName
            };
        }

        public static PersonInfo ToPersonInfo(this StaffResponse src)
        {
            if (src is null)
                return null;

            var res = new PersonInfo()
            {
                Name = src.NameRu,
                ImageUrl = src.PosterUrl,
                Role = string.IsNullOrWhiteSpace(src.Description) ? src.ProfessionText : src.Description,
                Type = src.ProfessionKey.ToPersonType()
            };
            if (string.IsNullOrWhiteSpace(res.Name))
                res.Name = src.NameEn ?? string.Empty;

            res.SetProviderId(Constants.ProviderId, Convert.ToString(src.StaffId));

            return res;
        }

        public static IEnumerable<PersonInfo> ToPersonInfos(this ICollection<StaffResponse> src)
        {
            var res = src.Select(s => s.ToPersonInfo())
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Name))
                .ToArray();

            var i = 0;
            foreach(var item in res)
                item.SortOrder = ++i;

            return res;
        }

        public static PersonKind ToPersonType(this StaffResponseProfessionKey src)
        {
            return src switch
            {
                StaffResponseProfessionKey.ACTOR
                    or StaffResponseProfessionKey.HIMSELF
                    or StaffResponseProfessionKey.HERSELF
                    or StaffResponseProfessionKey.VOICE_MALE
                    or StaffResponseProfessionKey.VOICE_FEMALE => PersonKind.Actor,
                StaffResponseProfessionKey.DIRECTOR
                    or StaffResponseProfessionKey.VOICE_DIRECTOR => PersonKind.Director,
                StaffResponseProfessionKey.WRITER => PersonKind.Writer,
                StaffResponseProfessionKey.COMPOSER => PersonKind.Composer,
                StaffResponseProfessionKey.PRODUCER
                    or StaffResponseProfessionKey.PRODUCER_USSR => PersonKind.Producer,
                StaffResponseProfessionKey.EDITOR => PersonKind.Editor,
                StaffResponseProfessionKey.TRANSLATOR => PersonKind.Translator,
                _ => PersonKind.Unknown,
            };
        }

        public static DateTime? ParseDate(this string src){
            if (string.IsNullOrWhiteSpace(src))
                return null;

            // Kinopoisk mixes plain "2019-02-13" with full round-trip timestamps.
            if (DateTime.TryParse(src, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var res))
                return res;

            return null;
        }

        public static int? GetProductionYear(this Film src)
        {
            if (src is null)
                return null;

            if (src.Year > 1900)
                return src.Year;
            if (src.StartYear > 1900)
                return src.StartYear;

            return null;
        }

        public static DateTime? GetPremiereDate(this Film src)
        {
            var year = src.GetProductionYear();
            if (year.HasValue)
                return new DateTime(year.Value, 1, 1);

            return null;
        }

        public static DateTime? GetPremiereDate(this FilmSearchResponse_films src)
        {
            var firstYear = GetFirstYear(src.Year);
            if (firstYear != null)
                return new DateTime(firstYear.Value, 1, 1);

            return null;
        }

        public static string GetLocalName(this Film src)
        {
            var res = src?.NameRu;
            if (string.IsNullOrWhiteSpace(res))
                res = src?.NameOriginal;
            if (string.IsNullOrWhiteSpace(res))
                res = src?.NameEn;
            return res;
        }

        public static string GetLocalName(this FilmSearchResponse_films src)
        {
            var res = src?.NameRu;
            if (string.IsNullOrWhiteSpace(res))
                res = src?.NameEn;
            return res;
        }

        public static string GetLocalName(this PersonByNameResponse_items src)
        {
            var res = src?.NameRu;
            if (string.IsNullOrWhiteSpace(res))
                res = src?.NameEn;
            return res;
        }

        public static string GetOriginalName(this Film src)
            => src?.NameOriginal ??
                (src.IsRussianSpokenOriginated()
                    ? src?.NameRu
                    : src?.NameEn);

        public static string GetOriginalNameIfNotSame(this Film src)
        {
            var localName = src.GetLocalName();
            var originalName = src.GetOriginalName();
            if (!string.IsNullOrWhiteSpace(originalName) && !string.Equals(localName, originalName))
                return originalName;

            return string.Empty;
        }

        public static bool IsRussianSpokenOriginated(this Film src)
            => src?.Countries?.IsRussianSpokenOriginated() ?? false;

        public static bool IsRussianSpokenOriginated(this IEnumerable<Country> src)
        {
            if (src is null)
                return false;

            foreach(var country in src)
                switch(country.Country1)
                {
                    case "Россия":
                        return true;
                }

            return false;
        }

        public static int? GetFirstYear(string years)
        {
            if (string.IsNullOrWhiteSpace(years) || years.ToLower() == "null")
                return null;

            years = years.Trim();

            if (int.TryParse(years, out var res))
                return res;

            var i = 0;
            while (true) {
                if (i > 4)
                    return null;
                if (!char.IsDigit(years[i]))
                    break;
                i++;
            }

            return Convert.ToInt32(years.Substring(0, i));
        }

        public static bool IsСontinuing(string years)
            => years?.EndsWith("-...") ?? false;

        public static int? GetLastYear(string years)
        {
            if (string.IsNullOrWhiteSpace(years))
                return null;

            years = years.Trim();

            if (int.TryParse(years, out var res))
                return res;

            var i = 0;
            int startindex() => years.Length - 1 - i;
            while (true) {
                if (i > 4)
                    return null;
                if (!char.IsDigit(years[startindex()]))
                {
                    i--;
                    break;
                }
                i++;
            }

            return i > 0
                ? (int?)Convert.ToInt32(years[startindex()..])
                : null;
        }

        public static MediaBrowser.Controller.Entities.TV.Episode ToEpisode(this KinopoiskUnofficialInfo.ApiClient.Episode src)
        {
            if (src is null)
                return null;

            var res = new MediaBrowser.Controller.Entities.TV.Episode()
            {
                Name = string.IsNullOrWhiteSpace(src.NameRu) ? src.NameEn : src.NameRu,
                Overview = src.Synopsis,
                ParentIndexNumber = src.SeasonNumber,
                IndexNumber = src.EpisodeNumber,
                PremiereDate = src.ReleaseDate.ParseDate()
            };
            res.ProductionYear = res.PremiereDate?.Year;

            return res;
        }

        public static KinopoiskUnofficialInfo.ApiClient.Episode FindEpisode(this SeasonResponse src, int seasonNumber, int episodeNumber)
            => src?.Items?
                .FirstOrDefault(s => s.Number == seasonNumber)?.Episodes?
                .FirstOrDefault(e => e.EpisodeNumber == episodeNumber);

        public static Person ToPerson(this PersonResponse src)
        {
            if (src is null)
                return null;

            var res = new Person()
            {
                Name = src.GetLocalName(),
                PremiereDate = src.Birthday.ParseDate(),
                EndDate = src.Death.ParseDate(),
                Overview = src.GetOverview()
            };
            res.ProductionYear = res.PremiereDate?.Year;

            if (!string.IsNullOrWhiteSpace(src.Birthplace))
                res.ProductionLocations = new[] { src.Birthplace };

            res.SetProviderId(Constants.ProviderId, Convert.ToString(src.PersonId));

            return res;
        }

        /// <summary>
        /// Kinopoisk has no biography field for persons - the "facts" list is the closest thing.
        /// </summary>
        public static string GetOverview(this PersonResponse src)
        {
            if (src?.Facts is null || src.Facts.Count < 1)
                return null;

            return string.Join("\n\n", src.Facts.Where(f => !string.IsNullOrWhiteSpace(f)));
        }

        public static string GetLocalName(this PersonResponse src)
        {
            var res = src?.NameRu;
            if (string.IsNullOrWhiteSpace(res))
                res = src?.NameEn;
            return res;
        }
    }
}
