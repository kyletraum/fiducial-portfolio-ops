using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Portfolio.Infrastructure;

/// <summary>slice-04.md "Saved reports": the three types a parameter may be declared as.</summary>
public enum ReportParameterType
{
    Date,
    Text,
    Numeric,
}

/// <summary>
/// Typed, positional parameters (FR-6.4, SRC-4). Each value is checked against its declared type
/// here, before the database (DoD 4: <c>400</c>), and bound as an unnamed <see cref="NpgsqlParameter"/>
/// with its <see cref="NpgsqlDbType"/>, so the statement's <c>$n</c> is PostgreSQL's own placeholder.
/// Nothing is ever written into the SQL text.
/// </summary>
public static class ReportParameters
{
    /// <summary>The declared type as PostgreSQL sees it, with no value: what a save describes the statement with.</summary>
    public static NpgsqlParameter Declare(ReportParameterType type) =>
        new() { NpgsqlDbType = DbType(type), Value = DBNull.Value };

    /// <summary>A bound parameter, or the reason the value is not of its type. Null binds SQL NULL.</summary>
    public static NpgsqlParameter? Bind(ReportParameterType type, string? value, out string? problem)
    {
        problem = null;
        object? bound = value;
        if (value is not null)
        {
            switch (type)
            {
                case ReportParameterType.Date:
                    if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        problem = "A date, as yyyy-MM-dd.";
                    bound = date;
                    break;
                case ReportParameterType.Numeric:
                    if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
                        problem = "A number: digits, an optional sign and decimal point, nothing else.";
                    bound = number;
                    break;
            }
        }
        return problem is null ? new NpgsqlParameter { NpgsqlDbType = DbType(type), Value = bound ?? DBNull.Value } : null;
    }

    static NpgsqlDbType DbType(ReportParameterType type) => type switch
    {
        ReportParameterType.Date => NpgsqlDbType.Date,
        ReportParameterType.Numeric => NpgsqlDbType.Numeric,
        _ => NpgsqlDbType.Text,
    };
}
