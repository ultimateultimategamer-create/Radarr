using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Movies.Versions
{
    public class MovieVersion : ModelBase
    {
        public int MovieId { get; set; }
        public string Name { get; set; }
        public int QualityProfileId { get; set; }
        public List<int> RequiredCustomFormatIds { get; set; } = new List<int>();

        // Added by hand from an interactive search or a manual import: it only gets the files picked for it,
        // and is never searched for, upgraded or given files automatically.
        public bool Pinned { get; set; }

        public string Path { get; set; }
        public int MovieFileId { get; set; }

        public bool HasFile => MovieFileId > 0;

        public override string ToString()
        {
            return string.Format("[{0}] Movie {1} ({2})", Id, MovieId, Path);
        }
    }
}
