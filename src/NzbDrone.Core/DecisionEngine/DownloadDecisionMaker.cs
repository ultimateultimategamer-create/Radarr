using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download.Aggregation;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine
{
    public interface IMakeDownloadDecision
    {
        List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false);
        List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase);
    }

    public class DownloadDecisionMaker : IMakeDownloadDecision
    {
        private readonly IEnumerable<IDownloadDecisionEngineSpecification> _specifications;
        private readonly IParsingService _parsingService;
        private readonly ICustomFormatCalculationService _formatCalculator;
        private readonly IRemoteMovieAggregationService _aggregationService;
        private readonly IMovieVersionService _movieVersionService;
        private readonly Logger _logger;

        public DownloadDecisionMaker(IEnumerable<IDownloadDecisionEngineSpecification> specifications,
                                     IParsingService parsingService,
                                     ICustomFormatCalculationService formatCalculator,
                                     IRemoteMovieAggregationService aggregationService,
                                     IMovieVersionService movieVersionService,
                                     Logger logger)
        {
            _specifications = specifications;
            _parsingService = parsingService;
            _formatCalculator = formatCalculator;
            _aggregationService = aggregationService;
            _movieVersionService = movieVersionService;
            _logger = logger;
        }

        public List<DownloadDecision> GetRssDecision(List<ReleaseInfo> reports, bool pushedRelease = false)
        {
            return GetDecisions(reports, pushedRelease).ToList();
        }

        public List<DownloadDecision> GetSearchDecision(List<ReleaseInfo> reports, SearchCriteriaBase searchCriteriaBase)
        {
            return GetDecisions(reports, false, searchCriteriaBase).ToList();
        }

        private IEnumerable<DownloadDecision> GetDecisions(List<ReleaseInfo> reports, bool pushedRelease = false, SearchCriteriaBase searchCriteria = null)
        {
            if (reports.Any())
            {
                _logger.ProgressInfo("Processing {0} releases", reports.Count);
            }
            else
            {
                _logger.ProgressInfo("No results found");
            }

            var reportNumber = 1;
            var versionsByMovie = new Dictionary<int, List<Movie>>();

            foreach (var report in reports)
            {
                var decisions = new List<DownloadDecision>();
                _logger.ProgressTrace("Processing release {0}/{1}", reportNumber, reports.Count);
                _logger.Debug("Processing release '{0}' from '{1}'", report.Title, report.Indexer);

                try
                {
                    var parsedMovieInfo = Parser.Parser.ParseMovieTitle(report.Title);

                    if (parsedMovieInfo != null && !parsedMovieInfo.PrimaryMovieTitle.IsNullOrWhiteSpace())
                    {
                        var remoteMovie = _parsingService.Map(parsedMovieInfo, report.ImdbId.ToString(), report.TmdbId, searchCriteria);
                        remoteMovie.Release = report;
                        remoteMovie.ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria);

                        if (remoteMovie.Movie == null)
                        {
                            decisions.Add(new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.UnknownMovie, pushedRelease ? "Unknown Movie. Unable to match to existing movie in Library using release title." : "Unknown Movie. Unable to match to correct movie using release title.")));
                        }
                        else
                        {
                            _aggregationService.Augment(remoteMovie);

                            remoteMovie.CustomFormats = _formatCalculator.ParseCustomFormat(remoteMovie, remoteMovie.Release.Size);
                            remoteMovie.DownloadAllowed = remoteMovie.Movie != null;

                            decisions.AddRange(GetDecisionsForVersions(remoteMovie, new ReleaseDecisionInformation(pushedRelease, searchCriteria), versionsByMovie));
                        }
                    }

                    if (searchCriteria != null)
                    {
                        if (parsedMovieInfo == null)
                        {
                            parsedMovieInfo = new ParsedMovieInfo
                            {
                                Languages = LanguageParser.ParseLanguages(report.Title),
                                Quality = QualityParser.ParseQuality(report.Title)
                            };
                        }

                        if (parsedMovieInfo.PrimaryMovieTitle.IsNullOrWhiteSpace())
                        {
                            var remoteMovie = new RemoteMovie
                            {
                                Release = report,
                                ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria),
                                ParsedMovieInfo = parsedMovieInfo,
                                Languages = parsedMovieInfo.Languages,
                            };

                            decisions = new List<DownloadDecision>
                            {
                                new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.UnableToParse, "Unable to parse release"))
                            };
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't process release.");

                    var remoteMovie = new RemoteMovie { Release = report, ReleaseSource = GetReleaseSource(pushedRelease, searchCriteria) };
                    decisions = new List<DownloadDecision>
                    {
                        new DownloadDecision(remoteMovie, new DownloadRejection(DownloadRejectionReason.Error, "Unexpected error processing release"))
                    };
                }

                reportNumber++;

                foreach (var decision in decisions)
                {
                    if (decision.Rejections.Any())
                    {
                        _logger.Debug("Release '{0}' from '{1}' rejected for the following reasons: {2}", report.Title, report.Indexer, string.Join(", ", decision.Rejections));
                    }
                    else if (decision.RemoteMovie.Movie is { IsVersionView: true })
                    {
                        _logger.Debug("Release '{0}' from '{1}' accepted for version {2} of the movie", report.Title, report.Indexer, decision.RemoteMovie.Movie.MovieVersionId);
                    }
                    else
                    {
                        _logger.Debug("Release '{0}' from '{1}' accepted", report.Title, report.Indexer);
                    }

                    yield return decision;
                }
            }
        }

        // A release is evaluated against each version of its movie, as each version has its own quality profile and file.
        // RSS and automatic searches keep every version that wants the release (or the primary version's rejections when none does),
        // interactive searches keep a single result per release: the first version that wants it.
        private List<DownloadDecision> GetDecisionsForVersions(RemoteMovie remoteMovie, ReleaseDecisionInformation information, Dictionary<int, List<Movie>> versionsByMovie)
        {
            var decisions = new List<DownloadDecision>();

            foreach (var movie in GetVersionsToEvaluate(remoteMovie.Movie, information.SearchCriteria, versionsByMovie))
            {
                var versionRemoteMovie = ReferenceEquals(movie, remoteMovie.Movie) ? remoteMovie : remoteMovie.CloneForMovie(movie);

                versionRemoteMovie.CustomFormatScore = movie.QualityProfile?.CalculateCustomFormatScore(versionRemoteMovie.CustomFormats) ?? 0;

                _logger.Trace("Custom Format Score of '{0}' [{1}] calculated for '{2}'", versionRemoteMovie.CustomFormatScore, versionRemoteMovie.CustomFormats?.ConcatToString(), remoteMovie.Release.Title);

                decisions.Add(GetDecisionForReport(versionRemoteMovie, information));
            }

            if (decisions.Count <= 1)
            {
                return decisions;
            }

            if (information.SearchCriteria is { InteractiveSearch: true })
            {
                return new List<DownloadDecision> { decisions.FirstOrDefault(d => d.Approved) ?? decisions.First() };
            }

            var qualified = decisions.Where(d => d.Approved || d.TemporarilyRejected).ToList();

            return qualified.Any() ? qualified : new List<DownloadDecision> { decisions.First() };
        }

        private List<Movie> GetVersionsToEvaluate(Movie movie, SearchCriteriaBase searchCriteria, Dictionary<int, List<Movie>> versionsByMovie)
        {
            // The search was for a specific version
            if (movie.IsVersionView || searchCriteria is { SingleMovieVersion: true })
            {
                return new List<Movie> { movie };
            }

            if (!versionsByMovie.TryGetValue(movie.Id, out var additionalVersions))
            {
                var views = _movieVersionService.GetAdditionalVersionViews(movie) ?? Enumerable.Empty<Movie>();

                additionalVersions = views.Where(v => v.QualityProfile != null).ToList();
                versionsByMovie[movie.Id] = additionalVersions;
            }

            var movies = new List<Movie> { movie };
            movies.AddRange(additionalVersions);

            return movies;
        }

        private DownloadDecision GetDecisionForReport(RemoteMovie remoteMovie, ReleaseDecisionInformation information)
        {
            var reasons = Array.Empty<DownloadRejection>();

            foreach (var specifications in _specifications.GroupBy(v => v.Priority).OrderBy(v => v.Key))
            {
                reasons = specifications.Select(c => EvaluateSpec(c, remoteMovie, information))
                                        .Where(c => c != null)
                                        .ToArray();

                if (reasons.Any())
                {
                    break;
                }
            }

            return new DownloadDecision(remoteMovie, reasons.ToArray());
        }

        private DownloadRejection EvaluateSpec(IDownloadDecisionEngineSpecification spec, RemoteMovie remoteMovie, ReleaseDecisionInformation information)
        {
            try
            {
                var result = spec.IsSatisfiedBy(remoteMovie, information);

                if (!result.Accepted)
                {
                    return new DownloadRejection(result.Reason, result.Message, spec.Type);
                }
            }
            catch (NotImplementedException)
            {
                _logger.Trace("Spec " + spec.GetType().Name + " does not care about movies.");
            }
            catch (Exception e)
            {
                e.Data.Add("report", remoteMovie.Release.ToJson());
                e.Data.Add("parsed", remoteMovie.ParsedMovieInfo.ToJson());
                _logger.Error(e, "Couldn't evaluate decision on {0}, with spec: {1}", remoteMovie.Release.Title, spec.GetType().Name);
                return new DownloadRejection(DownloadRejectionReason.DecisionError, $"{spec.GetType().Name}: {e.Message}");
            }

            return null;
        }

        private ReleaseSourceType GetReleaseSource(bool pushedRelease, SearchCriteriaBase searchCriteria = null)
        {
            if (searchCriteria == null)
            {
                return pushedRelease ? ReleaseSourceType.ReleasePush : ReleaseSourceType.Rss;
            }

            if (searchCriteria.InteractiveSearch)
            {
                return ReleaseSourceType.InteractiveSearch;
            }
            else if (searchCriteria.UserInvokedSearch)
            {
                return ReleaseSourceType.UserInvokedSearch;
            }
            else
            {
                return ReleaseSourceType.Search;
            }
        }
    }
}
