using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(1003)]
    public class plex_version_label_servers : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // The edition a version's files are named with, kept once handed out
            Alter.Table("MovieVersions").AddColumn("Edition").AsString().Nullable();

            // Labels are kept for a Plex server and item now, not for a connection. Labels recorded by 1002 can't be tied to
            // a server, so they're kept apart, and Radarr takes them off through the connection that added them.
            Rename.Table("PlexVersionLabels").To("PlexVersionLabelsLegacy");

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
