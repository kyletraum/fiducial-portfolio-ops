using Portfolio.Domain;
using Portfolio.Ingestion;

namespace Portfolio.Unit;

/// <summary>
/// The rule the bind endpoint and the parsers share: an id that can be bound is exactly an id a
/// file can carry. And each connector declares its own strength, server-side (S-27).
/// </summary>
public class SourceIdsTests
{
    [Theory]
    [InlineData("EXAMPLE-CHK-1", null)]
    [InlineData("", "is empty")]
    [InlineData(" EXAMPLE", "whitespace")]
    [InlineData("EXAMPLE\t1", "control character")]
    public void Says_why_an_id_cannot_be_bound(string id, string? expected)
    {
        var problem = SourceIds.Problem(id);

        if (expected is null) Assert.Null(problem);
        else Assert.Contains(expected, problem);
    }

    [Fact]
    public void An_id_one_character_over_the_limit_is_refused() =>
        Assert.Contains("longer than", SourceIds.Problem(new string('A', IngestionLimits.MaxSourceIdLength + 1)));

    [Fact]
    public void The_bindable_systems_are_exactly_the_two_file_formats() =>
        Assert.Equal(["import:csv", "import:ofx"], SourceIds.ImportSystems);

    [Fact]
    public void Both_file_formats_are_exports() =>
        Assert.All(new IBalanceSource[] { new CsvBalanceSource(), new OfxBalanceSource() },
            s => Assert.Equal(SourceStrength.Export, s.Strength));
}
