using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.MediaFiles
{
    public interface IRenameMovieFileService
    {
        List<RenameMovieFilePreview> GetRenamePreviews(List<int> movieIds);
    }

    public class RenameMovieFileService : IRenameMovieFileService,
                                          IExecute<RenameFilesCommand>,
                                          IExecute<RenameMovieCommand>
    {
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMoveMovieFiles _movieFileMover;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly IMovieVersionFileNameService _movieVersionFileNameService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RenameMovieFileService(IMovieService movieService,
                                      IMediaFileService mediaFileService,
                                      IMovieVersionService movieVersionService,
                                      IMoveMovieFiles movieFileMover,
                                      IEventAggregator eventAggregator,
                                      IBuildFileNames filenameBuilder,
                                      IMovieVersionFileNameService movieVersionFileNameService,
                                      IDiskProvider diskProvider,
                                      Logger logger)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _movieVersionService = movieVersionService;
            _movieFileMover = movieFileMover;
            _eventAggregator = eventAggregator;
            _filenameBuilder = filenameBuilder;
            _movieVersionFileNameService = movieVersionFileNameService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public List<RenameMovieFilePreview> GetRenamePreviews(List<int> movieIds)
        {
            var movies = _movieService.GetMovies(movieIds);
            var movieFiles = _mediaFileService.GetFilesByMovies(movieIds).ToLookup(f => f.MovieId);

            return movies.SelectMany(movie => GetPreviews(movie, movieFiles[movie.Id].ToList()))
                         .ToList();
        }

        // Covers the files of every version of the movie, each one named with its own version's quality profile
        private IEnumerable<RenameMovieFilePreview> GetPreviews(Movie movie, List<MovieFile> files)
        {
            var versionViews = GetVersionViews(movie);
            var additionalVersionIds = versionViews.Where(v => v.IsVersionView).Select(v => v.MovieVersionId).ToList();

            foreach (var file in files)
            {
                var fileMovie = GetMovieForFile(versionViews, additionalVersionIds, file);
                var movieFilePath = Path.Combine(fileMovie.Path, file.RelativePath);

                var newName = BuildFileName(fileMovie, file);
                var newPath = _filenameBuilder.BuildFilePath(fileMovie, newName, Path.GetExtension(movieFilePath));

                if (!movieFilePath.PathEquals(newPath, StringComparison.Ordinal))
                {
                    yield return new RenameMovieFilePreview
                    {
                        MovieId = movie.Id,
                        MovieFileId = file.Id,
                        ExistingPath = file.RelativePath,

                        NewPath = fileMovie.Path.GetRelativePath(newPath)
                    };
                }
            }
        }

        private List<RenamedMovieFile> RenameFiles(List<MovieFile> movieFiles, Movie movie)
        {
            var renamed = new List<RenamedMovieFile>();
            var versionViews = GetVersionViews(movie);
            var additionalVersionIds = versionViews.Where(v => v.IsVersionView).Select(v => v.MovieVersionId).ToList();

            foreach (var movieFile in movieFiles)
            {
                var fileMovie = GetMovieForFile(versionViews, additionalVersionIds, movieFile);
                var previousRelativePath = movieFile.RelativePath;
                var previousPath = Path.Combine(fileMovie.Path, movieFile.RelativePath);

                try
                {
                    _logger.Debug("Renaming movie file: {0}", movieFile);
                    _movieFileMover.MoveMovieFile(movieFile, fileMovie);

                    _mediaFileService.Update(movieFile);

                    // A version view isn't the movie itself, saving it would overwrite the movie with the version's values
                    if (!fileMovie.IsVersionView)
                    {
                        _movieService.UpdateMovie(fileMovie);
                    }

                    renamed.Add(new RenamedMovieFile
                                {
                                    MovieFile = movieFile,
                                    PreviousRelativePath = previousRelativePath,
                                    PreviousPath = previousPath
                                });

                    _logger.Debug("Renamed movie file: {0}", movieFile);

                    _eventAggregator.PublishEvent(new MovieFileRenamedEvent(fileMovie, movieFile, previousPath));
                }
                catch (FileAlreadyExistsException ex)
                {
                    _logger.Warn("File not renamed, there is already a file at the destination: {0}", ex.Filename);
                }
                catch (SameFilenameException ex)
                {
                    _logger.Debug("File not renamed, source and destination are the same: {0}", ex.Filename);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to rename file: {0}", previousPath);
                }
            }

            if (renamed.Any())
            {
                _diskProvider.RemoveEmptySubfolders(movie.Path);

                _eventAggregator.PublishEvent(new MovieRenamedEvent(movie, renamed));
            }

            return renamed;
        }

        public void Execute(RenameFilesCommand message)
        {
            var movie = _movieService.GetMovie(message.MovieId);
            var movieFiles = _mediaFileService.GetMovies(message.Files).Where(f => f.MovieId == movie.Id).ToList();

            _logger.ProgressInfo("Renaming {0} files for {1}", movieFiles.Count, movie.Title);
            var renamedFiles = RenameFiles(movieFiles, movie);
            _logger.ProgressInfo("{0} selected movie files renamed for {1}", renamedFiles.Count, movie.Title);

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }

        public void Execute(RenameMovieCommand message)
        {
            _logger.Debug("Renaming movie files for selected movie");
            var moviesToRename = _movieService.GetMovies(message.MovieIds);

            foreach (var movie in moviesToRename)
            {
                var movieFiles = _mediaFileService.GetFilesByMovie(movie.Id);
                _logger.ProgressInfo("Renaming movie files for {0}", movie.Title);
                var renamedFiles = RenameFiles(movieFiles, movie);
                _logger.ProgressInfo("{0} movie files renamed for {1}", renamedFiles.Count, movie.Title);
            }

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }

        // The movie (primary version) followed by a view for each additional version
        private List<Movie> GetVersionViews(Movie movie)
        {
            var views = _movieVersionService.GetAllVersionViews(movie);

            return views == null || views.Empty() ? new List<Movie> { movie } : views;
        }

        private static Movie GetMovieForFile(List<Movie> versionViews, List<int> additionalVersionIds, MovieFile movieFile)
        {
            var versionId = movieFile.GetVersionId(additionalVersionIds);

            return versionViews.FirstOrDefault(v => v.MovieVersionId == versionId) ?? versionViews.First();
        }

        private string BuildFileName(Movie movie, MovieFile movieFile)
        {
            var fileName = _filenameBuilder.BuildFileName(movie, movieFile);
            var uniqueFileName = _movieVersionFileNameService.GetUniqueFileName(movie, movieFile, fileName);

            return uniqueFileName.IsNullOrWhiteSpace() ? fileName : uniqueFileName;
        }
    }
}
