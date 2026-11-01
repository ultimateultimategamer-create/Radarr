using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    public class PlexMoviePart
    {
        public string File { get; set; }
    }

    public class PlexMovieMedia
    {
        public PlexMovieMedia()
        {
            Parts = new List<PlexMoviePart>();
        }

        [JsonProperty("Part")]
        public List<PlexMoviePart> Parts { get; set; }
    }

    public class PlexMovieField
    {
        public string Name { get; set; }

        // true, or 1 as in Plex's XML
        public string Locked { get; set; }

        public bool IsLocked => Locked == "1" || "true".Equals(Locked, StringComparison.OrdinalIgnoreCase);
    }

    // A movie item of a Plex library: an edition of a movie is an item of its own
    public class PlexMovie
    {
        public PlexMovie()
        {
            Media = new List<PlexMovieMedia>();
            Fields = new List<PlexMovieField>();
        }

        public string RatingKey { get; set; }
        public string Guid { get; set; }
        public string Title { get; set; }
        public string EditionTitle { get; set; }
        public int Year { get; set; }

        [JsonProperty("Media")]
        public List<PlexMovieMedia> Media { get; set; }

        // Fields locked against metadata refreshes, as edited by hand
        [JsonProperty("Field")]
        public List<PlexMovieField> Fields { get; set; }

        public List<string> GetFiles()
        {
            return (Media ?? new List<PlexMovieMedia>())
                .Where(m => m?.Parts != null)
                .SelectMany(m => m.Parts)
                .Where(p => p?.File != null)
                .Select(p => p.File)
                .ToList();
        }

        public bool IsLocked(string field)
        {
            return (Fields ?? new List<PlexMovieField>()).Any(f => f != null && f.IsLocked && f.Name == field);
        }
    }

    public class PlexMoviesContainer
    {
        public PlexMoviesContainer()
        {
            Movies = new List<PlexMovie>();
        }

        [JsonProperty("Metadata")]
        public List<PlexMovie> Movies { get; set; }
    }
}
