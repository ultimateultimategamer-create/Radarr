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
        private static MovieVersion Version(int id, string name)
        {
            return new MovieVersion { Id = id, MovieId = 5, Name = name };
        }

        [Test]
        public void should_use_the_version_name()
        {
            var version = Version(7, "IMAX");

            MovieVersionEditionName.Get(version, new List<MovieVersion> { version }).Should().Be("IMAX");
        }

        [Test]
        public void should_cut_a_name_longer_than_plex_allows()
        {
            var version = Version(7, "Director's Cut Extended Unrated Special");

            MovieVersionEditionName.Get(version, new List<MovieVersion> { version }).Should().Be("Director's Cut Extended Unrated");
        }

        [Test]
        public void should_leave_braces_out()
        {
            var version = Version(7, "Cut {Final}");

            MovieVersionEditionName.Get(version, new List<MovieVersion> { version }).Should().Be("Cut Final");
        }

        [Test]
        public void should_number_editions_that_are_alike_once_cut()
        {
            var first = Version(7, "Director's Cut Extended Unrated Special");
            var second = Version(9, "Director's Cut Extended Unrated Version");
            var versions = new List<MovieVersion> { first, second };

            MovieVersionEditionName.Get(first, versions).Should().Be("Director's Cut Extended Unrated");
            MovieVersionEditionName.Get(second, versions).Should().Be("Director's Cut Extended Unrate 2");
        }

        [Test]
        public void should_keep_the_edition_of_a_version_when_a_later_one_is_added()
        {
            var first = Version(7, "Director's Cut Extended Unrated Special");
            var second = Version(9, "Director's Cut Extended Unrated Version");

            MovieVersionEditionName.Get(first, new List<MovieVersion> { first, second })
                                   .Should().Be(MovieVersionEditionName.Get(first, new List<MovieVersion> { first }));
        }

        [Test]
        public void should_be_empty_without_a_usable_name()
        {
            var version = Version(7, "{}");

            MovieVersionEditionName.Get(version, new List<MovieVersion> { version }).Should().BeEmpty();
        }

        [Test]
        public void should_never_be_longer_than_plex_allows()
        {
            var versions = new List<MovieVersion>();

            for (var id = 1; id <= 12; id++)
            {
                versions.Add(Version(id, "An Edition Name That Goes On Much Longer Than Plex Allows"));
            }

            foreach (var version in versions)
            {
                MovieVersionEditionName.Get(version, versions).Length.Should().BeLessOrEqualTo(MovieVersionEditionName.MaxLength);
            }
        }
    }
}
