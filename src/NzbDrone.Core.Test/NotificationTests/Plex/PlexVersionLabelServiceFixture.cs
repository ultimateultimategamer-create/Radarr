using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Plex
{
    [TestFixture]
    public class PlexVersionLabelServiceFixture : CoreTest<PlexVersionLabelService>
    {
        private const string OriginalFile = @"D:\Media\Movies\Blade Runner (1982)\Blade Runner (1982) Bluray-1080p.mkv";
        private const string EditionFile = @"D:\Media\Movies\Blade Runner (1982)\Blade Runner (1982) {edition-IMAX}.mkv";

        private PlexServerSettings _settings;
        private NotificationDefinition _definition;
        private Movie _movie;
        private MovieVersion _version;
        private PlexMovie _original;
        private PlexMovie _edition;

        [SetUp]
        public void Setup()
        {
            _settings = new PlexServerSettings
            {
                Host = "127.0.0.1",
                Port = 32400,
                LabelVersions = true
            };

            _definition = new NotificationDefinition
            {
                Id = 3,
                Name = "Plex",
                Settings = _settings
            };

            var notification = new Mock<INotification>();
            notification.SetupGet(n => n.Definition).Returns(_definition);

            Mocker.GetMock<INotificationFactory>()
                  .Setup(s => s.GetAvailableProviders())
                  .Returns(new List<INotification> { notification.Object });

            _movie = new Movie
            {
                Id = 5,
                Path = @"C:\Movies\Blade Runner (1982)".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata { Title = "Blade Runner", Year = 1982 }
            };

            _version = new MovieVersion { Id = 7, MovieId = 5, Name = "IMAX", Pinned = true, Path = _movie.Path, MovieFileId = 11 };

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAllAdditionalVersions())
                  .Returns(new List<MovieVersion> { _version });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<Movie> { _movie });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<MovieFile>
                  {
                      new MovieFile { Id = 10, MovieId = 5, MovieVersionId = 0, RelativePath = "Blade Runner (1982) Bluray-1080p.mkv" },
                      new MovieFile { Id = 11, MovieId = 5, MovieVersionId = 7, RelativePath = "Blade Runner (1982) {edition-IMAX}.mkv" }
                  });

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovieSections(_settings))
                  .Returns(new List<PlexSection> { new PlexSection { Id = 1, Type = "movie" } });

            _original = GivenPlexMovie("100", "Blade Runner", null, false, OriginalFile);
            _edition = GivenPlexMovie("101", "Blade Runner", "IMAX", false, EditionFile);

            GivenPlexMovies(_original, _edition);
        }

        private static PlexMovie GivenPlexMovie(string ratingKey, string title, string edition, bool locked, params string[] files)
        {
            var movie = new PlexMovie
            {
                RatingKey = ratingKey,
                Guid = "plex://movie/5d776826151a60001f24a6b6",
                Title = title,
                EditionTitle = edition,
                Year = 1982
            };

            foreach (var file in files)
            {
                movie.Media.Add(new PlexMovieMedia { Parts = new List<PlexMoviePart> { new PlexMoviePart { File = file } } });
            }

            if (locked)
            {
                movie.Fields.Add(new PlexMovieField { Name = "title", Locked = "1" });
                movie.Fields.Add(new PlexMovieField { Name = "editionTitle", Locked = "true" });
            }

            return movie;
        }

        private void GivenPlexMovies(params PlexMovie[] movies)
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(1, It.IsAny<string>(), It.IsAny<int>(), _settings))
                  .Returns(movies.ToList());

            foreach (var movie in movies)
            {
                Mocker.GetMock<IPlexServerProxy>()
                      .Setup(s => s.GetMovie(movie.RatingKey, _settings))
                      .Returns(movie);
            }
        }

        private void GivenExtraVersionNaming(ExtraVersionNaming naming)
        {
            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(new NamingConfig { ExtraVersionNaming = naming });
        }

        private static bool Has(Dictionary<string, string> fields, string field, string value)
        {
            return fields.TryGetValue(field, out var fieldValue) && fieldValue == value;
        }

        private void VerifyEdited(string ratingKey, string title, string titleLocked, string edition)
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.EditMovie(1,
                                           ratingKey,
                                           It.Is<Dictionary<string, string>>(f => Has(f, "title.value", title) &&
                                                                                  Has(f, "title.locked", titleLocked) &&
                                                                                  (edition == null ? !f.ContainsKey("editionTitle.value") : Has(f, "editionTitle.value", edition) && Has(f, "editionTitle.locked", "1"))),
                                           _settings),
                          Times.Once());
        }

        private void VerifyNotEdited()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()), Times.Never());
        }

        private void VerifySearches(int times)
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovies(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PlexServerSettings>()), Times.Exactly(times));
        }

        [Test]
        public void should_put_the_version_name_at_the_end_of_the_title()
        {
            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner (IMAX)", "1", "IMAX");
        }

        [Test]
        public void should_never_edit_the_original()
        {
            Subject.Execute(new LabelPlexVersionsCommand());

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.EditMovie(It.IsAny<int>(), "100", It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()), Times.Never());
        }

        [Test]
        public void should_not_edit_an_item_that_is_already_labeled()
        {
            _edition = GivenPlexMovie("101", "Blade Runner (IMAX)", "IMAX", true, EditionFile);
            GivenPlexMovies(_original, _edition);

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyNotEdited();
        }

        [Test]
        public void should_lock_a_label_that_is_not_locked()
        {
            _edition = GivenPlexMovie("101", "Blade Runner (IMAX)", "IMAX", false, EditionFile);
            GivenPlexMovies(_original, _edition);

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner (IMAX)", "1", "IMAX");
        }

        [Test]
        public void should_follow_a_renamed_version()
        {
            _edition = GivenPlexMovie("101", "Blade Runner (IMAX)", "IMAX", true, EditionFile);
            GivenPlexMovies(_original, _edition);
            _version.Name = "IMAX Enhanced";

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner (IMAX Enhanced)", "1", "IMAX Enhanced");
        }

        [Test]
        public void should_not_edit_an_item_holding_files_of_another_version()
        {
            var merged = GivenPlexMovie("100", "Blade Runner", null, false, OriginalFile, EditionFile);
            GivenPlexMovies(merged);

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyNotEdited();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_wait_until_plex_has_matched_the_item()
        {
            _edition.Guid = "local://101";

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyNotEdited();

            _edition.Guid = "plex://movie/5d776826151a60001f24a6b6";

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner (IMAX)", "1", "IMAX");
        }

        [Test]
        public void should_look_for_a_version_plex_has_not_picked_up_on_the_next_run()
        {
            GivenPlexMovies(_original);

            Subject.Execute(new LabelPlexVersionsCommand());
            Subject.Execute(new LabelPlexVersionsCommand());

            // By title and year, by year, by title: on both runs
            VerifySearches(6);
        }

        [Test]
        public void should_not_look_for_a_labeled_version_again_until_it_changes()
        {
            Subject.Execute(new LabelPlexVersionsCommand());
            Subject.Execute(new LabelPlexVersionsCommand());

            VerifySearches(1);

            _version.Name = "IMAX Enhanced";

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifySearches(2);
        }

        [Test]
        public void should_look_again_when_the_connection_is_saved()
        {
            Subject.Execute(new LabelPlexVersionsCommand());

            Subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            Subject.Execute(new LabelPlexVersionsCommand());

            VerifySearches(2);
        }

        [Test]
        public void should_find_the_item_by_year_when_plex_titles_the_movie_otherwise()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(1, "Blade Runner", 1982, _settings))
                  .Returns(new List<PlexMovie>());

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(1, null, 1982, _settings))
                  .Returns(new List<PlexMovie> { _original, _edition });

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner (IMAX)", "1", "IMAX");
        }

        [Test]
        public void should_take_labels_off_when_turned_off()
        {
            _settings.LabelVersions = false;
            _edition = GivenPlexMovie("101", "Blade Runner (IMAX)", "IMAX", true, EditionFile);
            GivenPlexMovies(_original, _edition);

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner", "0", null);
        }

        [Test]
        public void should_take_labels_off_once()
        {
            _settings.LabelVersions = false;

            Subject.Execute(new LabelPlexVersionsCommand());
            Subject.Execute(new LabelPlexVersionsCommand());

            VerifySearches(1);
            VerifyNotEdited();
        }

        [Test]
        public void should_take_labels_off_when_extra_versions_are_not_named_as_plex_editions()
        {
            GivenExtraVersionNaming(ExtraVersionNaming.Label);
            _edition = GivenPlexMovie("101", "Blade Runner (IMAX)", "IMAX", true, EditionFile);
            GivenPlexMovies(_original, _edition);

            Subject.Execute(new LabelPlexVersionsCommand());

            VerifyEdited("101", "Blade Runner", "0", null);
        }

        [Test]
        public void should_not_look_in_plex_without_extra_versions()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAllAdditionalVersions())
                  .Returns(new List<MovieVersion>());

            Subject.Execute(new LabelPlexVersionsCommand());

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovieSections(It.IsAny<PlexServerSettings>()), Times.Never());
        }
    }
}
