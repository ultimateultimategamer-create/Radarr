using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(245)]
    public class named_movie_versions : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieVersions")
                .AddColumn("Name").AsString(100).WithDefaultValue("")
                .AddColumn("RequiredCustomFormatIds").AsString().WithDefaultValue("[]");

            // Preserve the labels used by the original quality-based versions.
            Execute.Sql(@"UPDATE ""MovieVersions""
                          SET ""Name"" = COALESCE(
                              (SELECT ""Name"" FROM ""QualityProfiles""
                               WHERE ""QualityProfiles"".""Id"" = ""MovieVersions"".""QualityProfileId""), 'Original')");
        }
    }
}
