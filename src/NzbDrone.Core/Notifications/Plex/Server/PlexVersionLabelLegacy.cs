using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    // A label recorded for a connection by an earlier build, which also edited and locked the item's edition. Such labels
    // are taken off through that connection, and only where the item still has the title Radarr wrote.
    public class PlexVersionLabelLegacy : ModelBase
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
