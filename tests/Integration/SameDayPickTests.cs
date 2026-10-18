using Microsoft.EntityFrameworkCore;
using Portfolio.Domain;
using Portfolio.Infrastructure;
using Portfolio.Ingestion;
using static Portfolio.Integration.ImportSupport;

namespace Portfolio.Integration;

/// <summary>
/// Spike G's three cases (spikes/spike-g-same-day-pick/RESULTS.md), now against migration 4's view
/// and through the real write paths - BalanceRecorder for a manual entry, ImportPipeline for a
/// file. The unfixed view decided by WRITE ORDER, so each case runs in both orders.
/// </summary>
/// <remarks>
/// The view sums every account open on a day, and the tests share one database. Each case lives
/// in its own year, in accounts closed at its end, so no case sees another's accounts.
/// </remarks>
public class SameDayPickTests(PostgresFixture pg) : IClassFixture<PostgresFixture>
{
    static readonly DateTimeOffset T0 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    async Task<decimal?> NetWorthAsync(DateOnly day)
    {
        await using var db = pg.CreateContext();
        return (await NetWorthDay.Query(db, day, day).SingleAsync(Ct)).NetWorth;
    }

    async Task<Guid> AccountInAsync(int year) =>
        await NewAccountAsync(pg, new DateOnly(year, 1, 1), closed: new DateOnly(year, 12, 31));

    async Task ManualAsync(Guid account, DateOnly day, decimal amount, DateTimeOffset at)
    {
        await using var db = pg.CreateContext();
        await BalanceRecorder.RecordAsync(db, new FixedClock(at), account, day, amount, "manual", SourceStrength.Manual, Ct);
    }

    [Theory]
    [InlineData(2030, true)]
    [InlineData(2031, false)]
    public async Task G1_on_the_same_day_the_stronger_source_counts_whichever_was_written_first(int year, bool manualFirst)
    {
        var account = await AccountInAsync(year);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);
        var day = new DateOnly(year, 3, 1);
        var import = () => ImportAsync(pg, new CsvBalanceSource(), Csv((id, $"{day:yyyy-MM-dd}", "200")), new FixedClock(T0));

        if (manualFirst) { await ManualAsync(account, day, 100, T0); await import(); }
        else { await import(); await ManualAsync(account, day, 100, T0); }

        Assert.Equal(200m, await NetWorthAsync(day)); // export outranks manual
    }

    [Fact]
    public async Task G2_a_newer_weaker_figure_outranks_an_older_stronger_one()
    {
        var account = await AccountInAsync(2032);
        var id = await BindAsync(pg, account, CsvBalanceSource.System);
        await ImportAsync(pg, new CsvBalanceSource(), Csv((id, "2032-03-01", "200")), new FixedClock(T0));
        await ManualAsync(account, new DateOnly(2032, 3, 2), 150, T0);

        Assert.Equal(200m, await NetWorthAsync(new DateOnly(2032, 3, 1)));
        Assert.Equal(150m, await NetWorthAsync(new DateOnly(2032, 3, 2)));
        Assert.Equal(150m, await NetWorthAsync(new DateOnly(2032, 3, 5))); // carried, still the newer date
    }

    [Theory]
    [InlineData(2033, true)]
    [InlineData(2034, false)]
    public async Task G3_two_sources_of_equal_strength_the_later_observation_counts(int year, bool csvFirst)
    {
        var account = await AccountInAsync(year);
        var csvId = await BindAsync(pg, account, CsvBalanceSource.System);
        var ofxId = await BindAsync(pg, account, OfxBalanceSource.System);
        var day = new DateOnly(year, 3, 1);
        // CSV observed at T0, OFX an hour later - whichever is WRITTEN first.
        var csv = () => ImportAsync(pg, new CsvBalanceSource(), Csv((csvId, $"{day:yyyy-MM-dd}", "300")), new FixedClock(T0));
        var ofx = () => ImportAsync(pg, new OfxBalanceSource(), Ofx(ofxId, "400", $"{day:yyyy-MM-dd}"), new FixedClock(T0.AddHours(1)));

        if (csvFirst) { await csv(); await ofx(); }
        else { await ofx(); await csv(); }

        Assert.Equal(400m, await NetWorthAsync(day));
    }
}
