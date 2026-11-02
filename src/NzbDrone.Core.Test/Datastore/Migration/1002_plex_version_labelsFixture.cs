using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class plex_version_labelsFixture : MigrationTest<plex_version_labels>
    {
        [Test]
        public void should_create_an_empty_table_for_labels_per_plex_server()
        {
            var db = WithMigrationTestDb();

            var labels = db.Query<PlexVersionLabel1002>("SELECT \"Id\", \"ServerId\", \"RatingKey\", \"SectionId\", \"MovieId\", \"MovieVersionId\", \"MovieFileId\", \"FilePath\", \"Name\", \"Title\", \"PendingTitle\", \"OriginalTitle\", \"OriginalTitleLocked\", \"LastChecked\" FROM \"PlexVersionLabels\"");

            labels.Should().BeEmpty();
        }
    }

    public class PlexVersionLabel1002
    {
        public int Id { get; set; }
        public string ServerId { get; set; }
        public string RatingKey { get; set; }
        public DateTime LastChecked { get; set; }
    }
}
