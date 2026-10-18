// Spike E: invented OFX files, generated at run time and never committed. A committed .ofx
// would carry DTASOF timestamps (fourteen digits) that the hygiene scanner's account-number
// rule refuses on sight in a data file - correctly, it cannot tell the two apart
// (slice-02.md step 5). Constitution IX: every value here is made up. BANKID is a run of
// one repeated digit for the same reason the scanner's validator gives: no entropy.

namespace SpikeE;

static class OfxFiles
{
    // Built from ISO dates and parts so this source file carries no eight-digit run either.
    static string D(string isoDate) => isoDate.Replace("-", "");
    static string Stamp(string isoDate, string time) => D(isoDate) + time;
    static readonly string BankId = new('1', 9);

    /// <summary>OFX 1.0.2: SGML, leaf elements unclosed, a header of KEY:VALUE lines.</summary>
    public static string Sgml102(bool closeLeaves = false)
    {
        string L(string tag, string value) => closeLeaves ? $"<{tag}>{value}</{tag}>" : $"<{tag}>{value}";
        return string.Join("\r\n",
            "OFXHEADER:100", "DATA:OFXSGML", "VERSION:102", "SECURITY:NONE", "ENCODING:USASCII",
            "CHARSET:1252", "COMPRESSION:NONE", "OLDFILEUID:NONE", "NEWFILEUID:NONE", "",
            "<OFX>",
            "<SIGNONMSGSRSV1><SONRS><STATUS>" + L("CODE", "0") + L("SEVERITY", "INFO") + "</STATUS>"
                + L("DTSERVER", Stamp("2026-10-18", "120000")) + L("LANGUAGE", "ENG") + "</SONRS></SIGNONMSGSRSV1>",
            "<BANKMSGSRSV1><STMTTRNRS>" + L("TRNUID", "1") + "<STATUS>" + L("CODE", "0") + L("SEVERITY", "INFO") + "</STATUS>",
            "<STMTRS>" + L("CURDEF", "USD"),
            "<BANKACCTFROM>" + L("BANKID", BankId) + L("ACCTID", "EXAMPLE-CHK-1") + L("ACCTTYPE", "CHECKING") + "</BANKACCTFROM>",
            "<BANKTRANLIST>" + L("DTSTART", D("2026-10-01")) + L("DTEND", D("2026-10-15")) + "</BANKTRANLIST>",
            "<LEDGERBAL>" + L("BALAMT", "1234.56") + L("DTASOF", Stamp("2026-10-15", "120000") + ".000[-5:EST]") + "</LEDGERBAL>",
            "<AVAILBAL>" + L("BALAMT", "1200.00") + L("DTASOF", Stamp("2026-10-15", "120000")) + "</AVAILBAL>",
            "</STMTRS></STMTTRNRS></BANKMSGSRSV1>",
            "<CREDITCARDMSGSRSV1><CCSTMTTRNRS>" + L("TRNUID", "2") + "<STATUS>" + L("CODE", "0") + L("SEVERITY", "INFO") + "</STATUS>",
            "<CCSTMTRS>" + L("CURDEF", "USD"),
            "<CCACCTFROM>" + L("ACCTID", "EXAMPLE-CC-1") + "</CCACCTFROM>",
            "<LEDGERBAL>" + L("BALAMT", "-250.00") + L("DTASOF", D("2026-10-16")) + "</LEDGERBAL>",
            "</CCSTMTRS></CCSTMTTRNRS></CREDITCARDMSGSRSV1>",
            "</OFX>", "");
    }

    /// <summary>OFX 2.2: XML, every element closed, a processing-instruction header.</summary>
    public static string Xml220(string? doctype = null) => string.Join("\n",
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>",
        "<?OFX OFXHEADER=\"200\" VERSION=\"220\" SECURITY=\"NONE\" OLDFILEUID=\"NONE\" NEWFILEUID=\"NONE\"?>",
        doctype ?? "",
        "<OFX>",
        "<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>"
            + $"<DTSERVER>{Stamp("2026-10-18", "120000")}</DTSERVER><LANGUAGE>ENG</LANGUAGE></SONRS></SIGNONMSGSRSV1>",
        "<BANKMSGSRSV1><STMTTRNRS><TRNUID>1</TRNUID><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>",
        "<STMTRS><CURDEF>USD</CURDEF>",
        $"<BANKACCTFROM><BANKID>{BankId}</BANKID><ACCTID>EXAMPLE-SAV-1</ACCTID><ACCTTYPE>SAVINGS</ACCTTYPE></BANKACCTFROM>",
        $"<LEDGERBAL><BALAMT>5000.0000</BALAMT><DTASOF>{Stamp("2026-10-17", "000000")}[0:GMT]</DTASOF></LEDGERBAL>",
        "</STMTRS></STMTTRNRS></BANKMSGSRSV1>",
        "<INVSTMTMSGSRSV1><INVSTMTTRNRS><TRNUID>3</TRNUID><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>",
        $"<INVSTMTRS><DTASOF>{Stamp("2026-10-17", "000000")}</DTASOF><CURDEF>USD</CURDEF>",
        "<INVACCTFROM><BROKERID>example.invalid</BROKERID><ACCTID>EXAMPLE-INV-1</ACCTID></INVACCTFROM>",
        "<INVBAL><AVAILCASH>10.00</AVAILCASH><MARGINBALANCE>0</MARGINBALANCE><SHORTBALANCE>0</SHORTBALANCE></INVBAL>",
        "</INVSTMTRS></INVSTMTTRNRS></INVSTMTMSGSRSV1>",
        "</OFX>", "");

    /// <summary>An XML file with an internal DTD and an external entity: must be refused, not resolved.</summary>
    public static string XmlWithEntity() => Xml220(
        "<!DOCTYPE OFX [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]>").Replace("<LANGUAGE>ENG", "<LANGUAGE>&x;");

    /// <summary>A 1.x file whose amount uses a comma decimal separator.</summary>
    public static string SgmlCommaDecimal() => Sgml102().Replace("<BALAMT>1234.56", "<BALAMT>1234,56");
}
