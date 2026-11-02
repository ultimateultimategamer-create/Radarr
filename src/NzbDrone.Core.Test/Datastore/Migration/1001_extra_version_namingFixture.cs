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
                    ReplaceIllegalCharacters = true,
                    ColonReplacementFormat = 4,
                    StandardMovieFormat = "{Movie Title} ({Release Year}) {Quality Full}",
                    MovieFolderFormat = "{Movie Title} ({Release Year})"
                });
            });

            var naming = db.Query<NamingConfig1001>("SELECT \"ExtraVersionNaming\" FROM \"NamingConfig\"");

            naming.Should().ContainSingle().Which.ExtraVersionNaming.Should().Be(1);
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

            var versions = db.Query<MovieVersion1001>("SELECT \"Name\", \"Edition\" FROM \"MovieVersions\"");

            versions.Should().ContainSingle();
            versions[0].Name.Should().Be("IMAX");
            versions[0].Edition.Should().BeNull();
        }
    }

    public class NamingConfig1001
    {
        public int ExtraVersionNaming { get; set; }
    }

    public class MovieVersion1001
    {
        public string Name { get; set; }
        public string Edition { get; set; }
    }
}
