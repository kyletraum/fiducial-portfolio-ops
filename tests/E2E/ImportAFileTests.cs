using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// slice-02.md step 4, in a real browser against the whole app model: link an identifier, import
/// a file, see the counts and the chart; import it again and see nothing change. Files are built in
/// memory (FilePayload) - nothing is written to disk or committed - from invented values.
/// </summary>
public class ImportAFileTests(AppFixture fixture) : IClassFixture<AppFixture>
{
    // The page's own calendar, as the existing E2E reads it: the browser runs on this machine.
    static string Day(int daysAgo) => DateTime.Now.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    /// <summary>The MIME type Windows reports for a .csv. The page must set text/csv itself.</summary>
    static FilePayload Csv(string name, string body) =>
        new() { Name = name, MimeType = "application/vnd.ms-excel", Buffer = Encoding.UTF8.GetBytes(body) };

    [Fact]
    public async Task A_file_imported_in_the_browser_appears_in_the_chart_and_importing_it_again_changes_nothing()
    {
        var context = await fixture.NewSignedInContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();

        var add = page.GetByRole(AriaRole.Form, new() { Name = "Add an account" });
        await add.GetByLabel("Institution").FillAsync("Example Bank");
        await add.GetByLabel("Account name").FillAsync("Imported checking");
        await add.GetByLabel("Opened on").FillAsync(Day(30));
        await add.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await Assertions.Expect(add.GetByRole(AriaRole.Status)).ToHaveTextAsync("Added Imported checking.");

        // Link the identifier the file will use.
        var link = page.GetByRole(AriaRole.Form, new() { Name = "Link an import identifier" });
        await link.GetByLabel("File format").SelectOptionAsync("import:csv");
        await link.GetByLabel("Identifier in the file").FillAsync("EXAMPLE-E2E-1");
        await link.GetByRole(AriaRole.Button, new() { Name = "Link identifier" }).ClickAsync();
        await Assertions.Expect(link.GetByRole(AriaRole.Status)).ToHaveTextAsync("Linked EXAMPLE-E2E-1.");
        await Assertions.Expect(page.GetByRole(AriaRole.Table, new() { Name = "Each account and its newest balance" })
            .GetByText("EXAMPLE-E2E-1")).ToBeVisibleAsync();

        var import = page.GetByRole(AriaRole.Form, new() { Name = "Import a balance file" });
        var result = import.Locator(".import-result");
        var file = Csv("e2e-balances.csv", $"account,as_of_date,balance\nEXAMPLE-E2E-1,{Day(10)},1000.00\nEXAMPLE-E2E-1,{Day(0)},1250.50\n");

        // First import: two new balances, and the chart draws them.
        await import.GetByLabel("File").SetInputFilesAsync(file);
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(result).ToHaveTextAsync("Imported e2e-balances.csv: 2 new, 0 restated, 0 unchanged.");
        await Assertions.Expect(page.Locator("figure.chart")).ToBeVisibleAsync();

        var history = page.GetByRole(AriaRole.Table, new() { NameRegex = new System.Text.RegularExpressions.Regex("^Every import") });
        await Assertions.Expect(history.Locator("tbody tr")).ToHaveCountAsync(1);
        await Assertions.Expect(history.Locator("tbody tr").First).ToContainTextAsync("imported");

        // The same file again: a second run, nothing changed (FR-4.2).
        await import.GetByLabel("File").SetInputFilesAsync(file);
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(result).ToHaveTextAsync("Imported e2e-balances.csv: 0 new, 0 restated, 2 unchanged.");
        await Assertions.Expect(history.Locator("tbody tr")).ToHaveCountAsync(2);

        // A file with an identifier nobody linked: refused whole, and the page says where.
        await import.GetByLabel("File").SetInputFilesAsync(Csv("unlinked.csv", $"account,as_of_date,balance\nEXAMPLE-NOBODY,{Day(1)},5\n"));
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(result).ToContainTextAsync("Nothing was imported from unlinked.csv: 1 problem found.");
        await Assertions.Expect(result.Locator(".problems li")).ToContainTextAsync("line 2");
        await Assertions.Expect(history.Locator("tbody tr")).ToHaveCountAsync(3);

        // Over 1 MiB is refused by the PAGE, before anything is sent (Spike F: through the dev
        // proxy the API's 413 would arrive as 502). No run is recorded.
        await import.GetByLabel("File").SetInputFilesAsync(new FilePayload
        {
            Name = "huge.csv", MimeType = "text/csv", Buffer = new byte[1024 * 1024 + 1],
        });
        await Assertions.Expect(result).ToContainTextAsync("files up to 1 MiB are accepted");
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(history.Locator("tbody tr")).ToHaveCountAsync(3);

        // A file that is neither format, likewise refused by the page.
        await import.GetByLabel("File").SetInputFilesAsync(new FilePayload { Name = "notes.txt", MimeType = "text/plain", Buffer = "x"u8.ToArray() });
        await Assertions.Expect(result).ToContainTextAsync("notes.txt is not a .csv or .ofx file.");

        // DoD 2: an OFX file too, through the same form, bound under its own format. Built from an
        // ISO date so this source carries no eight-digit stamp (the hygiene scanner refuses those).
        await link.GetByLabel("File format").SelectOptionAsync("import:ofx");
        await link.GetByLabel("Identifier in the file").FillAsync("EXAMPLE-E2E-OFX");
        await link.GetByRole(AriaRole.Button, new() { Name = "Link identifier" }).ClickAsync();
        await Assertions.Expect(link.GetByRole(AriaRole.Status)).ToHaveTextAsync("Linked EXAMPLE-E2E-OFX.");
        var ofx = new FilePayload
        {
            Name = "statement.ofx",
            MimeType = "application/octet-stream", // what a browser reports for .ofx: the page sets application/x-ofx itself
            Buffer = Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<?OFX OFXHEADER=\"200\" VERSION=\"220\"?>\n"
                + "<OFX><BANKMSGSRSV1><STMTTRNRS><TRNUID>1</TRNUID><STATUS><CODE>0</CODE><SEVERITY>INFO</SEVERITY></STATUS>"
                + "<STMTRS><CURDEF>USD</CURDEF><BANKACCTFROM><BANKID>X</BANKID><ACCTID>EXAMPLE-E2E-OFX</ACCTID><ACCTTYPE>SAVINGS</ACCTTYPE></BANKACCTFROM>"
                + $"<LEDGERBAL><BALAMT>500.00</BALAMT><DTASOF>{Day(2).Replace("-", "")}120000[-5:CDT]</DTASOF></LEDGERBAL>"
                + "</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>\n"),
        };
        await import.GetByLabel("File").SetInputFilesAsync(ofx);
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(result).ToHaveTextAsync("Imported statement.ofx: 1 new, 0 restated, 0 unchanged.");
        await import.GetByLabel("File").SetInputFilesAsync(ofx);
        await import.GetByRole(AriaRole.Button, new() { Name = "Import file" }).ClickAsync();
        await Assertions.Expect(result).ToHaveTextAsync("Imported statement.ofx: 0 new, 0 restated, 1 unchanged.");

