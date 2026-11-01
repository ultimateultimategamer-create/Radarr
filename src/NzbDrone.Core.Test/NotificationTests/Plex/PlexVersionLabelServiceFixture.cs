using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Plex;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.ThingiProvider.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.NotificationTests.Plex
{
    // Plex and the label records are faked in memory, so each test checks what ends up in Plex
    [TestFixture]
    public class PlexVersionLabelServiceFixture : CoreTest<PlexVersionLabelService>
    {
        private const string PlexGuid = "plex://movie/5d776826151a60001f24a6b6";
        private const string OriginalFile = @"D:\Media\Movies\Blade Runner (1982)\Blade Runner (1982) Bluray-1080p.mkv";
        private const string EditionFile = @"D:\Media\Movies\Blade Runner (1982)\Blade Runner (1982) {edition-IMAX}.mkv";
        private const string UntrackedFile = @"D:\Media\Movies\Blade Runner (1982)\Blade Runner (1982) Theatrical.mkv";

        private PlexServerSettings _settings;
        private NotificationDefinition _definition;
        private Movie _movie;
        private MovieVersion _version;
        private PlexMovie _original;
        private PlexMovie _edition;
        private List<PlexMovie> _plexItems;
        private List<PlexVersionLabel> _records;

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
                  .Returns(() => new List<MovieVersion> { _version });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(() => new List<Movie> { _movie });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<MovieFile>
                  {
                      new MovieFile { Id = 10, MovieId = 5, MovieVersionId = 0, RelativePath = "Blade Runner (1982) Bluray-1080p.mkv" },
                      new MovieFile { Id = 11, MovieId = 5, MovieVersionId = 7, RelativePath = "Blade Runner (1982) {edition-IMAX}.mkv" }
                  });

            _original = GivenPlexMovie("100", "Blade Runner", null, OriginalFile);
            _edition = GivenPlexMovie("101", "Blade Runner", "IMAX", EditionFile);
            _plexItems = new List<PlexMovie> { _original, _edition };

            var proxy = Mocker.GetMock<IPlexServerProxy>();

            proxy.Setup(s => s.GetMovieSections(It.IsAny<PlexServerSettings>()))
                 .Returns(new List<PlexSection> { new PlexSection { Id = 1, Type = "movie" } });

            proxy.Setup(s => s.GetMovies(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PlexServerSettings>()))
                 .Returns(() => _plexItems.ToList());

            GivenPlexReachable();

            proxy.Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                 .Callback<int, string, Dictionary<string, string>, PlexServerSettings>((sectionId, ratingKey, fields, settings) => Apply(_plexItems.Single(i => i.RatingKey == ratingKey), fields));

            _records = new List<PlexVersionLabel>();

            var repository = Mocker.GetMock<IPlexVersionLabelRepository>();

            repository.Setup(s => s.All())
                      .Returns(() => _records.ToList());

            repository.Setup(s => s.Insert(It.IsAny<PlexVersionLabel>()))
                      .Returns<PlexVersionLabel>(r =>
                      {
                          r.Id = _records.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
                          _records.Add(r);

                          return r;
                      });

            repository.Setup(s => s.Update(It.IsAny<PlexVersionLabel>()))
                      .Returns<PlexVersionLabel>(r => r);

            repository.Setup(s => s.Delete(It.IsAny<int>()))
                      .Callback<int>(id => _records.RemoveAll(r => r.Id == id));

            repository.Setup(s => s.DeleteMany(It.IsAny<IEnumerable<int>>()))
                      .Callback<IEnumerable<int>>(ids => _records.RemoveAll(r => ids.Contains(r.Id)));
        }

        private static PlexMovie GivenPlexMovie(string ratingKey, string title, string edition, string file)
        {
            var movie = new PlexMovie
            {
                RatingKey = ratingKey,
                Guid = PlexGuid,
                Title = title,
                EditionTitle = edition,
                Year = 1982
            };

            AddFile(movie, file);

            return movie;
        }

        private static void AddFile(PlexMovie movie, string file)
        {
            movie.Media.Add(new PlexMovieMedia { Parts = new List<PlexMoviePart> { new PlexMoviePart { File = file } } });
        }

        // Applies an edit as Plex does: "field.value" sets a field, "field.locked" locks or unlocks it
        private static void Apply(PlexMovie item, Dictionary<string, string> fields)
        {
            foreach (var field in fields)
            {
                var parts = field.Key.Split('.');

                if (parts[1] == "value" && parts[0] == "title")
                {
                    item.Title = field.Value;
                }
                else if (parts[1] == "value" && parts[0] == "editionTitle")
                {
                    item.EditionTitle = field.Value;
                }
                else if (parts[1] == "locked")
                {
                    item.Fields.RemoveAll(f => f.Name == parts[0]);
                    item.Fields.Add(new PlexMovieField { Name = parts[0], Locked = field.Value });
                }
            }
        }

        private void GivenPlexReachable()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovie(It.IsAny<string>(), It.IsAny<PlexServerSettings>()))
                  .Returns<string, PlexServerSettings>((ratingKey, settings) => _plexItems.FirstOrDefault(i => i.RatingKey == ratingKey));
        }

        private void GivenPlexUnreachable()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovie(It.IsAny<string>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server"));
        }

        private void GivenExtraVersionNaming(ExtraVersionNaming naming)
        {
            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(new NamingConfig { ExtraVersionNaming = naming });
        }

        private void GivenLabelsCheckedLongAgo()
        {
            _records.ForEach(r => r.LastChecked = DateTime.UtcNow.AddDays(-2));
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

        private void VerifyItemReads(int times)
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovie(It.IsAny<string>(), It.IsAny<PlexServerSettings>()), Times.Exactly(times));
        }

        private void Run()
        {
            Subject.Execute(new LabelPlexVersionsCommand());
        }

        [Test]
        public void should_put_the_version_name_at_the_end_of_the_title()
        {
            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _edition.EditionTitle.Should().Be("IMAX");
            _edition.IsLocked("title").Should().BeTrue();
            _edition.IsLocked("editionTitle").Should().BeTrue();

            _records.Should().ContainSingle();
            _records[0].RatingKey.Should().Be("101");
            _records[0].OriginalTitle.Should().Be("Blade Runner");
            _records[0].OriginalTitleLocked.Should().BeFalse();
        }

        [Test]
        public void should_never_edit_the_original()
        {
            Run();

            _original.Title.Should().Be("Blade Runner");
            _original.Fields.Should().BeEmpty();
        }

        [Test]
        public void should_not_edit_an_item_also_holding_a_file_radarr_does_not_track()
        {
            AddFile(_edition, UntrackedFile);

            Run();

            VerifyNotEdited();
            _records.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_edit_an_item_holding_files_of_another_version()
        {
            _plexItems.Remove(_edition);
            AddFile(_original, EditionFile);

            Run();

            VerifyNotEdited();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_take_the_label_off_an_item_that_comes_to_hold_other_files()
        {
            Run();

            AddFile(_edition, UntrackedFile);
            GivenLabelsCheckedLongAgo();

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();
            _records.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_leave_movies_alone_that_the_connection_does_not_apply_to()
        {
            _definition.Tags = new HashSet<int> { 1 };
            _movie.Tags = new HashSet<int> { 2 };

            Run();

            VerifySearches(0);
            VerifyNotEdited();
        }

        [Test]
        public void should_label_movies_with_a_tag_of_the_connection()
        {
            _definition.Tags = new HashSet<int> { 1 };
            _movie.Tags = new HashSet<int> { 1, 2 };

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_take_a_label_off_through_its_item_when_plex_has_not_found_the_renamed_file()
        {
            Run();

            // Renamed for label naming, and Plex hasn't scanned the new name yet
            GivenExtraVersionNaming(ExtraVersionNaming.Label);

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PlexServerSettings>()))
                  .Returns(new List<PlexMovie>());

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_keep_a_label_to_take_off_while_plex_cannot_be_reached()
        {
            Run();

            _settings.LabelVersions = false;
            GivenPlexUnreachable();

            Run();

            _records.Should().ContainSingle();
            ExceptionVerification.ExpectedWarns(1);

            GivenPlexReachable();

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_label_again_after_naming_is_switched_back()
        {
            Run();

            GivenExtraVersionNaming(ExtraVersionNaming.Label);

            Run();

            _edition.Title.Should().Be("Blade Runner");

            GivenExtraVersionNaming(ExtraVersionNaming.PlexEdition);

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _edition.IsLocked("title").Should().BeTrue();
        }

        [Test]
        public void should_follow_a_renamed_version()
        {
            Run();

            _version.Name = "IMAX Enhanced";

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX Enhanced)");
            _edition.EditionTitle.Should().Be("IMAX Enhanced");
            _records.Should().ContainSingle();
            _records[0].OriginalTitle.Should().Be("Blade Runner");
        }

        [Test]
        public void should_leave_a_title_changed_outside_radarr()
        {
            Run();

            _edition.Title = "Blade Runner: The IMAX Cut";
            GivenLabelsCheckedLongAgo();

            Run();

            _edition.Title.Should().Be("Blade Runner: The IMAX Cut");
        }

        [Test]
        public void should_not_take_off_a_title_changed_outside_radarr()
        {
            Run();

            _edition.Title = "Blade Runner: The IMAX Cut";
            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner: The IMAX Cut");
            _edition.IsLocked("editionTitle").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_take_labels_off_when_turned_off()
        {
            Run();

            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.EditionTitle.Should().Be("IMAX");
            _edition.IsLocked("title").Should().BeFalse();
            _edition.IsLocked("editionTitle").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_not_contact_plex_with_labels_off_and_nothing_labeled()
        {
            _settings.LabelVersions = false;

            Run();

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovieSections(It.IsAny<PlexServerSettings>()), Times.Never());

            VerifySearches(0);
            VerifyItemReads(0);
            VerifyNotEdited();
        }

        [Test]
        public void should_wait_until_plex_has_matched_the_item()
        {
            _edition.Guid = "local://101";

            Run();

            _edition.Title.Should().Be("Blade Runner");

            _edition.Guid = PlexGuid;

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_look_for_a_version_plex_has_not_picked_up_on_the_next_run()
        {
            _plexItems.Remove(_edition);

            Run();
            Run();

            // By title and year, by year, by title: on both runs
            VerifySearches(6);
        }

        [Test]
        public void should_check_a_labeled_version_again_only_after_a_day()
        {
            Run();
            Run();

            VerifyItemReads(1);

            GivenLabelsCheckedLongAgo();

            Run();

            // The labeled item is read again, no search is needed
            VerifyItemReads(2);
            VerifySearches(1);
        }

        [Test]
        public void should_find_the_item_by_year_when_plex_titles_the_movie_otherwise()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(1, "Blade Runner", 1982, It.IsAny<PlexServerSettings>()))
                  .Returns(new List<PlexMovie>());

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_not_label_an_item_twice()
        {
            // Labeled before, as by a connection that was deleted and added again
            _edition.Title = "Blade Runner (IMAX)";

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
            _records[0].OriginalTitle.Should().Be("Blade Runner");
        }

        [Test]
        public void should_cut_the_edition_to_plex_limit_and_keep_the_full_name_in_the_title()
        {
            _version.Name = "Director's Cut Extended Unrated Special";

            Run();

            _edition.Title.Should().Be("Blade Runner (Director's Cut Extended Unrated Special)");
            _edition.EditionTitle.Should().Be("Director's Cut Extended Unrated");
        }

        [Test]
        public void should_take_off_the_label_of_a_version_that_is_gone()
        {
            Run();

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAllAdditionalVersions())
                  .Returns(new List<MovieVersion>());

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_forget_labels_of_a_deleted_connection()
        {
            Run();

            Subject.Handle(new ProviderDeletedEvent<INotification>(_definition.Id));

            _records.Should().BeEmpty();
        }

        [Test]
        public void should_look_again_when_the_connection_is_saved()
        {
            _plexItems.Remove(_edition);

            Run();

            Subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));
            _plexItems.Add(_edition);

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_not_look_in_plex_without_extra_versions()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAllAdditionalVersions())
                  .Returns(new List<MovieVersion>());

            Run();

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovieSections(It.IsAny<PlexServerSettings>()), Times.Never());
        }
    }
}
