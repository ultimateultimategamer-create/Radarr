using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Labels Radarr put on Plex items of extra versions, with what the items had before
    [Migration(1002)]
    public class plex_version_labels : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("PlexVersionLabels")
                .WithColumn("NotificationId").AsInt32().Indexed()
                .WithColumn("MovieId").AsInt32()
                .WithColumn("MovieVersionId").AsInt32()
                .WithColumn("MovieFileId").AsInt32()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("SectionId").AsInt32()
                .WithColumn("RatingKey").AsString()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("EditionTitle").AsString().Nullable()
                .WithColumn("OriginalTitle").AsString().Nullable()
                .WithColumn("OriginalTitleLocked").AsBoolean().WithDefaultValue(false)
                .WithColumn("OriginalEditionTitle").AsString().Nullable()
                .WithColumn("OriginalEditionTitleLocked").AsBoolean().WithDefaultValue(false)
                .WithColumn("LastChecked").AsDateTimeOffset();
        }
    }
}
