using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class plex_version_label_serversFixture : MigrationTest<plex_version_label_servers>
    {
        [Test]
        public void should_keep_labels_per_plex_server()
        {
            var db = WithMigrationTestDb();

            var labels = db.Query<PlexVersionLabel1003>("SELECT \"Id\", \"ServerId\", \"RatingKey\", \"SectionId\", \"MovieId\", \"MovieVersionId\", \"MovieFileId\", \"FilePath\", \"Name\", \"Title\", \"PendingTitle\", \"OriginalTitle\", \"OriginalTitleLocked\", \"LastChecked\" FROM \"PlexVersionLabels\"");

            labels.Should().BeEmpty();
        }

        [Test]
        public void should_keep_labels_recorded_by_1002_apart()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("PlexVersionLabels").Row(new
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
                    OriginalEditionTitleLocked = true,
                    LastChecked = DateTime.UtcNow
                });
            });

            var legacy = db.Query<PlexVersionLabelLegacy1003>("SELECT \"NotificationId\", \"RatingKey\", \"Title\", \"EditionTitle\", \"OriginalTitle\", \"OriginalEditionTitle\", \"OriginalEditionTitleLocked\" FROM \"PlexVersionLabelsLegacy\"");

            legacy.Should().ContainSingle();
            legacy[0].NotificationId.Should().Be(3);
            legacy[0].RatingKey.Should().Be("101");
            legacy[0].Title.Should().Be("Blade Runner (IMAX)");
            legacy[0].EditionTitle.Should().Be("IMAX");
            legacy[0].OriginalTitle.Should().Be("Blade Runner");
            legacy[0].OriginalEditionTitle.Should().Be("IMAX");
            legacy[0].OriginalEditionTitleLocked.Should().BeTrue();

            db.Query<PlexVersionLabel1003>("SELECT \"Id\", \"ServerId\", \"RatingKey\", \"LastChecked\" FROM \"PlexVersionLabels\"").Should().BeEmpty();
        }

        [Test]
        public void should_add_an_empty_edition_to_existing_versions()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("MovieVersions").Row(new
                {
                    MovieId = 1,
                    Name = "IMAX",
                    QualityProfileId = 1,
                    RequiredCustomFormatIds = "[]",
                    Pinned = true,
                    MovieFileId = 0
                });
            });

            var versions = db.Query<MovieVersion1003>("SELECT \"Name\", \"Edition\" FROM \"MovieVersions\"");

            versions.Should().ContainSingle();
            versions[0].Name.Should().Be("IMAX");
            versions[0].Edition.Should().BeNull();
        }
    }

    public class PlexVersionLabel1003
    {
        public int Id { get; set; }
        public string ServerId { get; set; }
        public string RatingKey { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class PlexVersionLabelLegacy1003
    {
        public int NotificationId { get; set; }
        public string RatingKey { get; set; }
        public string Title { get; set; }
        public string EditionTitle { get; set; }
        public string OriginalTitle { get; set; }
        public string OriginalEditionTitle { get; set; }
        public bool OriginalEditionTitleLocked { get; set; }
    }

    public class MovieVersion1003
    {
        public string Name { get; set; }
        public string Edition { get; set; }
    }
}
