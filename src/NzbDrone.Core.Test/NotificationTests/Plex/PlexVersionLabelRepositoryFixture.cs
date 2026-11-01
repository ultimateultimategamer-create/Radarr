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
        public void should_store_a_label_with_what_the_item_had_before()
        {
            var lastChecked = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

            Subject.Insert(new PlexVersionLabel
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
                LastChecked = lastChecked
            });

            var label = StoredModel;

            label.NotificationId.Should().Be(3);
            label.MovieVersionId.Should().Be(7);
            label.RatingKey.Should().Be("101");
            label.Title.Should().Be("Blade Runner (IMAX)");
            label.OriginalTitle.Should().Be("Blade Runner");
            label.OriginalTitleLocked.Should().BeFalse();
            label.OriginalEditionTitleLocked.Should().BeTrue();
            label.LastChecked.ToUniversalTime().Should().Be(lastChecked);
        }
    }
}
