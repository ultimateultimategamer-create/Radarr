using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class RedownloadFailedDownloadServiceFixture : CoreTest<RedownloadFailedDownloadService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.AutoRedownloadFailed)
                  .Returns(true);
        }

        private static DownloadFailedEvent GivenFailedDownload(int movieVersionId)
        {
            var message = new DownloadFailedEvent { MovieId = 1 };

            if (movieVersionId > 0)
            {
                message.Data[MovieHistory.MOVIE_VERSION_ID] = movieVersionId.ToString();
            }

            return message;
        }

        private void VerifyMovieSearched(Times times)
        {
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Push(It.Is<MoviesSearchCommand>(c => c.MovieIds.Contains(1)), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), times);
        }

        [Test]
        public void should_search_the_movie_again_when_a_download_for_its_main_version_fails()
        {
            Subject.Handle(GivenFailedDownload(0));

            VerifyMovieSearched(Times.Once());
        }

        [Test]
        public void should_not_search_again_when_a_download_for_a_pinned_version_fails()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.Find(8))
                  .Returns(new MovieVersion { Id = 8, MovieId = 1, Pinned = true });

            Subject.Handle(GivenFailedDownload(8));

            VerifyMovieSearched(Times.Never());
        }

        [Test]
        public void should_not_search_again_when_the_version_of_a_failed_download_was_removed()
        {
            Subject.Handle(GivenFailedDownload(9));

            VerifyMovieSearched(Times.Never());
        }

        [Test]
        public void should_search_again_when_a_download_for_an_automatic_version_fails()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.Find(7))
                  .Returns(new MovieVersion { Id = 7, MovieId = 1 });

            Subject.Handle(GivenFailedDownload(7));

            VerifyMovieSearched(Times.Once());
        }
    }
}
