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
    // Files of extra versions named as Plex editions are items of their own in Plex. For each Plex Media Server connection
    // with 'Label Extra Versions' on, this puts the version's name at the end of those items' titles, "Movie (IMAX)", and
    // keeps their edition on the version's edition name. Both fields are locked, as when edited by hand, so a Plex metadata
    // refresh keeps them, and each label is recorded with what the item had before.
    // Only items holding nothing but the version's own files are labeled, and only for movies the connection applies to.
    // A title changed outside Radarr after labeling is left as it is. With the option off, or extra versions named as
    // labels, the recorded labels are taken off, restoring what the items had. Nothing else in Plex is changed.
    public class PlexVersionLabelService : IExecute<LabelPlexVersionsCommand>,
                                           IHandle<ProviderUpdatedEvent<INotification>>,
                                           IHandle<ProviderDeletedEvent<INotification>>
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
        private readonly IPlexVersionLabelRepository _labelRepository;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, Miss> _misses = new();
        private readonly ConcurrentDictionary<string, DateTime> _warnings = new();

        public PlexVersionLabelService(INotificationFactory notificationFactory,
                                       INamingConfigService namingConfigService,
                                       IMovieVersionService movieVersionService,
                                       IMovieService movieService,
                                       IMediaFileService mediaFileService,
                                       IPlexServerProxy plexServerProxy,
                                       IPlexVersionLabelRepository labelRepository,
                                       Logger logger)
        {
            _notificationFactory = notificationFactory;
            _namingConfigService = namingConfigService;
            _movieVersionService = movieVersionService;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _plexServerProxy = plexServerProxy;
            _labelRepository = labelRepository;
            _logger = logger;
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
            var labelingIds = connections.Where(d => namedAsEditions && ((PlexServerSettings)d.Settings).LabelVersions).Select(d => d.Id).ToHashSet();
            var connectionIds = connections.Select(d => d.Id).ToHashSet();

            var records = (_labelRepository.All() ?? Enumerable.Empty<PlexVersionLabel>())
                .Where(r => connectionIds.Contains(r.NotificationId))
                .ToList();

            // Without labels to add or take off, Plex isn't contacted at all
            if (labelingIds.Empty() && records.Empty())
            {
                return;
            }

            var library = GetLibrary(records);

            foreach (var definition in connections)
            {
                var connectionRecords = records.Where(r => r.NotificationId == definition.Id).ToList();

                if (labelingIds.Contains(definition.Id))
                {
                    Label(definition, library, connectionRecords);
                }
                else if (connectionRecords.Any())
                {
                    TakeOffLabels(definition, library, connectionRecords);
                }
            }
        }

        public void Handle(ProviderUpdatedEvent<INotification> message)
        {
            ForgetMisses(message.Definition.Id);
        }

        public void Handle(ProviderDeletedEvent<INotification> message)
        {
            ForgetMisses(message.ProviderId);

            var ids = (_labelRepository.All() ?? Enumerable.Empty<PlexVersionLabel>())
                .Where(r => r.NotificationId == message.ProviderId)
                .Select(r => r.Id)
                .ToList();

            if (ids.Any())
            {
                _labelRepository.DeleteMany(ids);
            }
        }

        private void ForgetMisses(int definitionId)
        {
            foreach (var key in _misses.Keys.Where(k => k.StartsWith($"{definitionId}:", StringComparison.Ordinal)))
            {
                _misses.TryRemove(key, out _);
            }
        }

        // Extra versions whose file is named as an edition, and the movies of those and of recorded labels
        private Library GetLibrary(List<PlexVersionLabel> records)
        {
            var versions = _movieVersionService.GetAllAdditionalVersions() ?? new List<MovieVersion>();
            var versionsWithFiles = versions.Where(v => v.MovieFileId > 0 && v.Name.IsNotNullOrWhiteSpace()).ToList();
            var fileMovieIds = versionsWithFiles.Select(v => v.MovieId).Distinct().ToList();
            var movieIds = fileMovieIds.Concat(records.Select(r => r.MovieId)).Distinct().ToList();

            var movies = movieIds.Any()
                ? (_movieService.GetMovies(movieIds) ?? new List<Movie>()).ToDictionary(m => m.Id)
                : new Dictionary<int, Movie>();

            var files = fileMovieIds.Any()
                ? (_mediaFileService.GetFilesByMovies(fileMovieIds) ?? new List<MovieFile>()).GroupBy(f => f.MovieId).ToDictionary(g => g.Key, g => g.ToList())
                : new Dictionary<int, List<MovieFile>>();

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
                var extraVersions = versionsOfMovies[version.MovieId];
                var editionName = MovieVersionEditionName.Get(version, extraVersions);

                if (file == null || file.RelativePath.IsNullOrWhiteSpace() || editionName.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var extraVersionIds = extraVersions.Select(v => v.Id).ToList();

                versionFiles.Add(new VersionFile
                {
                    Movie = movie,
                    Version = version,
                    File = file,
                    EditionName = editionName,
                    FilePath = GetPathEnd(movie, version, file),
                    OwnFilePaths = movieFiles
                        .Where(f => f.RelativePath.IsNotNullOrWhiteSpace() && f.GetVersionId(extraVersionIds) == version.Id)
                        .Select(f => GetPathEnd(movie, version, f))
                        .ToList()
                });
            }

            return new Library
            {
                Movies = movies,
                VersionFiles = versionFiles
            };
        }

        private void Label(NotificationDefinition definition, Library library, List<PlexVersionLabel> records)
        {
            var settings = (PlexServerSettings)definition.Settings;
            var now = DateTime.UtcNow;
            var versionIds = library.VersionFiles.Select(v => v.Version.Id).ToHashSet();

            // Labels of versions that are gone, or no longer have a file named as an edition
            var stale = records
                .Where(r => !versionIds.Contains(r.MovieVersionId) && AppliesTo(definition, FindMovie(library, r.MovieId)))
                .ToList();

            var due = library.VersionFiles
                .Where(v => AppliesTo(definition, v.Movie) && IsDue(definition, v, FindRecord(records, v), now))
                .ToList();

            if (stale.Empty() && due.Empty())
            {
                return;
            }

            try
            {
                foreach (var record in stale)
                {
                    TakeOffLabel(settings, record);
                }

                var sections = due.Any() ? _plexServerProxy.GetMovieSections(settings) ?? new List<PlexSection>() : new List<PlexSection>();

                foreach (var version in due)
                {
                    LabelVersion(definition, settings, version, FindRecord(records, version), sections, now);
                }
            }
            catch (Exception ex)
            {
                Warn($"connection:{definition.Id}", ex, "Unable to label extra versions in Plex ({0}): {1}", definition.Name, ex.Message);
            }
        }

        private void LabelVersion(NotificationDefinition definition, PlexServerSettings settings, VersionFile version, PlexVersionLabel record, List<PlexSection> sections, DateTime now)
        {
            var key = GetKey(definition, version);
            PlexMovie item = null;
            var sectionId = 0;

            // The item labeled before is usually still the version's
            if (record != null)
            {
                var labeled = _plexServerProxy.GetMovie(record.RatingKey, settings);

                if (labeled != null && labeled.GetFiles().Any(f => IsPath(f, version.FilePath)))
                {
                    item = labeled;
                    sectionId = record.SectionId;
                }
                else
                {
                    if (labeled != null)
                    {
                        Restore(settings, record, labeled);
                    }

                    _labelRepository.Delete(record.Id);
                    record = null;
                }
            }

            if (item == null)
            {
                foreach (var section in sections)
                {
                    var found = FindItem(section.Id, version, settings);

                    if (found != null)
                    {
                        // Listings can leave fields out, the item itself has them all
                        item = _plexServerProxy.GetMovie(found.RatingKey, settings) ?? found;
                        sectionId = section.Id;

                        break;
                    }
                }
            }

            if (item == null)
            {
                Missed(key, version, now);

                return;
            }

            // An item also holding a file that isn't the version's, tracked by Radarr or not, may be the original movie
            if (!item.GetFiles().All(f => version.OwnFilePaths.Any(p => IsPath(f, p))))
            {
                if (record != null)
                {
                    Restore(settings, record, item);
                    _labelRepository.Delete(record.Id);
                }

                Warn($"shared:{definition.Id}:{version.Version.Id}",
                     null,
                     "Plex holds the '{0}' version of {1} together with other files, so it isn't labeled. Name extra versions as Plex editions (Settings > Media Management) and rename the movie's files, or split the movie apart in Plex",
                     version.Version.Name,
                     version.Movie.Title);

                Missed(key, version, now);

                return;
            }

            // Until Plex has matched a new item its title comes from the file's name
            if (!IsMatched(item))
            {
                Missed(key, version, now);

                return;
            }

            _misses.TryRemove(key, out _);

            var name = version.Version.Name.Trim();
            var title = item.Title ?? string.Empty;

            if (record != null && title != record.Title)
            {
                // Changed outside Radarr since it was labeled: left as it is
                record.LastChecked = now;
                _labelRepository.Update(record);

                return;
            }

            // A label left from an earlier record, as after the connection was added again, isn't labeled twice
            var originalTitle = record?.OriginalTitle ?? RemoveLabel(RemoveLabel(title, name), item.EditionTitle);
            var labeledTitle = $"{originalTitle} ({name})";
            var fields = new Dictionary<string, string>();

            if (title != labeledTitle || !item.IsLocked("title"))
            {
                fields.Add("title.value", labeledTitle);
                fields.Add("title.locked", "1");
            }

            if (item.EditionTitle != version.EditionName || !item.IsLocked("editionTitle"))
            {
                fields.Add("editionTitle.value", version.EditionName);
                fields.Add("editionTitle.locked", "1");
            }

            if (fields.Any())
            {
                _plexServerProxy.EditMovie(sectionId, item.RatingKey, fields, settings);
                _logger.Info("Labeled the '{0}' version of {1} in Plex", name, version.Movie.Title);
            }

            if (record == null)
            {
                _labelRepository.Insert(new PlexVersionLabel
                {
                    NotificationId = definition.Id,
                    MovieId = version.Movie.Id,
                    MovieVersionId = version.Version.Id,
                    MovieFileId = version.File.Id,
                    Name = version.Version.Name,
                    SectionId = sectionId,
                    RatingKey = item.RatingKey,
                    Title = labeledTitle,
                    EditionTitle = version.EditionName,
                    OriginalTitle = originalTitle,
                    OriginalTitleLocked = item.IsLocked("title"),
                    OriginalEditionTitle = item.EditionTitle,
                    OriginalEditionTitleLocked = item.IsLocked("editionTitle"),
                    LastChecked = now
                });
            }
            else
            {
                record.MovieFileId = version.File.Id;
                record.Name = version.Version.Name;
                record.Title = labeledTitle;
                record.EditionTitle = version.EditionName;
                record.LastChecked = now;
                _labelRepository.Update(record);
            }
        }

        private void TakeOffLabels(NotificationDefinition definition, Library library, List<PlexVersionLabel> records)
        {
            var settings = (PlexServerSettings)definition.Settings;
            var applying = records.Where(r => AppliesTo(definition, FindMovie(library, r.MovieId))).ToList();

            if (applying.Empty())
            {
                return;
            }

            // A label stays recorded until it's taken off, so it's tried again on the next run when Plex can't be reached
            try
            {
                foreach (var record in applying)
                {
                    TakeOffLabel(settings, record);
                }
            }
            catch (Exception ex)
            {
                Warn($"connection:{definition.Id}", ex, "Unable to take version labels off in Plex ({0}): {1}", definition.Name, ex.Message);
            }
        }

        private void TakeOffLabel(PlexServerSettings settings, PlexVersionLabel record)
        {
            var item = _plexServerProxy.GetMovie(record.RatingKey, settings);

            if (item != null)
            {
                Restore(settings, record, item);
            }

            _labelRepository.Delete(record.Id);
        }

        // Restores what the item had before it was labeled, in each field that still has what Radarr put there
        private void Restore(PlexServerSettings settings, PlexVersionLabel record, PlexMovie item)
        {
            var fields = new Dictionary<string, string>();

            if (item.Title == record.Title)
            {
                fields.Add("title.value", record.OriginalTitle ?? string.Empty);
                fields.Add("title.locked", record.OriginalTitleLocked ? "1" : "0");
            }

            if (item.EditionTitle == record.EditionTitle &&
                (record.EditionTitle != record.OriginalEditionTitle || !record.OriginalEditionTitleLocked))
            {
                fields.Add("editionTitle.value", record.OriginalEditionTitle ?? string.Empty);
                fields.Add("editionTitle.locked", record.OriginalEditionTitleLocked ? "1" : "0");
            }

            if (fields.Any())
            {
                _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings);
                _logger.Info("Took the label off '{0}' in Plex", record.Title);
            }
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

        private bool IsDue(NotificationDefinition definition, VersionFile version, PlexVersionLabel record, DateTime now)
        {
            if (record != null &&
                record.MovieFileId == version.File.Id &&
                record.Name == version.Version.Name &&
                record.EditionTitle == version.EditionName)
            {
                return now - record.LastChecked >= RecheckLabeledAfter;
            }

            if (_misses.TryGetValue(GetKey(definition, version), out var miss) && IsSame(miss, version))
            {
                return now - miss.FirstMissed < RetryOnEveryRunFor || now - miss.LastTried >= RetryAfter;
            }

            return true;
        }

        private void Missed(string key, VersionFile version, DateTime now)
        {
            var firstMissed = _misses.TryGetValue(key, out var earlier) && IsSame(earlier, version) ? earlier.FirstMissed : now;

            _misses[key] = new Miss
            {
                FilePath = version.FilePath,
                Name = version.Version.Name,
                FirstMissed = firstMissed,
                LastTried = now
            };
        }

        private static bool IsSame(Miss miss, VersionFile version)
        {
            return miss.FilePath == version.FilePath && miss.Name == version.Version.Name;
        }

        // As for every connection: one with tags applies only to movies with one of them. Labels of a movie Radarr no
        // longer has can still be taken off.
        private static bool AppliesTo(NotificationDefinition definition, Movie movie)
        {
            if (definition.Tags == null || definition.Tags.Empty() || movie == null)
            {
                return true;
            }

            return movie.Tags != null && definition.Tags.Intersect(movie.Tags).Any();
        }

        private static Movie FindMovie(Library library, int movieId)
        {
            return library.Movies.TryGetValue(movieId, out var movie) ? movie : null;
        }

        private static PlexVersionLabel FindRecord(List<PlexVersionLabel> records, VersionFile version)
        {
            return records.FirstOrDefault(r => r.MovieVersionId == version.Version.Id);
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

        private class Library
        {
            public Dictionary<int, Movie> Movies { get; set; }
            public List<VersionFile> VersionFiles { get; set; }
        }

        private class VersionFile
        {
            public Movie Movie { get; set; }
            public MovieVersion Version { get; set; }
            public MovieFile File { get; set; }
            public string EditionName { get; set; }
            public string FilePath { get; set; }
            public List<string> OwnFilePaths { get; set; }
        }

        private class Miss
        {
            public string FilePath { get; set; }
            public string Name { get; set; }
            public DateTime FirstMissed { get; set; }
            public DateTime LastTried { get; set; }
        }
    }
}
