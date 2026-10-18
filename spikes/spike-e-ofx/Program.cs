// Spike E: what does it take to read a balance out of OFX in .NET?
//
//   dotnet run
//
// Every file is generated in memory from OfxFiles (invented values) and read twice: by the
// minimal reader in OfxBalanceReader.cs, and by OfxNet 1.8.1 for comparison. Nothing is
// written to disk, and nothing is fetched from the network.

using System.Text;
using OfxNet;
using SpikeE;

var cases = new (string Name, string Text, Encoding Enc)[]
{
    ("1.0.2 SGML, leaves unclosed", OfxFiles.Sgml102(), Encoding.ASCII),
    ("1.0.2 SGML, leaves closed", OfxFiles.Sgml102(closeLeaves: true), Encoding.ASCII),
    ("2.2 XML, bank + investment", OfxFiles.Xml220(), Encoding.UTF8),
    ("2.2 XML with DOCTYPE + external entity", OfxFiles.XmlWithEntity(), Encoding.UTF8),
    ("1.0.2 SGML, comma decimal", OfxFiles.SgmlCommaDecimal(), Encoding.ASCII),
    // Isolating why OfxNet throws on the 2.2 file: the investment statement, or the offset form.
    ("2.2 XML, bank only", BankOnly(OfxFiles.Xml220()), Encoding.UTF8),
    ("2.2 XML, bank only, offset [+0:GMT]", BankOnly(OfxFiles.Xml220()).Replace("[0:GMT]", "[+0:GMT]"), Encoding.UTF8),
    ("2.2 XML, bank only, no offset", BankOnly(OfxFiles.Xml220()).Replace("[0:GMT]", ""), Encoding.UTF8),
    ("2.2 XML, investment only", InvOnly(OfxFiles.Xml220()), Encoding.UTF8),
    ("2.2 XML, bank only, DOCTYPE + external entity", BankOnly(OfxFiles.XmlWithEntity()).Replace("[0:GMT]", ""), Encoding.UTF8),
    // Does a DTD get PROCESSED? Entities placed in ACCTID, the one text field both readers print.
    ("2.2 XML, internal entity in ACCTID", EntityIn("<!ENTITY y \"EXPANDED-INTERNAL\">", "&y;"), Encoding.UTF8),
    ("2.2 XML, external entity in ACCTID", EntityIn($"<!ENTITY z SYSTEM \"{MarkerUri()}\">", "&z;"), Encoding.UTF8),
};

// A local file with a known marker, so an external entity that IS resolved shows up as that
// text and nothing else on the machine is read. Deleted at exit.
static string MarkerUri()
{
    var path = Path.Combine(Path.GetTempPath(), "spike-e-entity-marker.txt");
    File.WriteAllText(path, "RESOLVED-EXTERNAL-FILE");
    AppDomain.CurrentDomain.ProcessExit += (_, _) => File.Delete(path);
    return new Uri(path).AbsoluteUri;
}

static string EntityIn(string entity, string reference) =>
    BankOnly(OfxFiles.Xml220($"<!DOCTYPE OFX [{entity}]>")).Replace("[0:GMT]", "")
        .Replace("<ACCTID>EXAMPLE-SAV-1</ACCTID>", $"<ACCTID>{reference}</ACCTID>");

static string BankOnly(string xml) => Cut(xml, "<INVSTMTMSGSRSV1>", "</INVSTMTMSGSRSV1>");
static string InvOnly(string xml) => Cut(xml, "<BANKMSGSRSV1>", "</BANKMSGSRSV1>");
static string Cut(string s, string from, string to)
{
    var a = s.IndexOf(from, StringComparison.Ordinal);
    var b = s.IndexOf(to, StringComparison.Ordinal) + to.Length;
    return s[..a] + s[b..];
}

// Last: the plain 1.0.2 file again, read on a machine whose culture uses ',' as the decimal
// separator. If OfxNet parses with the current culture, the '.' file is misread there too.
cases = [.. cases, ("1.0.2 SGML, read under de-DE culture", OfxFiles.Sgml102(), Encoding.ASCII)];

foreach (var (name, text, enc) in cases)
{
    System.Globalization.CultureInfo.CurrentCulture = name.Contains("de-DE")
        ? new System.Globalization.CultureInfo("de-DE")
        : System.Globalization.CultureInfo.InvariantCulture;
    var bytes = enc.GetBytes(text);
    Console.WriteLine($"=== {name} ({bytes.Length} bytes)");

    try
    {
        var (balances, errors, version) = OfxBalanceReader.Read(bytes);
        Console.WriteLine($"  minimal reader: version {version}");
        foreach (var b in balances)
            Console.WriteLine($"    {b.Kind,-10} {b.AccountId,-14} {b.AsOfDate:yyyy-MM-dd} {b.Amount,10} {b.Currency}  (DTASOF {b.DtAsOfRaw})");
        foreach (var e in errors)
            Console.WriteLine($"    refused: {e}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  minimal reader: THREW {ex.GetType().Name}: {ex.Message}");
    }

    try
    {
        var doc = OfxDocument.Load(new MemoryStream(bytes));
        var statements = doc.GetStatements().ToList();
        Console.WriteLine($"  OfxNet: {statements.Count} statement(s)");
        foreach (var s in statements)
        {
            var acct = s switch
            {
                OfxBankStatement bs => bs.Account?.AccountNumber,
                OfxCreditCardStatement cs => cs.Account?.AccountNumber,
                _ => "?",
            };
            Console.WriteLine($"    {s.GetType().Name,-22} {acct,-14} ledger {s.LedgerBalance?.Balance} as of {s.LedgerBalance?.DateAsOf:O}  currency {s.DefaultCurrency}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  OfxNet: THREW {ex.GetType().Name}: {ex.Message}");
    }
    Console.WriteLine();
}
