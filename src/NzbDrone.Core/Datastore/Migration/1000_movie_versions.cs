using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Migrations of this fork are numbered from 1000 up, so they never collide with upstream Radarr's own migrations.
    // FluentMigrator applies every migration it hasn't recorded yet, so upstream migrations merged later still run.
    [Migration(1000)]
    public class movie_versions : NzbDroneMigrationBase
    {
        private const string ForgetEarlierNumbersSql = @"DELETE FROM ""VersionInfo""
                                                         WHERE ""Version"" IN (244, 245)
                                                         AND ""Description"" IN ('add_movie_versions', 'named_movie_versions')";

        protected override void MainDbUpgrade()
        {
            if (!Schema.Table("MovieVersions").Exists())
            {
                Create.TableForModel("MovieVersions")
                    .WithColumn("MovieId").AsInt32().Indexed()
                    .WithColumn("Name").AsString(100).WithDefaultValue("")
                    .WithColumn("QualityProfileId").AsInt32()
                    .WithColumn("RequiredCustomFormatIds").AsString().WithDefaultValue("[]")
                    .WithColumn("Pinned").AsBoolean().WithDefaultValue(false)
                    .WithColumn("Path").AsString().Nullable()
                    .WithColumn("MovieFileId").AsInt32().WithDefaultValue(0);

                // 0 = file of the movie's primary version, so existing files need no update.
                Alter.Table("MovieFiles").AddColumn("MovieVersionId").AsInt32().WithDefaultValue(0);

                // Every existing movie becomes a movie with a single, primary version.
                Execute.Sql(@"INSERT INTO ""MovieVersions"" (""MovieId"", ""Name"", ""QualityProfileId"", ""Path"", ""MovieFileId"")
                              SELECT ""Id"", 'Original', ""QualityProfileId"", ""Path"", ""MovieFileId"" FROM ""Movies""");

                return;
            }

            // Databases that ran this fork's earlier migrations (then numbered 244 and 245) already hold the versions.
            if (!Schema.Table("MovieVersions").Column("Name").Exists())
            {
                Alter.Table("MovieVersions").AddColumn("Name").AsString(100).WithDefaultValue("");
            }

            if (!Schema.Table("MovieVersions").Column("RequiredCustomFormatIds").Exists())
            {
                Alter.Table("MovieVersions").AddColumn("RequiredCustomFormatIds").AsString().WithDefaultValue("[]");
            }

            if (!Schema.Table("MovieVersions").Column("Pinned").Exists())
            {
                Alter.Table("MovieVersions").AddColumn("Pinned").AsBoolean().WithDefaultValue(false);
            }

            if (!Schema.Table("MovieFiles").Column("MovieVersionId").Exists())
            {
                Alter.Table("MovieFiles").AddColumn("MovieVersionId").AsInt32().WithDefaultValue(0);
            }

            // Forget those numbers, so upstream Radarr's own migrations 244 and 245 still run once they exist.
            Execute.Sql(ForgetEarlierNumbersSql);
        }

        protected override void LogDbUpgrade()
        {
            Execute.Sql(ForgetEarlierNumbersSql);
        }
    }
}
