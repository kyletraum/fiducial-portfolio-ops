using System.Diagnostics;
using System.Text;

namespace Portfolio.Contract;

/// <summary>
/// slice-01 step 5, the contract test: regenerate the OpenAPI document and assert no diff
/// against the committed one. S-14, corrected: the ONLY generator is the build-time tool
/// (invariant culture, LF), never a curl of the runtime endpoint. Advisory in CI by the
/// middle cut; here it simply passes or fails.
/// </summary>
public class OpenApiDocumentTests
{
    const string DocumentName = "portfolio-api.json";
    const string Regenerate = "dotnet build src/Api";

    [Fact]
    public async Task The_committed_document_is_what_the_build_generates()
    {
        var repo = RepoRoot();
        var committed = Path.Combine(repo, "src", "Api", "openapi", DocumentName);
        var scratch = Directory.CreateTempSubdirectory("openapi-contract-");
        try
        {
            // GenerateOpenApiDocuments is incremental: Inputs=Api.dll, Outputs=a cache file in
            // obj/. With the dll unchanged it is skipped and writes nothing, whatever the output
            // directory. Pointing the cache into the scratch directory forces a real generation
            // without touching the working obj/ or rebuilding anything.
            var (exit, output) = await RunAsync(repo, "dotnet", "build", "src/Api", "--nologo", "-v", "q",
                $"-p:OpenApiDocumentsDirectory={scratch.FullName}",
                $"-p:_OpenApiDocumentsCache={Path.Combine(scratch.FullName, "openapi.cache")}");
            Assert.True(exit == 0, $"Building src/Api failed:\n{output}");

            var generated = await File.ReadAllBytesAsync(Path.Combine(scratch.FullName, DocumentName), TestContext.Current.CancellationToken);
            var expected = await File.ReadAllBytesAsync(committed, TestContext.Current.CancellationToken);

            Assert.True(generated.AsSpan().SequenceEqual(expected), Explain(expected, generated));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    static string Explain(byte[] committed, byte[] generated)
    {
        var a = Encoding.UTF8.GetString(committed).Split('\n');
        var b = Encoding.UTF8.GetString(generated).Split('\n');
        var line = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(i => a[i] != b[i], Math.Min(a.Length, b.Length));
        return $"""
            src/Api/openapi/{DocumentName} is not what the build generates.
            First difference at line {line + 1}:
              committed: {(line < a.Length ? a[line] : "<end of file>")}
              generated: {(line < b.Length ? b[line] : "<end of file>")}
            Committed {committed.Length} bytes ({Count(committed, "\r\n")} CRLF); generated {generated.Length} bytes ({Count(generated, "\r\n")} CRLF).
            If the API changed on purpose, regenerate and commit it:
                {Regenerate}
            then regenerate the TypeScript client:  cd web && npm run gen:api
            """;
    }

    static int Count(byte[] bytes, string needle) =>
        Encoding.UTF8.GetString(bytes).Split(needle).Length - 1;

    static async Task<(int Exit, string Output)> RunAsync(string cwd, string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await stdout + await stderr);
    }

    static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "aspire.config.json")))
                return dir.FullName;
        throw new InvalidOperationException("No aspire.config.json above " + AppContext.BaseDirectory);
    }
}
