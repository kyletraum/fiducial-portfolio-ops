using System.Globalization;
using System.Text.RegularExpressions;
using Portfolio.Domain;

namespace Portfolio.Api;

/// <summary>Money on the wire: a plain decimal string, invariant culture, scale 4.</summary>
static partial class MoneyText
{
    // No exponent, no grouping, no leading '+'. At most 15 integer digits: numeric(19,4).
    [GeneratedRegex(@"^-?\d{1,15}(\.\d+)?$")]
    private static partial Regex Shape();

    public static string Format(decimal amount) =>
        amount.ToString("0.0000", CultureInfo.InvariantCulture);

    /// <summary>
    /// R2-M8: more than four decimal places is refused, not rounded. Rounding here would store
    /// a figure the user did not type.
    /// </summary>
    public static bool TryParse(string? text, out decimal amount, out string? error)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text) || !Shape().IsMatch(text))
        {
            error = "Must be a decimal string such as \"1234.5678\": digits, an optional '-', at most 15 before the point.";
            return false;
        }
        var dot = text.IndexOf('.');
        if (dot >= 0 && text.Length - dot - 1 > Money.Scale)
        {
            error = $"At most {Money.Scale} decimal places; this has {text.Length - dot - 1}.";
            return false;
        }
        amount = decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        error = null;
        return true;
    }
}
