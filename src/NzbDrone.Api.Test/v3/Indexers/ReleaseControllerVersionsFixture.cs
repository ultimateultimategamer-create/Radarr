using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Download;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;
using Radarr.Api.V3.Indexers;

namespace NzbDrone.Api.Test.v3.Indexers
{
    [TestFixture]
    public class ReleaseControllerVersionsFixture : TestBase<ReleaseController>
    {
        private Movie _movie;
        private Movie _additional;
        private RemoteMovie _cached;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 5 };
            _additional = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 5, Name = "Director's Cut" });
            _cached = new RemoteMovie { Movie = _additional };

            var manager = new CacheManager();
            manager.GetCache<RemoteMovie>(typeof(ReleaseController), "remoteMovies")
                   .Set("1_release", _cached, TimeSpan.FromMinutes(30));
            Mocker.SetConstant<ICacheManager>(manager);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(5)).Returns(_movie);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.GetVersionView(_movie, 7)).Returns(_additional);
            Mocker.GetMock<IDownloadService>().Setup(s => s.DownloadReport(It.IsAny<RemoteMovie>(), null)).Returns(Task.CompletedTask);
        }

        [Test]
        public async Task should_honor_the_primary_target_after_another_version_was_searched()
        {
            await Subject.DownloadRelease(new ReleaseResource { IndexerId = 1, Guid = "release", MovieId = 5, MovieVersionId = 0 });

            Mocker.GetMock<IDownloadService>()
                  .Verify(s => s.DownloadReport(It.Is<RemoteMovie>(r => r.Movie == _movie), null), Times.Once());
            _cached.Movie.Should().BeSameAs(_additional);
        }

        [Test]
        public async Task should_honor_an_additional_target_without_mutating_the_cached_release()
        {
            _cached.Movie = _movie;

            await Subject.DownloadRelease(new ReleaseResource { IndexerId = 1, Guid = "release", MovieId = 5, MovieVersionId = 7 });

            Mocker.GetMock<IDownloadService>()
                  .Verify(s => s.DownloadReport(It.Is<RemoteMovie>(r => r.Movie == _additional), null), Times.Once());
            _cached.Movie.Should().BeSameAs(_movie);
        }

        [Test]
        public async Task should_grab_a_release_added_as_a_new_pinned_version()
        {
            var pinned = new MovieVersion { Id = 8, MovieId = 5, Name = "IMAX", Pinned = true };
            var pinnedView = _movie.ForVersion(pinned);
            _cached.Movie = _movie;
            _cached.ParsedMovieInfo = new ParsedMovieInfo { Edition = "IMAX" };

            Mocker.GetMock<IMovieVersionService>().Setup(s => s.AddPinnedVersion(_movie, null, "IMAX")).Returns(pinned);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.GetVersionView(_movie, 8)).Returns(pinnedView);

            await Subject.DownloadRelease(new ReleaseResource { IndexerId = 1, Guid = "release", MovieId = 5, AddAsMovieVersion = true });

            Mocker.GetMock<IDownloadService>()
                  .Verify(s => s.DownloadReport(It.Is<RemoteMovie>(r => r.Movie == pinnedView), null), Times.Once());
            Mocker.GetMock<IMovieVersionService>().Verify(s => s.RemoveVersion(It.IsAny<int>()), Times.Never());
            _cached.Movie.Should().BeSameAs(_movie);
        }

        [Test]
        public void should_remove_the_new_version_when_the_release_cannot_be_grabbed()
        {
            var pinned = new MovieVersion { Id = 8, MovieId = 5, Name = "Director's Cut", Pinned = true };
            _cached.Movie = _movie;

            Mocker.GetMock<IMovieVersionService>().Setup(s => s.AddPinnedVersion(_movie, "Director's Cut", null)).Returns(pinned);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.GetVersionView(_movie, 8)).Returns(_movie.ForVersion(pinned));
            Mocker.GetMock<IDownloadService>()
                  .Setup(s => s.DownloadReport(It.IsAny<RemoteMovie>(), null))
                  .ThrowsAsync(new InvalidOperationException("Download client unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(() => Subject.DownloadRelease(
                new ReleaseResource { IndexerId = 1, Guid = "release", MovieId = 5, AddAsMovieVersion = true, MovieVersionName = "Director's Cut" }));

            Mocker.GetMock<IMovieVersionService>().Verify(s => s.RemoveVersion(8), Times.Once());
        }

        [Test]
        public void should_not_grab_for_a_removed_version()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetVersionView(_movie, 7))
                  .Throws(new NzbDroneClientException(HttpStatusCode.Conflict, "Removed version"));

            Assert.ThrowsAsync<NzbDroneClientException>(() => Subject.DownloadRelease(
                new ReleaseResource { IndexerId = 1, Guid = "release", MovieId = 5, MovieVersionId = 7 }));

            Mocker.GetMock<IDownloadService>()
                  .Verify(s => s.DownloadReport(It.IsAny<RemoteMovie>(), It.IsAny<int?>()), Times.Never());
        }
    }
}
