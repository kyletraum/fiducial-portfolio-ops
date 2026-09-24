using Microsoft.EntityFrameworkCore;

namespace Portfolio.Infrastructure;

/// <summary>One row of <c>v_net_worth_daily</c>.</summary>
/// <remarks>
/// <see cref="NetWorth"/> is NULL when no in-window account is verified that day: sum() over
/// all-NULLs. Constitution II - that day is UNVERIFIED, and nothing downstream may read it
/// as zero.
/// </remarks>
public sealed record NetWorthDay(
    DateOnly AsOfDate,
    decimal? NetWorth,
    long AccountsInWindow,
    long AccountsVerified,
    long AccountsUnverified,
    long AccountsCarried,
    int? MaxStalenessDays)
{
    /// <summary>
    /// Read through a raw query rather than a view-mapped entity: the view is created by SQL in
    /// migration Initial, and mapping it would change the model snapshot for no schema change.
    /// </summary>
    /// <remarks>
    /// No column aliases: the snake_case naming convention applies to SqlQuery result types too,
    /// and EF composes over this SQL reading <c>accounts_carried</c>, not <c>AccountsCarried</c>.
    /// The view's own column names are already what it expects.
    /// </remarks>
    public static IQueryable<NetWorthDay> Query(PortfolioDbContext db, DateOnly from, DateOnly to) =>
        db.Database.SqlQuery<NetWorthDay>($"""
            SELECT as_of_date, net_worth, accounts_in_window, accounts_verified,
                   accounts_unverified, accounts_carried, max_staleness_days
              FROM v_net_worth_daily
             WHERE as_of_date BETWEEN {from} AND {to}
            """);
}
