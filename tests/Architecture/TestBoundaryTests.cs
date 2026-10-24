// slice-03.md step 2, round 2 TST-3 (b): test-only code stays in tests/. The integration tests
// sign in through a test authentication scheme; it is safe only while nothing the app ships can
// reference the project it lives in. Asserted over direct ProjectReferences, as the Aspire boundary
// is: a src project that referenced a test project would fail here, before anything ran.
namespace Portfolio.Architecture;

public class TestBoundaryTests
{
    static readonly string[] TestProjects = ["Architecture", "Contract", "E2E", "Integration", "Unit"];

    static readonly string[] AppProjects = ["Api", "AppHost", "Domain", "Infrastructure", "Ingestion", "Migrator", "ServiceDefaults"];

    [Fact]
    public void Finds_the_projects_it_is_meant_to_guard()
    {
        var found = ProjectFile.All().Select(p => p.Name).ToHashSet();
        Assert.All(TestProjects.Concat(AppProjects), name => Assert.Contains(name, found));
    }

    public static TheoryData<string> Apps() => new(AppProjects);

    [Theory]
    [MemberData(nameof(Apps))]
    public void No_app_project_references_a_test_project(string name) =>
        Assert.Empty(ProjectFile.Named(name).ProjectReferences.Intersect(TestProjects));
}
