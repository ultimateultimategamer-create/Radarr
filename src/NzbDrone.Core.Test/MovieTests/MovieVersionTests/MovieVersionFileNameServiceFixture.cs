using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieVersionTests
{
    [TestFixture]
    public class MovieVersionFileNameServiceFixture : CoreTest<MovieVersionFileNameService>
    {
        private Movie _movie;
        private Movie _versionView;
        private MovieFile _primaryFile;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 5,
                QualityProfileId = 1,
                QualityProfile = new QualityProfile { Id = 1, Name = "Ultra-HD" },
                Path = "/Movies/Movie (2020)"
            };

            _versionView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 5, QualityProfileId = 2, Path = _movie.Path });
            _versionView.QualityProfile = new QualityProfile { Id = 2, Name = "HD-1080p" };

            _primaryFile = new MovieFile { Id = 10, MovieId = 5, MovieVersionId = 0, RelativePath = "Movie (2020).mkv" };

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetByMovieId(5))
                  .Returns(new List<MovieVersion>
                  {
                      new MovieVersion { Id = 3, MovieId = 5, QualityProfileId = 1 },
                      new MovieVersion { Id = 7, MovieId = 5, QualityProfileId = 2 }
                  });

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetFilesByMovie(5))
                  .Returns(new List<MovieFile> { _primaryFile });
        }

        [Test]
        public void should_keep_the_name_when_no_other_version_uses_it()
        {
            Subject.GetUniqueFileName(_versionView, new MovieFile(), "Movie (2020) Bluray-1080p").Should().Be("Movie (2020) Bluray-1080p");
        }

        [Test]
        public void should_add_the_version_quality_profile_when_another_version_uses_the_name()
        {
            Subject.GetUniqueFileName(_versionView, new MovieFile(), "Movie (2020)").Should().Be("Movie (2020) - HD-1080p");
        }

        [Test]
        public void should_not_count_the_file_itself_as_another_version()
        {
            Subject.GetUniqueFileName(_movie, _primaryFile, "Movie (2020)").Should().Be("Movie (2020)");
        }

        [Test]
        public void should_not_count_files_of_the_same_version()
        {
            var oldFile = new MovieFile { Id = 11, MovieId = 5, MovieVersionId = 7, RelativePath = "Movie (2020) - Old.mkv" };

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(s => s.GetFilesByMovie(5))
                  .Returns(new List<MovieFile> { oldFile });

            Subject.GetUniqueFileName(_versionView, new MovieFile(), "Movie (2020) - Old").Should().Be("Movie (2020) - Old");
        }

        [Test]
        public void should_keep_the_name_for_a_movie_with_a_single_version()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetByMovieId(5))
                  .Returns(new List<MovieVersion> { new MovieVersion { Id = 3, MovieId = 5 } });

            Subject.GetUniqueFileName(_movie, new MovieFile(), "Movie (2020)").Should().Be("Movie (2020)");
        }

        [Test]
        public void should_use_the_version_name_when_profiles_are_shared()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.GetByMovieId(5))
                  .Returns(new List<MovieVersion>
                  {
                      new MovieVersion { Id = 3, MovieId = 5, Name = "Theatrical", QualityProfileId = 1 },
                      new MovieVersion { Id = 7, MovieId = 5, Name = "Director's Cut", QualityProfileId = 1 }
                  });

            Subject.GetUniqueFileName(_versionView, new MovieFile(), "Movie (2020)").Should().Be("Movie (2020) - Director's Cut");
        }

        [Test]
        public void should_compare_names_ignoring_case()
        {
            Subject.GetUniqueFileName(_versionView, new MovieFile(), "MOVIE (2020)").Should().Be("MOVIE (2020) - HD-1080p");
        }
    }
}
