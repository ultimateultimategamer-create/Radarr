using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieVersionTests
{
    [TestFixture]
    public class MovieVersionEditionNameFixture : CoreTest
    {
        private static readonly List<string> NoneTaken = new List<string>();

        [Test]
        public void should_use_the_version_name()
        {
            MovieVersionEditionName.Allocate("IMAX", NoneTaken).Should().Be("IMAX");
        }

        [Test]
        public void should_cut_a_name_longer_than_plex_allows()
        {
            MovieVersionEditionName.Allocate("Director's Cut Extended Unrated Special", NoneTaken).Should().Be("Director's Cut Extended Unrated");
        }

        [Test]
        public void should_leave_braces_out()
        {
            MovieVersionEditionName.Allocate("Cut {Final}", NoneTaken).Should().Be("Cut Final");
        }

        [Test]
        public void should_number_an_edition_that_is_taken()
        {
            MovieVersionEditionName.Allocate("imax", new List<string> { "IMAX" }).Should().Be("imax 2");
            MovieVersionEditionName.Allocate("IMAX", new List<string> { "IMAX", "IMAX 2" }).Should().Be("IMAX 3");
        }

        [Test]
        public void should_number_a_cut_name_within_the_length_plex_allows()
        {
            MovieVersionEditionName.Allocate("Director's Cut Extended Unrated Version", new List<string> { "Director's Cut Extended Unrated" })
                                   .Should().Be("Director's Cut Extended Unrate 2");
        }

        [Test]
        public void should_never_cut_through_a_character()
        {
            var name = "abcdefghijklmnopqrstuvwxyzABCDE\U0001F3AC";

            MovieVersionEditionName.Allocate(name, NoneTaken).Should().Be("abcdefghijklmnopqrstuvwxyzABCDE");
        }

        [Test]
        public void should_keep_a_character_that_fits()
        {
            var name = "abcdefghijklmnopqrstuvwxyzABCD\U0001F3AC and more";

            MovieVersionEditionName.Allocate(name, NoneTaken).Should().Be("abcdefghijklmnopqrstuvwxyzABCD\U0001F3AC");
        }

        [Test]
        public void should_be_empty_without_a_usable_name()
        {
            MovieVersionEditionName.Allocate("{}", NoneTaken).Should().BeEmpty();
        }

        [Test]
        public void should_never_be_longer_than_plex_allows()
        {
            var taken = new List<string>();

            for (var i = 0; i < 12; i++)
            {
                var edition = MovieVersionEditionName.Allocate("An Edition Name That Goes On Much Longer Than Plex Allows", taken);

                edition.Length.Should().BeLessOrEqualTo(MovieVersionEditionName.MaxLength);
                taken.Should().NotContain(edition);

                taken.Add(edition);
            }
        }

        [TestCase("Movie (2020) {edition-IMAX}.mkv", "IMAX")]
        [TestCase("Movie (2020) {Edition-Director's Cut} [Bluray-1080p].mkv", "Director's Cut")]
        [TestCase("Movie (2020) - IMAX.mkv", "")]
        [TestCase("", "")]
        public void should_read_the_edition_in_a_file_name(string fileName, string edition)
        {
            MovieVersionEditionName.FromFileName(fileName).Should().Be(edition);
        }

        [TestCase("Movie (2020) {edition-Theatrical}", "Movie (2020)")]
        [TestCase("Movie {Edition-Theatrical} (2020)", "Movie (2020)")]
        [TestCase("Movie (2020)", "Movie (2020)")]
        [TestCase("{edition-Theatrical}", "")]
        public void should_take_editions_out_of_a_name(string name, string withoutEditions)
        {
            MovieVersionEditionName.RemoveFromName(name).Should().Be(withoutEditions);
        }
    }
}
