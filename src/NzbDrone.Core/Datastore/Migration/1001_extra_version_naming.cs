using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Files of extra versions are named as Plex editions (1 = ExtraVersionNaming.PlexEdition) unless set otherwise, each
    // version with the edition it was handed, kept from then on
    [Migration(1001)]
    public class extra_version_naming : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("NamingConfig").AddColumn("ExtraVersionNaming").AsInt32().NotNullable().WithDefaultValue(1);
            Alter.Table("MovieVersions").AddColumn("Edition").AsString().Nullable();
        }
    }
}
