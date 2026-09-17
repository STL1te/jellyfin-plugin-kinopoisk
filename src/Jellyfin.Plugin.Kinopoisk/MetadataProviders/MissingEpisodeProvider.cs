using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using KpEpisode = KinopoiskUnofficialInfo.ApiClient.Episode;
using Season = MediaBrowser.Controller.Entities.TV.Season;

namespace Jellyfin.Plugin.Kinopoisk.MetadataProviders
{
    /// <summary>
    /// Creates metadata-only episodes for what Kinopoisk lists but the library does not have: episodes
    /// that have not aired yet (the "Upcoming" view) and aired ones with no file (shown as missing).
    /// Both are off by default - see the plugin settings.
    /// </summary>
    public class MissingEpisodeProvider : ICustomMetadataProvider<Series>, IHasItemChangeMonitor, IHasOrder
    {
        private readonly IKinopoiskApiClient _apiClient;
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILogger<MissingEpisodeProvider> _logger;

        public MissingEpisodeProvider(
            IKinopoiskApiClient kinopoiskApiClient,
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            ILogger<MissingEpisodeProvider> logger)
        {
            _apiClient = kinopoiskApiClient ?? throw new ArgumentNullException(nameof(kinopoiskApiClient));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => Constants.ProviderName;

        // Runs after the remote series provider, so the Kinopoisk id is already on the item.
        public int Order => 100;

        public bool HasChanged(BaseItem item, IDirectoryService directoryService)
            => item is Series series && series.HasProviderId(Constants.ProviderId);

        public async Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            var importUnaired = config?.ImportUnairedEpisodes ?? false;
            var importMissing = config?.ImportMissingEpisodes ?? false;

            // Turning the feature off has to clean up after itself, otherwise the placeholders stay
            // in the library forever with no way to get rid of them.
            if (!importUnaired && !importMissing)
            {
                if (!PruneAll(item))
                    return ItemUpdateType.None;

                item.Children = null;
                return ItemUpdateType.MetadataImport;
            }

            if (!item.TryGetProviderId(Constants.ProviderId, out var seriesIdStr)
                || !int.TryParse(seriesIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seriesId))
                return ItemUpdateType.None;

            var seasons = await _apiClient.GetSeasons(seriesId, cancellationToken);
            if (seasons?.Items is null || seasons.Items.Count < 1)
                return ItemUpdateType.None;

            var importSpecials = config?.ImportSpecials ?? false;
            var today = DateTime.UtcNow.Date;

            var (taken, ours) = SurveyExistingEpisodes(item, out var changed);

            var seasonsByNumber = item.GetRecursiveChildren(i => i is Season)
                .OfType<Season>()
                .Where(s => s.IndexNumber.HasValue)
                .GroupBy(s => s.IndexNumber.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var kpSeason in seasons.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var kpEpisode in kpSeason.Episodes ?? Array.Empty<KpEpisode>())
                {
                    var key = (kpSeason.Number, kpEpisode.EpisodeNumber);
                    var airDate = kpEpisode.ReleaseDate.ParseDate();

                    if (!ShouldImport(airDate, today, importUnaired, importMissing, kpSeason.Number == 0, importSpecials))
                    {
                        // The episode no longer qualifies (aired since, or specials were turned off)
                        // but we created it earlier - take it back out.
                        if (ours.TryGetValue(key, out var stale))
                        {
                            Delete(stale, "it no longer matches the import settings");
                            changed = true;
                        }

                        continue;
                    }

                    if (ours.TryGetValue(key, out var existing))
                    {
                        if (UpdateVirtualEpisode(existing, kpEpisode, airDate))
                        {
                            await existing.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken);
                            changed = true;
                        }

                        continue;
                    }

                    if (!taken.Add(key))
                        continue;

                    var season = await GetOrCreateSeason(item, kpSeason.Number, seasonsByNumber, cancellationToken);
                    AddVirtualEpisode(item, season, kpEpisode, airDate);
                    changed = true;
                }
            }

            if (!changed)
                return ItemUpdateType.None;

