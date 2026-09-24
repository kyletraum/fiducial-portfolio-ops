// Spike C: a one-shot migrator. Sleeps, then exits with MIGRATOR_EXIT (default 0).
// Stands in for "apply EF Core migrations, then exit"; the timing is the point.
var seconds = int.Parse(Environment.GetEnvironmentVariable("MIGRATOR_SECONDS") ?? "8");
var exitCode = int.Parse(Environment.GetEnvironmentVariable("MIGRATOR_EXIT") ?? "0");
Console.WriteLine($"[migrator] start {DateTime.UtcNow:O} sleeping {seconds}s, will exit {exitCode}");
Thread.Sleep(TimeSpan.FromSeconds(seconds));
Console.WriteLine($"[migrator] exit {DateTime.UtcNow:O} code {exitCode}");
return exitCode;
