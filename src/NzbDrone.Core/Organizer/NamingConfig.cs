using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfig : ModelBase
    {
        public static NamingConfig Default => new NamingConfig
        {
            RenameMovies = false,
            ReplaceIllegalCharacters = true,
            ColonReplacementFormat = ColonReplacementFormat.Smart,
            MovieFolderFormat = "{Movie Title} ({Release Year})",
            StandardMovieFormat = "{Movie Title} ({Release Year}) {Quality Full}",
            ExtraVersionNaming = ExtraVersionNaming.PlexEdition,
        };

        public bool RenameMovies { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardMovieFormat { get; set; }
        public string MovieFolderFormat { get; set; }
        public ExtraVersionNaming ExtraVersionNaming { get; set; }
    }
}
