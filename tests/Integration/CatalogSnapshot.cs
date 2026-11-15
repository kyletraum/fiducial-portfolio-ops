using Npgsql;

namespace Portfolio.Integration;

/// <summary>
/// slice-04.md step 1a: everything a login can do, read from the catalog as EFFECTIVE privileges
/// (has_*_privilege), so a grant through PUBLIC or a role membership counts as a grant (R2-DBS-3).
/// One line per fact, sorted, compared with a committed file (TST-4, R2-TST-4). Databases are named
/// by role, so one expected file serves Integration and the published stack (slice-04.md step 6).
/// </summary>
static class CatalogSnapshot
{
    /// <summary>
    /// The query, in <c>catalog-snapshot.sql</c> beside this file: the same file step 6 runs by hand
    /// through <c>psql</c> in the published stack's container, so the two cannot drift (R2-TST-11).
    /// The login is the custom setting <c>snapshot.login</c>, which both can set.
    /// </summary>
    public static async Task<List<string>> TakeAsync(NpgsqlConnection superuser, string login)
    {
        await using (var set = new NpgsqlCommand("SELECT set_config('snapshot.login', $1, false)", superuser) { Parameters = { new() { Value = login } } })
            await set.ExecuteNonQueryAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "catalog-snapshot.sql"));
        var lines = new List<string>();
        await using (var command = new NpgsqlCommand(sql, superuser))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                lines.Add(reader.GetString(0));
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    /// <summary>The committed expected snapshot, one fact per line, blank lines and # comments ignored.</summary>
    public static List<string> Expected(string login) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ExpectedPrivileges", login + ".txt"))
            .Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).Order(StringComparer.Ordinal).ToList();

    /// <summary>A diff a failing assertion can print: what is there and should not be, and the reverse.</summary>
    public static string Diff(IReadOnlyCollection<string> expected, IReadOnlyCollection<string> actual) =>
        string.Join(Environment.NewLine,
            actual.Except(expected).Select(l => "+ " + l).Concat(expected.Except(actual).Select(l => "- " + l)));
}
