using System.Data;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Portfolio.Infrastructure;

/// <summary>What cut a run short (part 5). Null on a run that returned everything.</summary>
public enum ReportCutOff
{
    /// <summary>The row cap: one more row was there, and was dropped.</summary>
    Rows,
    /// <summary>The byte cap: the row whose cell crossed it was dropped.</summary>
    Bytes,
    /// <summary>The deadline, the client's or the server's own <c>statement_timeout</c>.</summary>
    Deadline,
}

public sealed record ReportColumn(string Name, string Type);

/// <summary>How a run ended. The Api maps each to a response; none carries the SQL text.</summary>
public abstract record ReportOutcome
{
    private ReportOutcome() { }

    /// <summary>Columns and rows, possibly cut off. A deadline before the first row leaves both empty.</summary>
    public sealed record Completed(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<JsonNode?[]> Rows, ReportCutOff? CutOff) : ReportOutcome;

    /// <summary>PostgreSQL refused the statement: its SQLSTATE and MessageText only, never Detail or Where (TST-14).</summary>
    public sealed record Refused(string SqlState, string Message) : ReportOutcome;

    /// <summary>A column of a type the runner has no reader for (an enum, <c>regclass</c>, …).</summary>
    public sealed record UnreadableColumn(string Column, string Type) : ReportOutcome;

    /// <summary>A save whose statement uses a different number of <c>$n</c> parameters than it declares (SRC-4).</summary>
    public sealed record ParameterMismatch(int Used, int Declared) : ReportOutcome;

    /// <summary>Another run holds the one slot (S4-6).</summary>
    public sealed record Busy : ReportOutcome;

    /// <summary>The connection failed or was lost (a wrong password, a closed port, a crash-restart).</summary>
    public sealed record Unavailable : ReportOutcome;
}

/// <summary>One documented view of <c>reporting</c>, read live from the catalog (FR-6.6).</summary>
public sealed record ReportView(string Name, string? Description, IReadOnlyList<ReportViewColumn> Columns);

public sealed record ReportViewColumn(string Name, string Type, string? Description);

/// <summary>
/// slice-04.md "Running a query: the six parts of the boundary" (C4-1, C4-2). Every run:
/// <list type="number">
/// <item>a fresh, unpooled connection on the reporting login;</item>
/// <item><c>BEGIN TRANSACTION READ ONLY</c>, <c>SET LOCAL statement_timeout</c>, and a snapshot taken;</item>
/// <item>the user's text as a one-command <see cref="NpgsqlBatch"/>, so PostgreSQL refuses a second statement (<c>42601</c>);</item>
/// <item>read under one wall-clock deadline with <see cref="CommandBehavior.SequentialAccess"/>, rows and bytes counted
/// cell by cell, and <see cref="NpgsqlBatch.Cancel"/> before the reader is disposed;</item>
/// <item>the columns, the rows, and what cut it off;</item>
/// <item><c>ROLLBACK</c>, the user's statement the last before it, and the connection closed.</item>
/// </list>
/// One run at a time (S4-6): the slot is released in a <c>finally</c>, after the connection is closed.
/// </summary>
/// <remarks>
/// The app never reads the SQL (research.md D5): it is not parsed, logged or traced. The data source
/// emits no spans, since Npgsql's would carry the text, and has no logger.
/// </remarks>
public sealed class ReportRunner(IServiceProvider services, IOptions<ReportingLimits> options, ILogger<ReportRunner> log)
{
    public const string DataSourceKey = "reporting";

    /// <summary>The ROLLBACK and the schema read are ours, and short; this bounds them if the server is not answering.</summary>
    static readonly TimeSpan OwnStatementTimeout = TimeSpan.FromSeconds(5);

    readonly SemaphoreSlim slot = new(1, 1);

    /// <summary>
    /// The keyed data source, resolved on first use, not by the constructor. A route's services are
    /// bound before its endpoint filters run, so a constructor that threw on a missing connection
    /// string would answer 500 ahead of the CSRF filter's 403 (found by <c>AllowListTests</c>).
    /// Missing, it is a 503 and a logged error.
    /// </summary>
    NpgsqlDataSource? DataSource()
    {
        try
        {
            return services.GetRequiredKeyedService<NpgsqlDataSource>(DataSourceKey);
        }
        catch (InvalidOperationException e)
        {
            log.LogError("Reports cannot run: {Reason}", e.Message);
            return null;
        }
    }

