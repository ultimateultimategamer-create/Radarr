using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // An earlier 1003 dropped the labels recorded by 1002 instead of keeping them apart: such databases get the table empty
    [Migration(1004)]
    public class plex_version_labels_legacy : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            if (Schema.Table("PlexVersionLabelsLegacy").Exists())
            {
                return;
            }

            Create.TableForModel("PlexVersionLabelsLegacy")
                .WithColumn("NotificationId").AsInt32()
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
