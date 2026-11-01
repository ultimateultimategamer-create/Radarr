using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieVersionTests
{
    [TestFixture]
    public class MovieVersionServiceFixture : CoreTest<MovieVersionService>
    {
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 5)
                                   .With(m => m.QualityProfileId = 3)
                                   .With(m => m.Path = "/Movies/Movie (2020)")
                                   .With(m => m.MovieFileId = 0)
                                   .Build();
        }

        [Test]
        public void should_create_primary_version_when_movie_is_added()
        {
            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion>());

            Subject.Handle(new MovieAddedEvent(_movie));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.MovieId == 5 && m.QualityProfileId == 3 && m.Path == "/Movies/Movie (2020)")),
                          Times.Once());
        }

        [Test]
        public void should_not_create_version_when_movie_already_has_one()
        {
            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { MovieId = _movie.Id } });

            Subject.Handle(new MovieAddedEvent(_movie));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.IsAny<MovieVersion>()), Times.Never());
        }

        [Test]
        public void should_delete_versions_when_movies_are_deleted()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _movie }, false, false));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.DeleteForMovies(It.Is<List<int>>(l => l.Count == 1 && l[0] == 5)), Times.Once());
        }

        [Test]
        public void should_attach_file_to_its_additional_version()
        {
            var version = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 0 };
            var movieFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Find(7))
                  .Returns(version);

            Subject.Handle(new MovieFileAddedEvent(movieFile));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Update(It.Is<MovieVersion>(m => m.Id == 7 && m.MovieFileId == 42)), Times.Once());
        }

        [Test]
        public void should_attach_file_of_the_movie_to_its_primary_version()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id };
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id };
            var movieFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 0 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { additional, primary });

            Subject.Handle(new MovieFileAddedEvent(movieFile));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Update(It.Is<MovieVersion>(m => m.Id == 1 && m.MovieFileId == 42)), Times.Once());
        }

        [Test]
        public void should_detach_deleted_file_from_its_version()
        {
            var version = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 42 };
            var movieFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, MovieFileId = 10 }, version });

            Subject.Handle(new MovieFileDeletedEvent(movieFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<List<MovieVersion>>(l => l.Count == 1 && l[0].Id == 7 && l[0].MovieFileId == 0)), Times.Once());
        }

        [Test]
        public void should_keep_primary_version_in_sync_when_movie_is_edited()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id, Path = "/Old/Movie (2020)", QualityProfileId = 1 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { primary });

            Subject.Handle(new MovieEditedEvent(_movie, _movie));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<IList<MovieVersion>>(l => l.Count == 1 && l[0].Id == 1 && l[0].Path == _movie.Path && l[0].QualityProfileId == _movie.QualityProfileId)), Times.Once());
        }

        [Test]
        public void should_keep_pinned_versions_on_the_movie_quality_profile_when_the_movie_is_edited()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id, Path = _movie.Path, QualityProfileId = _movie.QualityProfileId };
            var pinned = new MovieVersion { Id = 7, MovieId = _movie.Id, Path = _movie.Path, QualityProfileId = 1, Pinned = true };
            var automatic = new MovieVersion { Id = 8, MovieId = _movie.Id, Path = _movie.Path, QualityProfileId = 4 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { primary, pinned, automatic });

            Subject.Handle(new MovieEditedEvent(_movie, _movie));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<IList<MovieVersion>>(l => l.Count == 1 && l[0].Id == 7 && l[0].QualityProfileId == _movie.QualityProfileId)), Times.Once());
            automatic.QualityProfileId.Should().Be(4);
        }

        [Test]
        public void should_return_the_movie_for_a_file_of_an_unknown_version()
        {
            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id } });

            Subject.GetMovieForFile(_movie, new MovieFile { MovieId = _movie.Id, MovieVersionId = 99 }).Should().BeSameAs(_movie);
        }

        [Test]
        public void should_return_a_view_for_a_file_of_an_additional_version()
        {
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id, Path = "/Movies 4K/Movie (2020)", QualityProfileId = 2 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id }, additional });

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Find(7))
                  .Returns(additional);

            var view = Subject.GetMovieForFile(_movie, new MovieFile { MovieId = _movie.Id, MovieVersionId = 7 });

            view.MovieVersionId.Should().Be(7);
            view.Path.Should().Be("/Movies 4K/Movie (2020)");
        }

        private void GivenQualityProfile(int id)
        {
            Mocker.GetMock<IQualityProfileRepository>()
                  .Setup(s => s.Find(id))
                  .Returns(new QualityProfile { Id = id });
        }

        private void GivenPrimaryVersion()
        {
            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, QualityProfileId = _movie.QualityProfileId, Path = _movie.Path } });
        }

        [Test]
        public void should_add_version_in_the_movie_folder()
        {
            GivenQualityProfile(4);
            GivenPrimaryVersion();

            Subject.AddVersion(_movie, 4, "Director's Cut");

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.MovieId == _movie.Id && m.QualityProfileId == 4 && m.Path == _movie.Path)), Times.Once());
        }

        [Test]
        public void should_create_primary_version_before_adding_a_version_to_a_movie_without_versions()
        {
            GivenQualityProfile(4);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion>());

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Insert(It.IsAny<MovieVersion>()))
                  .Returns<MovieVersion>(v => v);

            Subject.AddVersion(_movie, 4, "Director's Cut");

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.QualityProfileId == _movie.QualityProfileId)), Times.Once());

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.QualityProfileId == 4)), Times.Once());
        }

        [Test]
        public void should_not_add_the_same_version_name_twice()
        {
            GivenQualityProfile(3);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, Name = "Director's Cut", MovieId = _movie.Id, QualityProfileId = 3, Path = _movie.Path } });

            Assert.Throws<NzbDroneClientException>(() => Subject.AddVersion(_movie, 3, " director's cut "));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.IsAny<MovieVersion>()), Times.Never());
        }

        [Test]
        public void should_not_add_version_with_unknown_quality_profile()
        {
            GivenPrimaryVersion();
            Assert.Throws<NzbDroneClientException>(() => Subject.AddVersion(_movie, 42, "Unknown profile"));
        }

        [Test]
        public void should_check_a_new_version_name_against_the_primary_version_it_creates()
        {
            GivenQualityProfile(4);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion>());

            // The repository gives the primary version it creates an id
            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Insert(It.IsAny<MovieVersion>()))
                  .Returns<MovieVersion>(v =>
                  {
                      v.Id = 1;
                      return v;
                  });

            Assert.Throws<NzbDroneClientException>(() => Subject.AddVersion(_movie, 4, "original"));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.QualityProfileId == 4)), Times.Never());
        }

        [Test]
        public void should_add_pinned_version_in_the_movie_folder_with_the_movie_profile()
        {
            GivenPrimaryVersion();
            GivenQualityProfile(_movie.QualityProfileId);

            Subject.AddPinnedVersion(_movie, " Director's Cut ");

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.MovieId == _movie.Id &&
                                                                 m.Name == "Director's Cut" &&
                                                                 m.Pinned &&
                                                                 m.QualityProfileId == _movie.QualityProfileId &&
                                                                 m.RequiredCustomFormatIds.Count == 0 &&
                                                                 m.Path == _movie.Path)),
                          Times.Once());
        }

        [Test]
        public void should_name_pinned_version_after_the_suggestion_when_no_name_is_given()
        {
            GivenPrimaryVersion();
            GivenQualityProfile(_movie.QualityProfileId);

            Subject.AddPinnedVersion(_movie, null, "IMAX");

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.Name == "IMAX" && m.Pinned)), Times.Once());
        }

        [Test]
        public void should_number_the_suggested_name_when_another_version_uses_it()
        {
            GivenQualityProfile(_movie.QualityProfileId);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion>
                  {
                      new MovieVersion { Id = 1, MovieId = _movie.Id, Name = "Original" },
                      new MovieVersion { Id = 7, MovieId = _movie.Id, Name = "Bluray-2160p", Pinned = true },
                      new MovieVersion { Id = 8, MovieId = _movie.Id, Name = "bluray-2160p 2", Pinned = true }
                  });

            Subject.AddPinnedVersion(_movie, "", "Bluray-2160p");

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.Is<MovieVersion>(m => m.Name == "Bluray-2160p 3")), Times.Once());
        }

        [Test]
        public void should_not_add_a_pinned_version_with_a_name_already_used()
        {
            GivenQualityProfile(_movie.QualityProfileId);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion>
                  {
                      new MovieVersion { Id = 1, MovieId = _movie.Id, Name = "Original" },
                      new MovieVersion { Id = 7, MovieId = _movie.Id, Name = "IMAX", Pinned = true }
                  });

            Assert.Throws<NzbDroneClientException>(() => Subject.AddPinnedVersion(_movie, "imax", "Bluray-2160p"));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.Insert(It.IsAny<MovieVersion>()), Times.Never());
        }

        [Test]
        public void should_remove_pinned_version_when_its_file_is_deleted()
        {
            var pinned = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 42, Pinned = true };
            var movieFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, MovieFileId = 10 }, pinned });

            Subject.Handle(new MovieFileDeletedEvent(movieFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(pinned), Times.Once());
            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<List<MovieVersion>>(l => l.Count == 0)), Times.Once());
        }

        [Test]
        public void should_keep_pinned_version_when_an_older_file_of_it_is_deleted()
        {
            var pinned = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 43, Pinned = true };
            var olderFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, MovieFileId = 10 }, pinned });

            Subject.Handle(new MovieFileDeletedEvent(olderFile, DeleteMediaFileReason.Manual));

            pinned.MovieFileId.Should().Be(43);
            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(It.IsAny<MovieVersion>()), Times.Never());
            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<List<MovieVersion>>(l => l.Count == 0)), Times.Once());
        }

        [Test]
        public void should_point_pinned_version_at_its_other_file_when_its_current_file_is_deleted()
        {
            var pinned = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 42, Pinned = true };
            var currentFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, MovieFileId = 10 }, pinned });

            // The record of the deleted file is already gone, an earlier file of the version is left
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieFile>
                  {
                      new MovieFile { Id = 10, MovieId = _movie.Id, MovieVersionId = 0 },
                      new MovieFile { Id = 41, MovieId = _movie.Id, MovieVersionId = 7 }
                  });

            Subject.Handle(new MovieFileDeletedEvent(currentFile, DeleteMediaFileReason.Manual));

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(It.IsAny<MovieVersion>()), Times.Never());
            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<List<MovieVersion>>(l => l.Count == 1 && l[0].Id == 7 && l[0].MovieFileId == 41)), Times.Once());
        }

        [TestCase(DeleteMediaFileReason.Upgrade)]
        [TestCase(DeleteMediaFileReason.ManualOverride)]
        [TestCase(DeleteMediaFileReason.MissingFromDisk)]
        public void should_keep_pinned_version_when_its_file_is_replaced_or_missing(DeleteMediaFileReason reason)
        {
            var pinned = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 42, Pinned = true };
            var movieFile = new MovieFile { Id = 42, MovieId = _movie.Id, MovieVersionId = 7 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = _movie.Id, MovieFileId = 10 }, pinned });

            Subject.Handle(new MovieFileDeletedEvent(movieFile, reason));

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(It.IsAny<MovieVersion>()), Times.Never());
            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<List<MovieVersion>>(l => l.Count == 1 && l[0].Id == 7 && l[0].MovieFileId == 0)), Times.Once());
        }

        [Test]
        public void should_create_primary_versions_for_movies_imported_in_bulk()
        {
            var other = Builder<Movie>.CreateNew()
                                      .With(m => m.Id = 6)
                                      .With(m => m.QualityProfileId = 2)
                                      .With(m => m.Path = "/Movies/Other (2021)")
                                      .Build();

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieIds(It.IsAny<List<int>>()))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 1, MovieId = other.Id } });

            Subject.Handle(new MoviesImportedEvent(new List<Movie> { _movie, other }));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.InsertMany(It.Is<IList<MovieVersion>>(l => l.Count == 1 && l[0].MovieId == _movie.Id && l[0].QualityProfileId == 3)), Times.Once());
        }

        [Test]
        public void should_move_additional_versions_with_the_movie()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id, Path = "/Old/Movie (2020)", QualityProfileId = 3 };
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id, Path = "/Old/Movie (2020)", QualityProfileId = 4 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { primary, additional });

            Subject.Handle(new MovieMovedEvent(_movie, "/Old/Movie (2020)", _movie.Path));

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(v => v.UpdateMany(It.Is<IList<MovieVersion>>(l => l.Count == 2 && l.All(m => m.Path == _movie.Path))), Times.Once());
        }

        [Test]
        public void should_return_the_movie_for_the_primary_version_id()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.FindByMovieId(_movie.Id))
                  .Returns(new List<MovieVersion> { primary, new MovieVersion { Id = 7, MovieId = _movie.Id } });

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Find(1))
                  .Returns(primary);

            Subject.GetVersionView(_movie, 1).Should().BeSameAs(_movie);
        }

        [Test]
        public void should_reject_a_version_of_another_movie()
        {
            var otherMovieVersion = new MovieVersion { Id = 9, MovieId = 99 };

            Mocker.GetMock<IMovieVersionRepository>()
                  .Setup(s => s.Find(9))
                  .Returns(otherMovieVersion);

            Assert.Throws<NzbDroneClientException>(() => Subject.GetVersionView(_movie, 9));
        }

        [Test]
        public void should_edit_a_version_without_changing_its_identity_or_file()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id, Name = "Original", QualityProfileId = 3 };
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id, Name = "Old name", QualityProfileId = 4, MovieFileId = 42 };
            GivenQualityProfile(3);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.Get(7)).Returns(additional);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.FindByMovieId(_movie.Id)).Returns(new List<MovieVersion> { primary, additional });

            Subject.UpdateVersion(7, "Director's Cut", 3, null);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(s => s.Update(It.Is<MovieVersion>(v => v.Id == 7 && v.Name == "Director's Cut" && v.QualityProfileId == 3 && v.MovieFileId == 42)), Times.Once());
        }

        [Test]
        public void should_save_primary_version_settings_without_loading_the_movie_service()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id, Name = "Original", QualityProfileId = 3 };
            GivenQualityProfile(4);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.Get(1)).Returns(primary);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.FindByMovieId(_movie.Id)).Returns(new List<MovieVersion> { primary });

            Subject.UpdateVersion(1, "Theatrical", 4, null);

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(s => s.Update(It.Is<MovieVersion>(v => v.Id == 1 && v.Name == "Theatrical" && v.QualityProfileId == 4)), Times.Once());
            Mocker.GetMock<IMovieRepository>().Verify(s => s.Get(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_persist_required_custom_formats_for_a_named_version()
        {
            GivenPrimaryVersion();
            GivenQualityProfile(3);
            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat> { new CustomFormat { Id = 42 } });

            Subject.AddVersion(_movie, 3, "Remaster", new List<int> { 42, 42 });

            Mocker.GetMock<IMovieVersionRepository>()
                  .Verify(s => s.Insert(It.Is<MovieVersion>(v => v.Name == "Remaster" && v.RequiredCustomFormatIds.Count == 1 && v.RequiredCustomFormatIds[0] == 42)), Times.Once());
        }

        [Test]
        public void should_reject_rules_using_a_removed_custom_format()
        {
            GivenPrimaryVersion();
            GivenQualityProfile(3);

            Assert.Throws<NzbDroneClientException>(() => Subject.AddVersion(_movie, 3, "Remaster", new List<int> { 42 }));
        }

        [Test]
        public void should_allow_several_named_versions_with_the_same_quality_profile()
        {
            GivenPrimaryVersion();
            GivenQualityProfile(3);

            Subject.AddVersion(_movie, 3, "Director's Cut");
            Subject.AddVersion(_movie, 3, "Theatrical");

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Insert(It.Is<MovieVersion>(m => m.Name == "Director's Cut" && m.QualityProfileId == 3)), Times.Once());
            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Insert(It.Is<MovieVersion>(m => m.Name == "Theatrical" && m.QualityProfileId == 3)), Times.Once());
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_require_a_version_name(string name)
        {
            GivenPrimaryVersion();
            Assert.Throws<NzbDroneClientException>(() => Subject.AddVersion(_movie, 3, name));
        }

        [Test]
        public void should_reject_removed_download_targets_even_when_a_cached_view_is_available()
        {
            var cachedView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = _movie.Id });

            Assert.Throws<NzbDroneClientException>(() => Subject.GetVersionView(cachedView, 7));
        }

        [Test]
        public void should_not_remove_the_primary_version()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id };

            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.Get(1)).Returns(primary);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.FindByMovieId(_movie.Id)).Returns(new List<MovieVersion> { primary });

            Assert.Throws<NzbDroneClientException>(() => Subject.RemoveVersion(1));

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(It.IsAny<MovieVersion>()), Times.Never());
        }

        [Test]
        public void should_not_remove_a_version_that_still_has_a_file()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id };
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id, MovieFileId = 42 };

            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.Get(7)).Returns(additional);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.FindByMovieId(_movie.Id)).Returns(new List<MovieVersion> { primary, additional });

            Assert.Throws<NzbDroneClientException>(() => Subject.RemoveVersion(7));

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(It.IsAny<MovieVersion>()), Times.Never());
        }

        [Test]
        public void should_remove_an_additional_version_without_file()
        {
            var primary = new MovieVersion { Id = 1, MovieId = _movie.Id };
            var additional = new MovieVersion { Id = 7, MovieId = _movie.Id };

            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.Get(7)).Returns(additional);
            Mocker.GetMock<IMovieVersionRepository>().Setup(s => s.FindByMovieId(_movie.Id)).Returns(new List<MovieVersion> { primary, additional });

            Subject.RemoveVersion(7);

            Mocker.GetMock<IMovieVersionRepository>().Verify(v => v.Delete(additional), Times.Once());
        }
    }
}
