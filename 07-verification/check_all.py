#!/usr/bin/env python3
"""Run every check.

Verifying the repository should not depend on remembering three invocations.
This runs the claims check, the units check and the glossary check in turn and
reports each one's result.

It distinguishes a check that **could not run** — a missing input file, data it
cannot parse, exit code 2 — from a check that ran and **found violations** —
exit code 1. Collapsing the two is the failure mode this whole directory exists
to avoid: an unrunnable check must never be mistaken for a clean one.

Exit codes are the worst of the three: 2 if any check could not run, 1 if any
check found violations, 0 if all three are clean.

Stdlib only, no venv. Run: python3 07-verification/check_all.py
"""

from __future__ import annotations

import pathlib
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
CHECKS = ["check_claims.py", "check_units.py", "check_glossary.py"]

OK, VIOLATION, FATAL = 0, 1, 2
VERDICT = {OK: "OK", VIOLATION: "FAILED", FATAL: "COULD NOT RUN"}


def main(argv: list[str]) -> int:
    quiet = "--quiet" in argv or "-q" in argv
    worst = OK
    verdicts: list[tuple[str, int]] = []

    for name in CHECKS:
        print(f"── {name} " + "─" * max(0, 60 - len(name)))
        result = subprocess.run([sys.executable, str(HERE / name)],
                                capture_output=quiet, text=True)
        code = result.returncode
        if quiet and code != OK:
            sys.stdout.write(result.stdout or "")
            sys.stderr.write(result.stderr or "")
        verdicts.append((name, code))
        worst = max(worst, code)
        print()

    width = max(len(n) for n, _ in verdicts)
    print("─" * 62)
    for name, code in verdicts:
        print(f"{name:<{width}}  {VERDICT.get(code, f'exit {code}')}")

    if worst == FATAL:
        print("\nAt least one check could not run. That is not a clean tree.")
    elif worst == VIOLATION:
        print("\nAt least one check found violations.")
    else:
        print("\nAll checks clean.")
    return worst


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
