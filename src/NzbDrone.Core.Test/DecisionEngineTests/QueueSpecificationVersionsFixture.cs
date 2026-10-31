using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class QueueSpecificationVersionsFixture : CoreTest<QueueSpecification>
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

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(x => x.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat>());
        }

        private RemoteMovie GetRemoteMovie(Movie movie, Quality quality, string title)
        {
            return new RemoteMovie
            {
                Movie = movie,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(quality) },
                Release = new ReleaseInfo { Title = title },
                CustomFormats = new List<CustomFormat>()
            };
        }

        private void GivenQueue(params RemoteMovie[] remoteMovies)
        {
            var queue = remoteMovies.Select(remoteMovie => new Queue.Queue
            {
                Title = remoteMovie.Release.Title,
                RemoteMovie = remoteMovie,
                TrackedDownloadState = TrackedDownloadState.Downloading
            });

            Mocker.GetMock<IQueueService>()
                  .Setup(s => s.GetQueue())
                  .Returns(queue.ToList());
        }

        [Test]
        public void should_not_be_blocked_by_a_download_for_another_version()
        {
            GivenQueue(GetRemoteMovie(_movie, Quality.Bluray2160p, "Movie.2016.2160p.BluRay"));

            var subject = GetRemoteMovie(_versionView, Quality.Bluray1080p, "Movie.2016.1080p.BluRay");

            Subject.IsSatisfiedBy(subject, new ReleaseDecisionInformation()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_be_blocked_by_a_better_download_for_the_same_version()
        {
            GivenQueue(GetRemoteMovie(_versionView, Quality.Bluray1080p, "Movie.2016.1080p.BluRay"));

            var subject = GetRemoteMovie(_versionView, Quality.WEBDL720p, "Movie.2016.720p.WEB-DL");

            Subject.IsSatisfiedBy(subject, new ReleaseDecisionInformation()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_the_same_release_already_downloading_for_another_version()
        {
            GivenQueue(GetRemoteMovie(_versionView, Quality.Bluray1080p, "Movie.2016.1080p.BluRay"));

            var subject = GetRemoteMovie(_movie, Quality.Bluray1080p, "Movie.2016.1080p.BluRay");

            var decision = Subject.IsSatisfiedBy(subject, new ReleaseDecisionInformation());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.QueueSameReleaseOtherVersion);
        }
    }
}
