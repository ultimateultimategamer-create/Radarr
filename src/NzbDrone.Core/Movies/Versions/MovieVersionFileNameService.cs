using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Movies.Versions
{
    public interface IMovieVersionFileNameService
    {
        string GetFileName(Movie movie, MovieFile movieFile, string standardFileName);
        string GetUniqueFileName(Movie movie, MovieFile movieFile, string fileName);
    }

    // Versions of a movie share its folder. Files of additional versions are named after the folder and the version,
    // and the file of one version never takes the name of another version's file: on a clash the version's name,
    // then a number, is appended.
    public class MovieVersionFileNameService : IMovieVersionFileNameService
    {
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly INamingConfigService _namingConfigService;

        public MovieVersionFileNameService(IMovieVersionService movieVersionService,
                                           IMediaFileRepository mediaFileRepository,
                                           IQualityProfileRepository qualityProfileRepository,
                                           INamingConfigService namingConfigService)
        {
            _movieVersionService = movieVersionService;
            _mediaFileRepository = mediaFileRepository;
            _qualityProfileRepository = qualityProfileRepository;
            _namingConfigService = namingConfigService;
        }

        // Files of additional versions are named after the movie's folder and the version. As Plex editions,
        // "Movie (2020) {edition-Director's Cut}", each version is an item of its own in Plex. With labels,
        // "Movie (2020) - Director's Cut", Plex shows the versions as one movie, and Jellyfin and Emby label them.
        // The primary version keeps the naming format.
        public string GetFileName(Movie movie, MovieFile movieFile, string standardFileName)
        {
            var fileName = standardFileName;

            if (movie is { IsVersionView: true } && movie.Path.IsNotNullOrWhiteSpace())
            {
                var folderName = new OsPath(movie.Path).FileName;
                var versionName = FileNameBuilder.CleanFileName(movie.Version?.Name ?? string.Empty).Trim();

                if (folderName.IsNotNullOrWhiteSpace() && versionName.IsNotNullOrWhiteSpace())
                {
                    var editionName = GetExtraVersionNaming() == ExtraVersionNaming.PlexEdition ? GetEditionName(movie) : string.Empty;

                    fileName = editionName.IsNotNullOrWhiteSpace()
                        ? $"{folderName} {{edition-{editionName}}}"
                        : $"{folderName} - {versionName}";
                }
            }

            return GetUniqueFileName(movie, movieFile, fileName);
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
            var version = movie.IsVersionView ? versions.FirstOrDefault(v => v.Id == movie.MovieVersionId) : versions.OrderBy(v => v.Id).First();
            var versionName = FileNameBuilder.CleanFileName(version?.Name ?? profile?.Name ?? string.Empty).Trim();

            if (versionName.IsNullOrWhiteSpace())
            {
                versionName = $"Version {movie.MovieVersionId}";
            }

            var uniqueName = $"{fileName} - {versionName}";

            var suffix = 2;

            while (takenNames.Contains(uniqueName))
            {
                uniqueName = $"{fileName} - {versionName} {suffix++}";
            }

            return uniqueName;
        }

        private string GetEditionName(Movie versionView)
        {
            var extraVersions = (_movieVersionService.GetByMovieId(versionView.Id) ?? Enumerable.Empty<MovieVersion>()).OrderBy(v => v.Id).Skip(1);

            return MovieVersionEditionName.Get(versionView.Version, extraVersions);
        }

        private ExtraVersionNaming GetExtraVersionNaming()
        {
            return _namingConfigService.GetConfig()?.ExtraVersionNaming ?? NamingConfig.Default.ExtraVersionNaming;
        }

        private static string RemoveExtension(string relativePath)
        {
            var directory = Path.GetDirectoryName(relativePath);
            var name = Path.GetFileNameWithoutExtension(relativePath);

            return directory.IsNullOrWhiteSpace() ? name : Path.Combine(directory, name);
        }
    }
}
