using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications.Plex.Server;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Plex
{
    [TestFixture]
    public class PlexVersionLabelRepositoryFixture : DbTest<PlexVersionLabelRepository, PlexVersionLabel>
    {
        [Test]
        public void should_store_a_label_with_the_title_from_before()
        {
            var lastChecked = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

            Subject.Insert(new PlexVersionLabel
            {
                ServerId = "5d776826151a60001f24a6b6",
                RatingKey = "101",
                SectionId = 1,
                MovieId = 5,
                MovieVersionId = 7,
                MovieFileId = 11,
                FilePath = "Blade Runner (1982)/Blade Runner (1982) {edition-IMAX}.mkv",
                Name = "IMAX",
                PendingTitle = "Blade Runner (IMAX)",
                OriginalTitle = "Blade Runner",
                OriginalTitleLocked = true,
                LastChecked = lastChecked
            });

            var label = StoredModel;

            label.ServerId.Should().Be("5d776826151a60001f24a6b6");
            label.RatingKey.Should().Be("101");
            label.FilePath.Should().Be("Blade Runner (1982)/Blade Runner (1982) {edition-IMAX}.mkv");
            label.Title.Should().BeNull();
            label.PendingTitle.Should().Be("Blade Runner (IMAX)");
            label.OriginalTitle.Should().Be("Blade Runner");
            label.OriginalTitleLocked.Should().BeTrue();
            label.LastChecked.ToUniversalTime().Should().Be(lastChecked);
        }
    }
}
