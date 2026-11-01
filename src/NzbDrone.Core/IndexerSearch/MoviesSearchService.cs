using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.IndexerSearch
{
    public class MovieSearchService : IExecute<MoviesSearchCommand>, IExecute<MissingMoviesSearchCommand>, IExecute<CutoffUnmetMoviesSearchCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IMovieCutoffService _movieCutoffService;
        private readonly ISearchForReleases _releaseSearchService;
        private readonly IProcessDownloadDecisions _processDownloadDecisions;
        private readonly IQueueService _queueService;
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IQualityProfileService _qualityProfileService;
        private readonly IUpgradableSpecification _upgradableSpecification;
        private readonly Logger _logger;

        public MovieSearchService(IMovieService movieService,
                                   IMovieCutoffService movieCutoffService,
                                   ISearchForReleases releaseSearchService,
                                   IProcessDownloadDecisions processDownloadDecisions,
                                   IQueueService queueService,
                                   IMovieVersionService movieVersionService,
                                   IMediaFileService mediaFileService,
                                   IQualityProfileService qualityProfileService,
                                   IUpgradableSpecification upgradableSpecification,
                                   Logger logger)
        {
            _movieService = movieService;
            _movieCutoffService = movieCutoffService;
            _releaseSearchService = releaseSearchService;
            _processDownloadDecisions = processDownloadDecisions;
            _queueService = queueService;
            _movieVersionService = movieVersionService;
            _mediaFileService = mediaFileService;
            _qualityProfileService = qualityProfileService;
            _upgradableSpecification = upgradableSpecification;
            _logger = logger;
        }

        public void Execute(MoviesSearchCommand message)
        {
            var userInvokedSearch = message.Trigger == CommandTrigger.Manual;

            var movies = _movieService.GetMovies(message.MovieIds)
                .Where(m => (m.Monitored && m.IsAvailable()) || userInvokedSearch)
                .ToList();

            SearchForBulkMovies(movies, userInvokedSearch).GetAwaiter().GetResult();
        }

        public void Execute(MissingMoviesSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Movie>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            pagingSpec.FilterExpressions.Add(v => v.Monitored == true);

            var movies = _movieService.MoviesWithoutFiles(pagingSpec).Records.ToList();

            var queued = GetQueuedVersions();
            var missing = movies.Where(e => !queued.Contains((e.Id, 0))).ToList();

            // Additional versions without a file are searched for as well, except pinned versions which only get the releases picked for them
            var additionalVersions = _movieVersionService.GetAllAdditionalVersions() ?? new List<MovieVersion>();
            var missingVersionMovieIds = additionalVersions.Where(v => !v.Pinned && !v.HasFile && !queued.Contains((v.MovieId, v.Id)))
                                                           .Select(v => v.MovieId)
                                                           .ToList();

            missing.AddRange(GetMonitoredMovies(missingVersionMovieIds, missing));

            SearchForBulkMovies(missing, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        public void Execute(CutoffUnmetMoviesSearchCommand message)
        {
            var pagingSpec = new PagingSpec<Movie>
            {
                Page = 1,
                PageSize = 100000,
                SortDirection = SortDirection.Ascending,
                SortKey = "Id"
            };

            pagingSpec.FilterExpressions.Add(v => v.Monitored == true);

            var movies = _movieCutoffService.MoviesWhereCutoffUnmet(pagingSpec).Records.ToList();

            var queued = GetQueuedVersions();
            var missing = movies.Where(e => !queued.Contains((e.Id, 0))).ToList();

            // Additional versions whose file doesn't meet their own quality profile's cutoff are searched for as well
            missing.AddRange(GetMonitoredMovies(GetVersionsWhereCutoffUnmet(queued), missing));

            SearchForBulkMovies(missing, message.Trigger == CommandTrigger.Manual).GetAwaiter().GetResult();
        }

        private HashSet<(int MovieId, int MovieVersionId)> GetQueuedVersions()
        {
            return _queueService.GetQueue()
                                .Where(q => q.Movie != null)
                                .Select(q => (MovieId: q.Movie.Id, MovieVersionId: q.Movie.MovieVersionId))
                                .ToHashSet();
        }

        private List<int> GetVersionsWhereCutoffUnmet(HashSet<(int MovieId, int MovieVersionId)> queued)
        {
            var versions = (_movieVersionService.GetAllAdditionalVersions() ?? new List<MovieVersion>())
                .Where(v => !v.Pinned && v.HasFile && !queued.Contains((v.MovieId, v.Id)))
                .ToList();

            if (versions.Empty())
            {
                return new List<int>();
            }

            var profiles = _qualityProfileService.All().ToDictionary(p => p.Id);
            var files = _mediaFileService.GetMovies(versions.Select(v => v.MovieFileId)).ToDictionary(f => f.Id);

            return versions.Where(v => profiles.ContainsKey(v.QualityProfileId) &&
                                       files.ContainsKey(v.MovieFileId) &&
                                       _upgradableSpecification.QualityCutoffNotMet(profiles[v.QualityProfileId], files[v.MovieFileId].Quality))
                           .Select(v => v.MovieId)
                           .ToList();
        }

        private List<Movie> GetMonitoredMovies(List<int> movieIds, List<Movie> alreadyIncluded)
        {
            var ids = movieIds.Distinct()
                              .Except(alreadyIncluded.Select(m => m.Id))
                              .ToList();

            if (ids.Empty())
            {
                return new List<Movie>();
            }

            return _movieService.GetMovies(ids).Where(m => m.Monitored).ToList();
        }

        private async Task SearchForBulkMovies(List<Movie> movies, bool userInvokedSearch)
        {
            _logger.ProgressInfo("Performing search for {0} movies", movies.Count);
            var downloadedCount = 0;

            foreach (var movieId in movies.GroupBy(e => e.Id).OrderBy(g => g.Min(m => m.LastSearchTime ?? DateTime.MinValue)))
            {
                List<DownloadDecision> decisions;

                try
                {
                    decisions = await _releaseSearchService.MovieSearch(movieId.Key, userInvokedSearch, false);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to search for movie: [{0}]", movieId.Key);
                    continue;
                }

                var processDecisions = await _processDownloadDecisions.ProcessDecisions(decisions);

                downloadedCount += processDecisions.Grabbed.Count;
            }

            _logger.ProgressInfo("Completed search for {0} movies. {1} reports downloaded.", movies.Count, downloadedCount);
        }
    }
}
