using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Versions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    // Files of extra versions named as Plex editions are items of their own in Plex. For each Plex Media Server
    // connection with 'Label Extra Versions' on, this puts the version's name at the end of those items' titles,
    // "Movie (IMAX)", and keeps their edition on the version's name, so a version renamed in Radarr is renamed in Plex
    // too. Both fields are locked, as when edited by hand, so a Plex metadata refresh keeps them.
    // Items are found by their file. The original movie, and any item holding files of more than one version, is never
    // edited. With the option off, or extra versions not named as Plex editions, the labels are taken off again.
    public class PlexVersionLabelService : IExecute<LabelPlexVersionsCommand>, IHandle<ProviderUpdatedEvent<INotification>>
    {
        // A labeled version is checked again once a day, or as soon as it has another file or name
        private static readonly TimeSpan RecheckLabeledAfter = TimeSpan.FromHours(24);

        // A version Plex hasn't picked up yet is looked for on every run for an hour, then hourly
        private static readonly TimeSpan RetryOnEveryRunFor = TimeSpan.FromHours(1);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);
        private static readonly TimeSpan WarnAgainAfter = TimeSpan.FromHours(1);

        private readonly INotificationFactory _notificationFactory;
        private readonly INamingConfigService _namingConfigService;
        private readonly IMovieVersionService _movieVersionService;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IPlexServerProxy _plexServerProxy;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, LabelCheck> _checks = new();
        private readonly ConcurrentDictionary<int, bool> _labelsTakenOff = new();
        private readonly ConcurrentDictionary<string, DateTime> _warnings = new();

        public PlexVersionLabelService(INotificationFactory notificationFactory,
                                       INamingConfigService namingConfigService,
                                       IMovieVersionService movieVersionService,
                                       IMovieService movieService,
                                       IMediaFileService mediaFileService,
                                       IPlexServerProxy plexServerProxy,
                                       Logger logger)
        {
            _notificationFactory = notificationFactory;
            _namingConfigService = namingConfigService;
            _movieVersionService = movieVersionService;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _plexServerProxy = plexServerProxy;
            _logger = logger;
        }

        private enum LabelResult
        {
            Labeled,
            Unchanged,
            NotFound,
            NotMatched,
            Merged
        }

        public void Execute(LabelPlexVersionsCommand message)
        {
            var connections = (_notificationFactory.GetAvailableProviders() ?? new List<INotification>())
                .Select(n => n.Definition as NotificationDefinition)
                .Where(d => d?.Settings is PlexServerSettings)
                .ToList();

            if (connections.Empty())
            {
                return;
            }

            var namedAsEditions = (_namingConfigService.GetConfig()?.ExtraVersionNaming ?? NamingConfig.Default.ExtraVersionNaming) == ExtraVersionNaming.PlexEdition;

            var labeling = connections.Where(d => namedAsEditions && ((PlexServerSettings)d.Settings).LabelVersions).ToList();
            var labelingIds = labeling.Select(d => d.Id).ToHashSet();

            // Labels are taken off once per start of Radarr, and again whenever the connection is saved
            var takingOff = connections.Where(d => !labelingIds.Contains(d.Id) && !_labelsTakenOff.ContainsKey(d.Id)).ToList();

            if (labeling.Empty() && takingOff.Empty())
            {
                return;
            }

            var versions = GetVersionFiles();

            foreach (var definition in labeling)
            {
                _labelsTakenOff.TryRemove(definition.Id, out _);

                Label(definition, versions, true);
            }

            foreach (var definition in takingOff)
            {
                if (Label(definition, versions, false))
                {
                    _labelsTakenOff[definition.Id] = true;
                }
            }
        }

        public void Handle(ProviderUpdatedEvent<INotification> message)
        {
            var definitionId = message.Definition.Id;

            _labelsTakenOff.TryRemove(definitionId, out _);

            foreach (var key in _checks.Keys.Where(k => k.StartsWith($"{definitionId}:", StringComparison.Ordinal)))
            {
                _checks.TryRemove(key, out _);
            }
        }

        // Extra versions that have a file, each with the end of its file's path and those of the movie's other versions
        private List<VersionFile> GetVersionFiles()
        {
            var versions = _movieVersionService.GetAllAdditionalVersions() ?? new List<MovieVersion>();
            var versionsWithFiles = versions.Where(v => v.MovieFileId > 0 && v.Name.IsNotNullOrWhiteSpace()).ToList();

            if (versionsWithFiles.Empty())
            {
                return new List<VersionFile>();
            }

            var movieIds = versionsWithFiles.Select(v => v.MovieId).Distinct().ToList();

            var movies = (_movieService.GetMovies(movieIds) ?? new List<Movie>())
                .ToDictionary(m => m.Id);

            var files = (_mediaFileService.GetFilesByMovies(movieIds) ?? new List<MovieFile>())
                .GroupBy(f => f.MovieId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var versionsOfMovies = versions.GroupBy(v => v.MovieId).ToDictionary(g => g.Key, g => g.ToList());
            var versionFiles = new List<VersionFile>();

            foreach (var version in versionsWithFiles)
            {
                if (!movies.TryGetValue(version.MovieId, out var movie) ||
                    movie.MovieMetadata?.Value == null ||
                    !files.TryGetValue(version.MovieId, out var movieFiles))
                {
                    continue;
                }

                var file = movieFiles.FirstOrDefault(f => f.Id == version.MovieFileId);

                if (file == null || file.RelativePath.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var additionalVersions = versionsOfMovies[version.MovieId];
                var additionalVersionIds = additionalVersions.Select(v => v.Id).ToList();

                var otherVersionFilePaths = movieFiles
                    .Where(f => f.RelativePath.IsNotNullOrWhiteSpace() && f.GetVersionId(additionalVersionIds) != version.Id)
                    .Select(f => GetPathEnd(movie, additionalVersions.FirstOrDefault(v => v.Id == f.GetVersionId(additionalVersionIds)), f))
                    .ToList();

                versionFiles.Add(new VersionFile
                {
                    Movie = movie,
                    Version = version,
                    File = file,
                    FilePath = GetPathEnd(movie, version, file),
                    OtherVersionFilePaths = otherVersionFilePaths
                });
            }

            return versionFiles;
        }

        // False when Plex couldn't be reached
        private bool Label(NotificationDefinition definition, List<VersionFile> versions, bool add)
        {
            var settings = (PlexServerSettings)definition.Settings;
            var now = DateTime.UtcNow;
            var due = add ? versions.Where(v => IsDue(GetKey(definition, v), v, now)).ToList() : versions;

            if (due.Empty())
            {
                return true;
            }

            try
            {
                var sections = _plexServerProxy.GetMovieSections(settings) ?? new List<PlexSection>();

                foreach (var version in due)
                {
                    var result = LabelItem(version, sections, settings, add);

                    if (add)
                    {
                        Record(GetKey(definition, version), version, result, now);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Warn($"connection:{definition.Id}", ex, "Unable to label extra versions in Plex ({0}): {1}", definition.Name, ex.Message);

                return false;
            }
        }

        private LabelResult LabelItem(VersionFile version, List<PlexSection> sections, PlexServerSettings settings, bool add)
        {
            foreach (var section in sections)
            {
                var found = FindItem(section.Id, version, settings);

                if (found == null)
                {
                    continue;
                }

                // Listings can leave fields out, the item itself has them all
                var item = _plexServerProxy.GetMovie(found.RatingKey, settings) ?? found;

                if (item.GetFiles().Any(f => version.OtherVersionFilePaths.Any(p => IsPath(f, p))))
                {
                    if (add)
                    {
                        Warn($"merged:{version.Version.Id}",
                             null,
                             "Plex shows the '{0}' version of {1} together with another version, so it can't be labeled. Rename the movie's files to name the version as a Plex edition, or split the movie apart in Plex",
                             version.Version.Name,
                             version.Movie.Title);
                    }

                    return LabelResult.Merged;
                }

                // Until Plex has matched a new item its title comes from the file's name
                if (add && !IsMatched(item))
                {
                    return LabelResult.NotMatched;
                }

                var name = version.Version.Name.Trim();
                var fields = GetLabelFields(item, name, add);

                if (fields.Empty())
                {
                    return LabelResult.Unchanged;
                }

                _plexServerProxy.EditMovie(section.Id, item.RatingKey, fields, settings);

                if (add)
                {
                    _logger.Info("Labeled the '{0}' version of {1} in Plex", name, version.Movie.Title);
                }
                else
                {
                    _logger.Info("Took the label off the '{0}' version of {1} in Plex", name, version.Movie.Title);
                }

                return LabelResult.Labeled;
            }

            return LabelResult.NotFound;
        }

        // By title and year, then by year alone as Plex may title the movie otherwise, then by title alone as Plex may
        // date it otherwise. The item is the one holding the version's file.
        private PlexMovie FindItem(int sectionId, VersionFile version, PlexServerSettings settings)
        {
            var title = version.Movie.Title;
            var year = version.Movie.Year;

            var searches = new List<(string Title, int Year)>
            {
                (title, year),
                (null, year),
                (title, 0)
            };

            foreach (var search in searches.Distinct())
            {
                // Neither would list the whole section
                if (search.Title.IsNullOrWhiteSpace() && search.Year <= 0)
                {
                    continue;
                }

                var item = (_plexServerProxy.GetMovies(sectionId, search.Title, search.Year, settings) ?? new List<PlexMovie>())
                    .FirstOrDefault(m => m.GetFiles().Any(f => IsPath(f, version.FilePath)));

                if (item != null)
                {
                    return item;
                }
            }

            return null;
        }

        // The label is " (name)" at the end of the title. The item's edition is kept on the version's name, so a label
        // made for an earlier name of the version is found by the edition and replaced.
        private static Dictionary<string, string> GetLabelFields(PlexMovie item, string name, bool add)
        {
            var title = item.Title ?? string.Empty;
            var edition = item.EditionTitle ?? string.Empty;
            var unlabeled = RemoveLabel(RemoveLabel(title, edition), name);
            var wanted = add ? $"{unlabeled} ({name})" : unlabeled;
            var fields = new Dictionary<string, string>();

            if (wanted != title || (add && !item.IsLocked("title")))
            {
                fields.Add("title.value", wanted);
                fields.Add("title.locked", add ? "1" : "0");
            }

            if (add && (edition != name || !item.IsLocked("editionTitle")))
            {
                fields.Add("editionTitle.value", name);
                fields.Add("editionTitle.locked", "1");
            }

            return fields;
        }

        private static string RemoveLabel(string title, string name)
        {
            var label = $" ({name})";

            return name.IsNotNullOrWhiteSpace() && title.EndsWith(label, StringComparison.Ordinal)
                ? title.Substring(0, title.Length - label.Length)
                : title;
        }

        private static bool IsMatched(PlexMovie item)
        {
            return item.Guid.IsNotNullOrWhiteSpace() &&
                   !item.Guid.StartsWith("local://", StringComparison.OrdinalIgnoreCase) &&
                   !item.Guid.StartsWith("com.plexapp.agents.none", StringComparison.OrdinalIgnoreCase);
        }

        // The movie's folder and the file's path in it: the end of the path Radarr and Plex share, wherever their paths start
        private static string GetPathEnd(Movie movie, MovieVersion version, MovieFile file)
        {
            var folder = version != null && version.Path.IsNotNullOrWhiteSpace() ? version.Path : movie.Path;

            return Normalize($"{new OsPath(folder).FileName}/{file.RelativePath}");
        }

        private static bool IsPath(string plexFile, string pathEnd)
        {
            var path = Normalize(plexFile);

            return path.Equals(pathEnd, StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/" + pathEnd, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').Trim().Trim('/');
        }

        private bool IsDue(string key, VersionFile version, DateTime now)
        {
            if (!_checks.TryGetValue(key, out var check) || !IsSame(check, version))
            {
                return true;
            }

            if (check.Labeled)
            {
                return now - check.LastChecked >= RecheckLabeledAfter;
            }

            return now - check.FirstMissed < RetryOnEveryRunFor || now - check.LastChecked >= RetryAfter;
        }

        private void Record(string key, VersionFile version, LabelResult result, DateTime now)
        {
            var labeled = result == LabelResult.Labeled || result == LabelResult.Unchanged;
            var firstMissed = now;

            if (!labeled && _checks.TryGetValue(key, out var earlier) && !earlier.Labeled && IsSame(earlier, version))
            {
                firstMissed = earlier.FirstMissed;
            }

            _checks[key] = new LabelCheck
            {
                MovieFileId = version.File.Id,
                Name = version.Version.Name,
                Labeled = labeled,
                FirstMissed = firstMissed,
                LastChecked = now
            };
        }

        private static bool IsSame(LabelCheck check, VersionFile version)
        {
            return check.MovieFileId == version.File.Id && check.Name == version.Version.Name;
        }

        private static string GetKey(NotificationDefinition definition, VersionFile version)
        {
            return $"{definition.Id}:{version.Version.Id}";
        }

        private void Warn(string key, Exception ex, string message, params object[] args)
        {
            var now = DateTime.UtcNow;

            if (_warnings.TryGetValue(key, out var last) && now - last < WarnAgainAfter)
            {
                _logger.Debug(ex, message, args);

                return;
            }

            _warnings[key] = now;
            _logger.Warn(ex, message, args);
        }

        private class VersionFile
        {
            public Movie Movie { get; set; }
            public MovieVersion Version { get; set; }
            public MovieFile File { get; set; }
            public string FilePath { get; set; }
            public List<string> OtherVersionFilePaths { get; set; }
        }

        private class LabelCheck
        {
            public int MovieFileId { get; set; }
            public string Name { get; set; }
            public bool Labeled { get; set; }
            public DateTime FirstMissed { get; set; }
            public DateTime LastChecked { get; set; }
        }
    }
}
