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

    public class MovieVersion1003
    {
        public string Name { get; set; }
        public string Edition { get; set; }
    }
}
