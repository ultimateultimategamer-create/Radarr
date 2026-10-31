using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    [TestFixture]

    public class MovieRepositoryFixture : DbTest<MovieRepository, Movie>
    {
        private IQualityProfileRepository _profileRepository;

        [SetUp]
        public void Setup()
        {
            _profileRepository = Mocker.Resolve<QualityProfileRepository>();
            Mocker.SetConstant<IQualityProfileRepository>(_profileRepository);

            Mocker.GetMock<ICustomFormatService>()
                .Setup(x => x.All())
                .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_load_quality_profile()
        {
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.Bluray1080p, Quality.DVD, Quality.HDTV720p),
                FormatItems = CustomFormatsTestHelpers.GetDefaultFormatItems(),
                MinFormatScore = 0,
                Cutoff = Quality.Bluray1080p.Id,
                Name = "TestProfile"
            };

            _profileRepository.Insert(profile);

            var movie = Builder<Movie>.CreateNew().BuildNew();
            movie.QualityProfileId = profile.Id;

            Subject.Insert(movie);

            Subject.All().Single().QualityProfile.Should().NotBeNull();
        }

        [Test]
        public void should_load_the_movie_own_file_when_its_versions_have_other_files()
        {
            var profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.Bluray1080p, Quality.DVD, Quality.HDTV720p),
                FormatItems = CustomFormatsTestHelpers.GetDefaultFormatItems(),
                MinFormatScore = 0,
                Cutoff = Quality.Bluray1080p.Id,
                Name = "TestProfile"
            };

            _profileRepository.Insert(profile);

            var movieMetadata = Builder<MovieMetadata>.CreateNew()
                                                      .With(m => m.TmdbId = 123456)
                                                      .BuildNew();

            Db.Insert(movieMetadata);

            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.QualityProfileId = profile.Id)
                                      .With(m => m.MovieMetadataId = movieMetadata.Id)
                                      .With(m => m.MovieFileId = 0)
                                      .BuildNew();

            Subject.Insert(movie);

            // The additional version's file is older than the movie's own file
            var versionFile = Builder<MovieFile>.CreateNew()
                                                .With(f => f.MovieId = movie.Id)
                                                .With(f => f.MovieVersionId = 7)
                                                .With(f => f.Quality = new QualityModel(Quality.HDTV720p))
                                                .With(f => f.Languages = new List<Language> { Language.English })
                                                .BuildNew();

            var movieFile = Builder<MovieFile>.CreateNew()
                                              .With(f => f.MovieId = movie.Id)
                                              .With(f => f.Quality = new QualityModel(Quality.Bluray1080p))
                                              .With(f => f.Languages = new List<Language> { Language.English })
                                              .BuildNew();

            Db.Insert(versionFile);
            Db.Insert(movieFile);

            movie.MovieFileId = movieFile.Id;
            Subject.Update(movie);

            Subject.Get(movie.Id).MovieFile.Id.Should().Be(movieFile.Id);
            Subject.FindByTmdbId(123456).MovieFile.Id.Should().Be(movieFile.Id);
        }
    }
}
