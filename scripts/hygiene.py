#!/usr/bin/env python3
"""Constitution IX, mechanically: no personal data enters this repository.

Runs in two places, deliberately:

  * as a pre-commit hook  -- where it is PREVENTION
  * as a CI job on every push, including branches -- where it is DETECTION

Both matter, and they are not interchangeable. GitHub's own documentation is
explicit that once a commit is pushed to a public repository, rewriting history
does not remove it: the objects stay reachable "directly via their SHA-1 hashes
in cached views", "in any clones or forks", and "through any pull requests that
reference them". Detection after a push is damage assessment, not recovery.
Secret-scanning push protection does not close this either -- it matches provider
CREDENTIAL patterns, not balances and not institution names, which is what
Constitution IX is actually about.

False positives are expected and acceptable. The cost of one is a minute and an
allow-list entry with a comment. The cost of one miss on a public repository is
permanent. The allow-list is reviewed, never wildcarded.

Usage:
    hygiene.py --staged         # pre-commit: scan the staged diff
    hygiene.py --range A..B     # CI: scan every commit in a range, each at its own content
    hygiene.py --all            # scan every tracked file (pre-flight audit)
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys

# --- what we refuse to let through -----------------------------------------
#
# NOTE ON THE INSTITUTION LIST: it is deliberately a BROAD list of common US
# financial institutions, not a list of the ones this repository's author uses.
# A denylist naming exactly the author's banks would itself disclose which banks
# the author uses -- the precise class of leak Constitution IX forbids. Add to
# it generously; never prune it toward a specific person's accounts.

RULES: list[tuple[str, str, str]] = [
    (
        "account-number",
        r"\b\d{8,17}\b(?![.\-]\d)",
        "looks like an account number (8-17 consecutive digits)",
    ),
    (
        "routing-number",
        r"\b(?:0[0-9]|1[0-2]|2[1-9]|3[0-2]|6[1-9]|7[0-2]|80)\d{7}\b",
        "matches the ABA routing-number shape",
    ),
    (
        "card-number",
        r"\b(?:4\d{3}|5[1-5]\d{2}|3[47]\d{2}|6011)[ \-]?\d{4}[ \-]?\d{4}[ \-]?\d{4}\b",
        "matches a payment-card shape",
    ),
    (
        "ssn",
        r"\b\d{3}-\d{2}-\d{4}\b",
        "matches a US SSN shape",
    ),
    (
        "private-key",
        r"-----BEGIN (?:RSA |EC |OPENSSH |PGP )?PRIVATE KEY-----",
        "is a private key header",
    ),
    (
        "jwt",
        r"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}",
        "has the structure of a JWT",
    ),
    (
        "api-key-prefix",
        r"\b(?:sk-[A-Za-z0-9]{16,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}"
        r"|xox[baprs]-[A-Za-z0-9-]{10,}|AKIA[0-9A-Z]{16}|access-(?:sandbox|development|production)-[0-9a-f-]{20,})",
        "matches a known credential prefix",
    ),
    (
        "connection-string-password",
        r"(?i)(?:password|pwd)\s*=\s*(?!\s*[;\"']?\s*$)(?!\{)(?!\$)[^\s;\"']{3,}",
        "is a connection string carrying a password",
    ),
    (
        "institution-name",
        # Unambiguous names, plus AMBIGUOUS ones that require a qualifier.
        # "fidelity", "discover", "chase", "ally", "regions", "citizens", "marcus"
        # and "empower" are ordinary English words before they are banks, and a
        # rule that fires on the English word is a rule people learn to ignore.
        r"(?i)\b(?:"
        r"wells\s*fargo|bank\s*of\s*america|jpmorgan|citibank|citigroup|capital\s*one|"
        r"us\s*bank|pnc\s*bank|truist|td\s*bank|american\s*express|vanguard|"
        r"charles\s*schwab|schwab|e\*?trade|merrill(?:\s*lynch)?|morgan\s*stanley|"
        r"robinhood|betterment|wealthfront|sofi|navy\s*federal|usaa|synchrony|"
        r"barclays|hsbc|santander|fifth\s*third|keybank|huntington\s*ban(?:k|corp)|"
        r"m&t\s*bank|first\s*republic|forbright|personal\s*capital|amex|"
        # aggregators and data providers: legitimate to NAME in a spec as an
        # integration target (advisory in prose), never legitimate in a fixture
        r"plaid|monarch\s*money|yodlee|mx\.com|finicity|teller\.io|"
        # ambiguous -- qualifier required
        r"fidelity\s*(?:investments|brokerage|nb)|discover\s*(?:bank|card)|"
        r"chase\s*(?:bank|sapphire)|ally\s*(?:bank|financial|invest)|"
        r"regions\s*bank|citizens\s*bank|marcus\s*by\s*goldman|empower\s*retirement"
        r")\b",
        "names a real financial institution (generated data invents names)",
    ),
]

# The institution rule is an ERROR in files that carry DATA, and a WARNING in
# prose. A specification naming its integration targets -- "pull from an MCP
# server, like Monarch", "an API source, like Plaid" -- is the legitimate case
# and is not the thing Constitution IX protects against; naming an institution
# in a seed, a fixture or a test snapshot is. Suppressing the rule entirely in
# prose would be a wildcard; demoting it to a warning keeps the signal visible
# while letting a spec say what it integrates with.
PROSE_SUFFIXES = (".md", ".markdown", ".txt", ".rst", ".adoc")
PROSE_ADVISORY_RULES = {"institution-name"}


def _is_plausible_account_number(match: str) -> bool:
    """Reject numeric literals that happen to be long.

    A real account number carries entropy. `1000000000` in
    `generate_series(1,1000000000)`, `100000000` as a row cap, and a run of
    repeated digits in a placeholder do not -- two or fewer distinct digits
    means it is a round literal, not an identifier. Suppressing these is what
    keeps the rule credible; a control that fires on every SQL example is a
    control people learn to pass with --no-verify.
    """
    digits = "".join(ch for ch in match if ch.isdigit())
    return len(set(digits)) > 2


# Per-rule second opinion, applied only when the regex already matched.
VALIDATORS = {"account-number": _is_plausible_account_number}

# Paths that must never be committed at all.
FORBIDDEN_PATHS = [
    (re.compile(r"(^|/)\.env($|\.)(?!example)"), "an environment file"),
    (re.compile(r"\.(?:zip|tar\.gz|tgz|7z|rar)$"), "an archive (the scan cannot see inside one)"),
    (re.compile(r"\.(?:dump|bak)$|\.sql\.gz$"), "a database dump"),
    (re.compile(r"(^|/)(?:test-results|playwright-report|blob-report)/"), "a test artifact directory"),
    (re.compile(r"(^|/)appsettings\..*\.local\.json$"), "a local settings file"),
    (re.compile(r"(^|/)secrets\.json$"), "a secrets file"),
]

# Files exempt from CONTENT scanning. Each entry carries a reason. Never a wildcard.
ALLOWLIST: list[tuple[str, str]] = [
    ("scripts/hygiene.py", "this file: it necessarily contains the patterns it searches for"),
    (".gitignore", "names the forbidden paths on purpose"),
    (".github/workflows/hygiene.yml", "invokes this scanner"),
]

# Contexts where a long digit run is structural, not a balance.
BENIGN_LINE = re.compile(
    r"(?i)numeric\(\s*\d+\s*,\s*\d+\s*\)"      # numeric(19,4)
    r"|\bsha(?:256|512)?\b|integrity\s*=|[0-9a-f]{40,}"  # hashes, lockfile integrity
    r"|\bversion\b|\d+\.\d+\.\d+"              # version strings
    r"|\buuid\b|[0-9a-f]{8}-[0-9a-f]{4}-"      # uuids
    r"|\bport\b|\b\d{4}-\d{2}-\d{2}\b"         # ports, ISO dates
)


def _git(*args: str) -> str:
    # Explicit UTF-8, replacing undecodable bytes. With text=True alone the locale
    # codec is used - cp1252 on Windows - and a byte it cannot map kills the reader
    # thread, .stdout comes back None, and the file is skipped as if binary. That
    # skipped every file with an em dash in it. Binary files are still skipped, by
    # the NUL check in scan().
    return subprocess.run(
        ["git", *args], capture_output=True, text=True, check=True,
        encoding="utf-8", errors="replace",
    ).stdout


def _lines(out: str) -> list[str]:
    return [p for p in out.splitlines() if p.strip()]


def targets(mode: str, rev_range: str | None) -> list[tuple[str, str, str]]:
    """(label, path, object) for every file version to scan. `object` is what
    `git show` reads - ':path' for the index, 'sha:path' for a commit - or '' for
    the working tree.

    RANGE mode scans EVERY commit in the range, each at its own content - not the
    net diff of the range read from the working tree, which is what it did until
    2026-10-18. A value committed and then removed by a later commit is invisible
    to a net diff, yet that commit stays reachable by SHA and through the PR (the
    docstring above): exactly the leak this control exists for. A merge commit is
    scanned against its FIRST parent - whatever it brought in, plus any conflict
    resolution.
    """
    if mode == "all":
        return [(p, p, "") for p in _lines(_git("ls-files"))]
    if mode == "staged":
        return [(p, p, f":{p}") for p in _lines(_git("diff", "--cached", "--name-only", "--diff-filter=ACMR"))]

    found: list[tuple[str, str, str]] = []
    for sha in _lines(_git("rev-list", "--reverse", rev_range or "HEAD~1..HEAD")):
        parents = _git("rev-list", "--parents", "-n", "1", sha).split()[1:]
        base = [parents[0]] if parents else ["--root"]
        changed = _git("diff-tree", "-r", "--name-only", "--diff-filter=ACMR", "--no-commit-id", *base, sha)
        found += [(f"{sha[:10]} {p}", p, f"{sha}:{p}") for p in _lines(changed)]
    return found


def content_of(path: str, obj: str) -> str | None:
    try:
        if obj:
            return _git("show", obj)
        return open(path, encoding="utf-8", errors="replace").read()
    except (subprocess.CalledProcessError, OSError):
        return None


def scan(mode: str, rev_range: str | None) -> int:
    findings: list[str] = []
    warnings: list[str] = []
    allowed = {p for p, _ in ALLOWLIST}

    commits: set[str] = set()
    for label, path, obj in targets(mode, rev_range):
        if mode == "range":
            commits.add(obj.split(":", 1)[0])
        for pattern, why in FORBIDDEN_PATHS:
            if pattern.search(path):
                findings.append(f"{label}: refused - {why}")

        if path in allowed:
            continue
        text = content_of(path, obj)
        if text is None or "\0" in text[:2048]:
            continue

        for lineno, line in enumerate(text.splitlines(), 1):
            if len(line) > 2000 or BENIGN_LINE.search(line):
                continue
            for name, pattern, why in RULES:
                m = re.search(pattern, line)
                if m:
                    validator = VALIDATORS.get(name)
                    if validator and not validator(m.group(0)):
                        continue
                    snippet = line.strip()[:90]
                    entry = f"{label}:{lineno}: [{name}] {why}\n      {snippet}"
                    if name in PROSE_ADVISORY_RULES and path.lower().endswith(PROSE_SUFFIXES):
                        warnings.append(entry)
                    else:
                        findings.append(entry)
                    break

    if warnings:
        print("hygiene: advisory (prose naming an institution - not blocking):", file=sys.stderr)
        for w in warnings:
            print(f"  {w}", file=sys.stderr)
        print("", file=sys.stderr)

    # Says how many commits a range scan read, so "clean" over an empty range is visible as such.
    scope = f", {len(commits)} commit(s) each at its own content" if mode == "range" else ""
    if not findings:
        print(f"hygiene: clean{scope} ({len(warnings)} advisory)" if warnings else f"hygiene: clean{scope}")
        return 0

    print("\nCONSTITUTION IX: refusing this change.\n", file=sys.stderr)
    for f in findings:
        print(f"  {f}", file=sys.stderr)
    print(
        "\nThis repository is public. A real balance, account number or institution"
        "\nname committed here cannot be removed by rewriting history - the objects"
        "\nstay reachable by SHA, in forks, and through any PR that references them."
        "\n\nIf this is a false positive, add the path to ALLOWLIST in scripts/hygiene.py"
        "\nwith a reason. Never wildcard it."
        "\n\nA finding that names a commit is IN THAT COMMIT, even when a later commit"
        "\nremoved it, so fixing the file does not clear it. Squash the branch and"
        "\nforce-push to keep the commit out of main - and if the value is real, treat"
        "\nit as already published.\n",
        file=sys.stderr,
    )
    return 1


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--staged", action="store_true", help="scan the staged diff (pre-commit)")
    g.add_argument("--range", dest="rev_range", help="scan every commit in a range, e.g. main..HEAD")
    g.add_argument("--all", action="store_true", help="scan every tracked file")
    a = ap.parse_args()
    mode = "staged" if a.staged else "all" if a.all else "range"
    try:
        return scan(mode, a.rev_range)
    except subprocess.CalledProcessError as e:
        # Fail CLOSED: a range git cannot read is not a clean range.
        print(f"hygiene: git failed ({' '.join(e.cmd[1:3])}): {e.stderr.strip()}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
