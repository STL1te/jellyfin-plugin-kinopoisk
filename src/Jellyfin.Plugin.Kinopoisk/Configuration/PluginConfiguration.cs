using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Kinopoisk.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        // https://kinopoiskapiunofficial.tech/
        public string ApiToken { get; set; } = "85d30ae5-d875-4c5f-900d-8e37bb20625e";

        /// <summary>
        /// Gets or sets a value indicating whether episodes Kinopoisk knows about but that have not
        /// aired yet are created as virtual items. These populate the "Upcoming" view.
        /// </summary>
        public bool ImportUnairedEpisodes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether already aired episodes that have no file in the
        /// library are created as virtual items, so they show up as missing.
        /// </summary>
        public bool ImportMissingEpisodes { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether specials (season 0) take part in the above. Kinopoisk
        /// puts pilots and assorted extras there, so this is off by default.
        /// </summary>
        public bool ImportSpecials { get; set; }
    }
}
