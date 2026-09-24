namespace Portfolio.Domain;

/// <summary>
/// An amount in one currency, held at the database's scale: numeric(19,4).
/// </summary>
/// <remarks>
/// S-24. PostgreSQL rounds numeric half AWAY FROM ZERO; .NET's Math.Round defaults to
/// banker's rounding (half to even), and there is no global switch. So every rounding
/// in this type names its mode, and nothing else in the codebase rounds money.
/// </remarks>
public readonly record struct Money
{
    public const int Scale = 4;
    public const MidpointRounding Rounding = MidpointRounding.AwayFromZero;

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
            throw new ArgumentException($"'{currency}' is not an ISO 4217 code.", nameof(currency));

        Amount = Math.Round(amount, Scale, Rounding);
        Currency = currency;
    }

    public static Money operator +(Money left, Money right) =>
        new(left.Amount + right.Amount, SameCurrency(left, right));

    public static Money operator -(Money left, Money right) =>
        new(left.Amount - right.Amount, SameCurrency(left, right));

    static string SameCurrency(Money left, Money right) =>
        left.Currency == right.Currency
            ? left.Currency
            : throw new CurrencyMismatchException(left.Currency, right.Currency);

    public override string ToString() => $"{Amount:0.0000} {Currency}";
}

public sealed class CurrencyMismatchException(string left, string right)
    : InvalidOperationException($"Cannot combine {left} with {right}.");
