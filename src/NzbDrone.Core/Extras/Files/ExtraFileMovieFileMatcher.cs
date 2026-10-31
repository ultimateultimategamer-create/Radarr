using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Extras.Files
{
    public static class ExtraFileMovieFileMatcher
    {
        // The movie file an existing extra file belongs to: the file whose name starts the extra's name
        // ("Movie (2020) - HD-1080p.en.srt" goes with "Movie (2020) - HD-1080p.mkv"), otherwise the movie's own file.
        // With several versions of a movie in its folder this keeps each version's extras with that version's file,
        // so upgrading or deleting one version's file never takes the other versions' extras with it.
        public static int GetMovieFileId(Movie movie, List<MovieFile> movieFiles, string extraFilePath)
        {
            if (movieFiles == null || movieFiles.Count < 2 || movie.Path.IsNullOrWhiteSpace() || !movie.Path.IsParentPath(extraFilePath))
            {
                return movie.MovieFileId;
            }

            var relativePath = movie.Path.GetRelativePath(extraFilePath);
            var extraDirectory = Path.GetDirectoryName(relativePath) ?? string.Empty;
            var extraName = Path.GetFileName(relativePath);

            var match = movieFiles.Where(f => f.RelativePath.IsNotNullOrWhiteSpace())
                                  .Select(f => new
                                  {
                                      f.Id,
                                      Directory = Path.GetDirectoryName(f.RelativePath) ?? string.Empty,
                                      Name = Path.GetFileNameWithoutExtension(f.RelativePath)
                                  })
                                  .Where(f => f.Directory.Equals(extraDirectory, StringComparison.OrdinalIgnoreCase) &&
                                              extraName.StartsWith(f.Name + ".", StringComparison.OrdinalIgnoreCase))
                                  .OrderByDescending(f => f.Name.Length)
                                  .FirstOrDefault();

            return match?.Id ?? movie.MovieFileId;
        }
    }
}
