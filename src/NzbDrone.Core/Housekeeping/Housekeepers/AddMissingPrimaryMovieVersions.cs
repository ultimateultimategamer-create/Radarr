using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    // Every movie needs a primary version (its oldest MovieVersions row) mirroring the movie itself,
    // otherwise the first version added to it would silently become the primary version.
    public class AddMissingPrimaryMovieVersions : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public AddMissingPrimaryMovieVersions(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();

            mapper.Execute(@"INSERT INTO ""MovieVersions"" (""MovieId"", ""Name"", ""QualityProfileId"", ""Path"", ""MovieFileId"")
                             SELECT ""Movies"".""Id"", 'Original', ""Movies"".""QualityProfileId"", ""Movies"".""Path"", ""Movies"".""MovieFileId"" FROM ""Movies""
                             LEFT OUTER JOIN ""MovieVersions""
                             ON ""Movies"".""Id"" = ""MovieVersions"".""MovieId""
                             WHERE ""MovieVersions"".""Id"" IS NULL");

            mapper.Execute(@"UPDATE ""MovieVersions"" SET ""Name"" = 'Original'
                             WHERE ""Id"" IN (SELECT MIN(""Id"") FROM ""MovieVersions"" GROUP BY ""MovieId"")
                             AND TRIM(COALESCE(""Name"", '')) = ''");

            // Keep the primary version's file in sync with the movie's file
            mapper.Execute(@"UPDATE ""MovieVersions""
                             SET ""MovieFileId"" = (SELECT ""Movies"".""MovieFileId"" FROM ""Movies"" WHERE ""Movies"".""Id"" = ""MovieVersions"".""MovieId"")
                             WHERE ""Id"" IN (SELECT MIN(""Id"") FROM ""MovieVersions"" GROUP BY ""MovieId"")
                             AND ""MovieFileId"" <> (SELECT ""Movies"".""MovieFileId"" FROM ""Movies"" WHERE ""Movies"".""Id"" = ""MovieVersions"".""MovieId"")");

            // Pinned versions follow the movie's quality profile, so a profile the movie left behind can still be deleted
            mapper.Execute(@"UPDATE ""MovieVersions""
                             SET ""QualityProfileId"" = (SELECT ""Movies"".""QualityProfileId"" FROM ""Movies"" WHERE ""Movies"".""Id"" = ""MovieVersions"".""MovieId"")
                             WHERE ""Pinned"" = @Pinned
                             AND ""MovieId"" IN (SELECT ""Id"" FROM ""Movies"")
                             AND ""QualityProfileId"" <> (SELECT ""Movies"".""QualityProfileId"" FROM ""Movies"" WHERE ""Movies"".""Id"" = ""MovieVersions"".""MovieId"")",
                           new { Pinned = true });
        }
    }
}
