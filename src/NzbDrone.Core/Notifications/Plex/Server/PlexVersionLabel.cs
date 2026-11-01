using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    // A label Radarr put on an item of a Plex server, with the item's exact title before, so the label can be taken off
    // again exactly, and only where the item still has it. The record is saved before Plex is edited: PendingTitle is the
    // title being written, Title the one Plex took. Without either, the title already showed the name and was left alone.
    public class PlexVersionLabel : ModelBase
    {
        // The server's machine identifier
        public string ServerId { get; set; }
        public string RatingKey { get; set; }
        public int SectionId { get; set; }
        public int MovieId { get; set; }
        public int MovieVersionId { get; set; }
        public int MovieFileId { get; set; }

        // The movie's folder and the file's path in it
        public string FilePath { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string PendingTitle { get; set; }
        public string OriginalTitle { get; set; }
        public bool OriginalTitleLocked { get; set; }
        public DateTime LastChecked { get; set; }
    }
}
