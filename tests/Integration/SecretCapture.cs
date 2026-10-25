using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Portfolio.Integration;

/// <summary>
/// Every log entry the app writes, as text: the formatted message, every structured state value,
/// every scope value and the whole exception chain (round 2 TST-14: a provider that kept only the
/// message would miss most of where a value can travel).
/// </summary>
public sealed class LogCapture : ILoggerProvider, ISupportExternalScope
{
    readonly List<(string Category, LogLevel Level, string Text)> entries = [];
    IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<(string Category, LogLevel Level, string Text)> Entries { get { lock (entries) return [.. entries]; } }

    public ILogger CreateLogger(string category) => new Logger(this, category);
    public void SetScopeProvider(IExternalScopeProvider provider) => scopes = provider;
    public void Dispose() { }

    sealed class Logger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => capture.scopes.Push(state);
        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = new StringBuilder(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
                foreach (var (key, value) in values) text.Append(" | ").Append(key).Append('=').Append(value);
            capture.scopes.ForEachScope((scope, sb) => sb.Append(" | scope ").Append(Describe(scope)), text);
            if (exception is not null) text.Append(" | ").Append(exception); // ToString: the inner chain too
            lock (capture.entries) capture.entries.Add((category, level, text.ToString()));
        }

        static string? Describe(object? scope) => scope is IEnumerable<KeyValuePair<string, object?>> values
            ? string.Join(", ", values.Select(v => $"{v.Key}={v.Value}"))
            : scope?.ToString();
    }
}

/// <summary>
/// Every non-2xx body the clients received, into one shared list. One handler per client: a
/// DelegatingHandler belongs to the one pipeline it was first built into.
/// </summary>
public sealed class ErrorBodies(List<string> bodies) : DelegatingHandler
{

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync(ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            lock (bodies) bodies.Add(body); // the body alone: the request line is not what the client was sent
        }
        return response;
    }
}

/// <summary>
/// Where a secret might be found in text: as itself, URL-encoded, or base64 or base64url-encoded
/// inside a larger value at any of the three byte alignments (TST-14). A secret buried in a base64
/// blob shows its own encoding only at offset 0 mod 3, so each offset is encoded separately, keeping
/// only the characters the secret's bytes alone determine.
/// </summary>
public static class SecretScan
{
    public static IEnumerable<string> Forms(string secret)
    {
        yield return secret;
        yield return Uri.EscapeDataString(secret);
        var bytes = Encoding.UTF8.GetBytes(secret);
        for (var offset = 0; offset < 3; offset++)
        {
            var encoded = Convert.ToBase64String([.. new byte[offset], .. bytes]);
            var first = (offset * 8 + 5) / 6;           // the first character wholly inside the secret
            var last = (offset + bytes.Length) * 8 / 6;  // past the last such character
            var core = encoded[first..last];
            if (core.Length >= 12)
            {
                yield return core;
                yield return core.Replace('+', '-').Replace('/', '_');
            }
        }
    }

    public static IEnumerable<string> Hits(IEnumerable<(string Name, string Secret)> secrets, IEnumerable<string> haystack)
    {
        var texts = haystack.ToList();
        foreach (var (name, secret) in secrets)
            foreach (var form in Forms(secret).Distinct())
                foreach (var text in texts.Where(t => t.Contains(form, StringComparison.Ordinal)))
                    yield return $"{name} (as '{form}') in: {(text.Length > 300 ? text[..300] + "…" : text)}";
    }

    /// <summary>A span as text: its name, every tag, every event and its status.</summary>
    public static string Describe(Activity a) =>
        $"{a.DisplayName} {a.Status} {a.StatusDescription} "
        + string.Join(" ", a.TagObjects.Select(t => $"{t.Key}={t.Value}"))
        + string.Join(" ", a.Events.Select(e => e.Name + " " + string.Join(" ", e.Tags.Select(t => $"{t.Key}={t.Value}"))));
}

/// <summary>
/// The in-memory exporter's collection, locked: spans are added from request threads while the test
/// reads them (EXECUTED: "Collection was modified" in a full run). Read through <see cref="Snapshot"/>.
/// </summary>
public sealed class Spans : ICollection<Activity>
{
    readonly List<Activity> items = [];
    public List<Activity> Snapshot() { lock (items) return [.. items]; }
    public void Add(Activity item) { lock (items) items.Add(item); }
    public void Clear() { lock (items) items.Clear(); }
    public bool Contains(Activity item) { lock (items) return items.Contains(item); }
    public void CopyTo(Activity[] array, int index) { lock (items) items.CopyTo(array, index); }
    public bool Remove(Activity item) { lock (items) return items.Remove(item); }
    public int Count { get { lock (items) return items.Count; } }
    public bool IsReadOnly => false;
    public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
