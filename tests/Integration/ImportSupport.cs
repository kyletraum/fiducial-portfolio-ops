using System.Text;
using Portfolio.Domain;
using Portfolio.Infrastructure;
using Portfolio.Ingestion;

namespace Portfolio.Integration;

/// <summary>A clock that says what it is told, so observed_at order is the test's, not the machine's.</summary>
sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Accounts, bindings and files for the slice-02 integration tests. Invented values only.</summary>
static class ImportSupport
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> NewAccountAsync(PostgresFixture pg, DateOnly opened, DateOnly? closed = null)
    {
        await using var db = pg.CreateContext();
        var institution = new Institution { Id = Guid.CreateVersion7(), Name = $"Import Test Bank {Guid.NewGuid():N}" };
        var account = new Account
        {
            Id = Guid.CreateVersion7(), InstitutionId = institution.Id, DisplayName = "Imported", OpenedOn = opened, ClosedOn = closed,
        };
        db.AddRange(institution, account);
        await db.SaveChangesAsync(Ct);
        return account.Id;
    }

    /// <summary>Binds a source id to an account, as step 3's endpoint will. Returns the id.</summary>
    public static async Task<string> BindAsync(PostgresFixture pg, Guid accountId, string sourceSystem, string? sourceId = null)
    {
        sourceId ??= $"EXAMPLE-{Guid.NewGuid():N}"[..24];
        await using var db = pg.CreateContext();
        db.AccountSources.Add(new AccountSource { Id = Guid.CreateVersion7(), AccountId = accountId, SourceSystem = sourceSystem, SourceId = sourceId });
        await db.SaveChangesAsync(Ct);
        return sourceId;
    }

    public static byte[] Csv(params (string Account, string Date, string Balance)[] rows) =>
        Encoding.UTF8.GetBytes("account,as_of_date,balance\n" + string.Concat(rows.Select(r => $"{r.Account},{r.Date},{r.Balance}\n")));

    /// <summary>
    /// A minimal OFX 2.2 bank statement. The stamp is built from an ISO date so this source carries
    /// no eight-digit run (the hygiene scanner refuses those in a data file, correctly).
    /// </summary>
    public static byte[] Ofx(string accountId, string balance, string isoDate, string time = "", string bracket = "") =>
        Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<?OFX OFXHEADER=\"200\" VERSION=\"220\"?>\n"
            + "<OFX><BANKMSGSRSV1><STMTTRNRS><TRNUID>1</TRNUID><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>"
            + "<STMTRS><CURDEF>USD</CURDEF><BANKACCTFROM><BANKID>X</BANKID><ACCTID>" + accountId + "</ACCTID><ACCTTYPE>CHECKING</ACCTTYPE></BANKACCTFROM>"
            + "<LEDGERBAL><BALAMT>" + balance + "</BALAMT><DTASOF>" + isoDate.Replace("-", "") + time + bracket + "</DTASOF></LEDGERBAL>"
            + "</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>\n");

    public static async Task<ImportPipeline.Outcome> ImportAsync(PostgresFixture pg, IBalanceSource source, byte[] file,
        TimeProvider? clock = null, string fileName = "balances.csv")
    {
        await using var db = pg.CreateContext();
        return await new ImportPipeline(db, clock ?? TimeProvider.System).RunAsync(source, fileName, file, Ct);
    }
}
