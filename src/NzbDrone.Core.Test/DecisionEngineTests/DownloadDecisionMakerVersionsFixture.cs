using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class DownloadDecisionMakerVersionsFixture : CoreTest<DownloadDecisionMaker>
    {
        private List<ReleaseInfo> _reports;
        private RemoteMovie _remoteMovie;
        private Movie _movie;
        private Movie _versionView;
        private Mock<IDownloadDecisionEngineSpecification> _spec;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                QualityProfileId = 1,
                QualityProfile = new QualityProfile { Id = 1, Name = "Ultra-HD" },
                Path = "/Movies/Trolls (2016)"
            };

            _versionView = _movie.ForVersion(new MovieVersion { Id = 7, MovieId = 1, QualityProfileId = 2, Path = _movie.Path });
            _versionView.QualityProfile = new QualityProfile { Id = 2, Name = "HD-1080p" };

            _reports = new List<ReleaseInfo> { new ReleaseInfo { Title = "Trolls.2016.1080p.WEB-DL.DD5.1.H264-FGT" } };
            _remoteMovie = new RemoteMovie
            {
                Movie = _movie,
                ParsedMovieInfo = new ParsedMovieInfo()
            };

            _spec = new Mock<IDownloadDecisionEngineSpecification>();
            Mocker.SetConstant<IEnumerable<IDownloadDecisionEngineSpecification>>(new[] { _spec.Object });

            Mocker.GetMock<IParsingService>()
                  .Setup(c => c.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_remoteMovie);

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(c => c.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat>());

            Mocker.GetMock<IMovieVersionService>()
                  .Setup(c => c.GetAdditionalVersionViews(It.IsAny<Movie>()))
                  .Returns(new List<Movie> { _versionView });
        }

        private void GivenPrimaryVersionAccepts(bool accepts)
        {
            _spec.Setup(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 0), It.IsAny<ReleaseDecisionInformation>()))
                 .Returns(accepts ? DownloadSpecDecision.Accept() : DownloadSpecDecision.Reject(DownloadRejectionReason.QualityNotWanted, "primary"));
        }

        private void GivenAdditionalVersionAccepts(bool accepts)
        {
            _spec.Setup(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 7), It.IsAny<ReleaseDecisionInformation>()))
                 .Returns(accepts ? DownloadSpecDecision.Accept() : DownloadSpecDecision.Reject(DownloadRejectionReason.QualityNotWanted, "additional"));
        }

        [Test]
        public void should_preserve_standard_quality_rejections_for_a_movie_without_additional_versions()
        {
            GivenPrimaryVersionAccepts(false);
            _movie.QualityProfile.Items = new List<QualityProfileQualityItem>
            {
                new QualityProfileQualityItem { Quality = Quality.Bluray1080p, Allowed = false }
            };
            _remoteMovie.ParsedMovieInfo.Quality = new QualityModel(Quality.Bluray1080p);
            Mocker.GetMock<IMovieVersionService>().Setup(s => s.GetAdditionalVersionViews(_movie)).Returns(new List<Movie>());

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Rejections.Single().Reason.Should().Be(DownloadRejectionReason.QualityNotWanted);
            decision.Rejections.Single().Message.Should().Be("primary");
            _spec.Verify(s => s.IsSatisfiedBy(_remoteMovie, It.IsAny<ReleaseDecisionInformation>()), Times.Once());
        }

        [Test]
        public void should_run_standard_checks_even_when_a_version_does_not_match()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(false);
            _versionView.Version.RequiredCustomFormatIds.Add(42);

            Subject.GetRssDecision(_reports);

            _spec.Verify(s => s.IsSatisfiedBy(It.Is<RemoteMovie>(m => m.Movie.MovieVersionId == 7), It.IsAny<ReleaseDecisionInformation>()), Times.Once());
        }

        [Test]
        public void should_load_primary_version_metadata_once_per_movie_per_rss_batch()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);
            _reports.Add(new ReleaseInfo { Title = _reports[0].Title });
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(() => new RemoteMovie
                  {
                      Movie = new Movie { Id = _movie.Id, QualityProfileId = _movie.QualityProfileId, QualityProfile = _movie.QualityProfile },
                      ParsedMovieInfo = new ParsedMovieInfo()
                  });

            Subject.GetRssDecision(_reports);

            Mocker.GetMock<IMovieVersionService>().Verify(s => s.GetPrimary(_movie.Id), Times.Once());
            Mocker.GetMock<IMovieVersionService>().Verify(s => s.GetAdditionalVersionViews(It.IsAny<Movie>()), Times.Once());
        }

        [Test]
        public void should_evaluate_release_against_every_version()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(false);

            Subject.GetRssDecision(_reports);

            _spec.Verify(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 0), It.IsAny<ReleaseDecisionInformation>()), Times.Once());
            _spec.Verify(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 7), It.IsAny<ReleaseDecisionInformation>()), Times.Once());
        }

        [Test]
        public void should_return_a_decision_for_each_version_that_wants_the_release()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);

            var decisions = Subject.GetRssDecision(_reports);

            decisions.Should().HaveCount(2);
            decisions.Should().OnlyContain(d => d.Approved);
            decisions.Select(d => d.RemoteMovie.Movie.MovieVersionId).Should().BeEquivalentTo(new[] { 0, 7 });
        }

        [Test]
        public void should_grab_release_for_additional_version_when_primary_version_rejects_it()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(true);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(7);
            decision.RemoteMovie.Movie.QualityProfile.Name.Should().Be("HD-1080p");
        }

        [Test]
        public void should_return_primary_version_rejections_when_no_version_wants_the_release()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(false);

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Approved.Should().BeFalse();
            decision.RemoteMovie.Movie.Should().BeSameAs(_movie);
            decision.Rejections.Single().Message.Should().Be("primary");
        }

        [Test]
        public void should_return_one_result_per_release_for_interactive_search()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);

            var criteria = new MovieSearchCriteria { Movie = _movie, InteractiveSearch = true };

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(0);
        }

        [Test]
        public void should_offer_release_for_the_version_that_wants_it_in_interactive_search()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(true);

            var criteria = new MovieSearchCriteria { Movie = _movie, InteractiveSearch = true };

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.Approved.Should().BeTrue();
            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(7);
        }

        [Test]
        public void should_only_evaluate_the_searched_version()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);

            _remoteMovie.Movie = _versionView;

            var criteria = new MovieSearchCriteria { Movie = _versionView, InteractiveSearch = true };

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(7);
            _spec.Verify(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 0), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_only_evaluate_the_primary_version_when_it_is_searched_on_its_own()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(true);

            var criteria = new MovieSearchCriteria { Movie = _movie, InteractiveSearch = true, SingleMovieVersion = true };

            var decision = Subject.GetSearchDecision(_reports, criteria).Single();

            decision.Approved.Should().BeFalse();
            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(0);
            _spec.Verify(c => c.IsSatisfiedBy(It.Is<RemoteMovie>(r => r.Movie.MovieVersionId == 7), It.IsAny<ReleaseDecisionInformation>()), Times.Never());
        }

        [Test]
        public void should_match_a_named_version_using_its_required_custom_formats()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);
            _versionView.Version.RequiredCustomFormatIds = new List<int> { 42 };
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(c => c.ParseCustomFormat(It.IsAny<RemoteMovie>(), It.IsAny<long>()))
                  .Returns(new List<CustomFormat> { new CustomFormat { Id = 42 } });

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.RemoteMovie.Movie.MovieVersionId.Should().Be(7);
            decision.Approved.Should().BeTrue();
        }

        [Test]
        public void should_not_automatically_choose_between_named_versions_with_the_same_profile()
        {
            GivenPrimaryVersionAccepts(true);
            GivenAdditionalVersionAccepts(true);
            _versionView.QualityProfileId = _movie.QualityProfileId;

            Subject.GetRssDecision(_reports).Should().OnlyContain(d => !d.Approved);
        }

        [Test]
        public void should_allow_an_explicit_search_for_a_named_version_with_a_shared_profile()
        {
            GivenAdditionalVersionAccepts(true);
            _versionView.QualityProfileId = _movie.QualityProfileId;
            _remoteMovie.Movie = _versionView;

            var criteria = new MovieSearchCriteria { Movie = _versionView, InteractiveSearch = true, SingleMovieVersion = true };

            Subject.GetSearchDecision(_reports, criteria).Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_skip_versions_without_quality_profile()
        {
            GivenPrimaryVersionAccepts(false);
            GivenAdditionalVersionAccepts(true);

            _versionView.QualityProfile = null;

            var decision = Subject.GetRssDecision(_reports).Single();

            decision.Approved.Should().BeFalse();
            decision.RemoteMovie.Movie.Should().BeSameAs(_movie);
        }
    }
}
