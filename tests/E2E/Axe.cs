using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace Portfolio.E2E;

/// <summary>
/// axe at WCAG 2.2 AA, as slice 01's one call ran it. slice-03.md DoD 6 runs it on every view the
/// slice adds, in both colour schemes. axe cannot see focus, live regions or timing (C3-20): the
/// tests assert those themselves.
/// </summary>
public static class Axe
{
    public static async Task AssertCleanAsync(IPage page, string what)
    {
        var result = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"] },
        });
        Assert.True(result.Violations.Length == 0, $"{what}: " +
            string.Join("\n", result.Violations.Select(v => $"{v.Id} ({v.Impact}): {v.Nodes.Length} node(s), e.g. {v.Nodes[0].Target}")));
    }
}
