// slice-01.md step 1, the invariant as corrected there:
//
//   No project outside AppHost may reference Aspire.Hosting.*, with one named
//   exception: the integration-test project may reference Aspire.Hosting.Testing
//   and the AppHost project itself. Domain references nothing outside the shared
//   framework.
//
// Asserted over DIRECT PackageReference/ProjectReference, read from the project
// files, not over the transitive closure - the integration tests' AppHost
// reference would defeat a transitive check.
using System.Xml.Linq;

namespace Portfolio.Architecture;

public class AspireBoundaryTests
{
    const string AppHost = "AppHost";
    const string IntegrationTests = "Integration";

    static readonly string[] Expected = [AppHost, "Api", "Migrator", "Domain", "ServiceDefaults"];

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
        var allowed = name == IntegrationTests ? new[] { "Aspire.Hosting.Testing" } : [];
        var offending = ProjectFile.Named(name).Packages
            .Where(p => p.StartsWith("Aspire.Hosting", StringComparison.OrdinalIgnoreCase))
            .Except(allowed, StringComparer.OrdinalIgnoreCase);
        Assert.Empty(offending);
    }

    [Theory]
    [MemberData(nameof(Projects))]
    public void Only_the_integration_tests_reference_AppHost(string name)
    {
        if (name is AppHost or IntegrationTests) return;
        Assert.DoesNotContain(AppHost, ProjectFile.Named(name).ProjectReferences);
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
