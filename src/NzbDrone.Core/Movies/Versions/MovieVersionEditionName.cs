using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Movies.Versions
{
    // The edition an extra version's files are named with for Plex, "{edition-<name>}". Plex allows 32 characters, so a
    // longer name is cut, and a number keeps a movie's editions apart where cut names are alike. Editions are handed out in
    // the order versions were added, so adding a version never changes the edition of an earlier one.
    // Empty when the version's name has nothing usable.
    public static class MovieVersionEditionName
    {
        public const int MaxLength = 32;

        // extraVersions: the movie's extra versions, not its primary version
        public static string Get(MovieVersion version, IEnumerable<MovieVersion> extraVersions)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var earlier in (extraVersions ?? Enumerable.Empty<MovieVersion>()).Where(v => v.Id < version.Id).OrderBy(v => v.Id))
            {
                Pick(earlier.Name, taken);
            }

            return Pick(version.Name, taken);
        }

        private static string Pick(string name, HashSet<string> taken)
        {
            // Braces would end Plex's edition tag early
            var clean = FileNameBuilder.CleanFileName(name ?? string.Empty).Replace("{", string.Empty).Replace("}", string.Empty).Trim();
            var edition = Shorten(clean, MaxLength);

            if (edition.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var number = 2;

            while (taken.Contains(edition))
            {
                var suffix = $" {number++}";

                edition = Shorten(clean, MaxLength - suffix.Length) + suffix;
            }

            taken.Add(edition);

            return edition;
        }

        private static string Shorten(string name, int length)
        {
            return name.Length <= length ? name : name.Substring(0, length).TrimEnd(' ', '-', '.', ',', '_');
        }
    }
}
