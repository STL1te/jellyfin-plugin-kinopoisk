# AGENTS.md

Guide for coding agents working in this repository.

## What this is

A Jellyfin metadata plugin that fetches films, series and people from Kinopoisk through the
unofficial API at <https://kinopoiskapiunofficial.tech>. The audience is Russian-speaking, so
user-facing strings (plugin name, settings page, README, CHANGELOG) are in Russian. Code,
comments and commit messages are in English.

This repository is a fork of `LinFor/jellyfin-plugin-kinopoisk`; the published repository is
`STL1te/jellyfin-plugin-kinopoisk`.

## Layout

```
src/Jellyfin.Plugin.Kinopoisk/          the plugin itself
  MetadataProviders/                    IRemoteMetadataProvider implementations
  RemoteImageProviders/                 IRemoteImageProvider implementations
  SearchProviders/                      IExternalSearchProvider (Jellyfin 12+)
  SimilarItemsProviders/                IRemoteSimilarItemsProvider (Jellyfin 12+)
  ProviderIdResolvers/                  how an item is matched to a Kinopoisk id
  Model/                                external id and external url providers
  ApiModelExtensions.cs                 all API-model -> Jellyfin-entity mapping lives here
  Configuration/configPage.html         settings UI (Russian)
src/KinopoiskUnofficialInfo.ApiClient/  the API client
  GeneratedClient.generated.cs          NSwag output, do not hand-edit
  Patchers/                             Newtonsoft converters that survive the API's bad JSON
  KinopoiskApiClient.cs                 error handling policy lives here
  CachedApiClient.cs                    1-minute in-memory dedupe of identical calls
src/*.Tests/                            xunit; API tests replay VCR cassettes, no network needed
dist/manifest.json                      the plugin repository catalogue, updated by the release workflow
```

Providers are discovered by assembly scan, not registered by hand. `KinopoiskPluginServiceRegistrator`
only registers the API client and the id resolvers. Adding a provider class is enough for Jellyfin
to find it; the user still has to enable it per library in the library's metadata fetcher settings.

## Build and test

```bash
dotnet build src/Jellyfin.Plugin.Kinopoisk.sln
dotnet test  src/Jellyfin.Plugin.Kinopoisk.sln
```

Targets **.NET 10** and **Jellyfin 12.0**. Plugins built for 10.11 and earlier do not load on 12.0
and vice versa, so there is no supporting both from one build. Jellyfin's own sources are a useful
reference for provider interfaces; if a checkout exists next to this repository the
`kinopoisk.code-workspace` picks it up as a second folder.

## Regenerating the API client

`dotnet msbuild -target:GenerateApiClientSourceCode src/KinopoiskUnofficialInfo.ApiClient/...csproj`,
or the `regenerate-api-proxy` task. It downloads the live OpenAPI document and rewrites
`GeneratedClient.generated.cs`.

The NSwag toolchain is a .NET 7 executable. If no .NET 7 runtime is installed it will not start, and
a roll-forward is needed:

```bash
DOTNET_ROLL_FORWARD=LatestMajor DOTNET_ROLL_FORWARD_TO_PRERELEASE=1 \
  "$HOME/.nuget/packages/nswag.msbuild/13.18.2/tools/Net70/dotnet-nswag.exe" openapi2csclient ...
```

After regenerating, check two things: the image-type enum is still named `Type` (three
`GlobalUsings.cs` files alias it to `KinopoiskImageType`), and the type names the `Patchers/`
converters reference still exist.

## Error handling policy

Everything goes through `KinopoiskApiClient.InvokeOptional`, which turns the expected failures into
an empty result plus one warning line, never an exception:

- **404** — the film has no such data. Normal for most optional endpoints.
- **402** — the ApiToken is out of quota. The free tier is 500 requests a day.
- **transport failures and timeouts** — Kinopoisk drops TLS connections fairly often.

Letting these propagate aborts the whole item refresh and fills the server log with stack traces.
If you add an endpoint, route it through `InvokeOptional` too.

## Kinopoisk data quirks

These are not hypothetical; each one has cost a bug.

- Dates come both as `2019-02-13` and as full round-trip timestamps. Use `ParseDate`.
- `ratingAgeLimits` is `"age18"`, not `"18"`. `GetOfficialRating` strips the prefix.
- A film object dates the premiere as 1 January of the production year. Real dates are in
  `/distributions`.
- In `/distributions` the theatrical rows carry the dates but usually have no companies; the
  company names sit on the DVD/digital rows.
- `/seasons` episodes frequently have `nameRu` and `synopsis` null even for well-known series, and
  season 0 is a dumping ground for pilots and extras.
- `/relations` is mostly noise — for a popular film it is hundreds of `REFERENCES_IN` and `SPOOFED`
  entries. `/sequels_and_prequels` is the clean franchise list.
- `/films/collections` is a fixed set of editorial charts, not per-franchise box sets. Kinopoisk has
  no collection entity, so there is nothing to map a Jellyfin BoxSet onto.
- Episodes have no Kinopoisk id of their own. Virtual episodes are tagged with
  `Constants.VirtualEpisodeProviderId`, deliberately not the normal provider id.

## Jellyfin behaviour worth knowing

- `IRemoteSimilarItemsProvider` and `IExternalSearchProvider` both resolve to **local library
  items**. Neither can surface something the user does not own; Kinopoisk acts as a matching index.
- NFO files next to the media win over remote providers for the fields they define, so a field
  looking "wrong" may never have come from this plugin at all.
- The ApiToken is captured when the singleton client is built, so changing it in the settings needs
  a server restart.

## Releasing

Run the **Release** workflow manually from `master`. It takes the version from the top
`## [x.y.z.w]` heading in `CHANGELOG.md`, builds, creates the tag and GitHub release, and appends an
entry to `dist/manifest.json`, which it commits back to the branch it ran on — so run it from
`master`, not a feature branch. Bump `TARGET_ABI` in the workflow when the supported Jellyfin
version changes.

Release archives are not committed; older manifest entries point at the upstream forks' copies.

## Testing against a real server

A plugin folder is just the two DLLs plus `meta.json` dropped into
`<jellyfin-config>/plugins/Kinopoisk_<version>/`, owned by whatever user the server runs as, then
restart the server. Settings live separately in `plugins/configurations/Jellyfin.Plugin.Kinopoisk.xml`
and survive a reinstall.

Jellyfin 12 disabled `api_key` as a query parameter; use the header instead:

```
Authorization: MediaBrowser Token="<token>"
```
