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
    // Plex servers and the label records are faked in memory, so each test checks what ends up in Plex
    [TestFixture]
    public class PlexVersionLabelServiceFixture : CoreTest<PlexVersionLabelService>
    {
        private const string PlexGuid = "plex://movie/5d776826151a60001f24a6b6";
        private const string Folder = @"D:\Media\Movies\Blade Runner (1982)\";
        private const string OriginalFile = Folder + "Blade Runner (1982) Bluray-1080p.mkv";
        private const string EditionFile = Folder + "Blade Runner (1982) {edition-IMAX}.mkv";
        private const string UntrackedFile = Folder + "Blade Runner (1982) Theatrical.mkv";

        private PlexServerSettings _settings;
        private NotificationDefinition _definition;
        private List<NotificationDefinition> _definitions;
        private Movie _movie;
        private MovieVersion _version;
        private List<MovieVersion> _versions;
        private List<MovieFile> _files;
        private PlexMovie _original;
        private PlexMovie _edition;
        private List<PlexMovie> _plexItems;
        private Dictionary<string, int> _itemSections;
        private List<PlexSection> _sections;
        private string _serverId;
        private List<PlexVersionLabel> _records;
        private bool _failNextUpdate;

        [SetUp]
        public void Setup()
        {
            _settings = new PlexServerSettings { Host = "127.0.0.1", Port = 32400, LabelVersions = true };

            // No notification event is on: labels have a setting of their own
            _definition = new NotificationDefinition { Id = 3, Name = "Plex", Settings = _settings };
            _definitions = new List<NotificationDefinition> { _definition };

            Mocker.GetMock<INotificationFactory>()
                  .Setup(s => s.All())
                  .Returns(() => _definitions.ToList());

            _movie = new Movie
            {
                Id = 5,
                Path = @"C:\Movies\Blade Runner (1982)".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata { Title = "Blade Runner", Year = 1982 }
            };

            _version = new MovieVersion { Id = 7, MovieId = 5, Name = "IMAX", Pinned = true, Path = _movie.Path, MovieFileId = 11 };
            _versions = new List<MovieVersion> { _version };

            _files = new List<MovieFile>
            {
                new MovieFile { Id = 10, MovieId = 5, MovieVersionId = 0, RelativePath = "Blade Runner (1982) Bluray-1080p.mkv" },
                new MovieFile { Id = 11, MovieId = 5, MovieVersionId = 7, RelativePath = "Blade Runner (1982) {edition-IMAX}.mkv" }
            };

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetAllAdditionalVersions())
                  .Returns(() => _versions.ToList());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(() => new List<Movie> { _movie });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(() => _files.ToList());

            _original = GivenPlexMovie("100", "Blade Runner", null, OriginalFile);
            _edition = GivenPlexMovie("101", "Blade Runner", "IMAX", EditionFile);
            _plexItems = new List<PlexMovie> { _original, _edition };
            _itemSections = new Dictionary<string, int>();
            _sections = new List<PlexSection> { new PlexSection { Id = 1, Type = "movie" } };
            _serverId = "server-a";

            var proxy = Mocker.GetMock<IPlexServerProxy>();

            proxy.Setup(s => s.GetMachineIdentifier(It.IsAny<PlexServerSettings>()))
                 .Returns(() => _serverId);

            proxy.Setup(s => s.GetMovieSections(It.IsAny<PlexServerSettings>()))
                 .Returns(() => _sections.ToList());

            proxy.Setup(s => s.GetMovies(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PlexServerSettings>()))
                 .Returns<int, string, int, PlexServerSettings>((sectionId, title, year, settings) => _plexItems.Where(i => SectionOf(i) == sectionId).ToList());

            GivenPlexReachable();

            proxy.Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                 .Callback<int, string, Dictionary<string, string>, PlexServerSettings>((sectionId, ratingKey, fields, settings) => Apply(_plexItems.Single(i => i.RatingKey == ratingKey), fields));

            _records = new List<PlexVersionLabel>();

            var repository = Mocker.GetMock<IPlexVersionLabelRepository>();

            // Stored as copies, as a database would: a change only counts once it's saved
            repository.Setup(s => s.All())
                      .Returns(() => _records.Select(Copy).ToList());

            repository.Setup(s => s.Insert(It.IsAny<PlexVersionLabel>()))
                      .Returns<PlexVersionLabel>(r =>
                      {
                          r.Id = _records.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1;
                          _records.Add(Copy(r));

                          return r;
                      });

            repository.Setup(s => s.Update(It.IsAny<PlexVersionLabel>()))
                      .Returns<PlexVersionLabel>(r =>
                      {
                          if (_failNextUpdate)
                          {
                              _failNextUpdate = false;

                              throw new InvalidOperationException("database is locked");
                          }

                          var index = _records.FindIndex(x => x.Id == r.Id);

                          if (index >= 0)
                          {
                              _records[index] = Copy(r);
                          }

                          return r;
                      });

            repository.Setup(s => s.Delete(It.IsAny<int>()))
                      .Callback<int>(id => _records.RemoveAll(r => r.Id == id));
        }

        private static PlexVersionLabel Copy(PlexVersionLabel record)
        {
            return new PlexVersionLabel
            {
                Id = record.Id,
                ServerId = record.ServerId,
                RatingKey = record.RatingKey,
                SectionId = record.SectionId,
                MovieId = record.MovieId,
                MovieVersionId = record.MovieVersionId,
                MovieFileId = record.MovieFileId,
                FilePath = record.FilePath,
                Name = record.Name,
                Title = record.Title,
                PendingTitle = record.PendingTitle,
                OriginalTitle = record.OriginalTitle,
                OriginalTitleLocked = record.OriginalTitleLocked,
                LastChecked = record.LastChecked
            };
        }

        private static PlexMovie GivenPlexMovie(string ratingKey, string title, string edition, params string[] files)
        {
            var movie = new PlexMovie
            {
                RatingKey = ratingKey,
                Guid = PlexGuid,
                Title = title,
                EditionTitle = edition,
                Year = 1982
            };

            foreach (var file in files)
            {
                AddFile(movie, file);
            }

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

        private int SectionOf(PlexMovie item)
        {
            return _itemSections.TryGetValue(item.RatingKey, out var sectionId) ? sectionId : 1;
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

        // Plex answers for the item without its files
        private void GivenPlexLeavesOutFiles(string ratingKey)
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovie(ratingKey, It.IsAny<PlexServerSettings>()))
                  .Returns(() => new PlexMovie { RatingKey = ratingKey, Guid = PlexGuid, Title = _edition.Title, EditionTitle = _edition.EditionTitle });
        }

        private PlexServerSettings GivenSecondConnection()
        {
            var otherSettings = new PlexServerSettings { Host = "plex.local", Port = 32400, LabelVersions = true };
            _definitions.Add(new NotificationDefinition { Id = 4, Name = "Plex too", Settings = otherSettings });

            return otherSettings;
        }

        // Plex says which server it is without a token, so a connection with one Plex no longer takes still reaches it
        private void GivenLibraryRefused(PlexServerSettings settings)
        {
            var refused = new PlexException("Unable to connect to Plex Media Server, Status Code: Unauthorized");
            var proxy = Mocker.GetMock<IPlexServerProxy>();

            proxy.Setup(s => s.GetMovieSections(It.Is<PlexServerSettings>(c => c == settings))).Throws(refused);
            proxy.Setup(s => s.GetMovies(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.Is<PlexServerSettings>(c => c == settings))).Throws(refused);
            proxy.Setup(s => s.GetMovie(It.IsAny<string>(), It.Is<PlexServerSettings>(c => c == settings))).Throws(refused);
            proxy.Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.Is<PlexServerSettings>(c => c == settings))).Throws(refused);
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

        private void GivenSecondVersion()
        {
            _versions.Add(new MovieVersion { Id = 8, MovieId = 5, Name = "Open Matte", Pinned = true, Path = _movie.Path, MovieFileId = 12 });
            _files.Add(new MovieFile { Id = 12, MovieId = 5, MovieVersionId = 8, RelativePath = "Blade Runner (1982) {edition-Open Matte}.mkv" });
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
            _edition.IsLocked("title").Should().BeTrue();

            _records.Should().ContainSingle();
            _records[0].ServerId.Should().Be("server-a");
            _records[0].RatingKey.Should().Be("101");
            _records[0].OriginalTitle.Should().Be("Blade Runner");
            _records[0].Title.Should().Be("Blade Runner (IMAX)");
            _records[0].PendingTitle.Should().BeNull();
        }

        [Test]
        public void should_leave_the_edition_to_the_file_name()
        {
            Run();

            _edition.EditionTitle.Should().Be("IMAX");
            _edition.IsLocked("editionTitle").Should().BeFalse();
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
        public void should_not_edit_an_item_plex_lists_without_its_files()
        {
            GivenPlexLeavesOutFiles("101");

            Run();

            VerifyNotEdited();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_keep_a_label_recorded_while_plex_leaves_out_the_files()
        {
            Run();

            GivenPlexLeavesOutFiles("101");
            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
        }

        [Test]
        public void should_not_label_an_item_whose_edition_is_not_the_files()
        {
            _edition.EditionTitle = "Director's Cut";

            Run();

            VerifyNotEdited();
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
        public void should_not_take_labels_off_movies_the_connection_does_not_apply_to()
        {
            Run();

            _definition.Tags = new HashSet<int> { 1 };
            _movie.Tags = new HashSet<int> { 2 };
            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
        }

        [Test]
        public void should_not_touch_another_server_a_connection_is_changed_to()
        {
            Run();

            // The connection now reaches another server, which has an unrelated movie under the same rating key
            var unrelated = GivenPlexMovie("101", "Another Movie", "IMAX", @"E:\Films\Another Movie (2001)\Another Movie (2001) {edition-IMAX}.mkv");
            _plexItems = new List<PlexMovie> { unrelated };
            _serverId = "server-b";
            _settings.LabelVersions = false;
            Subject.Handle(new ProviderUpdatedEvent<INotification>(_definition));

            Run();

            unrelated.Title.Should().Be("Another Movie");
            unrelated.EditionTitle.Should().Be("IMAX");
            VerifyItemReads(1);
            _records.Should().ContainSingle(r => r.ServerId == "server-a");
        }

        [Test]
        public void should_not_change_items_when_another_server_answers_at_the_address()
        {
            Run();

            // Another server now answers at the same address, while the first one is still remembered for it
            var unrelated = GivenPlexMovie("101", "Another Movie", "IMAX", @"E:\Films\Another Movie (2001)\Another Movie (2001) {edition-IMAX}.mkv");
            _plexItems = new List<PlexMovie> { unrelated };
            _serverId = "server-b";
            _settings.LabelVersions = false;

            Run();

            unrelated.Title.Should().Be("Another Movie");
            _records.Should().ContainSingle(r => r.ServerId == "server-a");
        }

        [Test]
        public void should_keep_labels_another_connection_to_the_same_server_still_adds()
        {
            GivenSecondConnection();

            Run();

            _records.Should().ContainSingle();

            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
        }

        [Test]
        public void should_not_edit_plex_when_the_label_cannot_be_recorded()
        {
            Mocker.GetMock<IPlexVersionLabelRepository>()
                  .Setup(s => s.Insert(It.IsAny<PlexVersionLabel>()))
                  .Throws(new InvalidOperationException("database is locked"));

            Run();

            VerifyNotEdited();
            _edition.Title.Should().Be("Blade Runner");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_restore_the_exact_title_when_recording_failed_after_the_edit()
        {
            _failNextUpdate = true;

            Run();

            ExceptionVerification.ExpectedWarns(1);
            _records.Should().ContainSingle();
            _records[0].PendingTitle.Should().Be("Blade Runner (IMAX)");

            Run();

            _records[0].Title.Should().Be("Blade Runner (IMAX)");
            _records[0].OriginalTitle.Should().Be("Blade Runner");

            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_label_once_plex_takes_an_edit_that_failed()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server. Status Code: BadRequest"));

            Run();

            ExceptionVerification.ExpectedWarns(1);
            _records.Should().ContainSingle();
            _records[0].Title.Should().BeNull();

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                  .Callback<int, string, Dictionary<string, string>, PlexServerSettings>((sectionId, ratingKey, fields, settings) => Apply(_plexItems.Single(i => i.RatingKey == ratingKey), fields));

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records[0].Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_leave_a_title_that_already_shows_the_name_on_and_off()
        {
            _edition.Title = "Blade Runner (IMAX)";

            Run();

            VerifyNotEdited();
            _records.Should().ContainSingle();
            _records[0].OriginalTitle.Should().Be("Blade Runner (IMAX)");

            _settings.LabelVersions = false;

            Run();

            VerifyNotEdited();
            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_take_a_label_off_through_its_item_when_plex_has_not_found_the_renamed_file()
        {
            Run();

            // Renamed for label naming, and Plex hasn't scanned the new name yet
            GivenExtraVersionNaming(ExtraVersionNaming.Label);
            _files[1].RelativePath = "Blade Runner (1982) - IMAX.mkv";

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
        public void should_not_look_again_at_a_title_changed_outside_radarr_after_a_rename()
        {
            Run();

            _edition.Title = "Blade Runner: The IMAX Cut";
            _version.Name = "Open Matte";

            Run();

            _edition.Title.Should().Be("Blade Runner: The IMAX Cut");

            Mocker.GetMock<IPlexServerProxy>().Invocations.Clear();

            Run();

            VerifySearches(0);
            VerifyItemReads(0);
        }

        [Test]
        public void should_not_take_off_a_title_changed_outside_radarr()
        {
            Run();

            _edition.Title = "Blade Runner: The IMAX Cut";
            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner: The IMAX Cut");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_take_labels_off_when_turned_off()
        {
            Run();

            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_not_contact_plex_with_labels_off_and_nothing_labeled()
        {
            _settings.LabelVersions = false;

            Run();

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMachineIdentifier(It.IsAny<PlexServerSettings>()), Times.Never());

            VerifySearches(0);
            VerifyItemReads(0);
            VerifyNotEdited();
        }

        [Test]
        public void should_label_the_version_in_every_library_holding_its_file()
        {
            var otherLibrary = GivenPlexMovie("201", "Blade Runner", "IMAX", EditionFile);
            _plexItems.Add(otherLibrary);
            _itemSections["201"] = 2;
            _sections.Add(new PlexSection { Id = 2, Type = "movie" });

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            otherLibrary.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().HaveCount(2);
        }

        [Test]
        public void should_label_the_new_item_of_a_renamed_file_on_the_next_run()
        {
            Run();

            // Renamed with the same file id, and Plex added the new name as a new item
            _files[1].RelativePath = "Blade Runner (1982) {edition-IMAX} [Remux].mkv";
            var renamed = GivenPlexMovie("301", "Blade Runner", "IMAX", Folder + "Blade Runner (1982) {edition-IMAX} [Remux].mkv");
            _plexItems.Remove(_edition);
            _plexItems.Add(renamed);

            Run();

            renamed.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle(r => r.RatingKey == "301");
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

            VerifyItemReads(2);
            VerifySearches(2);
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
        public void should_keep_the_full_version_name_in_the_title()
        {
            _version.Name = "Director's Cut Extended Unrated Special";

            Run();

            _edition.Title.Should().Be("Blade Runner (Director's Cut Extended Unrated Special)");
        }

        [Test]
        public void should_take_off_the_label_of_a_version_that_is_gone()
        {
            Run();

            _versions.Clear();

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_label_other_versions_when_plex_refuses_an_edit()
        {
            GivenSecondVersion();
            var openMatte = GivenPlexMovie("102", "Blade Runner", "Open Matte", Folder + "Blade Runner (1982) {edition-Open Matte}.mkv");
            _plexItems.Add(openMatte);

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.EditMovie(It.IsAny<int>(), "101", It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server. Status Code: BadRequest"));

            Run();

            _edition.Title.Should().Be("Blade Runner");
            openMatte.Title.Should().Be("Blade Runner (Open Matte)");
            ExceptionVerification.ExpectedWarns(1);
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
            _versions.Clear();

            Run();

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovieSections(It.IsAny<PlexServerSettings>()), Times.Never());
        }

        [Test]
        public void should_take_the_label_off_when_plex_follows_the_file_renamed_for_label_naming()
        {
            Run();

            // Renamed for label naming, and Plex moved the item to the new name
            GivenExtraVersionNaming(ExtraVersionNaming.Label);
            _files[1].RelativePath = "Blade Runner (1982) - IMAX.mkv";
            _edition.Media.Clear();
            AddFile(_edition, Folder + "Blade Runner (1982) - IMAX.mkv");

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_keep_a_label_to_take_off_while_its_item_holds_neither_file()
        {
            Run();

            // Plex moved the item to a file Radarr doesn't know: it can't be told the item is still the one labeled
            _edition.Media.Clear();
            AddFile(_edition, @"E:\Elsewhere\Blade Runner.mkv");
            _settings.LabelVersions = false;
            Mocker.GetMock<IPlexServerProxy>().Invocations.Clear();

            Run();

            VerifyNotEdited();
            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();

            // Back at the labeled file, the label comes off
            _edition.Media.Clear();
            AddFile(_edition, EditionFile);

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_keep_a_label_to_take_off_while_plex_leaves_out_a_file_of_its_item()
        {
            Run();

            _edition.Media.Add(new PlexMovieMedia { Parts = new List<PlexMoviePart> { new PlexMoviePart() } });
            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
        }

        [Test]
        public void should_not_edit_an_item_plex_lists_with_a_part_without_its_file()
        {
            _edition.Media.Add(new PlexMovieMedia { Parts = new List<PlexMoviePart> { new PlexMoviePart() } });

            Run();

            VerifyNotEdited();
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_use_another_connection_to_the_server_when_plex_refuses_one()
        {
            GivenSecondConnection();
            GivenLibraryRefused(_settings);

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            _records.Should().ContainSingle();
        }

        [Test]
        public void should_take_labels_off_through_another_connection_when_plex_refuses_one()
        {
            var otherSettings = GivenSecondConnection();

            Run();

            _settings.LabelVersions = false;
            otherSettings.LabelVersions = false;
            GivenLibraryRefused(_settings);

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();
        }

        [Test]
        public void should_not_use_a_connection_that_reaches_another_server_now()
        {
            var otherSettings = GivenSecondConnection();

            Run();

            // The first connection reaches another server now, which it's still remembered for
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMachineIdentifier(It.Is<PlexServerSettings>(c => c == _settings)))
                  .Returns("server-b");

            _settings.LabelVersions = false;
            otherSettings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _records.Should().BeEmpty();

            Mocker.GetMock<IPlexServerProxy>()
                  .Verify(v => v.GetMovie(It.IsAny<string>(), It.Is<PlexServerSettings>(c => c == _settings)), Times.Once());
        }

        [Test]
        public void should_label_the_version_in_other_libraries_when_plex_refuses_an_edit_in_one()
        {
            var otherLibrary = GivenPlexMovie("201", "Blade Runner", "IMAX", EditionFile);
            _plexItems.Add(otherLibrary);
            _itemSections["201"] = 2;
            _sections.Add(new PlexSection { Id = 2, Type = "movie" });

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.EditMovie(It.IsAny<int>(), "101", It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server. Status Code: BadRequest"));

            Run();

            _edition.Title.Should().Be("Blade Runner");
            otherLibrary.Title.Should().Be("Blade Runner (IMAX)");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_label_the_version_in_other_libraries_when_plex_fails_to_list_one()
        {
            var otherLibrary = GivenPlexMovie("201", "Blade Runner", "IMAX", EditionFile);
            _plexItems.Add(otherLibrary);
            _itemSections["201"] = 2;
            _sections.Insert(0, new PlexSection { Id = 2, Type = "movie" });
            _sections.Insert(0, new PlexSection { Id = 3, Type = "movie" });

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovies(3, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server. Status Code: InternalServerError"));

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");
            otherLibrary.Title.Should().Be("Blade Runner (IMAX)");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_retrying_a_failed_edit_once_the_title_is_changed_outside_radarr()
        {
            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.EditMovie(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<PlexServerSettings>()))
                  .Throws(new PlexException("Unable to connect to Plex Media Server. Status Code: BadRequest"));

            Run();

            ExceptionVerification.ExpectedWarns(1);

            _edition.Title = "Blade Runner: The IMAX Cut";

            Run();

            _edition.Title.Should().Be("Blade Runner: The IMAX Cut");
            _records.Should().ContainSingle();
            _records[0].PendingTitle.Should().BeNull();

            Mocker.GetMock<IPlexServerProxy>().Invocations.Clear();

            Run();

            VerifySearches(0);
            VerifyItemReads(0);
        }

        [Test]
        public void should_label_and_take_labels_off_with_every_notification_event_off()
        {
            _definition.Enable.Should().BeFalse();

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX)");

            _settings.LabelVersions = false;

            Run();

            _edition.Title.Should().Be("Blade Runner");
            _edition.IsLocked("title").Should().BeFalse();

            Mocker.GetMock<INotificationFactory>()
                  .Verify(v => v.GetAvailableProviders(), Times.Never());
        }

        [Test]
        public void should_not_label_a_labeled_item_again_when_plex_fails_to_answer_for_it_once()
        {
            Run();

            // On the daily check Plex fails to answer for the labeled item once, then answers again
            GivenLabelsCheckedLongAgo();
            var reads = 0;

            Mocker.GetMock<IPlexServerProxy>()
                  .Setup(s => s.GetMovie("101", It.IsAny<PlexServerSettings>()))
                  .Returns<string, PlexServerSettings>((ratingKey, settings) =>
                  {
                      if (reads++ == 0)
                      {
                          throw new PlexException("Unable to connect to Plex Media Server");
                      }

                      return _edition;
                  });

            Run();

            ExceptionVerification.ExpectedWarns(1);
            _records.Should().ContainSingle();

            _version.Name = "IMAX Updated";

            Run();

            _edition.Title.Should().Be("Blade Runner (IMAX Updated)");
            _records.Should().ContainSingle();
            _records[0].OriginalTitle.Should().Be("Blade Runner");
        }
    }
}
