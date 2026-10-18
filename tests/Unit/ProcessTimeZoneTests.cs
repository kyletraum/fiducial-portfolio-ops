namespace Portfolio.Unit;

/// <summary>
/// The CI time-zone jobs (slice-02.md DoD 10) set TZ and EXPECT_TZ. This makes a green run there
/// prove it ran in that zone: if .NET did not pick up TZ, every other test would still pass - in
/// UTC - and the job would prove nothing. Skipped where EXPECT_TZ is unset, as it is everywhere else.
/// </summary>
public class ProcessTimeZoneTests
{
    [Fact]
    public void The_process_runs_in_the_zone_the_job_asked_for()
    {
        var expected = Environment.GetEnvironmentVariable("EXPECT_TZ");
        Assert.SkipWhen(string.IsNullOrEmpty(expected), "EXPECT_TZ is unset: not a time-zone job.");

        Assert.Equal(expected, TimeZoneInfo.Local.Id);
        Assert.NotEqual(TimeSpan.Zero, TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.UtcNow));
    }
}
