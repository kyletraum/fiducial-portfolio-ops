// Spike H: argon2id for the password fallback. .NET 10 has none in the box (its reference
// documentation names no Argon2 type), so this compares three candidates on what a password
// store needs: a self-describing hash string (algorithm, parameters and salt travel with the
// hash), verification that accepts the right password and refuses a wrong one, and a cost
// measured on this machine rather than copied from a guide.
//
//   dotnet run -c Release            # all three candidates, all parameter sets
//   dotnet run -c Release -- quick   # one parameter set (the Linux container run)

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var quick = args.Contains("quick");
const string Password = "correct horse battery staple";   // invented
const string Wrong = "correct horse battery stapler";

// (label, memory in KiB, iterations). OWASP's floor; libsodium's INTERACTIVE; a 64 MiB/t=3 middle; libsodium's MODERATE.
(string Label, int MemKiB, int Iter)[] sets = quick
    ? [("64MiB t3", 64 * 1024, 3)]
    : [("19MiB t2", 19 * 1024, 2), ("64MiB t2", 64 * 1024, 2), ("64MiB t3", 64 * 1024, 3), ("256MiB t3", 256 * 1024, 3)];

Console.WriteLine($"{Environment.OSVersion} | {Environment.ProcessorCount} logical CPUs | .NET {Environment.Version}");

// --- Sodium.Core (libsodium crypto_pwhash_str*) ---
string SodiumHash(int memKiB, int iter) => Sodium.PasswordHash.ArgonHashString(Password, iter, memKiB * 1024);
bool SodiumVerify(string hash, string pw) => Sodium.PasswordHash.ArgonHashStringVerify(hash, pw);

// --- Geralt (libsodium, Span-based) ---
string GeraltHash(int memKiB, int iter)
{
    Span<char> hash = stackalloc char[Geralt.Argon2id.HashSize];
    Geralt.Argon2id.ComputeHash(hash, Encoding.UTF8.GetBytes(Password), iter, memKiB * 1024);
    return new string(hash).TrimEnd('\0');
}
bool GeraltVerify(string hash, string pw)
{
    var buffer = new char[Geralt.Argon2id.HashSize]; // the fixed-size, zero-padded form ComputeHash writes
    hash.AsSpan().CopyTo(buffer);
    return Geralt.Argon2id.VerifyHash(buffer, Encoding.UTF8.GetBytes(pw));
}

// --- Konscious (managed; raw bytes only - no encoded string, so salt and parameters are ours to store) ---
string KonsciousHash(int memKiB, int iter)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var raw = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(Password))
        { Salt = salt, MemorySize = memKiB, Iterations = iter, DegreeOfParallelism = 1 }.GetBytes(32);
    return $"konscious:{memKiB}:{iter}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(raw)}";
}
bool KonsciousVerify(string hash, string pw)
{
    var p = hash.Split(':');
    var raw = new Konscious.Security.Cryptography.Argon2id(Encoding.UTF8.GetBytes(pw))
        { Salt = Convert.FromBase64String(p[3]), MemorySize = int.Parse(p[1]), Iterations = int.Parse(p[2]), DegreeOfParallelism = 1 }.GetBytes(32);
    return CryptographicOperations.FixedTimeEquals(raw, Convert.FromBase64String(p[4]));
}

var candidates = new (string Name, Func<int, int, string> Hash, Func<string, string, bool> Verify)[]
{
    ("Sodium.Core 1.4.1", SodiumHash, SodiumVerify),
    ("Geralt 4.4.0", GeraltHash, GeraltVerify),
    ("Konscious 1.3.1", KonsciousHash, KonsciousVerify),
};

foreach (var (label, mem, iter) in sets)
{
    Console.WriteLine($"\n=== {label}");
    foreach (var (name, hash, verify) in candidates)
    {
        try
        {
            var times = new List<double>();
            string h = "";
            for (var i = 0; i < (quick ? 3 : 5); i++)
            {
                var sw = Stopwatch.StartNew();
                h = hash(mem, iter);
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            var right = verify(h, Password);
            var wrong = verify(h, Wrong);
            Console.WriteLine($"  {name,-20} median {times[times.Count / 2],7:F0} ms | verifies right: {right} | refuses wrong: {!wrong} | {(h.Length > 64 ? h[..64] + "..." : h)}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"  {name,-20} FAILED {e.GetType().Name}: {e.Message.Split('\n')[0]}");
        }
    }
}

// Both libsodium wrappers write the standard PHC-style string, so a hash from one must verify in the other.
var fromSodium = SodiumHash(64 * 1024, 3);
var fromGeralt = GeraltHash(64 * 1024, 3);
Console.WriteLine($"\ninterop: Sodium hash verified by Geralt: {GeraltVerify(fromSodium, Password)}; Geralt hash verified by Sodium: {SodiumVerify(fromGeralt, Password)}");
