using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using KinopoiskUnofficialInfo.ApiClient;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk.SearchProviders
{
    /// <summary>
    /// Lets Jellyfin's search find library items through Kinopoisk's index (Jellyfin 12+).
    /// Jellyfin itself matches on the titles it stored, so a film filed under its Russian name is
    /// invisible to a search for the original one, and vice versa - Kinopoisk knows both.
    /// </summary>
    public class KinopoiskSearchProvider : IExternalSearchProvider
    {
        private readonly IKinopoiskApiClient _apiClient;
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<KinopoiskSearchProvider> _logger;

        public KinopoiskSearchProvider(IKinopoiskApiClient kinopoiskApiClient, ILibraryManager libraryManager, ILogger<KinopoiskSearchProvider> logger)
        {
            _apiClient = kinopoiskApiClient ?? throw new ArgumentNullException(nameof(kinopoiskApiClient));
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => Constants.ProviderName;

        public MetadataPluginType Type => MetadataPluginType.MetadataFetcher;

        // Runs behind Jellyfin's own search: a local title match is always the better answer, this
        // only adds the items that match under a title Jellyfin never stored.
        public int Priority => 100;

        public bool CanSearch(SearchProviderQuery query)
        {
            if (string.IsNullOrWhiteSpace(query?.SearchTerm))
                return false;

            // Kinopoisk only indexes films, series and people.
            return IsWanted(query, BaseItemKind.Movie)
                || IsWanted(query, BaseItemKind.Series)
                || IsWanted(query, BaseItemKind.Person);
        }

        public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchProviderQuery query, CancellationToken cancellationToken)
        {
            var res = new List<SearchResult>();
            await foreach (var item in ((IExternalSearchProvider)this).SearchAsync(query, cancellationToken).WithCancellation(cancellationToken))
                res.Add(item);

            return res;
        }

        async IAsyncEnumerable<SearchResult> IExternalSearchProvider.SearchAsync(
            SearchProviderQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var limit = query.Limit ?? 20;
            var emitted = 0;

            foreach (var (kinopoiskId, kind, rank) in await LookUp(query, cancellationToken))
            {
                if (emitted >= limit || cancellationToken.IsCancellationRequested)
                    yield break;

                var item = FindLocalItem(query, kinopoiskId, kind);
                if (item is null)
                    continue;

                // Kinopoisk returns its hits best-first; keep that order and stay below the score a
                // direct local title match gets.
                yield return new SearchResult(item.Id, 0.5f / (1 + rank));
                emitted++;
            }
        }

        private async Task<IReadOnlyList<(string KinopoiskId, BaseItemKind Kind, int Rank)>> LookUp(SearchProviderQuery query, CancellationToken cancellationToken)
        {
            var res = new List<(string, BaseItemKind, int)>();

            try
            {
                if (IsWanted(query, BaseItemKind.Movie) || IsWanted(query, BaseItemKind.Series))
                {
                    var films = await _apiClient.SearchByKeyword(query.SearchTerm, cancellationToken: cancellationToken);
                    res.AddRange((films?.Films ?? Enumerable.Empty<FilmSearchResponse_films>())
                        .Select((f, i) => (f.FilmId.ToString(CultureInfo.InvariantCulture), ToItemKind(f.Type), i)));
                }

                if (IsWanted(query, BaseItemKind.Person))
                {
                    var persons = await _apiClient.SearchPersonByName(query.SearchTerm, cancellationToken: cancellationToken);
                    res.AddRange((persons?.Items ?? Enumerable.Empty<PersonByNameResponse_items>())
                        .Select((p, i) => (p.KinopoiskId.ToString(CultureInfo.InvariantCulture), BaseItemKind.Person, i)));
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Kinopoisk search for '{SearchTerm}' failed", query.SearchTerm);
            }

            return res;
        }

        private BaseItem FindLocalItem(SearchProviderQuery query, string kinopoiskId, BaseItemKind kind)
        {
            if (!IsWanted(query, kind))
                return null;

            var internalQuery = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { kind },
                Limit = 1,
                DtoOptions = new MediaBrowser.Controller.Dto.DtoOptions(false),
                HasAnyProviderId = new Dictionary<string, string> { [Constants.ProviderId] = kinopoiskId }
            };

            if (query.ParentId.HasValue && !query.ParentId.Value.Equals(Guid.Empty))
                internalQuery.AncestorIds = new[] { query.ParentId.Value };

            return _libraryManager.GetItemList(internalQuery).FirstOrDefault();
        }

        private static bool IsWanted(SearchProviderQuery query, BaseItemKind kind)
        {
            if (query.IncludeItemTypes.Length > 0)
                return query.IncludeItemTypes.Contains(kind);

            return !query.ExcludeItemTypes.Contains(kind);
        }

        private static BaseItemKind ToItemKind(FilmSearchResponse_filmsType? type)
            => type switch
            {
                FilmSearchResponse_filmsType.TV_SERIES
                    or FilmSearchResponse_filmsType.MINI_SERIES
                    or FilmSearchResponse_filmsType.TV_SHOW => BaseItemKind.Series,
                _ => BaseItemKind.Movie,
            };
    }
}
