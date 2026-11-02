using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Datastore;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class plex_version_labels_legacyFixture : MigrationTest<plex_version_labels_legacy>
    {
        [Test]
        public void should_keep_the_labels_1003_kept_apart()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("PlexVersionLabelsLegacy").Row(new
                {
                    NotificationId = 3,
                    MovieId = 5,
                    MovieVersionId = 7,
                    MovieFileId = 11,
                    Name = "IMAX",
                    SectionId = 1,
                    RatingKey = "101",
                    Title = "Blade Runner (IMAX)",
                    EditionTitle = "IMAX",
                    OriginalTitle = "Blade Runner",
                    OriginalTitleLocked = false,
                    OriginalEditionTitle = "IMAX",
                    OriginalEditionTitleLocked = false,
                    LastChecked = DateTime.UtcNow
                });
            });

            var legacy = db.Query<PlexVersionLabelLegacy1004>("SELECT \"Id\", \"NotificationId\", \"RatingKey\", \"Title\" FROM \"PlexVersionLabelsLegacy\"");

            legacy.Should().ContainSingle();
            legacy[0].NotificationId.Should().Be(3);
            legacy[0].RatingKey.Should().Be("101");
            legacy[0].Title.Should().Be("Blade Runner (IMAX)");
        }

        [Test]
        public void should_add_the_table_to_a_database_an_earlier_1003_dropped_it_from()
        {
            if (PostgresDatabase.GetTestOptions().Host.IsNotNullOrWhiteSpace())
            {
                Assert.Ignore("Migrates one database in two steps, which only a SQLite test database allows");
            }

            // What an earlier 1003 left: the labels of 1002 dropped, not kept apart
            var earlier = WithTestDb(new MigrationContext(MigrationType, MigrationVersion - 1) { BeforeMigration = m => { } });

            using (var connection = earlier.OpenConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DROP TABLE \"PlexVersionLabelsLegacy\"";
                command.ExecuteNonQuery();
            }

            var db = WithTestDb(new MigrationContext(MigrationType, MigrationVersion) { BeforeMigration = m => { } }).GetDirectDataMapper();

            db.Query<PlexVersionLabelLegacy1004>("SELECT \"Id\", \"NotificationId\", \"RatingKey\", \"Title\" FROM \"PlexVersionLabelsLegacy\"").Should().BeEmpty();
        }
    }

    public class PlexVersionLabelLegacy1004
    {
        public int Id { get; set; }
        public int NotificationId { get; set; }
        public string RatingKey { get; set; }
        public string Title { get; set; }
    }
}
