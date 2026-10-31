using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    public static class MovieVersionMatcher
    {
        public static bool Matches(Movie movie, Quality quality, IEnumerable<CustomFormat> customFormats)
        {
            var profile = movie.QualityProfile;

            if (profile?.Items != null && !profile.Items.Where(i => i.Allowed).SelectMany(i => i.GetQualities()).Contains(quality))
            {
                return false;
            }

            var matchedIds = (customFormats ?? Enumerable.Empty<CustomFormat>()).Select(f => f.Id).ToHashSet();

            return movie.Version?.RequiredCustomFormatIds?.All(matchedIds.Contains) ?? true;
        }

        // Prefer explicit rules to a general profile. Equal matches require a user to choose a version.
        public static List<Movie> BestMatches(IEnumerable<Movie> movies, Quality quality, IEnumerable<CustomFormat> customFormats)
        {
            var matches = movies.Where(m => Matches(m, quality, customFormats)).ToList();

            if (matches.Count < 2)
            {
                return matches;
            }

            var requirements = matches.Select(movie => new
            {
                Movie = movie,
                Formats = (movie.Version?.RequiredCustomFormatIds ?? new List<int>()).ToHashSet()
            }).ToList();

            return requirements.Where(candidate => !requirements.Any(other => other.Formats.IsProperSupersetOf(candidate.Formats)))
                               .Select(candidate => candidate.Movie)
                               .ToList();
        }
    }
}
