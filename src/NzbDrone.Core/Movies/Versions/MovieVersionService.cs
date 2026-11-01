using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    public interface IMovieVersionService
    {
        MovieVersion Get(int id);
        MovieVersion Find(int id);
        List<MovieVersion> GetByMovieId(int movieId);
        List<MovieVersion> GetByMovieIds(List<int> movieIds);
        List<MovieVersion> GetAllAdditionalVersions();
        MovieVersion FindByPath(string path);
        MovieVersion GetPrimary(int movieId);
        IEnumerable<Movie> GetAdditionalVersionViews(Movie movie);
        List<Movie> GetAllVersionViews(Movie movie);
        IEnumerable<int> GetAdditionalVersionIds(int movieId);
        IEnumerable<int> GetVersionIdsWithPath(int movieId, string path);
        Movie GetMovieForFile(Movie movie, MovieFile movieFile);
        Movie GetVersionView(Movie movie, int movieVersionId);
        MovieVersion Add(MovieVersion version);
        MovieVersion AddVersion(Movie movie, int qualityProfileId, string name = null, List<int> requiredCustomFormatIds = null);
        MovieVersion AddPinnedVersion(Movie movie, string name, string suggestedName = null);
        MovieVersion UpdateVersion(int id, string name, int qualityProfileId, List<int> requiredCustomFormatIds);
        void SetEdition(MovieVersion version, string edition);
        void RemoveVersion(int id);
        MovieVersion Update(MovieVersion version);
        void Delete(int id);
    }

    public class MovieVersionService : IMovieVersionService,
                                       IHandle<MovieAddedEvent>,
                                       IHandle<MoviesImportedEvent>,
                                       IHandle<MovieEditedEvent>,
                                       IHandle<MoviesBulkEditedEvent>,
                                       IHandle<MovieMovedEvent>,
                                       IHandle<MovieFileAddedEvent>,
                                       IHandle<MovieFileDeletedEvent>,
                                       IHandleAsync<MoviesDeletedEvent>
    {
        private readonly IMovieVersionRepository _repository;
        private readonly IMovieRepository _movieRepository;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly ICustomFormatService _customFormatService;
        private readonly IPendingReleaseRepository _pendingReleaseRepository;
        private readonly Logger _logger;

        public MovieVersionService(IMovieVersionRepository repository,
                                   IMovieRepository movieRepository,
                                   IMediaFileRepository mediaFileRepository,
                                   IQualityProfileRepository qualityProfileRepository,
                                   ICustomFormatService customFormatService,
                                   IPendingReleaseRepository pendingReleaseRepository,
                                   Logger logger)
        {
            _repository = repository;
            _movieRepository = movieRepository;
            _mediaFileRepository = mediaFileRepository;
            _qualityProfileRepository = qualityProfileRepository;
            _customFormatService = customFormatService;
            _pendingReleaseRepository = pendingReleaseRepository;
            _logger = logger;
        }

        public MovieVersion Get(int id)
        {
            return _repository.Get(id);
        }

        public MovieVersion Find(int id)
        {
            return _repository.Find(id);
        }

        public List<MovieVersion> GetByMovieId(int movieId)
        {
            return _repository.FindByMovieId(movieId);
        }

        public List<MovieVersion> GetByMovieIds(List<int> movieIds)
        {
            return _repository.FindByMovieIds(movieIds);
        }

        // Every version that isn't the primary version of its movie
        public List<MovieVersion> GetAllAdditionalVersions()
        {
            return _repository.All()
                              .GroupBy(v => v.MovieId)
                              .SelectMany(g => g.OrderBy(v => v.Id).Skip(1))
                              .ToList();
        }

        public MovieVersion FindByPath(string path)
        {
            return _repository.FindByPath(path);
        }

        // The primary version mirrors Movies.Path/QualityProfileId/MovieFileId and is the oldest one
        public MovieVersion GetPrimary(int movieId)
        {
            return _repository.FindByMovieId(movieId).OrderBy(v => v.Id).FirstOrDefault();
        }

        public IEnumerable<Movie> GetAdditionalVersionViews(Movie movie)
        {
            return GetAdditionalVersions(movie.Id).Select(v => BuildView(movie, v)).ToList();
        }

        // The movie itself (primary version) followed by a view for each of its additional versions
        public List<Movie> GetAllVersionViews(Movie movie)
        {
            var baseMovie = movie.IsVersionView ? _movieRepository.Get(movie.Id) : movie;
            baseMovie.Version = GetPrimary(baseMovie.Id);

            var views = new List<Movie> { baseMovie };
            views.AddRange(GetAdditionalVersionViews(baseMovie));

            return views;
        }

        public IEnumerable<int> GetAdditionalVersionIds(int movieId)
        {
            return GetAdditionalVersions(movieId).Select(v => v.Id).ToList();
        }

        // Versions stored in the given folder, 0 standing for the primary version
        public IEnumerable<int> GetVersionIdsWithPath(int movieId, string path)
        {
            var versions = _repository.FindByMovieId(movieId).OrderBy(v => v.Id).ToList();

            return versions.Where(v => v.Path.PathEquals(path))
                           .Select(v => v.Id == versions[0].Id ? 0 : v.Id)
                           .ToList();
        }

        public Movie GetMovieForFile(Movie movie, MovieFile movieFile)
        {
            var versionId = movieFile.GetVersionId(GetAdditionalVersionIds(movie.Id));

            if (versionId == movie.MovieVersionId)
            {
                return movie;
            }

            return GetVersionView(movie, versionId);
        }

        // The view of an additional version of the movie, or the movie itself for its primary version.
        // Explicit targets must still exist and belong to this movie, including when reloading a cached view.
        public Movie GetVersionView(Movie movie, int movieVersionId)
        {
            if (movie == null)
            {
                return null;
            }

            if (movieVersionId == 0)
            {
                var primaryMovie = movie.IsVersionView ? _movieRepository.Get(movie.Id) : movie;
                primaryMovie.Version = GetPrimary(movie.Id);
                return primaryMovie;
            }

            var version = movieVersionId > 0 ? _repository.Find(movieVersionId) : null;

            if (version == null || version.MovieId != movie.Id)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie version {0} no longer exists or belongs to another movie. Choose a version before importing", movieVersionId);
            }

            if (GetPrimary(movie.Id)?.Id == version.Id)
            {
                var primaryMovie = movie.IsVersionView ? _movieRepository.Get(movie.Id) : movie;
                primaryMovie.Version = version;
                return primaryMovie;
            }

            return BuildView(movie, version);
        }

        public MovieVersion Add(MovieVersion version)
        {
            return _repository.Insert(version);
        }

        // Every version is stored in the movie's own folder, next to the files of the other versions
        public MovieVersion AddVersion(Movie movie, int qualityProfileId, string name = null, List<int> requiredCustomFormatIds = null)
        {
            // Movies added in bulk before versions existed may not have their primary version yet,
            // create it first so the new version's name is checked against it too.
            var existing = EnsurePrimary(movie);
            name = name?.Trim();
            ValidateVersion(existing, 0, name, qualityProfileId, requiredCustomFormatIds);

            _logger.Info("Adding version of {0} with quality profile {1}", movie, qualityProfileId);

            return _repository.Insert(new MovieVersion
            {
                MovieId = movie.Id,
                Name = name,
                QualityProfileId = qualityProfileId,
                RequiredCustomFormatIds = requiredCustomFormatIds?.Distinct().ToList() ?? new List<int>(),
                Path = movie.Path
            });
        }

        // A version added by hand, from an interactive search grab or a manual import. It shares the movie's folder and
        // quality profile, and only gets the files picked for it: it's never searched for, upgraded or given files automatically.
        public MovieVersion AddPinnedVersion(Movie movie, string name, string suggestedName = null)
        {
            var existing = EnsurePrimary(movie);
            name = name?.Trim();

            if (name.IsNullOrWhiteSpace())
            {
                name = GetUnusedName(existing, suggestedName?.Trim());
            }

            ValidateVersion(existing, 0, name, movie.QualityProfileId, null);

            _logger.Info("Adding pinned version '{0}' of {1}", name, movie);

            return _repository.Insert(new MovieVersion
            {
                MovieId = movie.Id,
                Name = name,
                QualityProfileId = movie.QualityProfileId,
                RequiredCustomFormatIds = new List<int>(),
                Pinned = true,
                Path = movie.Path
            });
        }

        public MovieVersion UpdateVersion(int id, string name, int qualityProfileId, List<int> requiredCustomFormatIds)
        {
            var version = _repository.Get(id);
            name = name?.Trim();
            ValidateVersion(_repository.FindByMovieId(version.MovieId), id, name, qualityProfileId, requiredCustomFormatIds);

            // A renamed version is handed a new edition the next time its files are named
            if (!string.Equals(version.Name, name, StringComparison.Ordinal))
            {
                version.Edition = null;
            }

            version.Name = name;
            version.QualityProfileId = qualityProfileId;
            version.RequiredCustomFormatIds = requiredCustomFormatIds?.Distinct().ToList() ?? new List<int>();

            return _repository.Update(version);
        }

        public void SetEdition(MovieVersion version, string edition)
        {
            version.Edition = edition;
            _repository.SetFields(version, v => v.Edition);
        }

        private void ValidateVersion(List<MovieVersion> existing, int id, string name, int qualityProfileId, List<int> requiredCustomFormatIds)
        {
            if (name.IsNullOrWhiteSpace() || name.Length > 100)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Version name must contain between 1 and 100 characters");
            }

            if (existing.Any(v => v.Id != id && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "This movie already has a version named '{0}'", name);
            }

            if (_qualityProfileRepository.Find(qualityProfileId) == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Quality profile {0} does not exist", qualityProfileId);
            }

            var knownFormats = (_customFormatService.All() ?? new List<CustomFormat>()).Select(f => f.Id).ToHashSet();

            if (requiredCustomFormatIds?.Any(formatId => !knownFormats.Contains(formatId)) == true)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "One or more required custom formats no longer exist");
            }
        }

        // The primary version can't be removed, and a version keeps its file on disk so it must be deleted first
        public void RemoveVersion(int id)
        {
            var version = _repository.Get(id);
            var primary = GetPrimary(version.MovieId);

            if (primary.Id == version.Id)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "The primary version of a movie can't be removed");
            }

            if (version.HasFile)
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Delete the file of this version before removing it");
            }

            DeleteVersion(version);
        }

        public MovieVersion Update(MovieVersion version)
        {
            return _repository.Update(version);
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void Handle(MovieAddedEvent message)
        {
            EnsurePrimary(message.Movie);
        }

        // Import lists, collections and the library import add movies in bulk without a MovieAddedEvent
        public void Handle(MoviesImportedEvent message)
        {
            var movies = message.Movies?.Where(m => m != null && m.Id > 0).ToList() ?? new List<Movie>();

            if (movies.Empty())
            {
                return;
            }

            var withVersions = _repository.FindByMovieIds(movies.Select(m => m.Id).ToList())
                                          .Select(v => v.MovieId)
                                          .ToHashSet();

            var primaries = movies.Where(m => !withVersions.Contains(m.Id))
                                  .Select(CreatePrimary)
                                  .ToList();

            if (primaries.Any())
            {
                _logger.Debug("Creating primary versions for {0} imported movies", primaries.Count);
                _repository.InsertMany(primaries);
            }
        }

        public void Handle(MovieEditedEvent message)
        {
            SyncVersions(message.Movie);
        }

        public void Handle(MoviesBulkEditedEvent message)
        {
            foreach (var movie in message.Movies)
            {
                SyncVersions(movie);
            }
        }

        public void Handle(MovieMovedEvent message)
        {
            SyncVersions(message.Movie);
        }

        public void Handle(MovieFileAddedEvent message)
        {
            var movieFile = message.MovieFile;

            var version = movieFile.MovieVersionId > 0
                ? _repository.Find(movieFile.MovieVersionId)
                : GetPrimary(movieFile.MovieId);

            if (version == null || version.MovieId != movieFile.MovieId)
            {
                return;
            }

            version.MovieFileId = movieFile.Id;
            _repository.Update(version);
        }

        public void Handle(MovieFileDeletedEvent message)
        {
            var movieFile = message.MovieFile;
            var versions = _repository.FindByMovieId(movieFile.MovieId).OrderBy(v => v.Id).ToList();
            var primaryId = versions.FirstOrDefault()?.Id ?? 0;
            var updated = new List<MovieVersion>();
            var removed = new List<MovieVersion>();

            // Only the versions whose current file this was
            foreach (var version in versions.Where(v => v.MovieFileId == movieFile.Id))
            {
                // A pinned version only exists to hold its file, so deleting that file by hand removes the version as well,
                // unless another file of the version is left. Upgrades and re-imports replace the file and keep the version,
                // a file missing from disk keeps it too.
                if (message.Reason == DeleteMediaFileReason.Manual && version.Pinned && version.Id != primaryId && version.Id == movieFile.MovieVersionId)
                {
                    var otherFiles = GetOtherFilesOfVersion(version, movieFile.Id);

                    if (otherFiles.Empty())
                    {
                        removed.Add(version);
                        continue;
                    }

                    version.MovieFileId = otherFiles.Max(f => f.Id);
                }
                else
                {
                    version.MovieFileId = 0;
                }

                updated.Add(version);
            }

            _repository.UpdateMany(updated);

            foreach (var version in removed)
            {
                _logger.Info("Removing pinned version '{0}' of movie {1} along with its file", version.Name, version.MovieId);
                DeleteVersion(version);
            }
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            _repository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        private List<MovieVersion> GetAdditionalVersions(int movieId)
        {
            return _repository.FindByMovieId(movieId).OrderBy(v => v.Id).Skip(1).ToList();
        }

        // The files of the version other than the one being deleted, such as a file imported for it before its current one
        private List<MovieFile> GetOtherFilesOfVersion(MovieVersion version, int deletedMovieFileId)
        {
            return (_mediaFileRepository.GetFilesByMovie(version.MovieId) ?? new List<MovieFile>())
                .Where(f => f.MovieVersionId == version.Id && f.Id != deletedMovieFileId)
                .ToList();
        }

        // Removes the version along with the releases waiting to be grabbed for it
        private void DeleteVersion(MovieVersion version)
        {
            var pending = (_pendingReleaseRepository.AllByMovieId(version.MovieId) ?? new List<PendingRelease>())
                .Where(p => p.AdditionalInfo?.MovieVersionId == version.Id).Select(p => p.Id).ToList();

            _pendingReleaseRepository.DeleteMany(pending);
            _repository.Delete(version);
        }

        // The suggested name, followed by a number when another version of the movie already uses it ("2160p 2")
        private static string GetUnusedName(List<MovieVersion> existing, string suggestedName)
        {
            var baseName = suggestedName.IsNullOrWhiteSpace() ? "Version" : suggestedName;

            if (baseName.Length > 90)
            {
                baseName = baseName.Substring(0, 90).Trim();
            }

            var taken = existing.Select(v => v.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = baseName;
            var number = 2;

            while (taken.Contains(name))
            {
                name = $"{baseName} {number++}";
            }

            return name;
        }

        private Movie BuildView(Movie movie, MovieVersion version)
        {
            var view = movie.ForVersion(version);

            // Versions share the movie's folder, a missing path never points somewhere else
            if (view.Path.IsNullOrWhiteSpace())
            {
                view.Path = movie.Path;
            }

            view.QualityProfile = _qualityProfileRepository.Find(version.QualityProfileId);

            if (version.MovieFileId > 0)
            {
                view.MovieFile = _mediaFileRepository.Find(version.MovieFileId);
            }

            return view;
        }

        private List<MovieVersion> EnsurePrimary(Movie movie)
        {
            var versions = _repository.FindByMovieId(movie.Id);

            if (versions.Any())
            {
                return versions;
            }

            _logger.Debug("Creating primary version for {0}", movie);

            versions.Add(_repository.Insert(CreatePrimary(movie)));

            return versions;
        }

        private static MovieVersion CreatePrimary(Movie movie)
        {
            return new MovieVersion
            {
                MovieId = movie.Id,
                Name = "Original",
                QualityProfileId = movie.QualityProfileId,
                Path = movie.Path,
                MovieFileId = movie.MovieFileId
            };
        }

        // Keeps the primary version in sync with the movie, every version lives in the movie's folder and follows it when it moves
        private void SyncVersions(Movie movie)
        {
            if (movie == null || movie.IsVersionView)
            {
                return;
            }

            var versions = _repository.FindByMovieId(movie.Id).OrderBy(v => v.Id).ToList();

            if (versions.Empty())
            {
                return;
            }

            var updated = new List<MovieVersion>();
            var primary = versions.First();

            if (primary.Path != movie.Path || primary.QualityProfileId != movie.QualityProfileId)
            {
                primary.Path = movie.Path;
                primary.QualityProfileId = movie.QualityProfileId;
                updated.Add(primary);
            }

            foreach (var version in versions.Skip(1))
            {
                var changed = false;

                if (version.Path != movie.Path)
                {
                    version.Path = movie.Path;
                    changed = true;
                }

                // A pinned version has no quality profile of its own, it follows the movie's.
                // Otherwise a profile the movie left behind would stay in use and couldn't be deleted.
                if (version.Pinned && version.QualityProfileId != movie.QualityProfileId)
                {
                    version.QualityProfileId = movie.QualityProfileId;
                    changed = true;
                }

                if (changed)
                {
                    updated.Add(version);
                }
            }

            if (updated.Any())
            {
                _repository.UpdateMany(updated);
            }
        }
    }
}
