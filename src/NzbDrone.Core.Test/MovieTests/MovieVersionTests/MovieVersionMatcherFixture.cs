using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.MovieTests.MovieVersionTests
{
    [TestFixture]
    public class MovieVersionMatcherFixture
    {
        private Movie CreateVersion(string name, params int[] formats)
        {
            return new Movie
            {
                Version = new MovieVersion { Name = name, RequiredCustomFormatIds = new List<int>(formats) },
                QualityProfile = new QualityProfile
                {
                    Items = new List<QualityProfileQualityItem>
                    {
                        new QualityProfileQualityItem { Quality = Quality.Bluray1080p, Allowed = true },
                        new QualityProfileQualityItem { Quality = Quality.Bluray2160p, Allowed = false }
                    }
                }
            };
        }

        [Test]
        public void should_require_all_selected_custom_formats()
        {
            var version = CreateVersion("Director's Cut", 3, 4);

            MovieVersionMatcher.Matches(version, Quality.Bluray1080p, new[] { new CustomFormat { Id = 3 } }).Should().BeFalse();
            MovieVersionMatcher.Matches(version, Quality.Bluray1080p, new[] { new CustomFormat { Id = 3 }, new CustomFormat { Id = 4 } }).Should().BeTrue();
        }

        [Test]
        public void should_not_assign_a_disallowed_quality_to_a_version()
        {
            MovieVersionMatcher.Matches(CreateVersion("1080p"), Quality.Bluray2160p, null).Should().BeFalse();
        }

        [Test]
        public void should_prefer_explicit_rules_over_an_unrestricted_version()
        {
            var original = CreateVersion("Original");
            var directorsCut = CreateVersion("Director's Cut", 3);

            MovieVersionMatcher.BestMatches(new[] { original, directorsCut }, Quality.Bluray1080p, new[] { new CustomFormat { Id = 3 } })
                               .Should().Equal(directorsCut);
        }

        [Test]
        public void should_not_choose_between_independent_matching_rules_by_their_count()
        {
            var directorsCut = CreateVersion("Director's Cut", 1);
            var frenchRemaster = CreateVersion("French Remaster", 2, 3);

            MovieVersionMatcher.BestMatches(
                new[] { directorsCut, frenchRemaster },
                Quality.Bluray1080p,
                new[] { new CustomFormat { Id = 1 }, new CustomFormat { Id = 2 }, new CustomFormat { Id = 3 } })
                .Should().HaveCount(2);
        }

        [Test]
        public void should_prefer_a_narrower_quality_profile_to_any_when_the_rules_are_the_same()
        {
            var any = CreateVersion("Any");
            any.QualityProfile.Items[1].Allowed = true;
            var hd = CreateVersion("HD");

            MovieVersionMatcher.BestMatches(new[] { any, hd }, Quality.Bluray1080p, null)
                               .Should().Equal(hd);
        }

        [Test]
        public void should_not_choose_between_different_profiles_with_the_same_allowed_qualities()
        {
            MovieVersionMatcher.BestMatches(new[] { CreateVersion("Theatrical"), CreateVersion("Extended") }, Quality.Bluray1080p, null)
                               .Should().HaveCount(2);
        }

        [Test]
        public void should_keep_ambiguous_matches_for_manual_assignment()
        {
            MovieVersionMatcher.BestMatches(new[] { CreateVersion("Theatrical"), CreateVersion("Remaster") }, Quality.Bluray1080p, null)
                               .Should().HaveCount(2);
        }
    }
}
