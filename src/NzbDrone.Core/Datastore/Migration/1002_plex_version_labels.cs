using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Labels Radarr put on Plex items of extra versions, kept per Plex server and item, with what each item had before
    [Migration(1002)]
    public class plex_version_labels : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("PlexVersionLabels")
                .WithColumn("ServerId").AsString().Indexed()
                .WithColumn("RatingKey").AsString()
                .WithColumn("SectionId").AsInt32()
                .WithColumn("MovieId").AsInt32()
                .WithColumn("MovieVersionId").AsInt32()
                .WithColumn("MovieFileId").AsInt32()
                .WithColumn("FilePath").AsString().Nullable()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("Title").AsString().Nullable()
                .WithColumn("PendingTitle").AsString().Nullable()
                .WithColumn("OriginalTitle").AsString().Nullable()
                .WithColumn("OriginalTitleLocked").AsBoolean().WithDefaultValue(false)
                .WithColumn("LastChecked").AsDateTimeOffset();
        }
    }
}
