// slice-01.md step 1, the invariant as corrected there:
//
//   No project outside AppHost may reference Aspire.Hosting.*, with one named
//   exception: the E2E project may reference Aspire.Hosting.Testing and the AppHost
//   project itself. Domain references nothing outside the shared framework.
//
// The exception was written for "the integration-test project"; step 5 gave that project
// Testcontainers instead, and the E2E is what starts the app model. Moved by Kyle's ruling,
// 2026-09-24 - still exactly ONE named exception, on the project that uses it.
//
// Asserted over DIRECT PackageReference/ProjectReference, read from the project
// files, not over the transitive closure - the E2E project's AppHost reference
// would defeat a transitive check.
using System.Xml.Linq;

namespace Portfolio.Architecture;

public class AspireBoundaryTests
{
    const string AppHost = "AppHost";
    const string AppModelTests = "E2E";

    static readonly string[] Expected = [AppHost, "Api", "Migrator", "Domain", "Infrastructure", "ServiceDefaults"];

    // S-25c: Aspire *client* integrations (Aspire.Npgsql.EntityFrameworkCore.PostgreSQL and
    // the like) belong in the composition roots that run as processes. Infrastructure owns
    // EF Core but is not Aspire-aware (plan.md, "The Aspire boundary").
    static readonly string[] CompositionRoots = [AppHost, "Api", "Migrator", AppModelTests];

    public static TheoryData<string> Projects() => new(ProjectFile.All().Select(p => p.Name));

    [Fact]
    public void Finds_the_projects_it_is_meant_to_guard()
    {
        var found = ProjectFile.All().Select(p => p.Name).ToHashSet();
        Assert.All(Expected, name => Assert.Contains(name, found));
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Only_AppHost_uses_an_Aspire_SDK(string name)
    {
        if (name == AppHost) return;
        var sdk = ProjectFile.Named(name).Sdk;
        Assert.False(sdk.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase),
            $"{name} uses SDK '{sdk}'");
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Only_AppHost_references_Aspire_Hosting(string name)
    {
        if (name == AppHost) return;
        var allowed = name == AppModelTests ? new[] { "Aspire.Hosting.Testing" } : [];
        var offending = ProjectFile.Named(name).Packages
            .Where(p => p.StartsWith("Aspire.Hosting", StringComparison.OrdinalIgnoreCase))
            .Except(allowed, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(offending);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Only_the_E2E_tests_reference_AppHost(string name)
    {
        if (name is AppHost or AppModelTests) return;
        Assert.DoesNotContain(AppHost, ProjectFile.Named(name).ProjectReferences);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Only_composition_roots_reference_any_Aspire_package(string name)
    {
        if (CompositionRoots.Contains(name)) return;
        var offending = ProjectFile.Named(name).Packages
            .Where(p => p.StartsWith("Aspire.", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(offending);
    }

    [Fact]
    public void Domain_references_nothing()
    {
        var domain = ProjectFile.Named("Domain");
        Assert.Empty(domain.Packages);
        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.FrameworkReferences);
    }
}

sealed record ProjectFile(string Name, string Sdk, string[] Packages, string[] ProjectReferences, string[] FrameworkReferences)
{
    public static IEnumerable<ProjectFile> All() =>
        new[] { "src", "tests" }
            .SelectMany(dir => Directory.EnumerateFiles(Path.Combine(RepoRoot, dir), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Select(Load)
            .OrderBy(p => p.Name);

    public static ProjectFile Named(string name) => All().Single(p => p.Name == name);

    static ProjectFile Load(string path)
    {
        var xml = XDocument.Load(path);
        string[] Includes(string element) =>
            xml.Descendants(element).Select(e => (string?)e.Attribute("Include") ?? "").ToArray();

        return new(
            Path.GetFileNameWithoutExtension(path),
            (string?)xml.Root!.Attribute("Sdk") ?? "",
            Includes("PackageReference"),
            // Include paths are written with '\'; Linux's Path does not split on it.
            Includes("ProjectReference").Select(p => Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))).ToArray()!,
            Includes("FrameworkReference"));
    }

    static string RepoRoot { get; } = FindRepoRoot();

    static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "aspire.config.json")))
                return dir.FullName;
        throw new InvalidOperationException("No aspire.config.json above " + AppContext.BaseDirectory);
    }
}
