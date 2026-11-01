using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
    // Files of extra versions named as Plex editions are items of their own in Plex. For Plex Media Server connections with
    // 'Label Extra Versions' on, this puts the version's name at the end of those items' titles, "Movie (IMAX)", locked so
    // a Plex metadata refresh keeps it. Only the title is edited: Plex takes the edition from the file's name.
    // Labels are kept per Plex server and item, so connections to the same server share them, and each is recorded with the
    // item's exact title before it's edited, to take it off again exactly. An item is labeled only when Plex reports the
    // version's file in it, no file that isn't the version's, and the edition Radarr named the file with.
    // A connection only handles movies it applies to. A title changed outside Radarr, or one that already showed the name,
    // is left as it is. Labels are taken off when no labeling connection to their server applies to the movie any longer.
    public class PlexVersionLabelService : IExecute<LabelPlexVersionsCommand>, IHandle<ProviderUpdatedEvent<INotification>>
    {
        // A labeled version is checked again once a day, or as soon as its file, file name or name changes
        private static readonly TimeSpan RecheckLabeledAfter = TimeSpan.FromHours(24);

        // A version Plex hasn't picked up yet is looked for on every run for an hour, then hourly
        private static readonly TimeSpan RetryOnEveryRunFor = TimeSpan.FromHours(1);
        private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(1);
        private static readonly TimeSpan WarnAgainAfter = TimeSpan.FromHours(1);
        private static readonly TimeSpan ServerKnownFor = TimeSpan.FromHours(1);

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
        private readonly ConcurrentDictionary<string, KnownServer> _servers = new();

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

        private enum ItemResult
        {
            Done,
            NotMatched,
            NotTheVersions
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
            var records = (_labelRepository.All() ?? Enumerable.Empty<PlexVersionLabel>()).ToList();

            // Without labels to add or take off, Plex isn't contacted at all
            if (labelingIds.Empty() && records.Empty())
            {
                return;
            }

            var library = GetLibrary(records);
            var servers = new Dictionary<string, Server>();

            foreach (var definition in connections)
            {
                var labeling = labelingIds.Contains(definition.Id);

                // A connection that doesn't label is only needed to take labels off
                if (!labeling && records.Empty())
                {
                    continue;
                }

                string serverId = null;

                if (!TryInPlex($"connection:{definition.Id}", definition.Name, () => serverId = GetServerId((PlexServerSettings)definition.Settings)))
                {
                    continue;
                }

                if (!servers.TryGetValue(serverId, out var server))
                {
                    server = new Server { Id = serverId };
                    servers.Add(serverId, server);
                }

                server.Connections.Add(definition);

                if (labeling)
                {
                    server.Labeling.Add(definition);
                }
            }

            foreach (var server in servers.Values)
            {
                var serverRecords = records.Where(r => r.ServerId == server.Id).ToList();
                var takenOff = TakeOffLabels(server, library, serverRecords);

                if (server.Labeling.Any())
                {
                    Label(server, library, serverRecords.Where(r => !takenOff.Contains(r.Id)).ToList());
                }
            }
        }

        // A saved connection may reach another server, or apply to other movies
        public void Handle(ProviderUpdatedEvent<INotification> message)
        {
            _servers.Clear();
            _misses.Clear();
        }

        // Which server a connection reaches is known for an hour, and asked again before anything in it is changed
        private string GetServerId(PlexServerSettings settings)
        {
            var address = $"{(settings.UseSsl ? "https" : "http")}://{settings.Host}:{settings.Port}{settings.UrlBase}";
            var now = DateTime.UtcNow;

            if (_servers.TryGetValue(address, out var known) && now - known.Since < ServerKnownFor)
            {
                return known.Id;
            }

            var serverId = _plexServerProxy.GetMachineIdentifier(settings);

            if (serverId.IsNullOrWhiteSpace())
            {
                throw new PlexException("Plex Media Server didn't say which server it is");
            }

            _servers[address] = new KnownServer { Id = serverId, Since = now };

            return serverId;
        }

        // Checks the connection still reaches the server, as the server at an address can change. If it doesn't, the
        // servers are asked again on the next run.
        private bool Confirm(Server server)
        {
            if (server.Confirmed)
            {
                return true;
            }

            string serverId = null;

            if (!TryInPlex($"server:{server.Id}", server.Name, () => serverId = _plexServerProxy.GetMachineIdentifier(server.Settings)))
            {
                return false;
            }

            if (serverId != server.Id)
            {
                _servers.Clear();
                _logger.Debug("The Plex server at {0} has changed, its labels are looked at again on the next run", server.Settings.Host);

                return false;
            }

            server.Confirmed = true;

            return true;
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
                var edition = file?.RelativePath.IsNotNullOrWhiteSpace() == true
                    ? MovieVersionEditionName.FromFileName(Path.GetFileName(file.RelativePath))
                    : string.Empty;

                if (edition.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var extraVersionIds = versionsOfMovies[version.MovieId].Select(v => v.Id).ToList();

                versionFiles.Add(new VersionFile
                {
                    Movie = movie,
                    Version = version,
                    File = file,
                    Edition = edition,
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

        // Labels whose version is gone, or that no labeling connection to the server applies to any longer, but one of
        // the server's connections still does. The ids of those tried, taken off or not.
        private HashSet<int> TakeOffLabels(Server server, Library library, List<PlexVersionLabel> records)
        {
            var labeled = library.VersionFiles
                .Where(v => server.Labeling.Any(c => AppliesTo(c, v.Movie)))
                .Select(v => v.Version.Id)
                .ToHashSet();

            var taking = records
                .Where(r => !labeled.Contains(r.MovieVersionId) && server.Connections.Any(c => AppliesTo(c, FindMovie(library, r.MovieId))))
                .ToList();

            if (taking.Empty() || !Confirm(server))
            {
                return new HashSet<int>();
            }

            // A label stays recorded until it's taken off, so it's tried again on the next run when Plex can't be reached
            foreach (var record in taking)
            {
                var current = library.VersionFiles.FirstOrDefault(v => v.Version.Id == record.MovieVersionId);

                TryInPlex($"server:{server.Id}", server.Name, () => TakeOffLabel(server.Settings, record, current?.FilePath));
            }

            return taking.Select(r => r.Id).ToHashSet();
        }

        private void Label(Server server, Library library, List<PlexVersionLabel> records)
        {
            var now = DateTime.UtcNow;

            var due = library.VersionFiles
                .Where(v => server.Labeling.Any(c => AppliesTo(c, v.Movie)) && IsDue(server.Id, v, RecordsOf(records, v), now))
                .ToList();

            if (due.Empty() || !Confirm(server))
            {
                return;
            }

            List<PlexSection> sections = null;

            if (!TryInPlex($"server:{server.Id}", server.Name, () => sections = _plexServerProxy.GetMovieSections(server.Settings) ?? new List<PlexSection>()))
            {
                return;
            }

            // One version Plex can't take an edit for doesn't hold up the others
            foreach (var version in due)
            {
                if (!TryInPlex($"server:{server.Id}", server.Name, () => LabelVersion(server, version, RecordsOf(records, version), sections, now)))
                {
                    Missed(server.Id, version, now);
                }
            }
        }

        // Every item of the version, in every library of the server
        private void LabelVersion(Server server, VersionFile version, List<PlexVersionLabel> records, List<PlexSection> sections, DateTime now)
        {
            var settings = server.Settings;
            var items = new Dictionary<string, Candidate>();
            var unsure = false;

            foreach (var record in records)
            {
                var labeled = _plexServerProxy.GetMovie(record.RatingKey, settings);

                // Gone from Plex
                if (labeled == null)
                {
                    _labelRepository.Delete(record.Id);

                    continue;
                }

                var labeledFiles = labeled.GetFiles();

                // Plex said nothing of the item's files: kept as it is and looked at again
                if (labeledFiles.Empty())
                {
                    unsure = true;

                    continue;
                }

                if (labeledFiles.Any(f => IsPath(f, version.FilePath)))
                {
                    items[record.RatingKey] = new Candidate { Item = labeled, SectionId = record.SectionId, Record = record };

                    continue;
                }

                // The version's file is in another item now
                Restore(settings, record, labeled, null);
                _labelRepository.Delete(record.Id);
            }

            foreach (var section in sections)
            {
                var found = FindItem(section.Id, version, settings);

                if (found == null || items.ContainsKey(found.RatingKey))
                {
                    continue;
                }

                // Listings can leave fields out, the item itself has them all
                var item = _plexServerProxy.GetMovie(found.RatingKey, settings);

                if (item != null)
                {
                    items[found.RatingKey] = new Candidate { Item = item, SectionId = section.Id };
                }
            }

            var results = items.Values.Select(c => LabelItem(server, version, c, now)).ToList();

            if (unsure || results.Empty() || results.Any(r => r != ItemResult.Done))
            {
                Missed(server.Id, version, now);
            }
            else
            {
                _misses.TryRemove(GetKey(server.Id, version), out _);
            }
        }

        private ItemResult LabelItem(Server server, VersionFile version, Candidate candidate, DateTime now)
        {
            var settings = server.Settings;
            var item = candidate.Item;
            var record = candidate.Record;
            var files = item.GetFiles();

            // Plex said nothing of the item's files: nothing is known, so nothing is changed
            if (files.Empty())
            {
                return ItemResult.NotTheVersions;
            }

            // Plex has to report the version's file in the item, and no file that isn't the version's: the item could
            // otherwise be the original movie
            if (!files.Any(f => IsPath(f, version.FilePath)) || !files.All(f => version.OwnFilePaths.Any(p => IsPath(f, p))))
            {
                if (record != null)
                {
                    Restore(settings, record, item, version.FilePath);
                    _labelRepository.Delete(record.Id);
                }

                if (files.Any(f => !version.OwnFilePaths.Any(p => IsPath(f, p))))
                {
                    Warn($"shared:{server.Id}:{version.Version.Id}",
                         null,
                         "Plex holds the '{0}' version of {1} together with other files, so it isn't labeled. Name extra versions as Plex editions (Settings > Media Management) and rename the movie's files, or split the movie apart in Plex",
                         version.Version.Name,
                         version.Movie.Title);
                }

                return ItemResult.NotTheVersions;
            }

            // Until Plex has matched a new item its title comes from the file's name
            if (!IsMatched(item))
            {
                return ItemResult.NotMatched;
            }

            var name = version.Version.Name.Trim();
            var title = item.Title ?? string.Empty;

            if (record != null)
            {
                // The first edit never reached Plex, which still has the title from before
                var firstEditPending = record.Title == null && record.PendingTitle != null && title == record.OriginalTitle;

                // Left as it is when it showed the name already, or was changed outside Radarr since
                if ((record.Title == null && record.PendingTitle == null) || (!IsWrittenByRadarr(record, title) && !firstEditPending))
                {
                    Track(record, version, now);
                    _labelRepository.Update(record);

                    return ItemResult.Done;
                }

                Write(settings, record, item, $"{record.OriginalTitle} ({name})", version, now);

                return ItemResult.Done;
            }

            // A new label needs the edition Radarr named the file with, read by Plex from the file's name
            if (!string.Equals(item.EditionTitle?.Trim(), version.Edition, StringComparison.OrdinalIgnoreCase))
            {
                return ItemResult.NotTheVersions;
            }

            var labeledTitle = title.EndsWith($" ({name})", StringComparison.Ordinal) ? title : $"{title} ({name})";

            record = new PlexVersionLabel
            {
                ServerId = server.Id,
                RatingKey = item.RatingKey,
                SectionId = candidate.SectionId,
                MovieId = version.Movie.Id,
                MovieVersionId = version.Version.Id,
                OriginalTitle = title,
                OriginalTitleLocked = item.IsLocked("title")
            };

            Track(record, version, now);

            // A title that already shows the name, from a person or an earlier label, is recorded as nothing to take off
            if (labeledTitle == title)
            {
                _labelRepository.Insert(record);

                return ItemResult.Done;
            }

            // Saved before Plex is edited, so the title before is never lost
            record.PendingTitle = labeledTitle;
            record = _labelRepository.Insert(record) ?? record;

            Write(settings, record, item, labeledTitle, version, now);

            return ItemResult.Done;
        }

        // Writes the label, recording it as pending first and as taken once Plex took it
        private void Write(PlexServerSettings settings, PlexVersionLabel record, PlexMovie item, string labeledTitle, VersionFile version, DateTime now)
        {
            if (item.Title != labeledTitle || !item.IsLocked("title"))
            {
                if (record.PendingTitle != labeledTitle)
                {
                    record.PendingTitle = labeledTitle;
                    _labelRepository.Update(record);
                }

                var fields = new Dictionary<string, string>
                {
                    { "title.value", labeledTitle },
                    { "title.locked", "1" }
                };

                _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings);

                _logger.Info("Labeled the '{0}' version of {1} in Plex", version.Version.Name, version.Movie.Title);
            }

            record.Title = labeledTitle;
            record.PendingTitle = null;
            Track(record, version, now);
            _labelRepository.Update(record);
        }

        private void TakeOffLabel(PlexServerSettings settings, PlexVersionLabel record, string currentFilePath)
        {
            var item = _plexServerProxy.GetMovie(record.RatingKey, settings);

            if (item != null)
            {
                // Plex said nothing of the item's files: the label stays recorded, to be taken off on a later run
                if (item.GetFiles().Empty())
                {
                    return;
                }

                Restore(settings, record, item, currentFilePath);
            }

            _labelRepository.Delete(record.Id);
        }

        // Restores the title the item had, only where the item is still the one labeled, holding the labeled file or the
        // version's file now, and still has a title Radarr wrote
        private void Restore(PlexServerSettings settings, PlexVersionLabel record, PlexMovie item, string currentFilePath)
        {
            var sameItem = item.GetFiles().Any(f => IsPath(f, record.FilePath) || (currentFilePath.IsNotNullOrWhiteSpace() && IsPath(f, currentFilePath)));

            if (!sameItem || !IsWrittenByRadarr(record, item.Title))
            {
                return;
            }

            var fields = new Dictionary<string, string>
            {
                { "title.value", record.OriginalTitle ?? string.Empty },
                { "title.locked", record.OriginalTitleLocked ? "1" : "0" }
            };

            _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings);

            _logger.Info("Took the label off '{0}' in Plex", item.Title);
        }

        private static bool IsWrittenByRadarr(PlexVersionLabel record, string title)
        {
            return title != null &&
                   title != record.OriginalTitle &&
                   (title == record.Title || title == record.PendingTitle);
        }

        private static void Track(PlexVersionLabel record, VersionFile version, DateTime now)
        {
            record.MovieFileId = version.File.Id;
            record.FilePath = version.FilePath;
            record.Name = version.Version.Name;
            record.LastChecked = now;
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

        private bool IsDue(string serverId, VersionFile version, List<PlexVersionLabel> records, DateTime now)
        {
            if (_misses.TryGetValue(GetKey(serverId, version), out var miss) && IsSame(miss, version))
            {
                return now - miss.FirstMissed < RetryOnEveryRunFor || now - miss.LastTried >= RetryAfter;
            }

            var tracked = records.Any() && records.All(r => r.MovieFileId == version.File.Id &&
                                                            r.FilePath == version.FilePath &&
                                                            r.Name == version.Version.Name &&
                                                            r.PendingTitle == null);

            return !tracked || records.Any(r => now - r.LastChecked >= RecheckLabeledAfter);
        }

        private void Missed(string serverId, VersionFile version, DateTime now)
        {
            var key = GetKey(serverId, version);
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

        private static List<PlexVersionLabel> RecordsOf(List<PlexVersionLabel> records, VersionFile version)
        {
            return records.Where(r => r.MovieVersionId == version.Version.Id).ToList();
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
            if (pathEnd.IsNullOrWhiteSpace())
            {
                return false;
            }

            var path = Normalize(plexFile);

            return path.Equals(pathEnd, StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith("/" + pathEnd, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').Trim().Trim('/');
        }

        private static string GetKey(string serverId, VersionFile version)
        {
            return $"{serverId}:{version.Version.Id}";
        }

        // False after logging what went wrong, as a warning at most hourly for each key
        private bool TryInPlex(string key, string connectionName, Action action)
        {
            try
            {
                action();

                return true;
            }
            catch (Exception ex)
            {
                Warn(key, ex, "Unable to update version labels in Plex ({0}): {1}", connectionName, ex.Message);

                return false;
            }
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

        private class Server
        {
            public string Id { get; set; }
            public bool Confirmed { get; set; }
            public List<NotificationDefinition> Connections { get; } = new();
            public List<NotificationDefinition> Labeling { get; } = new();

            // Connections to one server all reach the same items, so any of them does
            public PlexServerSettings Settings => (PlexServerSettings)(Labeling.FirstOrDefault() ?? Connections.First()).Settings;
            public string Name => (Labeling.FirstOrDefault() ?? Connections.First()).Name;
        }

        private class KnownServer
        {
            public string Id { get; set; }
            public DateTime Since { get; set; }
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
            public string Edition { get; set; }
            public string FilePath { get; set; }
            public List<string> OwnFilePaths { get; set; }
        }

        private class Candidate
        {
            public PlexMovie Item { get; set; }
            public int SectionId { get; set; }
            public PlexVersionLabel Record { get; set; }
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
