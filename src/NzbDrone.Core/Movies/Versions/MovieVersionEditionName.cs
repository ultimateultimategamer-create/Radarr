using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Movies.Versions
{
    // Editions extra versions' files are named with for Plex, "{edition-<name>}". Plex allows 32 characters, so a longer
    // name is cut, never inside a character, and a number keeps an edition apart from those already taken.
    public static class MovieVersionEditionName
    {
        public const int MaxLength = 32;

        private static readonly Regex EditionTagRegex = new Regex(@"\{edition-(?<edition>[^{}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex MultipleSpacesRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        // Empty when the name has nothing usable
        public static string Allocate(string name, IEnumerable<string> taken)
        {
            var takenEditions = new HashSet<string>(taken ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            // Braces would end Plex's edition tag early
            var clean = FileNameBuilder.CleanFileName(name ?? string.Empty).Replace("{", string.Empty).Replace("}", string.Empty).Trim();
            var edition = Shorten(clean, MaxLength);

            if (edition.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var number = 2;

            while (takenEditions.Contains(edition))
            {
                var suffix = $" {number++}";
                var start = Shorten(clean, MaxLength - suffix.Length);

                if (start.IsNullOrWhiteSpace())
                {
                    return string.Empty;
                }

                edition = start + suffix;
            }

            return edition;
        }

        // The edition in a file's name, empty when it has none
        public static string FromFileName(string fileName)
        {
            if (fileName.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var match = EditionTagRegex.Match(fileName);

            return match.Success ? match.Groups["edition"].Value.Trim() : string.Empty;
        }

        public static string RemoveFromName(string name)
        {
            return name.IsNullOrWhiteSpace() ? name : MultipleSpacesRegex.Replace(EditionTagRegex.Replace(name, " "), " ").Trim();
        }

        // Whole characters only, so a cut never splits one, like an emoji's surrogate pair or a letter and its accent
        private static string Shorten(string name, int length)
        {
            if (name.Length <= length)
            {
                return name;
            }

            var shortened = new StringBuilder();
            var characters = StringInfo.GetTextElementEnumerator(name);

            while (characters.MoveNext())
            {
                var character = characters.GetTextElement();

                if (shortened.Length + character.Length > length)
                {
                    break;
                }

                shortened.Append(character);
            }

            return shortened.ToString().TrimEnd(' ', '-', '.', ',', '_');
        }
    }
}
