using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class AddMissingPrimaryMovieVersionsFixture : DbTest<AddMissingPrimaryMovieVersions, MovieVersion>
    {
        [Test]
        public void should_add_primary_version_for_movie_without_versions()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.QualityProfileId = 3)
                                      .With(m => m.Path = "/Movies/Movie (2020)")
                                      .With(m => m.MovieFileId = 12)
                                      .BuildNew();

            Db.Insert(movie);
            Subject.Clean();

            var version = AllStoredModels.Single();

            version.MovieId.Should().Be(movie.Id);
            version.Name.Should().Be("Original");
            version.QualityProfileId.Should().Be(3);
            version.Path.Should().Be("/Movies/Movie (2020)");
            version.MovieFileId.Should().Be(12);
        }

        [Test]
        public void should_not_add_a_version_to_movie_that_already_has_one()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .BuildNew();

            Db.Insert(movie);

            var version = Builder<MovieVersion>.CreateNew()
                                               .With(v => v.MovieId = movie.Id)
                                               .With(v => v.MovieFileId = movie.MovieFileId)
                                               .BuildNew();

            Db.Insert(version);
            Subject.Clean();

            AllStoredModels.Should().HaveCount(1);
        }

        [Test]
        public void should_repair_blank_primary_names_without_renaming_other_versions()
        {
            var movie = Builder<Movie>.CreateNew().BuildNew();
            Db.Insert(movie);
            var primary = new MovieVersion { MovieId = movie.Id, Name = "", QualityProfileId = movie.QualityProfileId };
            var additional = new MovieVersion { MovieId = movie.Id, Name = "Director's Cut", QualityProfileId = movie.QualityProfileId };
            Db.Insert(primary);
            Db.Insert(additional);

            Subject.Clean();

            AllStoredModels.Single(v => v.Id == primary.Id).Name.Should().Be("Original");
            AllStoredModels.Single(v => v.Id == additional.Id).Name.Should().Be("Director's Cut");
        }

        [Test]
        public void should_keep_pinned_versions_on_their_movie_quality_profile()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.QualityProfileId = 3)
                                      .BuildNew();

            Db.Insert(movie);

            var primary = new MovieVersion { MovieId = movie.Id, Name = "Original", QualityProfileId = 3, Path = movie.Path };
            var pinned = new MovieVersion { MovieId = movie.Id, Name = "IMAX", QualityProfileId = 5, Pinned = true, Path = movie.Path };
            var automatic = new MovieVersion { MovieId = movie.Id, Name = "4K", QualityProfileId = 6, Path = movie.Path };
            Db.Insert(primary);
            Db.Insert(pinned);
            Db.Insert(automatic);

            Subject.Clean();

            AllStoredModels.Single(v => v.Id == pinned.Id).QualityProfileId.Should().Be(3);
            AllStoredModels.Single(v => v.Id == automatic.Id).QualityProfileId.Should().Be(6);
        }

        [Test]
        public void should_sync_primary_version_file_with_the_movie()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.MovieFileId = 20)
                                      .BuildNew();

            Db.Insert(movie);

            var primary = Builder<MovieVersion>.CreateNew()
                                               .With(v => v.MovieId = movie.Id)
                                               .With(v => v.MovieFileId = 0)
                                               .BuildNew();

            var additional = Builder<MovieVersion>.CreateNew()
                                                  .With(v => v.MovieId = movie.Id)
                                                  .With(v => v.MovieFileId = 30)
                                                  .BuildNew();

            Db.Insert(primary);
            Db.Insert(additional);
            Subject.Clean();

            AllStoredModels.Single(v => v.Id == primary.Id).MovieFileId.Should().Be(20);
            AllStoredModels.Single(v => v.Id == additional.Id).MovieFileId.Should().Be(30);
        }
    }
}
