// slice-03.md step 6 and DoD 8: the pre-commit hook, installed by the documented command, refuses a
// staged secret-shaped value and commits a clean one, and still runs where `python3` exists but
// fails (round 2 TST-16, EXECUTED there: the Microsoft Store stub made the old hook refuse every
// commit). Each case is a fresh repository, with git's global and system config shut out so a
// developer's own settings (signing, templates, another hooksPath) cannot decide the result.
// The CI hygiene job is the backstop: `git commit --no-verify` skips any hook by design.
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Portfolio.Architecture;

public sealed class PreCommitHookTests : IDisposable
{
    // Built at run time, so this file never carries the shape the scanner refuses.
    static readonly string SecretShaped = "-----BEGIN " + "PRIVATE KEY-----";

    readonly DirectoryInfo root = Directory.CreateTempSubdirectory("portfolio-hook-");
    string Repo => Path.Combine(root.FullName, "repo");
    string EmptyConfig => Path.Combine(root.FullName, "empty.gitconfig");

    public PreCommitHookTests()
    {
        Directory.CreateDirectory(Repo);
        File.WriteAllText(EmptyConfig, "");
        Run("git", "init", "-q");
        Run("git", "config", "user.name", "Hook Test");
        Run("git", "config", "user.email", "hook-test@example.invalid");
        Run("git", "config", "commit.gpgsign", "false");
        foreach (var file in new[] { ".githooks/pre-commit", "scripts/hygiene.py", "scripts/install-hooks.sh" })
        {
            var target = Path.Combine(Repo, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(RepoRoot, file), target);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, (UnixFileMode)0b111_101_101); // 0755
        }

        // The README's command, run as the README runs it.
        var installed = Run(Shell(), "scripts/install-hooks.sh");
        Assert.Equal(0, installed.Exit);
        Assert.Equal(".githooks", Run("git", "config", "--get", "core.hooksPath").Out.Trim());
    }

    public void Dispose()
    {
        try { root.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void A_staged_secret_is_refused()
    {
        var commit = Commit("leak.txt", "config\n" + SecretShaped + "\n");

        Assert.NotEqual(0, commit.Exit);
        Assert.Contains("CONSTITUTION IX: refusing this change.", commit.Err);
        Assert.Contains("private-key", commit.Err);
    }

    [Fact]
    public void A_clean_file_commits()
    {
        // The twin that tells "refused the secret" from "could not run the scanner".
        var commit = Commit("notes.txt", "nothing to see\n");

        Assert.True(commit.Exit == 0, commit.Err);
        Assert.Contains("hygiene: clean", commit.Out + commit.Err);
    }

    [Fact]
    public void A_python3_that_exists_but_fails_does_not_stop_a_clean_commit()
    {
        // First on PATH: a `python3` that exits 49, as the Store stub does, and a `python` that runs a
        // real interpreter, so the fallback is exercised on any machine, CI's included.
        var shims = Directory.CreateDirectory(Path.Combine(root.FullName, "shims")).FullName;
        WriteScript(Path.Combine(shims, "python3"), "#!/bin/sh\necho 'Python was not found (test stub)' >&2\nexit 49\n");
        WriteScript(Path.Combine(shims, "python"), $"#!/bin/sh\nexec \"{ShellPath(RealPython())}\" \"$@\"\n");

        var commit = Commit("notes.txt", "nothing to see\n", prependPath: shims);

        Assert.True(commit.Exit == 0, commit.Err);
        Assert.Contains("hygiene: clean", commit.Out + commit.Err);
    }

    (int Exit, string Out, string Err) Commit(string file, string content, string? prependPath = null)
    {
        File.WriteAllText(Path.Combine(Repo, file), content);
        Assert.Equal(0, Run("git", "add", file).Exit);
        return Run("git", ["commit", "-q", "-m", "test"], prependPath);
    }

    (int Exit, string Out, string Err) Run(string exe, params string[] args) => Run(exe, args, null);

    (int Exit, string Out, string Err) Run(string exe, string[] args, string? prependPath)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["GIT_CONFIG_GLOBAL"] = EmptyConfig;
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        if (prependPath is not null)
            start.Environment["PATH"] = prependPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(2))) { process.Kill(true); throw new TimeoutException($"{exe} {string.Join(' ', args)}"); }
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    static void WriteScript(string path, string content)
    {
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
    }

    /// <summary>`sh` from PATH, or on Windows the one Git for Windows ships beside git.</summary>
    static string Shell()
    {
        if (!OperatingSystem.IsWindows() || Works("sh", "-c", "exit 0")) return "sh";
        var execPath = Output("git", "--exec-path").Trim();           // …/Git/mingw64/libexec/git-core
        var gitRoot = Path.GetFullPath(Path.Combine(execPath, "..", "..", ".."));
        return new[] { Path.Combine(gitRoot, "bin", "sh.exe"), Path.Combine(gitRoot, "usr", "bin", "sh.exe") }.First(File.Exists);
    }

    /// <summary>A Python 3 that runs, found the way the hook finds one: by running it.</summary>
    static string RealPython()
    {
        foreach (var candidate in new[] { "python3", "python" })
            if (Works(candidate, "-c", "import sys; assert sys.version_info >= (3,)"))
                return Output(candidate, "-c", "import sys; print(sys.executable)").Trim();
        throw new InvalidOperationException("no working Python 3 on PATH; the hook needs one");
    }

    /// <summary>A Windows path as the hook's shell reads it (C:\x → /c/x).</summary>
    static string ShellPath(string path) =>
        OperatingSystem.IsWindows() && path.Length > 2 && path[1] == ':'
            ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
            : path;

    static bool Works(string exe, params string[] args)
    {
        try { return Exec(exe, args).Exit == 0; } catch (System.ComponentModel.Win32Exception) { return false; }
    }

    static string Output(string exe, params string[] args) => Exec(exe, args).Out;

    static (int Exit, string Out) Exec(string exe, string[] args)
    {
        var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout);
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
