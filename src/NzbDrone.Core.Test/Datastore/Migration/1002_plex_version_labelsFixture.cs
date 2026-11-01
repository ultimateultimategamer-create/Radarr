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
        public void should_create_an_empty_table_for_plex_version_labels()
        {
            var db = WithMigrationTestDb();

            var labels = db.Query<PlexVersionLabel1002>("SELECT \"Id\", \"NotificationId\", \"MovieId\", \"MovieVersionId\", \"MovieFileId\", \"Name\", \"SectionId\", \"RatingKey\", \"Title\", \"EditionTitle\", \"OriginalTitle\", \"OriginalTitleLocked\", \"OriginalEditionTitle\", \"OriginalEditionTitleLocked\", \"LastChecked\" FROM \"PlexVersionLabels\"");

            labels.Should().BeEmpty();
        }
    }

    public class PlexVersionLabel1002
    {
        public int Id { get; set; }
        public int NotificationId { get; set; }
        public string RatingKey { get; set; }
    }
}
