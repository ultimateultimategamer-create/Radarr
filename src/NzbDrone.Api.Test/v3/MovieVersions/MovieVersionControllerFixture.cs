using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Test.Common;
using Radarr.Api.V3.MovieVersions;

namespace NzbDrone.Api.Test.v3.MovieVersions
{
    [TestFixture]
    public class MovieVersionControllerFixture : TestBase<MovieVersionController>
    {
        private MovieVersion _version;
        private Movie _movie;
        private MovieVersionResource _resource;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 5, QualityProfileId = 3 };
            _version = new MovieVersion { Id = 1, MovieId = 5, Name = "Theatrical", QualityProfileId = 4 };
            _resource = new MovieVersionResource { Id = 1, MovieId = 5, Name = "Theatrical", QualityProfileId = 4 };
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(5)).Returns(_movie);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.Get(It.IsAny<int>())).Returns(_version);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.GetPrimary(5)).Returns(new MovieVersion { Id = 1, MovieId = 5 });
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.UpdateVersion(It.IsAny<int>(), _resource.Name, _resource.QualityProfileId, _resource.RequiredCustomFormatIds))
                  .Returns(_version);
            Subject.Url = new Mock<IUrlHelper>().Object;
        }

        [Test]
        public void should_coordinate_primary_profile_updates_through_the_movie_service()
        {
            Subject.UpdateMovieVersion(_resource);

            Mocker.GetMock<IMovieService>()
                  .Verify(s => s.UpdateMovie(It.Is<Movie>(m => m.Id == 5 && m.QualityProfileId == 4)), Times.Once());
        }

        [Test]
        public void should_not_change_the_movie_profile_when_editing_an_additional_version()
        {
            _version.Id = 7;
            _resource.Id = 7;

            Subject.UpdateMovieVersion(_resource);

            _movie.QualityProfileId.Should().Be(3);
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_validate_the_version_before_updating_the_movie()
        {
            Mocker.GetMock<IMovieVersionService>()
                  .Setup(s => s.UpdateVersion(_resource.Id, _resource.Name, _resource.QualityProfileId, _resource.RequiredCustomFormatIds))
                  .Throws(new NzbDroneClientException(HttpStatusCode.BadRequest, "Invalid version"));

            Assert.Throws<NzbDroneClientException>(() => Subject.UpdateMovieVersion(_resource));

            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(It.IsAny<Movie>()), Times.Never());
        }
    }
}
