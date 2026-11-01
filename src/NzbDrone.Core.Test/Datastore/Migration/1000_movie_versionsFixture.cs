using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class movie_versionsFixture : MigrationTest<movie_versions>
    {
        private void InsertMovie(movie_versions c, int metadataId, int qualityProfileId, string path, int movieFileId)
        {
            c.Insert.IntoTable("Movies").Row(new
            {
                Monitored = true,
                MinimumAvailability = 4,
                QualityProfileId = qualityProfileId,
                MovieFileId = movieFileId,
                MovieMetadataId = metadataId,
                Path = path,
                Added = DateTime.UtcNow,
                Tags = "[]"
            });
        }

        private void InsertMovieFile(movie_versions c, int movieId)
        {
            c.Insert.IntoTable("MovieFiles").Row(new
            {
                MovieId = movieId,
                RelativePath = "Movie.mkv",
                Quality = new { }.ToJson(),
                IndexerFlags = 0,
                Size = 0,
                DateAdded = DateTime.UtcNow,
                Languages = new List<int> { 1 }.ToJson()
            });
        }

        [Test]
        public void should_create_a_primary_version_named_original_for_each_movie()
        {
            var db = WithMigrationTestDb(c =>
            {
                InsertMovie(c, 1, 2, "/Movies/Movie One (2020)", 10);
                InsertMovie(c, 2, 3, "/Movies/Movie Two (2021)", 0);
            });

            var movies = db.Query<Movie1000>("SELECT \"Id\", \"Path\" FROM \"Movies\"");
            var versions = db.Query<MovieVersion1000>("SELECT * FROM \"MovieVersions\"");

            versions.Should().HaveCount(2);
            versions.Should().OnlyContain(v => v.Name == "Original" && !v.Pinned && v.RequiredCustomFormatIds == "[]");

            var first = versions.Single(v => v.MovieId == movies.Single(m => m.Path == "/Movies/Movie One (2020)").Id);
            first.QualityProfileId.Should().Be(2);
            first.Path.Should().Be("/Movies/Movie One (2020)");
            first.MovieFileId.Should().Be(10);

            var second = versions.Single(v => v.MovieId == movies.Single(m => m.Path == "/Movies/Movie Two (2021)").Id);
            second.QualityProfileId.Should().Be(3);
            second.MovieFileId.Should().Be(0);
        }

        [Test]
        public void should_leave_existing_movie_files_on_the_primary_version()
        {
            var db = WithMigrationTestDb(c =>
            {
                InsertMovie(c, 1, 2, "/Movies/Movie One (2020)", 1);
                InsertMovieFile(c, 1);
            });

            var files = db.Query<MovieFile1000>("SELECT \"Id\", \"MovieVersionId\" FROM \"MovieFiles\"");

            files.Single().MovieVersionId.Should().Be(0);
        }

        [Test]
        public void should_name_versions_when_upgrading_a_database_that_ran_the_first_versions_migration()
        {
            // Recreates what this fork's first migration, then numbered 244, left behind: versions without names
            var db = WithTestDb(new MigrationContext(MigrationType, MigrationVersion)
            {
                BeforeMigration = m =>
                {
                    if (m is mediainfo_multiple_streams earlier)
                    {
                        earlier.Create.TableForModel("MovieVersions")
                               .WithColumn("MovieId").AsInt32().Indexed()
                               .WithColumn("QualityProfileId").AsInt32()
                               .WithColumn("Path").AsString().Nullable()
                               .WithColumn("MovieFileId").AsInt32().WithDefaultValue(0);

                        earlier.Alter.Table("MovieFiles").AddColumn("MovieVersionId").AsInt32().WithDefaultValue(0);

                        earlier.Insert.IntoTable("MovieVersions").Row(new { MovieId = 5, QualityProfileId = 41, Path = "/Movies/Movie (2020)", MovieFileId = 0 });
                        earlier.Insert.IntoTable("MovieVersions").Row(new { MovieId = 5, QualityProfileId = 42, Path = "/Movies/Movie (2020)", MovieFileId = 0 });
                        earlier.Insert.IntoTable("VersionInfo").Row(new { Version = 244L, AppliedOn = DateTime.UtcNow, Description = "add_movie_versions" });
                    }
                }
            }).GetDirectDataMapper();

            var versions = db.Query<MovieVersion1000>("SELECT * FROM \"MovieVersions\" ORDER BY \"Id\"");

            versions.Should().HaveCount(2);
            versions[0].Name.Should().Be("Original");
            versions[1].Name.Should().Be($"Version {versions[1].Id}");
            versions.Should().OnlyContain(v => !v.Pinned && v.RequiredCustomFormatIds == "[]");

            db.Query<VersionInfo1000>("SELECT \"Version\" FROM \"VersionInfo\" WHERE \"Version\" = 244").Should().BeEmpty();
        }
    }

    public class Movie1000
    {
        public int Id { get; set; }
        public string Path { get; set; }
    }

    public class MovieVersion1000
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string Name { get; set; }
        public int QualityProfileId { get; set; }
        public string RequiredCustomFormatIds { get; set; }
        public bool Pinned { get; set; }
        public string Path { get; set; }
        public int MovieFileId { get; set; }
    }

    public class MovieFile1000
    {
        public int Id { get; set; }
        public int MovieVersionId { get; set; }
    }

    public class VersionInfo1000
    {
        public long Version { get; set; }
    }
}