        // DoD 7: keyboard-reachable. Tab from the top of a fresh load reaches every new control.
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText("API: reachable")).ToBeVisibleAsync();
        var wanted = new Dictionary<string, bool>
        {
            ["Identifier in the file"] = false, ["Link identifier"] = false, ["File"] = false,
            ["Import file"] = false, ["import-history"] = false,
        };
        for (var i = 0; i < 80 && wanted.ContainsValue(false); i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var focused = await page.EvaluateAsync<string>("""
                () => { const e = document.activeElement; if (!e) return '';
                  if (e.getAttribute('aria-labelledby') === 'import-history-caption') return 'import-history';
                  return (e.labels && e.labels[0] && e.labels[0].textContent) || e.textContent || ''; }
                """);
            foreach (var key in wanted.Keys.ToList())
                if (focused.Trim() == key) wanted[key] = true;
        }
        Assert.True(!wanted.ContainsValue(false), "not reached by Tab: " + string.Join(", ", wanted.Where(w => !w.Value).Select(w => w.Key)));

        // The accessibility floor over the page as it now stands, with a refusal open.
        await history.Locator("details summary").First.ClickAsync();
        var axe = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        Assert.True(axe.Violations.Length == 0,
            string.Join("\n", axe.Violations.Select(v => $"{v.Id} ({v.Impact}): {v.Nodes.Length} node(s), e.g. {v.Nodes[0].Target}")));
    }
}
