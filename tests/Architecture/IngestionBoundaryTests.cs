// slice-02.md step 1: the parsers reference neither EF Core nor the DbContext.
//
// plan.md "Ingestion": a connector parses and maps; one pipeline owns what an import means to
// the data. Held by giving the parsers their own project and asserting what that project may
// reference, read from the project file as AspireBoundaryTests does. Domain references nothing
// (Domain_references_nothing), so Ingestion -> Domain closes the graph: no package can arrive
// transitively.
namespace Portfolio.Architecture;

public class IngestionBoundaryTests
{
    [Fact]
    public void Ingestion_references_Domain_and_nothing_else()
    {
        var ingestion = ProjectFile.Named("Ingestion");

        Assert.Equal(["Domain"], ingestion.ProjectReferences);
        Assert.Empty(ingestion.Packages);
        Assert.Empty(ingestion.FrameworkReferences);
    }

    [Fact]
    public void Ingestion_is_not_referenced_by_Domain()
    {
        // The other direction: a Domain that knew about parsers would make them part of the model.
        Assert.DoesNotContain("Ingestion", ProjectFile.Named("Domain").ProjectReferences);
    }
}
