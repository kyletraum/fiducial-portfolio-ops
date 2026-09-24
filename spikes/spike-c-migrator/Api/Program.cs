// Spike C: the API records when it started, so the order against the migrator is measurable.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
Console.WriteLine($"[api] start {DateTime.UtcNow:O}");
app.MapGet("/health", () => "ok");
app.Run();
