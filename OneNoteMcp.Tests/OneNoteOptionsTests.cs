using OneNoteMcp.Core.Configuration;

namespace OneNoteMcp.Tests;

[TestFixture]
public class OneNoteOptionsTests
{
    [Test]
    public void The_default_idle_release_delay_is_ten_seconds()
    {
        Assert.That(new OneNoteOptions().IdleReleaseSeconds, Is.EqualTo(10));
    }

    [Test]
    public void Zero_is_a_valid_idle_release_delay()
    {
        Assert.DoesNotThrow(() => new OneNoteOptions { IdleReleaseSeconds = 0 }.Validate());
    }

    [Test]
    public void A_negative_idle_release_delay_is_rejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new OneNoteOptions { IdleReleaseSeconds = -1 }.Validate())!;

        Assert.That(ex.Message, Is.EqualTo("OneNote:IdleReleaseSeconds must be between 0 and 4294967 (0 releases OneNote right after each operation)."));
    }

    [Test]
    public void The_longest_delay_the_timer_supports_is_accepted()
    {
        Assert.DoesNotThrow(() => new OneNoteOptions { IdleReleaseSeconds = 4294967 }.Validate());
    }

    [Test]
    public void A_delay_beyond_what_the_timer_supports_is_rejected()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new OneNoteOptions { IdleReleaseSeconds = 4294968 }.Validate())!;

        Assert.That(ex.Message, Is.EqualTo("OneNote:IdleReleaseSeconds must be between 0 and 4294967 (0 releases OneNote right after each operation)."));
    }
}
