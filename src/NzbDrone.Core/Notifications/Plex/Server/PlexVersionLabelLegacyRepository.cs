using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    public interface IPlexVersionLabelLegacyRepository : IBasicRepository<PlexVersionLabelLegacy>
    {
    }

    public class PlexVersionLabelLegacyRepository : BasicRepository<PlexVersionLabelLegacy>, IPlexVersionLabelLegacyRepository
    {
        public PlexVersionLabelLegacyRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
