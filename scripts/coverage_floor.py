#!/usr/bin/env python3
"""Fail if one package's line coverage in a Cobertura report is below a floor.

slice-01 step 5: coverage floors apply to Domain ONLY - "a floor measured in a job that
cannot execute the code is decoration" (S-11). The floor sits just under the measured
figure, so removing tested logic fails, and it only ever moves up.

    python scripts/coverage_floor.py <cobertura.xml> <package> <minimum-percent>
"""
import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) != 4:
        print(__doc__.strip().splitlines()[-1].strip(), file=sys.stderr)
        return 2
    report, package, floor = sys.argv[1], sys.argv[2], float(sys.argv[3])

    matches = [p for p in ET.parse(report).getroot().iter("package") if p.get("name") == package]
    if not matches:
        print(f"coverage: package '{package}' is not in {report} - nothing was measured", file=sys.stderr)
        return 1

    rate = float(matches[0].get("line-rate", "0")) * 100
    verdict = "ok" if rate >= floor else "BELOW FLOOR"
    print(f"coverage: {package} lines {rate:.1f}% (floor {floor:.0f}%) - {verdict}")
    return 0 if rate >= floor else 1


if __name__ == "__main__":
    sys.exit(main())
