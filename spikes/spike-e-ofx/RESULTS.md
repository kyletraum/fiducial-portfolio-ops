# Spike E — what does it take to read a balance out of OFX in .NET?

**Ran:** 2026-10-18. **Command:** `dotnet run` in this directory (.NET SDK 10.0.402,
Windows) [`EXECUTED 2026-10-18`].
**Against:** thirteen invented files, generated in memory by `OfxFiles.cs` and
never written to disk. Each was read twice: by the minimal reader in
`OfxBalanceReader.cs` (about 150 lines), and by **OfxNet 1.8.1**, the only
recently published OFX parser on NuGet that targets .NET 10.
**Result:** a balance is cheap to read, and **step 2 writes its own reader.**
The library misread amounts silently, by a factor of 100, in two ways. It also
processes DTDs, and it drops investment statements without saying so.

> **OFX timestamps in this file are written split**, the date part apart from the time.
> Eight or more unbroken digits in a committed file trip the hygiene scanner's
> account-number rule, which is the reason the files are generated
> (`slice-02.md` step 5).

---

## 1. The three claims

| Claim (`slice-02.md` Spike E) | Verdict |
|---|---|
| **(1)** The balance and its date are `LEDGERBAL` / `BALAMT`, `DTASOF`; the account is `ACCTID` under `BANKACCTFROM` or `CCACCTFROM` | **HOLDS, with a limit.** Both readers extract the same values from those elements. OfxNet was written independently, against files its author had, so that agreement is corroboration. But the test files are mine, written from a recollection of the specification, which was **not read** [`INFERRED` for the specification itself] |
| **(2)** 1.x is SGML with optional closing tags and is not XML; 2.x is XML | **HOLDS.** 1.0.2 files with leaves unclosed and with leaves closed both read the same through one tokenizer. 2.2 reads through `XmlReader`. The two share nothing below the element tree |
| **(3)** No maintained .NET library is worth a dependency for these few elements | **HOLDS.** See section 3 |

## 2. What was read (minimal reader)

| File | Account | As of | Amount |
|---|---|---|---|
| 1.0.2, leaves unclosed | `EXAMPLE-CHK-1` (bank) | 2026-10-15, from `20261015` `120000.000[-5:EST]` | 1234.56 |
| | `EXAMPLE-CC-1` (credit card) | 2026-10-16, from a date-only `DTASOF` | -250.00 |
| 1.0.2, leaves closed | identical | identical | identical |
| 2.2 | `EXAMPLE-SAV-1` (bank) | 2026-10-17, from `20261017` `000000[0:GMT]` | 5000.0000 |
| 2.2 | `EXAMPLE-INV-1` (investment) | **refused**: `INVSTMTRS` has no `LEDGERBAL` | — |

**The as-of date is the first eight digits as written**: the date in the zone
the file states, not converted to UTC. For an end-of-day balance that is the
day the institution meant. Converting `[-5:EST]` to UTC would move any balance
stamped after 19:00 to the next day.

## 3. OfxNet 1.8.1, the same files

| File | OfxNet | Why it matters |
|---|---|---|
| 1.0.2, `BALAMT` `1234,56` | **reads `123456`**, no error | A comma decimal is parsed as a thousands separator: the balance comes out 100 times too large. The minimal reader refuses the line |
| 1.0.2, plain `1234.56`, on a **de-DE** machine | **reads `123456`**, and `-250.00` as **`-25000`** | The library parses with the machine's culture. The same file gives a different balance on a different machine. Constitution I forbids a figure the system did not observe |
| 2.2, `DTASOF` offset `[0:GMT]` | **throws** `FormatException` | It accepts `[+0:GMT]` and no offset; the unsigned form fails the whole file. Whether `[0:GMT]` is valid OFX was **not checked against the specification** |
| 2.2, investment statement only | **0 statements**, no error | An account in the file silently produces nothing |
| 2.2 with an internal `<!ENTITY>` in `ACCTID` | **expanded it**: `EXPANDED-INTERNAL` | DTDs are processed. Entity-expansion limits were not tested |
| 2.2 with an external `SYSTEM` entity in `ACCTID` | not resolved; **empty account id**, no error | The entity was not substituted (the marker file's text never appeared in the account id), but the file was not refused either |

The other packages on NuGet: OFXSharp, OfxSharpLib, OfxSharp.NetStandard and
OFXParser.NetCore were last published between 2014 and 2018. OFXParser 1.1.0
(June 2025) targets .NET Standard 2.0; its README says it reads transactions,
balances and account details, and it takes a file path. It was not run. Mocoding.Ofx is a 2.0.0 release candidate for .NET Standard
1.0. Aspose.Finance and CData.OFX are commercial. The `OfX` packages are an
unrelated object-mapping library. [`EXECUTED 2026-10-18` — `dotnet package
search ofx` and the nuget.org registration index.] Only OfxNet was run.

**So the dependency would buy a parser that is wrong in exactly the way this
platform cannot be,** and avoiding it costs about 150 lines. The rest of OfxNet
covers transactions and investment positions, which this slice does not read.

## 4. What step 2 inherits

- **One reader, two front ends.** An SGML tokenizer for 1.x and `XmlReader` for
  2.x, both building the same element tree, with one extraction over the tree.
- **The XML reader runs with `DtdProcessing.Prohibit` and `XmlResolver = null`.**
  A `DOCTYPE` refuses the whole file. Shown: all four files carrying a
  `DOCTYPE` throw in the minimal reader before any element is read.
- **Amounts are invariant-culture `.`-decimals or the line is refused.** No
  culture-dependent parse anywhere in the importer. The parser unit tests run
  one case under a comma-decimal culture so this cannot regress.
- **Every statement type the reader does not understand refuses the file**
  rather than being skipped. The spike's reader only *reports* `INVSTMTRS`,
  while step 2's all-or-nothing rule makes it fail the file. A file that names
  an account the slice cannot read has not been imported.
- **Both offset forms are accepted** (`[0:GMT]`, `[+0:GMT]`), and the date is
  taken as written.
- **1.x encodings:** `ENCODING:USASCII` with `CHARSET:1252` or `ISO-8859-1`, and
  `ENCODING:UTF-8`. Anything else refuses the file. Code page 1252 needs
  `CodePagesEncodingProvider`, which is in the box on .NET 10.
- **`AVAILBAL` is not read.** The ledger balance is the balance; available
  credit or available cash is a different figure.
- **Not settled here:** QFX (an OFX dialect with an extra header block), files
  with several statements for the same account, and whether a real
  institution's file looks like these. `D-041` §2 says invented OFX proves the
  format, not the bank, and that limit stands.

## Running it

```sh
dotnet run
```

The spike project references OfxNet only to compare against it, and is in no
solution. Nothing in `src/` references it.
