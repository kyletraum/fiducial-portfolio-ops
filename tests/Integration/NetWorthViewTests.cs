using Microsoft.EntityFrameworkCore;
using Portfolio.Infrastructure;

namespace Portfolio.Integration;

/// <summary>
/// v_net_worth_daily - "the only verification of the one piece of novel logic in the build"
/// (slice-01 step 5), so it is not cut. Two accounts, invented figures, 90-day cutoff:
///
///   A  opened 2026-01-01, balance 100 on 01-10, 150 on 03-01      (then silent)
///   B  opened 2026-02-01, balance 200 on 02-15, closed 2026-04-30
///
/// Every expected figure below is derived from those six lines and the rules in
/// Constitution Amendment 3, not read off the view.
/// </summary>
public sealed class NetWorthFixture : PostgresFixture
{
    protected override Task SeedAsync() => ExecuteAsync("""
        INSERT INTO institution (id, name) VALUES ('10000000-0000-4000-8000-000000000001', 'View Test Bank');
        INSERT INTO account (id, institution_id, display_name, currency, opened_on, closed_on) VALUES
          ('20000000-0000-4000-8000-00000000000a', '10000000-0000-4000-8000-000000000001', 'A', 'USD', '2026-01-01', NULL),
          ('20000000-0000-4000-8000-00000000000b', '10000000-0000-4000-8000-000000000001', 'B', 'USD', '2026-02-01', NULL);
        INSERT INTO account_balance (account_id, as_of_date, balance, currency, source_system, source_strength, observed_at) VALUES
          ('20000000-0000-4000-8000-00000000000a', '2026-01-10', 100, 'USD', 'manual', 'manual', now()),
          ('20000000-0000-4000-8000-00000000000a', '2026-03-01', 150, 'USD', 'manual', 'manual', now()),
          ('20000000-0000-4000-8000-00000000000b', '2026-02-15', 200, 'USD', 'manual', 'manual', now());
        UPDATE account SET closed_on = '2026-04-30' WHERE id = '20000000-0000-4000-8000-00000000000b';
        """);
}

public class NetWorthViewTests(NetWorthFixture pg) : IClassFixture<NetWorthFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    async Task<NetWorthDay> DayAsync(string date)
    {
        await using var db = pg.CreateContext();
        var d = DateOnly.Parse(date);
        return await NetWorthDay.Query(db, d, d).SingleAsync(Ct);
    }

    [Fact]
    public async Task A_day_with_nothing_verified_is_NULL_not_zero()
    {
        // 01-05: A is open, nothing recorded yet. Constitution II: UNVERIFIED.
        var day = await DayAsync("2026-01-05");

        Assert.Null(day.NetWorth);
        Assert.Equal(1, day.AccountsInWindow);
        Assert.Equal(0, day.AccountsVerified);
        Assert.Equal(1, day.AccountsUnverified); // non-zero only because the join is LEFT JOIN
    }

    [Fact]
    public async Task An_open_account_with_no_value_stays_in_the_denominator()
    {
        // 02-10: A carries 100 from 01-10; B opened 02-01 with nothing yet - counted, unverified.
        var day = await DayAsync("2026-02-10");

        Assert.Equal(100.0000m, day.NetWorth);
        Assert.Equal(2, day.AccountsInWindow);
        Assert.Equal(1, day.AccountsVerified);
        Assert.Equal(1, day.AccountsCarried);
        Assert.Equal(31, day.MaxStalenessDays);  // 02-10 minus 01-10
    }

    [Fact]
    public async Task Values_carry_forward_within_the_window()
    {
        // 03-20: A = 150 (from 03-01, 19 days), B = 200 (from 02-15, 33 days). Both carried.
        var day = await DayAsync("2026-03-20");

        Assert.Equal(350.0000m, day.NetWorth);
        Assert.Equal(2, day.AccountsVerified);
        Assert.Equal(2, day.AccountsCarried);
        Assert.Equal(33, day.MaxStalenessDays);
    }

    [Fact]
    public async Task A_carried_value_expires_after_the_cutoff_and_the_account_goes_unverified()
    {
        // A's last value is 03-01. At 90 days it is still carried on 05-30 and gone on 05-31.
        var lastCarried = await DayAsync("2026-05-30");
        var expired = await DayAsync("2026-05-31");

        Assert.Equal(150.0000m, lastCarried.NetWorth);
        Assert.Equal(90, lastCarried.MaxStalenessDays);
        Assert.Null(expired.NetWorth);
        Assert.Equal(1, expired.AccountsInWindow); // stale is NOT removed from the denominator
        Assert.Equal(1, expired.AccountsUnverified);
    }

    [Fact]
    public async Task A_closed_account_leaves_the_window_and_nothing_is_carried_past_it()
    {
        // B closed 04-30 (Constitution III). On 04-30 it still counts; on 05-01 it is gone,
        // not carried and not unverified - so the total drops by B's 200, visibly.
        var lastDay = await DayAsync("2026-04-30");
        var after = await DayAsync("2026-05-01");

        Assert.Equal(350.0000m, lastDay.NetWorth);
        Assert.Equal(2, lastDay.AccountsInWindow);
        Assert.Equal(150.0000m, after.NetWorth);
        Assert.Equal(1, after.AccountsInWindow);
        Assert.Equal(0, after.AccountsUnverified);
    }

    [Fact]
    public async Task The_series_reaches_today_after_every_value_has_expired()
    {
        // Found by this class: the spine used to END at "last balance + cutoff" (05-30 here),
        // so every later day had no row - the series stopped instead of saying UNVERIFIED.
        // Migration SpineRunsToToday. A is still open, so today it is in the window, unverified.
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        var day = await DayAsync(today);

        Assert.Null(day.NetWorth);
        Assert.Equal(1, day.AccountsInWindow);
        Assert.Equal(1, day.AccountsUnverified);
    }

    [Fact]
    public async Task The_cutoff_is_the_stored_setting_not_a_literal()
    {
        // Amendment 3 condition 2: move the setting and the view moves with it.
        await pg.ExecuteAsync("UPDATE app_setting SET balance_staleness_days = 30");
        try
        {
            var day = await DayAsync("2026-04-15"); // A from 03-01 (45 days), B from 02-15 (59): both past 30
            Assert.Null(day.NetWorth);
            Assert.Equal(2, day.AccountsUnverified);
        }
        finally
        {
            await pg.ExecuteAsync("UPDATE app_setting SET balance_staleness_days = 90");
        }
    }
}
