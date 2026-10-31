using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class named_movie_versionsFixture : MigrationTest<named_movie_versions>
    {
        [Test]
        public void should_preserve_existing_version_identity_and_files()
        {
            var db = WithMigrationTestDb(c => c.Insert.IntoTable("MovieVersions").Row(new
            {
                Id = 7,
                MovieId = 5,
                QualityProfileId = 3,
                Path = "/Movies/Movie",
                MovieFileId = 42
            }));

            var versions = db.Query<MovieVersion245>("SELECT \"Id\", \"MovieFileId\", \"Name\", \"RequiredCustomFormatIds\" FROM \"MovieVersions\"");

            versions.Should().ContainSingle();
            versions[0].Id.Should().Be(7);
            versions[0].MovieFileId.Should().Be(42);
            versions[0].Name.Should().Be("Original");
            versions[0].RequiredCustomFormatIds.Should().Be("[]");
        }
    }

    public class MovieVersion245
    {
        public int Id { get; set; }
        public int MovieFileId { get; set; }
        public string Name { get; set; }
        public string RequiredCustomFormatIds { get; set; }
    }
}
