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

        // Prefer more specific format rules, then narrower profiles with the same format rules.
        // Equally specific or independent matches still require a user to choose a version.
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
                Formats = (movie.Version?.RequiredCustomFormatIds ?? new List<int>()).ToHashSet(),
                Qualities = movie.QualityProfile?.Items?.Where(i => i.Allowed)
                                 .SelectMany(i => i.GetQualities()).Select(q => q.Id).ToHashSet()
            }).ToList();

            return requirements.Where(candidate => !requirements.Any(other =>
                                   other.Formats.IsProperSupersetOf(candidate.Formats) ||
                                   (other.Formats.SetEquals(candidate.Formats) && other.Qualities != null && candidate.Qualities != null &&
                                    other.Qualities.IsProperSubsetOf(candidate.Qualities))))
                               .Select(candidate => candidate.Movie)
                               .ToList();
        }
    }
}
