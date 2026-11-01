using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class extra_version_namingFixture : MigrationTest<extra_version_naming>
    {
        [Test]
        public void should_name_extra_versions_as_plex_editions()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("NamingConfig").Row(new
                {
                    RenameMovies = true,
                    StandardMovieFormat = "{Movie Title} ({Release Year}) {Quality Full}",
                    MovieFolderFormat = "{Movie Title} ({Release Year})"
                });
            });

            var naming = db.Query<NamingConfig1001>("SELECT \"ExtraVersionNaming\" FROM \"NamingConfig\"");

            naming.Should().ContainSingle().Which.ExtraVersionNaming.Should().Be(1);
        }
    }

    public class NamingConfig1001
    {
        public int ExtraVersionNaming { get; set; }
    }
}
