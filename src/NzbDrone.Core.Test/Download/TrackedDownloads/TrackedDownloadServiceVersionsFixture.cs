using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceVersionsFixture : CoreTest<TrackedDownloadService>
    {
        private Movie _movie;
        private Movie _versionView;
        private DownloadClientDefinition _client;
        private DownloadClientItem _item;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 3, Path = "/Movies/A Movie (1998)" };
            _versionView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 3, QualityProfileId = 2, Path = _movie.Path });

            var remoteMovie = new RemoteMovie
            {
                Movie = _movie,
                ParsedMovieInfo = new ParsedMovieInfo
                {
                    MovieTitles = new List<string> { "A Movie" },
                    Year = 1998
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedMovieInfo>(i => i.PrimaryMovieTitle == "A Movie"), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(remoteMovie);

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetVersionView(It.IsAny<Movie>(), 7))
                  .Returns(_versionView);

            _client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            _item = new DownloadClientItem
            {
                Title = "A Movie 1998",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = _client.Protocol,
                    Id = _client.Id,
                    Name = _client.Name
                }
            };
        }

        private void GivenGrabHistory(string movieVersionId)
        {
            var grab = new MovieHistory
            {
                DownloadId = "35238",
                SourceTitle = "A Movie 1998",
                MovieId = 3,
                EventType = MovieHistoryEventType.Grabbed
            };

            if (movieVersionId != null)
            {
                grab.Data.Add(MovieHistory.MOVIE_VERSION_ID, movieVersionId);
            }

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId("35238"))
                  .Returns(new List<MovieHistory> { grab });
        }

        [Test]
        public void should_track_download_for_the_version_it_was_grabbed_for()
        {
            GivenGrabHistory("7");

            var trackedDownload = Subject.TrackDownload(_client, _item);

            trackedDownload.RemoteMovie.Movie.MovieVersionId.Should().Be(7);
        }

        [Test]
        public void should_require_manual_import_when_the_grabbed_version_has_been_removed()
        {
            GivenGrabHistory("7");
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetVersionView(It.IsAny<Movie>(), 7))
                  .Throws(new NzbDroneClientException(System.Net.HttpStatusCode.Conflict, "Removed version"));

            var trackedDownload = Subject.TrackDownload(_client, _item);

            trackedDownload.RemoteMovie.Movie.Should().BeNull();
        }

        [Test]
        public void should_track_download_for_the_movie_when_grabbed_for_its_primary_version()
        {
            GivenGrabHistory(null);

            var trackedDownload = Subject.TrackDownload(_client, _item);

            trackedDownload.RemoteMovie.Movie.Should().BeSameAs(_movie);

            Mocker.GetMock<IMovieVersionService>()
                  .Verify(s => s.GetVersionView(It.IsAny<Movie>(), It.IsAny<int>()), Times.Never());
        }
    }
}
