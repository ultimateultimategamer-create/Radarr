using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadApprovedReportsTests
{
    [TestFixture]
    public class DownloadApprovedVersionsFixture : CoreTest<ProcessDownloadDecisions>
    {
        private Movie _movie;
        private Movie _versionView;

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IPrioritizeDownloadDecision>()
                  .Setup(v => v.PrioritizeDecisionsForMovies(It.IsAny<List<DownloadDecision>>()))
                  .Returns<List<DownloadDecision>>(v => v);

            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.Tags = new HashSet<int>())
                                   .Build();

            _movie.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() };

            _versionView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 1, QualityProfileId = 2, Path = _movie.Path });
            _versionView.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() };
        }

        private RemoteMovie GetRemoteMovie(Movie movie, Quality quality, string title, ReleaseInfo release = null)
        {
            return new RemoteMovie
            {
                ParsedMovieInfo = new ParsedMovieInfo
                {
                    Quality = new QualityModel(quality),
                    Year = 1998,
                    MovieTitles = new List<string> { "A Movie" },
                },
                Movie = movie,
                Release = release ?? new ReleaseInfo
                {
                    PublishDate = DateTime.UtcNow,
                    Title = title,
                    Guid = title,
                    Size = 200,
                    DownloadProtocol = DownloadProtocol.Usenet
                }
            };
        }

        [Test]
        public async Task should_download_a_release_for_each_version_of_a_movie()
        {
            var decisions = new List<DownloadDecision>
            {
                new DownloadDecision(GetRemoteMovie(_movie, Quality.Bluray2160p, "A.Movie.1998.2160p.BluRay")),
                new DownloadDecision(GetRemoteMovie(_versionView, Quality.Bluray1080p, "A.Movie.1998.1080p.BluRay"))
            };

            await Subject.ProcessDecisions(decisions);

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 0), null), Times.Once());
            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 7), null), Times.Once());
        }

        [Test]
        public async Task should_only_download_one_release_per_version()
        {
            var decisions = new List<DownloadDecision>
            {
                new DownloadDecision(GetRemoteMovie(_versionView, Quality.Bluray1080p, "A.Movie.1998.1080p.BluRay")),
                new DownloadDecision(GetRemoteMovie(_versionView, Quality.WEBDL1080p, "A.Movie.1998.1080p.WEB-DL"))
            };

            await Subject.ProcessDecisions(decisions);

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_not_download_the_same_release_for_two_versions()
        {
            var primary = GetRemoteMovie(_movie, Quality.Bluray1080p, "A.Movie.1998.1080p.BluRay");
            var additional = GetRemoteMovie(_versionView, Quality.Bluray1080p, null, primary.Release);

            var decisions = new List<DownloadDecision>
            {
                new DownloadDecision(primary),
                new DownloadDecision(additional)
            };

            await Subject.ProcessDecisions(decisions);

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }

        [Test]
        public async Task should_not_download_a_release_with_the_same_title_from_another_indexer_for_another_version()
        {
            var primary = GetRemoteMovie(_movie, Quality.Bluray1080p, "A.Movie.1998.1080p.BluRay");
            var additional = GetRemoteMovie(_versionView, Quality.Bluray1080p, "A.Movie.1998.1080p.BluRay");
            additional.Release.Guid = "another-indexer-guid";
            additional.Release.IndexerId = 2;

            var decisions = new List<DownloadDecision>
            {
                new DownloadDecision(primary),
                new DownloadDecision(additional)
            };

            await Subject.ProcessDecisions(decisions);

            Mocker.GetMock<IDownloadService>().Verify(v => v.DownloadReport(It.IsAny<RemoteMovie>(), null), Times.Once());
        }
    }
}
