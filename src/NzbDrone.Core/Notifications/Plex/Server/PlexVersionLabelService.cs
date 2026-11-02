using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
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
    // Labels are kept per Plex server and item, so connections to the same server share them, and the server is reached
    // through whichever of those works. Each label is recorded with the item's exact title before it's edited, to take it
    // off again exactly. An item is labeled only when Plex reports every
    // file of the item, the version's file among them, no file that isn't the version's, and the edition Radarr named the
    // file with. A connection only handles movies it applies to. A title changed outside Radarr, or one that already showed
    // the name, is left as it is. Labels are taken off when no labeling connection to their server applies to the movie any
    // longer, and only where the item is known to be the one labeled.
    public class PlexVersionLabelService : IExecute<LabelPlexVersionsCommand>, IHandle<ProviderUpdatedEvent<INotification>>
    {
        // A labeled version is checked again once a day, or as soon as its file, file name or name changes
        private static readonly TimeSpan RecheckLabeledAfter = TimeSpan.FromHours(24);

        // A version Plex hasn't picked up yet, or a label that can't be taken off yet, is tried on every run for an hour,
        // then hourly
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
        private readonly IPlexVersionLabelLegacyRepository _legacyLabelRepository;
        private readonly Logger _logger;

        private readonly ConcurrentDictionary<string, Miss> _misses = new();
        private readonly ConcurrentDictionary<int, Miss> _takeOffMisses = new();
        private readonly ConcurrentDictionary<string, DateTime> _warnings = new();
        private readonly ConcurrentDictionary<string, KnownServer> _servers = new();

        public PlexVersionLabelService(INotificationFactory notificationFactory,
                                       INamingConfigService namingConfigService,
                                       IMovieVersionService movieVersionService,
                                       IMovieService movieService,
                                       IMediaFileService mediaFileService,
                                       IPlexServerProxy plexServerProxy,
                                       IPlexVersionLabelRepository labelRepository,
                                       IPlexVersionLabelLegacyRepository legacyLabelRepository,
                                       Logger logger)
        {
            _notificationFactory = notificationFactory;
            _namingConfigService = namingConfigService;
            _movieVersionService = movieVersionService;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _plexServerProxy = plexServerProxy;
            _labelRepository = labelRepository;
            _legacyLabelRepository = legacyLabelRepository;
            _logger = logger;
        }

        private enum RestoreResult
        {
            Restored,
            NotLabeled,
            Unsure
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
            var legacyRecords = (_legacyLabelRepository.All() ?? Enumerable.Empty<PlexVersionLabelLegacy>()).ToList();

            // Labels of an earlier build come off first, and no label is added to an item while one of those is still on it
            var legacyLabeled = legacyRecords.Any() ? TakeOffLegacyLabels(connections, legacyRecords) : new HashSet<string>();

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
                    Label(server, library, serverRecords.Where(r => !takenOff.Contains(r.Id)).ToList(), legacyLabeled);
                }
            }
        }

        // A saved connection may reach another server, or apply to other movies
        public void Handle(ProviderUpdatedEvent<INotification> message)
        {
            _servers.Clear();
            _misses.Clear();
            _takeOffMisses.Clear();
        }

        // Which server a connection reaches is known for an hour, and asked again before anything in it is changed
        private string GetServerId(PlexServerSettings settings)
        {
            var now = DateTime.UtcNow;

            if (_servers.TryGetValue(GetAddress(settings), out var known) && now - known.Since < ServerKnownFor)
            {
                return known.Id;
            }

            var serverId = _plexServerProxy.GetMachineIdentifier(settings);

            if (serverId.IsNullOrWhiteSpace())
            {
                throw new PlexException("Plex Media Server didn't say which server it is");
            }

            _servers[GetAddress(settings)] = new KnownServer { Id = serverId, Since = now };

            return serverId;
        }

        private static string GetAddress(PlexServerSettings settings)
        {
            return $"{(settings.UseSsl ? "https" : "http")}://{settings.Host}:{settings.Port}{settings.UrlBase}";
        }

        // Makes a Plex call through one of the server's connections: the one that worked last, then labeling connections,
        // then the others. Each is asked first which server it reaches, as the server at an address can change.
        private T InPlex<T>(Server server, Func<PlexServerSettings, T> call)
        {
            Exception failure = null;

            foreach (var connection in server.InOrder())
            {
                var settings = (PlexServerSettings)connection.Settings;

                try
                {
                    if (!server.Confirmed.Contains(connection.Id))
                    {
                        if (_plexServerProxy.GetMachineIdentifier(settings) != server.Id)
                        {
                            // Not used again this run, and connections are grouped anew on the next
                            server.Rejected.Add(connection.Id);
                            _servers.Clear();

                            continue;
                        }

                        server.Confirmed.Add(connection.Id);
                    }

                    var result = call(settings);
                    server.Working = connection;

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Plex didn't take a call through {0}", connection.Name);
                    failure = ex;
                }
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            throw new ServerChangedException();
        }

        private void InPlex(Server server, Action<PlexServerSettings> call)
        {
            InPlex(server, settings =>
            {
                call(settings);

                return true;
            });
        }

        // Every extra version that has a file, whatever its naming, and the movies of those and of recorded labels
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

                if (file == null || file.RelativePath.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var extraVersionIds = versionsOfMovies[version.MovieId].Select(v => v.Id).ToList();

                versionFiles.Add(new VersionFile
                {
                    Movie = movie,
                    Version = version,
                    File = file,

                    // Empty when the file isn't named as an edition: such a version is never labeled
                    Edition = MovieVersionEditionName.FromFileName(Path.GetFileName(file.RelativePath)),
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

        // Labels whose version is gone or no longer named as an edition, or that no labeling connection to the server
        // applies to any longer, but one of the server's connections still does. The ids of those looked at.
        private HashSet<int> TakeOffLabels(Server server, Library library, List<PlexVersionLabel> records)
        {
            var now = DateTime.UtcNow;

            var labeled = library.VersionFiles
                .Where(v => v.Edition.IsNotNullOrWhiteSpace() && server.Labeling.Any(c => AppliesTo(c, v.Movie)))
                .Select(v => v.Version.Id)
                .ToHashSet();

            var taking = records
                .Where(r => !labeled.Contains(r.MovieVersionId) && server.Connections.Any(c => AppliesTo(c, FindMovie(library, r.MovieId))))
                .ToList();

            // A label stays recorded until it's taken off, or known to be gone, so it's tried again later when it can't be
            foreach (var record in taking.Where(r => IsTakeOffDue(r, now)))
            {
                var current = library.VersionFiles.FirstOrDefault(v => v.Version.Id == record.MovieVersionId);

                if (!TryInPlex($"server:{server.Id}", server.Name, () => TakeOffLabel(server, record, current?.FilePath, now)))
                {
                    TakeOffMissed(record, now);
                }
            }

            return taking.Select(r => r.Id).ToHashSet();
        }

        private void TakeOffLabel(Server server, PlexVersionLabel record, string currentFilePath, DateTime now)
        {
            var item = InPlex(server, settings => _plexServerProxy.GetMovie(record.RatingKey, settings));

            // Gone from Plex with its label
            if (item == null)
            {
                Forget(record);

                return;
            }

            if (Restore(server, record, item, currentFilePath) == RestoreResult.Unsure)
            {
                TakeOffMissed(record, now);

                return;
            }

            Forget(record);
        }

        private void Label(Server server, Library library, List<PlexVersionLabel> records, HashSet<string> legacyLabeled)
        {
            var now = DateTime.UtcNow;

            var due = library.VersionFiles
                .Where(v => v.Edition.IsNotNullOrWhiteSpace() &&
                            server.Labeling.Any(c => AppliesTo(c, v.Movie)) &&
                            IsDue(server.Id, v, RecordsOf(records, v), now))
                .ToList();

            if (due.Empty())
            {
                return;
            }

            List<PlexSection> sections = null;

            if (!TryInPlex($"server:{server.Id}", server.Name, () => sections = InPlex(server, settings => _plexServerProxy.GetMovieSections(settings)) ?? new List<PlexSection>()))
            {
                return;
            }

            foreach (var version in due)
            {
                LabelVersion(server, version, RecordsOf(records, version), sections, legacyLabeled, now);
            }
        }

        // Every item of the version, in every library of the server. A label, library or item Plex fails on, like an item
        // whose edit Plex refuses, doesn't hold up the others, and the version is looked at again.
        private void LabelVersion(Server server, VersionFile version, List<PlexVersionLabel> records, List<PlexSection> sections, HashSet<string> legacyLabeled, DateTime now)
        {
            var key = $"server:{server.Id}";
            var items = new Dictionary<string, Candidate>();
            var done = true;

            foreach (var record in records)
            {
                var settled = false;

                if (!TryInPlex(key, server.Name, () => settled = AddLabeledItem(server, version, record, items)) || !settled)
                {
                    done = false;
                }
            }

            foreach (var section in sections)
            {
                PlexMovie found = null;

                if (!TryInPlex(key, server.Name, () => found = FindItem(server, section.Id, version)))
                {
                    done = false;

                    continue;
                }

                if (found == null || items.ContainsKey(found.RatingKey))
                {
                    continue;
                }

                // Labeled by an earlier build, whose label comes off first
                if (legacyLabeled.Contains(found.RatingKey))
                {
                    done = false;

                    continue;
                }

                // Listings can leave fields out, the item itself has them all
                PlexMovie item = null;

                if (!TryInPlex(key, server.Name, () => item = InPlex(server, settings => _plexServerProxy.GetMovie(found.RatingKey, settings))))
                {
                    done = false;

                    continue;
                }

                if (item != null)
                {
                    items[found.RatingKey] = new Candidate { Item = item, SectionId = section.Id };
                }
            }

            if (items.Empty())
            {
                done = false;
            }

            foreach (var candidate in items.Values)
            {
                var labeled = false;

                if (!TryInPlex(key, server.Name, () => labeled = LabelItem(server, version, candidate, now)) || !labeled)
                {
                    done = false;
                }
            }

            if (done)
            {
                _misses.TryRemove(GetKey(server.Id, version), out _);
            }
            else
            {
                Missed(server.Id, version, now);
            }
        }

        // Adds the labeled item when it still holds the version's file, and takes the label off when it doesn't. False while
        // that isn't known.
        private bool AddLabeledItem(Server server, VersionFile version, PlexVersionLabel record, Dictionary<string, Candidate> items)
        {
            var labeled = InPlex(server, settings => _plexServerProxy.GetMovie(record.RatingKey, settings));

            // Gone from Plex with its label
            if (labeled == null)
            {
                Forget(record);

                return true;
            }

            // Not all of the item's files are known: kept as it is and looked at again
            if (!labeled.IsComplete())
            {
                return false;
            }

            if (labeled.GetFiles().Any(f => IsPath(f, version.FilePath)))
            {
                items[record.RatingKey] = new Candidate { Item = labeled, SectionId = record.SectionId, Record = record };

                return true;
            }

            // The version's file is in another item now
            if (Restore(server, record, labeled, null) == RestoreResult.Unsure)
            {
                return false;
            }

            Forget(record);

            return true;
        }

        // True when the item is labeled, or left as it is for good
        private bool LabelItem(Server server, VersionFile version, Candidate candidate, DateTime now)
        {
            var item = candidate.Item;
            var record = candidate.Record;

            // Not all of the item's files are known: nothing is changed
            if (!item.IsComplete())
            {
                return false;
            }

            var files = item.GetFiles();

            // Plex has to report the version's file in the item, and no file that isn't the version's: the item could
            // otherwise be the original movie
            if (!files.Any(f => IsPath(f, version.FilePath)) || !files.All(f => version.OwnFilePaths.Any(p => IsPath(f, p))))
            {
                if (record != null && Restore(server, record, item, version.FilePath) != RestoreResult.Unsure)
                {
                    Forget(record);
                }

                if (files.Any(f => !version.OwnFilePaths.Any(p => IsPath(f, p))))
                {
                    Warn($"shared:{server.Id}:{version.Version.Id}",
                         null,
                         "Plex holds the '{0}' version of {1} together with other files, so it isn't labeled. Name extra versions as Plex editions (Settings > Media Management) and rename the movie's files, or split the movie apart in Plex",
                         version.Version.Name,
                         version.Movie.Title);
                }

                return false;
            }

            // Until Plex has matched a new item its title comes from the file's name
            if (!IsMatched(item))
            {
                return false;
            }

            var name = version.Version.Name.Trim();
            var title = item.Title ?? string.Empty;

            if (record != null)
            {
                // The first edit never reached Plex, which still has the title from before
                var firstEditPending = record.Title == null && record.PendingTitle != null && title == record.OriginalTitle;

                // Left as it is when it showed the name already, or was changed outside Radarr since. Nothing is pending
                // any longer, so it's checked daily like any other label.
                if ((record.Title == null && record.PendingTitle == null) || (!IsWrittenByRadarr(record, title) && !firstEditPending))
                {
                    record.PendingTitle = null;
                    Track(record, version, now);
                    _labelRepository.Update(record);

                    return true;
                }

                Write(server, record, item, $"{record.OriginalTitle} ({name})", version, now);

                return true;
            }

            // A new label needs the edition Radarr named the file with, read by Plex from the file's name
            if (!string.Equals(item.EditionTitle?.Trim(), version.Edition, StringComparison.OrdinalIgnoreCase))
            {
                return false;
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

                return true;
            }

            // Saved before Plex is edited, so the title before is never lost
            record.PendingTitle = labeledTitle;
            record = _labelRepository.Insert(record) ?? record;

            Write(server, record, item, labeledTitle, version, now);

            return true;
        }

        // Writes the label, recording it as pending first and as taken once Plex took it
        private void Write(Server server, PlexVersionLabel record, PlexMovie item, string labeledTitle, VersionFile version, DateTime now)
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

                InPlex(server, settings => _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings));

                _logger.Info("Labeled the '{0}' version of {1} in Plex", version.Version.Name, version.Movie.Title);
            }

            record.Title = labeledTitle;
            record.PendingTitle = null;
            Track(record, version, now);
            _labelRepository.Update(record);
        }

        // Restores the title the item had, where the item is known to be the one labeled: all its files are known, it holds
        // the labeled file or the version's file now, and it still has a title Radarr wrote. Unsure when that isn't known.
        private RestoreResult Restore(Server server, PlexVersionLabel record, PlexMovie item, string currentFilePath)
        {
            if (!item.IsComplete())
            {
                return RestoreResult.Unsure;
            }

            var sameItem = item.GetFiles().Any(f => IsPath(f, record.FilePath) || IsPath(f, currentFilePath));

            if (!sameItem)
            {
                return RestoreResult.Unsure;
            }

            if (!IsWrittenByRadarr(record, item.Title))
            {
                return RestoreResult.NotLabeled;
            }

            var fields = new Dictionary<string, string>
            {
                { "title.value", record.OriginalTitle ?? string.Empty },
                { "title.locked", record.OriginalTitleLocked ? "1" : "0" }
            };

            InPlex(server, settings => _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings));

            _logger.Info("Took the label off '{0}' in Plex", item.Title);

            return RestoreResult.Restored;
        }

        // Labels recorded for a connection by an earlier build, which also edited and locked editions. Each is taken off
        // through that connection, as that build would have, and then forgotten. The rating keys of those Plex failed on,
        // to take off on the next run.
        private HashSet<string> TakeOffLegacyLabels(List<NotificationDefinition> connections, List<PlexVersionLabelLegacy> legacyRecords)
        {
            var left = new HashSet<string>();

            // The earliest label of an item has what the item had before any, so it's taken off first
            foreach (var record in legacyRecords.OrderBy(r => r.Id))
            {
                var connection = connections.FirstOrDefault(c => c.Id == record.NotificationId);

                // Kept until the connection that added it is available
                if (connection == null)
                {
                    continue;
                }

                var settings = (PlexServerSettings)connection.Settings;

                if (!TryInPlex($"connection:{connection.Id}", connection.Name, () => TakeOffLegacyLabel(settings, record)))
                {
                    left.Add(record.RatingKey);
                }
            }

            return left;
        }

        // Restores what the item had, in each field that still has what that build wrote
        private void TakeOffLegacyLabel(PlexServerSettings settings, PlexVersionLabelLegacy record)
        {
            var item = _plexServerProxy.GetMovie(record.RatingKey, settings);
            var fields = new Dictionary<string, string>();

            if (item != null && item.Title != null && item.Title == record.Title)
            {
                fields.Add("title.value", record.OriginalTitle ?? string.Empty);
                fields.Add("title.locked", record.OriginalTitleLocked ? "1" : "0");
            }

            if (item != null &&
                item.EditionTitle == record.EditionTitle &&
                (record.EditionTitle != record.OriginalEditionTitle || !record.OriginalEditionTitleLocked))
            {
                fields.Add("editionTitle.value", record.OriginalEditionTitle ?? string.Empty);
                fields.Add("editionTitle.locked", record.OriginalEditionTitleLocked ? "1" : "0");
            }

            if (fields.Any())
            {
                _plexServerProxy.EditMovie(record.SectionId, record.RatingKey, fields, settings);
                _logger.Info("Took the label of an earlier build off '{0}' in Plex", item.Title);
            }

            _legacyLabelRepository.Delete(record.Id);
        }

        private void Forget(PlexVersionLabel record)
        {
            _labelRepository.Delete(record.Id);
            _takeOffMisses.TryRemove(record.Id, out _);
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
        private PlexMovie FindItem(Server server, int sectionId, VersionFile version)
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

                var movies = InPlex(server, settings => _plexServerProxy.GetMovies(sectionId, search.Title, search.Year, settings)) ?? new List<PlexMovie>();
                var item = movies.FirstOrDefault(m => m.GetFiles().Any(f => IsPath(f, version.FilePath)));

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
                return IsRetryDue(miss, now);
            }

            var tracked = records.Any() && records.All(r => r.MovieFileId == version.File.Id &&
                                                            r.FilePath == version.FilePath &&
                                                            r.Name == version.Version.Name &&
                                                            r.PendingTitle == null);

            return !tracked || records.Any(r => now - r.LastChecked >= RecheckLabeledAfter);
        }

        private bool IsTakeOffDue(PlexVersionLabel record, DateTime now)
        {
            return !_takeOffMisses.TryGetValue(record.Id, out var miss) || IsRetryDue(miss, now);
        }

        private static bool IsRetryDue(Miss miss, DateTime now)
        {
            return now - miss.FirstMissed < RetryOnEveryRunFor || now - miss.LastTried >= RetryAfter;
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

        private void TakeOffMissed(PlexVersionLabel record, DateTime now)
        {
            var firstMissed = _takeOffMisses.TryGetValue(record.Id, out var earlier) ? earlier.FirstMissed : now;

            _takeOffMisses[record.Id] = new Miss
            {
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
            if (pathEnd.IsNullOrWhiteSpace() || plexFile.IsNullOrWhiteSpace())
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
            catch (ServerChangedException ex)
            {
                _logger.Debug(ex, "Version labels in Plex ({0}) wait until its connections are looked at again", connectionName);

                return false;
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
            public List<NotificationDefinition> Connections { get; } = new();
            public List<NotificationDefinition> Labeling { get; } = new();

            // Connections found this run to reach the server or another one, and the one that worked last
            public HashSet<int> Confirmed { get; } = new();
            public HashSet<int> Rejected { get; } = new();
            public NotificationDefinition Working { get; set; }

            public string Name => (Labeling.FirstOrDefault() ?? Connections.First()).Name;

            public List<NotificationDefinition> InOrder()
            {
                var ordered = new List<NotificationDefinition>();
                var ids = new HashSet<int>(Rejected);

                foreach (var connection in new[] { Working }.Concat(Labeling).Concat(Connections))
                {
                    if (connection != null && ids.Add(connection.Id))
                    {
                        ordered.Add(connection);
                    }
                }

                return ordered;
            }
        }

        // None of the server's connections reaches it any longer: its labels wait until connections are grouped anew
        private class ServerChangedException : PlexException
        {
            public ServerChangedException()
                : base("No connection reaches the Plex server any longer")
            {
            }
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
