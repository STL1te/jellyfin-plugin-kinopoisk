using System;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Kinopoisk
{
    public static class Constants
    {
        public const string PluginName = "КиноПоиск";
        public const string PluginDescription = "Информация о фильмах и сериалах с КиноПоиска";
        public const string ProviderId = "kinopoisk";

        /// <summary>
        /// Marks the metadata-only episodes the plugin creates for unaired and missing entries.
        /// Kinopoisk gives episodes no id of their own, so this cannot be <see cref="ProviderId"/> -
        /// that would put a value there that looks like a Kinopoisk film id but is not one.
        /// </summary>
        public const string VirtualEpisodeProviderId = "kinopoisk-virtual-episode";
        public const string ProviderName = "КиноПоиск";
        public const string ProviderMetadataLanguage = "ru";
    }
}
