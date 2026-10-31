using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    public interface IMovieVersionFileNameService
    {
        string GetUniqueFileName(Movie movie, MovieFile movieFile, string fileName);
    }

    // Versions of a movie share its folder, so the file of one version must never take the name of another version's file.
    // When the naming format gives two versions the same name (no quality in it, same quality for both...),
    // the name gets the version's quality profile appended: "Movie (2020) - Ultra-HD.mkv", which Plex, Jellyfin and Emby
    // all group as versions of the same movie.
    public class MovieVersionFileNameService : IMovieVersionFileNameService
    {
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IQualityProfileRepository _qualityProfileRepository;

        public MovieVersionFileNameService(IMovieVersionService movieVersionService,
                                           IMediaFileRepository mediaFileRepository,
                                           IQualityProfileRepository qualityProfileRepository)
        {
            _movieVersionService = movieVersionService;
            _mediaFileRepository = mediaFileRepository;
            _qualityProfileRepository = qualityProfileRepository;
        }

        public string GetUniqueFileName(Movie movie, MovieFile movieFile, string fileName)
        {
            if (movie == null || movie.Id <= 0 || fileName.IsNullOrWhiteSpace())
            {
                return fileName;
            }

            var versions = _movieVersionService.GetByMovieId(movie.Id);

            if (versions == null || versions.Count < 2)
            {
                return fileName;
            }

            var additionalVersionIds = versions.OrderBy(v => v.Id).Skip(1).Select(v => v.Id).ToList();
            var movieFileId = movieFile?.Id ?? 0;

            var takenNames = (_mediaFileRepository.GetFilesByMovie(movie.Id) ?? Enumerable.Empty<MovieFile>())
                .Where(f => (movieFileId == 0 || f.Id != movieFileId) &&
                            f.GetVersionId(additionalVersionIds) != movie.MovieVersionId &&
                            f.RelativePath.IsNotNullOrWhiteSpace())
                .Select(f => RemoveExtension(f.RelativePath))
                .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

            if (!takenNames.Contains(fileName))
            {
                return fileName;
            }

            var profile = movie.QualityProfile ?? _qualityProfileRepository.Find(movie.QualityProfileId);
            var versionName = FileNameBuilder.CleanFileName(profile?.Name ?? string.Empty).Trim();

            if (versionName.IsNullOrWhiteSpace())
            {
                versionName = $"Version {movie.MovieVersionId}";
            }

            var uniqueName = $"{fileName} - {versionName}";

            if (takenNames.Contains(uniqueName))
            {
                uniqueName = $"{fileName} - {versionName} {movie.MovieVersionId}";
            }

            return uniqueName;
        }

        private static string RemoveExtension(string relativePath)
        {
            var directory = Path.GetDirectoryName(relativePath);
            var name = Path.GetFileNameWithoutExtension(relativePath);

            return directory.IsNullOrWhiteSpace() ? name : Path.Combine(directory, name);
        }
    }
}
