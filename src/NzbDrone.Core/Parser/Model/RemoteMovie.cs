using System.Collections.Generic;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Parser.Model
{
    public class RemoteMovie
    {
        public ReleaseInfo Release { get; set; }
        public ParsedMovieInfo ParsedMovieInfo { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public MovieMatchType MovieMatchType { get; set; }
        public Movie Movie { get; set; }
        public bool MovieRequested { get; set; }
        public bool DownloadAllowed { get; set; }
        public TorrentSeedConfiguration SeedConfiguration { get; set; }
        public List<Language> Languages { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }

        public RemoteMovie()
        {
            CustomFormats = new List<CustomFormat>();
            Languages = new List<Language>();
        }

        // A copy of the release for another version of the same movie, so each version is evaluated on its own
        public RemoteMovie CloneForMovie(Movie movie)
        {
            return new RemoteMovie
            {
                Release = Release,
                ParsedMovieInfo = ParsedMovieInfo,
                CustomFormats = CustomFormats,
                CustomFormatScore = CustomFormatScore,
                MovieMatchType = MovieMatchType,
                Movie = movie,
                MovieRequested = MovieRequested,
                DownloadAllowed = DownloadAllowed,
                SeedConfiguration = SeedConfiguration,
                Languages = Languages,
                ReleaseSource = ReleaseSource
            };
        }

        public override string ToString()
        {
            return Release.Title;
        }
    }

    public enum ReleaseSourceType
    {
        Unknown = 0,
        Rss = 1,
        Search = 2,
        UserInvokedSearch = 3,
        InteractiveSearch = 4,
        ReleasePush = 5
    }
}