            // Let the season bookkeeping that runs later in the refresh see what we just created.
            item.Children = null;
            return ItemUpdateType.MetadataImport;
        }

        /// <summary>
        /// Walks the series' episodes once, collecting every (season, episode) slot that is already
        /// taken and the placeholders this provider owns, and dropping placeholders that have since
        /// been superseded by a real file.
        /// </summary>
        private (HashSet<(int, int)> Taken, Dictionary<(int, int), Episode> Ours) SurveyExistingEpisodes(Series series, out bool pruned)
        {
            var taken = new HashSet<(int, int)>();
            var physical = new HashSet<(int, int)>();
            var candidates = new List<((int, int) Key, Episode Episode)>();
            pruned = false;

            foreach (var episode in series.GetRecursiveChildren(i => i is Episode).OfType<Episode>())
            {
                // During an initial scan a freshly resolved file may not have its numbers filled in
                // yet, and without them we would create a duplicate placeholder next to it.
                if (episode.IsFileProtocol && (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue))
                {
                    try
                    {
                        _libraryManager.FillMissingEpisodeNumbersFromPath(episode, false);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error resolving episode number from path for {Path}", episode.Path);
                    }
                }

                if (!episode.ParentIndexNumber.HasValue || !episode.IndexNumber.HasValue)
                    continue;

                var key = (episode.ParentIndexNumber.Value, episode.IndexNumber.Value);
                taken.Add(key);

                if (IsOurs(episode))
                    candidates.Add((key, episode));
                else if (!episode.IsVirtualItem)
                    physical.Add(key);
            }

            var ours = new Dictionary<(int, int), Episode>();
            foreach (var (key, episode) in candidates)
            {
                // A file showed up for a slot we were holding - the file wins.
                if (physical.Contains(key))
                {
                    Delete(episode, "a real episode file now exists for this slot");
                    pruned = true;
                }
                else
                {
                    ours[key] = episode;
                }
            }

            return (taken, ours);
        }

        private bool PruneAll(Series series)
        {
            var pruned = false;
            foreach (var episode in series.GetRecursiveChildren(i => i is Episode).OfType<Episode>())
            {
                if (IsOurs(episode))
                {
                    Delete(episode, "importing unaired and missing episodes is switched off");
                    pruned = true;
                }
            }

            return pruned;
        }

        /// <summary>
        /// Kinopoisk episodes have no id of their own, so placeholders are tagged with a key that
        /// cannot be mistaken for a real Kinopoisk id - it is what makes them ours to delete.
        /// </summary>
        private static bool IsOurs(Episode episode)
            => episode.IsVirtualItem && episode.HasProviderId(Constants.VirtualEpisodeProviderId);

        internal static bool ShouldImport(DateTime? airDate, DateTime today, bool importUnaired, bool importMissing, bool isSpecial, bool importSpecials)
        {
            // Without a date there is no way to tell "announced" from "lost to time" - skip it.
            if (!airDate.HasValue)
                return false;

            if (isSpecial && !importSpecials)
                return false;

            return airDate.Value.Date >= today ? importUnaired : importMissing;
        }

        private async Task<Season> GetOrCreateSeason(Series series, int seasonNumber, Dictionary<int, Season> seasonsByNumber, CancellationToken cancellationToken)
        {
            if (seasonsByNumber.TryGetValue(seasonNumber, out var existing))
                return existing;

            _logger.LogInformation("Creating virtual season {SeasonNumber} for series {SeriesName}", seasonNumber, series.Name);

            var season = new Season
            {
                IndexNumber = seasonNumber,
                Id = _libraryManager.GetNewItemId(
                    series.Id.ToString("N", CultureInfo.InvariantCulture) + "Season" + seasonNumber.ToString(CultureInfo.InvariantCulture),
                    typeof(Season)),
                IsVirtualItem = true,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey()
            };

            series.AddChild(season);
            await season.RefreshMetadata(new MetadataRefreshOptions(new DirectoryService(_fileSystem)), cancellationToken);

            seasonsByNumber[seasonNumber] = season;
            return season;
        }

        private void AddVirtualEpisode(Series series, Season season, KpEpisode src, DateTime? airDate)
        {
            var seasonNumber = season.IndexNumber.GetValueOrDefault();

            // Leaving Path unset is what makes this a metadata-only episode.
            var episode = new Episode
            {
                Name = GetName(src),
                Overview = src.Synopsis,
                IndexNumber = src.EpisodeNumber,
                ParentIndexNumber = seasonNumber,
                Id = _libraryManager.GetNewItemId(
                    series.Id.ToString("N", CultureInfo.InvariantCulture)
                        + "Season" + seasonNumber.ToString(CultureInfo.InvariantCulture)
                        + "Episode" + src.EpisodeNumber.ToString(CultureInfo.InvariantCulture),
                    typeof(Episode)),
                IsVirtualItem = true,
                PremiereDate = airDate,
                ProductionYear = airDate?.Year,
                SeasonId = season.Id,
                SeasonName = season.Name,
                SeriesId = series.Id,
                SeriesName = series.Name,
                SeriesPresentationUniqueKey = series.GetPresentationUniqueKey()
            };

            episode.PresentationUniqueKey = episode.CreatePresentationUniqueKey();
            episode.SetProviderId(
                Constants.VirtualEpisodeProviderId,
                string.Create(CultureInfo.InvariantCulture, $"{seasonNumber}:{src.EpisodeNumber}"));

            _logger.LogInformation(
                "Creating virtual episode S{SeasonNumber}E{EpisodeNumber} for series {SeriesName}",
                seasonNumber,
                src.EpisodeNumber,
                series.Name);

            season.AddChild(episode);
        }

        internal static bool UpdateVirtualEpisode(Episode episode, KpEpisode src, DateTime? airDate)
        {
            var changed = false;
            var name = GetName(src);

            if (!string.IsNullOrEmpty(name) && !string.Equals(episode.Name, name, StringComparison.Ordinal))
            {
                episode.Name = name;
                changed = true;
            }

            if (!string.IsNullOrEmpty(src.Synopsis) && !string.Equals(episode.Overview, src.Synopsis, StringComparison.Ordinal))
            {
                episode.Overview = src.Synopsis;
                changed = true;
            }

            if (airDate.HasValue && episode.PremiereDate != airDate)
            {
                episode.PremiereDate = airDate;
                episode.ProductionYear = airDate.Value.Year;
                changed = true;
            }

            return changed;
        }

        private static string GetName(KpEpisode src)
            => string.IsNullOrWhiteSpace(src.NameRu) ? src.NameEn : src.NameRu;

        private void Delete(Episode episode, string reason)
        {
            _logger.LogInformation(
                "Removing virtual episode S{SeasonNumber}E{EpisodeNumber} in series {SeriesName}: {Reason}",
                episode.ParentIndexNumber,
                episode.IndexNumber,
                episode.SeriesName,
                reason);

            _libraryManager.DeleteItem(episode, new DeleteOptions { DeleteFileLocation = false }, false);
        }
    }
}
