using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class ExtraFileMovieFileMatcherFixture : CoreTest
    {
        private Movie _movie;
        private List<MovieFile> _movieFiles;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                MovieFileId = 10,
                Path = @"C:\Movies\Movie (2020)".AsOsAgnostic()
            };

            _movieFiles = new List<MovieFile>
            {
                new MovieFile { Id = 10, MovieId = 1, RelativePath = "Movie (2020).mkv" },
                new MovieFile { Id = 11, MovieId = 1, MovieVersionId = 7, RelativePath = "Movie (2020) - HD-1080p.mkv" }
            };
        }

        [TestCase("Movie (2020).en.srt", 10)]
        [TestCase("Movie (2020) - HD-1080p.en.srt", 11)]
        [TestCase("Movie (2020) - HD-1080p.nfo", 11)]
        [TestCase("Movie (2020).nfo", 10)]
        [TestCase("poster.jpg", 10)]
        public void should_attach_extra_file_to_the_file_it_was_named_after(string extraFileName, int movieFileId)
        {
            var extraFilePath = Path.Combine(_movie.Path, extraFileName);

            ExtraFileMovieFileMatcher.GetMovieFileId(_movie, _movieFiles, extraFilePath).Should().Be(movieFileId);
        }

        [Test]
        public void should_use_the_movie_file_when_the_movie_has_a_single_file()
        {
            var extraFilePath = Path.Combine(_movie.Path, "Something Else.en.srt");

            ExtraFileMovieFileMatcher.GetMovieFileId(_movie, new List<MovieFile> { _movieFiles[0] }, extraFilePath).Should().Be(10);
        }
    }
}
