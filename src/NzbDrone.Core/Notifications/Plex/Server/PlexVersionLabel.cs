using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    // A label Radarr put on a Plex item through a Plex Media Server connection, with what the item had before, so the label
    // can be taken off again exactly, and only where the item still has it
    public class PlexVersionLabel : ModelBase
    {
        public int NotificationId { get; set; }
        public int MovieId { get; set; }
        public int MovieVersionId { get; set; }
        public int MovieFileId { get; set; }
        public string Name { get; set; }
        public int SectionId { get; set; }
        public string RatingKey { get; set; }
        public string Title { get; set; }
        public string EditionTitle { get; set; }
        public string OriginalTitle { get; set; }
        public bool OriginalTitleLocked { get; set; }
        public string OriginalEditionTitle { get; set; }
        public bool OriginalEditionTitleLocked { get; set; }
        public DateTime LastChecked { get; set; }
    }
}
