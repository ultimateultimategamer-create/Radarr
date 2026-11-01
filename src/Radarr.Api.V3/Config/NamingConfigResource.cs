using NzbDrone.Core.Organizer;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Config
{
    public class NamingConfigResource : RestResource
    {
        public bool RenameMovies { get; set; }
        public bool ReplaceIllegalCharacters { get; set; }
        public ColonReplacementFormat ColonReplacementFormat { get; set; }
        public string StandardMovieFormat { get; set; }
        public string MovieFolderFormat { get; set; }

        // Left out by clients that predate it: the saved setting is kept
        public ExtraVersionNaming? ExtraVersionNaming { get; set; }
    }
}