    /// <summary>
    /// Keyed, so it can never become EF's connection (SRC-8). Unpooled whatever the connection
    /// string says: part 1, so no <c>set_config</c>, <c>PREPARE</c> or advisory lock outlives a run.
    /// </summary>
    public static NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.ConnectionStringBuilder.Pooling = false;
        builder.ConfigureTracing(t => t.ConfigureBatchFilter(_ => false).ConfigureCommandFilter(_ => false));
        return builder.Build();
    }

    /// <summary>
    /// Part 2, before the user's text. The <c>SELECT 1</c> takes the transaction's snapshot, after
    /// which <c>SET TRANSACTION READ WRITE</c> is refused (<c>25001</c>): without it, that statement
    /// alone succeeded (step 2's correction to DBS-1).
    /// </summary>
    public static string Begin(ReportingLimits limits) =>
        $"BEGIN TRANSACTION READ ONLY; SET LOCAL statement_timeout = {(long)limits.StatementTimeout.TotalMilliseconds}; SELECT 1";

    public Task<ReportOutcome> RunAsync(string sql, CancellationToken requestAborted) => RunAsync(sql, [], requestAborted);

    /// <summary>A run with positional parameters, already checked against their types (<see cref="ReportParameters"/>).</summary>
    public async Task<ReportOutcome> RunAsync(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken requestAborted)
    {
        if (!slot.Wait(0))
            return new ReportOutcome.Busy();
        try
        {
            return await RunInSlotAsync(sql, parameters, requestAborted);
        }
        finally
        {
            slot.Release();
        }
    }

    async Task<ReportOutcome> RunInSlotAsync(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken requestAborted)
    {
        var limits = options.Value;
        var clock = Stopwatch.StartNew();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        deadline.CancelAfter(limits.Deadline);
        var token = deadline.Token;
        bool DeadlinePassed() => deadline.IsCancellationRequested && !requestAborted.IsCancellationRequested;

        var columns = new List<ReportColumn>();
        var rows = new List<JsonNode?[]>();
        ReportCutOff? cutOff = null;
        var pid = 0;
        var outcome = "failed";
        string? endedBy = null; // which deadline: the log says, so a test can tell the client's cancel from the server's backstop

        if (DataSource() is not { } reporting)
            return new ReportOutcome.Unavailable();
        await using var connection = reporting.CreateConnection();
        try
        {
            try
            {
                await connection.OpenAsync(token);
            }
            catch (Exception e) when (e is NpgsqlException or TimeoutException || e is OperationCanceledException && DeadlinePassed())
            {
                // A wrong password (28P01) or a closed port: nothing of the user's has run. The
                // exception's type and SQLSTATE only; Npgsql's message names host, port and user.
                log.LogWarning("Report run could not connect: {Error} {SqlState}", e.GetType().Name, (e as PostgresException)?.SqlState);
                return new ReportOutcome.Unavailable();
            }
            pid = connection.ProcessID; // read from the connection, never by a statement after the user's (R2-DBS-5)
            log.LogInformation("Report run started on backend {Pid}", pid);

            await using (var begin = new NpgsqlCommand(Begin(limits), connection))
                await begin.ExecuteNonQueryAsync(token);

            await using var batch = new NpgsqlBatch(connection) { BatchCommands = { Command(sql, parameters) } };
            NpgsqlDataReader? reader = null;
            try
            {
                reader = await batch.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var column = new ReportColumn(reader.GetName(i), reader.GetDataTypeName(i));
                    if (!ReportCells.CanRead(reader, i))
                    {
                        batch.Cancel();
                        outcome = "unreadable column";
                        return new ReportOutcome.UnreadableColumn(column.Name, column.Type);
                    }
                    columns.Add(column);
                }

                long bytes = 0;
                while (cutOff is null && await reader.ReadAsync(token))
                {
                    if (rows.Count == limits.MaxRows)
                    {
                        cutOff = ReportCutOff.Rows;
                        break;
                    }
                    var row = new JsonNode?[reader.FieldCount];
                    var used = 0L;
                    for (var i = 0; i < row.Length; i++)
                    {
                        var cell = await ReportCells.ReadAsync(reader, i, limits.MaxBytes - bytes - used, token);
                        if (cell is null)
                        {
                            cutOff = ReportCutOff.Bytes;
                            break;
                        }
                        (row[i], var size) = cell.Value;
                        used += size;
                    }
                    if (cutOff is null)
                    {
                        bytes += used;
                        rows.Add(row);
                    }
                }
                // Part 4: at a cap, cancel BEFORE the reader is disposed. Disposing alone reads the
                // whole remaining result (SRC-1, TST-6).
                if (cutOff is not null)
                    batch.Cancel();
            }
            catch (OperationCanceledException) when (DeadlinePassed())
            {
                cutOff = ReportCutOff.Deadline; // Npgsql cancelled on the server as the token fired
                endedBy = "the client's deadline";
            }
            catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.QueryCanceled)
            {
                cutOff = ReportCutOff.Deadline; // the server's statement_timeout, or a statement that cancelled itself
                endedBy = DeadlinePassed() ? "the client's deadline" : "the server's statement_timeout";
            }
            catch (InvalidCastException e)
            {
                // A value the column's reader could not convert (an interval array with months, say).
                batch.Cancel();
                log.LogInformation("Report run on backend {Pid}: a value could not be read ({Error})", pid, e.GetType().Name);
                outcome = "unreadable column";
                return new ReportOutcome.UnreadableColumn("?", "a value the API cannot convert");
            }
            catch (PostgresException e) when (!IsConnectionFailure(e.SqlState))
            {
                outcome = "refused " + e.SqlState;
                return new ReportOutcome.Refused(e.SqlState, e.MessageText);
            }
            finally
            {
                if (reader is not null)
                {
                    try
                    {
                        await reader.DisposeAsync();
                    }
                    catch (Exception e) when (e is NpgsqlException or OperationCanceledException)
                    {
                        // The cancel's own 57014, arriving as the reader drains; or a connection
                        // already gone, which the ROLLBACK below meets too.
                    }
                }
            }

            outcome = cutOff is { } c ? "cut off by " + (endedBy ?? c.ToString()) : "completed";
            return new ReportOutcome.Completed(columns, rows, cutOff);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException || e is OperationCanceledException && DeadlinePassed())
        {
            // The connection failed mid-run: a crash-restart (S4-1), or 08xxx/57Pxx from the server.
            log.LogWarning("Report run on backend {Pid} lost its connection: {Error} {SqlState}", pid, e.GetType().Name, (e as PostgresException)?.SqlState);
            return new ReportOutcome.Unavailable();
        }
        finally
        {
            // Part 6: ROLLBACK, the user's statement the last before it. A broken connection has
            // nothing to roll back; closing it ends the backend either way, since nothing is pooled.
            if (connection.State == ConnectionState.Open)
            {
                try
                {
                    await using var rollback = new NpgsqlCommand("ROLLBACK", connection) { CommandTimeout = (int)OwnStatementTimeout.TotalSeconds };
                    await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch (Exception e) when (e is NpgsqlException or TimeoutException)
                {
                }
            }
            await connection.CloseAsync();
            if (pid != 0)
                log.LogInformation("Report run on backend {Pid} ended: {Outcome}, {Rows} rows, {Elapsed} ms", pid, outcome, rows.Count, clock.ElapsedMilliseconds);
        }
    }

    /// <summary>The user's text and its positional parameters, as the one command of a batch: no rewriting (SRC-4).</summary>
    static NpgsqlBatchCommand Command(string sql, IReadOnlyList<NpgsqlParameter> parameters)
    {
        var command = new NpgsqlBatchCommand(sql);
        foreach (var parameter in parameters)
            command.Parameters.Add(parameter.Clone());
        return command;
    }

    /// <summary>
    /// A save's check (SRC-4), without running the statement and without parsing it here: PostgreSQL
    /// parses and describes it (<see cref="CommandBehavior.SchemaOnly"/>, Parse and Describe, no
    /// Execute) on the reporting login, inside the same read-only transaction, with the declared
    /// parameter types. That refuses a second statement, a syntax error, an unknown name, a
    /// <c>$n</c> beyond the declared parameters, and a column the reader cannot return. Privileges are
    /// checked when a statement executes, not when it is described, so a report over a table the login
    /// cannot read is saved, and refused when run. Takes no slot.
    /// </summary>
    public async Task<ReportOutcome> DescribeAsync(string sql, IReadOnlyList<NpgsqlParameter> declared, CancellationToken ct)
    {
        if (DataSource() is not { } reporting)
            return new ReportOutcome.Unavailable();
        var limits = options.Value;
        try
        {
            await using var connection = await reporting.OpenConnectionAsync(ct);
            await using (var begin = new NpgsqlCommand(Begin(limits), connection))
                await begin.ExecuteNonQueryAsync(ct);
            await using var batch = new NpgsqlBatch(connection) { BatchCommands = { Command(sql, declared) } };
            var columns = new List<ReportColumn>();
            try
            {
                await using var reader = await batch.ExecuteReaderAsync(CommandBehavior.SchemaOnly, ct);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var column = new ReportColumn(reader.GetName(i), reader.GetDataTypeName(i));
                    if (!ReportCells.CanRead(reader, i))
                        return new ReportOutcome.UnreadableColumn(column.Name, column.Type);
                    columns.Add(column);
                }
            }
            catch (PostgresException e) when (!IsConnectionFailure(e.SqlState))
            {
                return new ReportOutcome.Refused(e.SqlState, e.MessageText);
            }

            // Described with the declared types, PostgreSQL infers a type for a $n beyond them rather
            // than refusing it, and a run would then fail to bind (08P01). So the statement is also
            // prepared on the server, which counts its parameters itself: a $n beyond the declared
            // shows as an inferred extra type in pg_prepared_statements, this session's own. Npgsql's
            // DeriveParameters cannot do this: it refuses positional $n (EXECUTED). A declared
            // parameter the statement never uses cannot be seen this way, and is harmless: its value is
            // bound and ignored.
            await using (var prepared = new NpgsqlBatch(connection) { BatchCommands = { Command(sql, declared) } })
                await prepared.PrepareAsync(ct);
            await using (var count = new NpgsqlCommand("SELECT coalesce(max(cardinality(parameter_types)), 0) FROM pg_prepared_statements", connection))
            {
                var used = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
                if (used > declared.Count)
                    return new ReportOutcome.ParameterMismatch(used, declared.Count);
            }
            return new ReportOutcome.Completed(columns, [], null);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            log.LogWarning("Report could not be described: {Error} {SqlState}", e.GetType().Name, (e as PostgresException)?.SqlState);
            return new ReportOutcome.Unavailable();
        }
    }

    /// <summary>08xxx (connection) and 57Pxx (the server shutting down or crash-restarting): not the statement's fault.</summary>
    static bool IsConnectionFailure(string sqlState) =>
        sqlState != PostgresErrorCodes.ProtocolViolation // 08P01: a bind that does not fit the statement, not a lost connection
        && (sqlState.StartsWith("08", StringComparison.Ordinal) || sqlState.StartsWith("57P", StringComparison.Ordinal));

    /// <summary>
    /// The schema documentation (FR-6.6): the views of <c>reporting</c> the login can read, with every
    /// column's type and the <c>COMMENT</c> text, live from the catalog on the reporting login. Not
    /// a run: it takes no slot. Null when the database cannot be reached.
    /// </summary>
    public async Task<IReadOnlyList<ReportView>?> SchemaAsync(CancellationToken ct)
    {
        if (DataSource() is not { } reporting)
            return null;
        try
        {
            await using var connection = await reporting.OpenConnectionAsync(ct);
            await using var command = new NpgsqlCommand("""
                SELECT c.relname, obj_description(c.oid, 'pg_class'),
                       a.attname, format_type(a.atttypid, a.atttypmod), col_description(c.oid, a.attnum)
                  FROM pg_class c
                  JOIN pg_namespace n ON n.oid = c.relnamespace AND n.nspname = 'reporting'
                  JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped
                 WHERE c.relkind = 'v' AND has_table_privilege(c.oid, 'SELECT')
                 ORDER BY c.relname, a.attnum
                """, connection) { CommandTimeout = (int)OwnStatementTimeout.TotalSeconds };
            var views = new List<(string Name, string? Description, List<ReportViewColumn> Columns)>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var name = reader.GetString(0);
                if (views.Count == 0 || views[^1].Name != name)
                    views.Add((name, reader.IsDBNull(1) ? null : reader.GetString(1), []));
                views[^1].Columns.Add(new(reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
            return views.Select(v => new ReportView(v.Name, v.Description, v.Columns)).ToList();
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            log.LogWarning("Report schema could not be read: {Error} {SqlState}", e.GetType().Name, (e as PostgresException)?.SqlState);
            return null;
        }
    }
}
