using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.DecisionEngine.Specifications.RssSync;
using NzbDrone.Core.History;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class HistorySpecificationVersionsFixture : CoreTest<HistorySpecification>
    {
        private Movie _movie;
        private Movie _versionView;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();

            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.QualityProfile = new QualityProfile
                                   {
                                       Items = Qualities.QualityFixture.GetDefaultQualities(),
                                       Cutoff = Quality.Bluray2160p.Id,
                                       UpgradeAllowed = true
                                   })
                                   .Build();

            _versionView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 1, QualityProfileId = 2, Path = _movie.Path });
            _versionView.QualityProfile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(),
                Cutoff = Quality.Bluray1080p.Id,
                UpgradeAllowed = true
            };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(It.IsAny<MovieHistory>(), It.IsAny<Movie>()))
                  .Returns(new List<CustomFormat>());

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAdditionalVersionIds(1))
                  .Returns(new List<int> { 7 });

            // A recent grab of a 1080p release for the additional version
            var grab = new MovieHistory
            {
                MovieId = 1,
                EventType = MovieHistoryEventType.Grabbed,
                Date = DateTime.UtcNow.AddMinutes(-5),
                Quality = new QualityModel(Quality.Bluray1080p)
            };

            grab.Data[MovieHistory.MOVIE_VERSION_ID] = "7";

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByMovieId(1))
                  .Returns(new List<MovieHistory> { grab });
        }

        private RemoteMovie GetRemoteMovie(Movie movie, Quality quality)
        {
            return new RemoteMovie
            {
                Movie = movie,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(quality) },
                CustomFormats = new List<CustomFormat>()
            };
        }

        [Test]
        public void should_not_be_blocked_by_a_recent_grab_for_another_version()
        {
            Subject.IsSatisfiedBy(GetRemoteMovie(_movie, Quality.Bluray2160p), new ReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_blocked_by_a_recent_grab_for_the_same_version()
        {
            Subject.IsSatisfiedBy(GetRemoteMovie(_versionView, Quality.WEBDL1080p), new ReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }
    }
}
