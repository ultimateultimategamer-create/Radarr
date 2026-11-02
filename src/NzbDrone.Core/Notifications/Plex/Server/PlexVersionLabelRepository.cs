using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Notifications.Plex.Server
{
    public interface IPlexVersionLabelRepository : IBasicRepository<PlexVersionLabel>
    {
    }

    public class PlexVersionLabelRepository : BasicRepository<PlexVersionLabel>, IPlexVersionLabelRepository
    {
        public PlexVersionLabelRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
