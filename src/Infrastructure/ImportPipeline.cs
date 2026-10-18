using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portfolio.Domain;
using Portfolio.Ingestion;

namespace Portfolio.Infrastructure;

/// <summary>
/// plan.md "Ingestion": a connector parses and maps; THIS decides what an import means to the
/// data - and is the only place that does (slice-02.md step 2). In order: refuse what is too
/// large, parse everything, resolve identity, validate against each account, then write the
/// whole file in one transaction. Every run is recorded, succeeded or failed.
/// </summary>
/// <remarks>
/// <para><b>All or nothing.</b> Any refused row fails the file and writes no balance. A partly
/// imported balance file would leave net worth describing a moment that never existed.</para>
/// <para><b>Idempotent by content.</b> The same file twice is a second run with every row
/// <c>unchanged</c> and no new balance row (FR-4.2). A file whose hash matches an earlier run is
/// still processed: the outcome comes from the data, not from remembering the bytes.</para>
/// <para><b>Identity is never guessed.</b> A row's source id must already be bound to an account
/// in <c>account_source</c> under this source system; the importer never creates an account.</para>
/// </remarks>
public sealed class ImportPipeline(PortfolioDbContext db, TimeProvider clock)
{
    /// <summary>Spike F: also enforced on the endpoint, before the body is read. Held here too.</summary>
    public const int MaxFileBytes = 1 << 20;
    public const int MaxFileNameLength = 255;

    public sealed record Outcome(IngestRun Run, IReadOnlyList<ParseError> Errors);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Outcome> RunAsync(IBalanceSource source, string fileName, ReadOnlyMemory<byte> content, CancellationToken ct = default)
    {
        var started = clock.GetUtcNow();
        var sha = Convert.ToHexStringLower(SHA256.HashData(content.Span));
        var name = fileName.Length <= MaxFileNameLength ? fileName : fileName[..MaxFileNameLength];

        if (content.Length > MaxFileBytes)
            return await FailAsync(source, name, sha, started, 0, [new("file", $"is {content.Length} bytes; at most {MaxFileBytes} are accepted")], ct);
        if (fileName.Length > MaxFileNameLength)
            return await FailAsync(source, name, sha, started, 0, [new("file", $"name is longer than {MaxFileNameLength} characters")], ct);

        // 2. Parse everything first.
        var parsed = source.Parse(content.Span);
        if (!parsed.Succeeded)
            return await FailAsync(source, name, sha, started, 0, parsed.Errors, ct);
        var rows = parsed.Balances;

        // 3. Resolve identity, and 4. validate against each account's window. The window guard
        // trigger is the backstop, not the check: a refusal here names the row.
        var ids = rows.Select(r => r.SourceId).Distinct().ToList();
        var bound = await (
                from s in db.AccountSources
                join a in db.Accounts on s.AccountId equals a.Id
                where s.SourceSystem == source.SourceSystem && ids.Contains(s.SourceId) && a.DeletedAt == null
                select new { s.SourceId, a.Id, a.OpenedOn, a.ClosedOn })
            .ToDictionaryAsync(x => x.SourceId, ct);

        var errors = new List<ParseError>();
        var requests = new List<BalanceWrites.Request>(rows.Count);
        foreach (var r in rows)
        {
            if (!bound.TryGetValue(r.SourceId, out var account))
            {
                errors.Add(new(r.Location, $"account {Quote(r.SourceId)} is not bound to any account under {source.SourceSystem}; bind it, then import again"));
                continue;
            }
            if (r.AsOfDate < account.OpenedOn)
                errors.Add(new(r.Location, $"{r.AsOfDate:yyyy-MM-dd} precedes the account's opened_on {account.OpenedOn:yyyy-MM-dd}"));
            else if (account.ClosedOn is { } closed && r.AsOfDate > closed)
                errors.Add(new(r.Location, $"{r.AsOfDate:yyyy-MM-dd} follows the account's closed_on {closed:yyyy-MM-dd} (Constitution III)"));
            else
                requests.Add(new(account.Id, r.AsOfDate, r.Amount, source.SourceSystem, source.Strength, r.SourceAsOf));
        }
        if (errors.Count > 0)
            return await FailAsync(source, name, sha, started, rows.Count, errors, ct);

        // 5. One transaction for the file, inside the execution strategy: a retry replays the
        // whole import, never half of it.
        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            var run = await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var plan = await BalanceWrites.PlanAsync(db, requests, ct);
                var finished = clock.GetUtcNow();

                // The run row first, with its final counts: the balances reference it, and it is
                // never updated afterwards (the trigger would refuse).
                var run = new IngestRun
                {
                    Id = Guid.CreateVersion7(),
                    SourceSystem = source.SourceSystem,
                    FileName = name,
                    FileSha256 = sha,
                    StartedAt = started,
                    FinishedAt = finished,
                    Status = IngestRunStatus.Succeeded,
                    RowsRead = rows.Count,
                    Created = plan.Count(p => p.Outcome == BalanceWrites.Outcome.Created),
                    Restated = plan.Count(p => p.Outcome == BalanceWrites.Outcome.Restated),
                    Unchanged = plan.Count(p => p.Outcome == BalanceWrites.Outcome.Unchanged),
                };
                db.IngestRuns.Add(run);
                await db.SaveChangesAsync(ct);
                await BalanceWrites.ApplyAsync(db, plan, run.Id, finished, ct);
                await tx.CommitAsync(ct);
                return run;
            });
            return new Outcome(run, []);
        }
        catch (Exception e) when (e.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.CheckViolation } pg)
        {
            // Something wrote the same (account, date, source) between the plan and the write, or
            // a guard refused a row. Nothing was committed; the run says why.
            db.ChangeTracker.Clear();
            return await FailAsync(source, name, sha, started, rows.Count,
                [new("file", $"the database refused the write ({pg.SqlState}): {pg.MessageText}; nothing was imported - try again")], ct);
        }
    }

    async Task<Outcome> FailAsync(IBalanceSource source, string name, string sha, DateTimeOffset started,
        int rowsRead, IReadOnlyList<ParseError> errors, CancellationToken ct)
    {
        var run = new IngestRun
        {
            Id = Guid.CreateVersion7(),
            SourceSystem = source.SourceSystem,
            FileName = name,
            FileSha256 = sha,
            StartedAt = started,
            FinishedAt = clock.GetUtcNow(),
            Status = IngestRunStatus.Failed,
            RowsRead = rowsRead,
            ErrorDetail = JsonSerializer.Serialize(Bounded(errors), Json),
        };
        db.IngestRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return new Outcome(run, Bounded(errors));
    }

    /// <summary>The parser's bound, applied to the pipeline's own errors too: every row of a
    /// 10,000-row file can be unbound, and the run row is not the place for 10,000 of them.</summary>
    static IReadOnlyList<ParseError> Bounded(IReadOnlyList<ParseError> errors) =>
        errors.Count <= IngestionLimits.MaxErrorsReported ? errors
            : [.. errors.Take(IngestionLimits.MaxErrorsReported),
               new("file", $"and {errors.Count - IngestionLimits.MaxErrorsReported} more errors, not listed")];

    /// <summary>A source id has already passed the parser's checks: bounded, no control characters.</summary>
    static string Quote(string value) => "'" + value + "'";
}
