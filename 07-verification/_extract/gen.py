#!/usr/bin/env python3
"""Assemble 07-verification/claims.yaml from the per-corpus TSV fragments.

TSV columns, tab-separated:
    statement | owner | class | blast_radius | br_source | anchors | vague

`anchors` is semicolon-separated or empty. `vague` is `y` or empty. Ids are
assigned sequentially in fragment order, so fragment order fixes the register
order. Lines starting with `#` are comments; blank lines are skipped.
"""

from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parent / "claims.yaml"

# Fragment order = register order = specification reading order. Fragments are
# every `*.tsv` here, sorted by filename, so batch files are named to sort.
ORDER = sorted(p.name for p in HERE.glob("*.tsv"))

HEADER = """# claims.yaml — the claim register
#
# One entry per claim the specification asserts. Every evidence label attaches
# here. Built by ticket #5 (Extract the claim register); checked by
# `07-verification/check_claims.py`.
#
# Statements are transcribed, not improved. A claim too vague to be testable as
# written carries `vague: true`; that flag is a finding about the specification,
# not a defect in the entry.
#
# A claim is registered iff it constrains the engine, its mathematics, its data,
# or the regulation. Prose about how to read the documents is not a claim.
#
# class         analytic   — true by derivation, definition or construction
#               empirical  — truth depends on data or runtime behaviour
#               regulatory — transcribed from law or market rules; given
# blast_radius  cost of retrofitting if the claim turns out false
# br_source     the ADR whose reversibility was copied, or `argued`
# evidence      unlabelled | derived | prototyped | empirical-pending
#               | refuted | assumed-declared   (labelling is not this ticket's job)
# tn            the technical note that earned the evidence label
# band          acceptance band; required once an empirical claim is labelled
#
# GENERATED FILE — edit `_extract/*.tsv` and re-run `_extract/gen.py`.
"""


def quote(s: str) -> str:
    if s != s.strip():
        s = s.strip()
    needs = (":" in s or s[:1] in "-?*&!|>%@`[{\"'#" or "  #" in s)
    if needs:
        return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'
    return s


def main() -> int:
    lines = [HEADER]
    n = 0
    for name in ORDER:
        path = HERE / name
        if not path.exists():
            print(f"warning: {name} missing, skipped", file=sys.stderr)
            continue
        lines.append(f"\n# ---- {name} ----")
        for raw in path.read_text().splitlines():
            if not raw.strip() or raw.lstrip().startswith("#"):
                if raw.lstrip().startswith("#"):
                    lines.append(raw.rstrip())
                continue
            cols = raw.split("\t")
            if len(cols) < 5:
                print(f"FATAL {name}: {len(cols)} columns: {raw[:80]!r}",
                      file=sys.stderr)
                return 2
            cols += [""] * (7 - len(cols))
            stmt, owner, cls, radius, br, anchors, vague = [c.strip() for c in cols[:7]]
            n += 1
            anchor_list = [a.strip() for a in anchors.split(";") if a.strip()]
            lines.append(f"- id: CLM-{n:04d}")
            lines.append(f"  statement: {quote(stmt)}")
            lines.append(f"  owner: {owner}")
            lines.append(f"  class: {cls}")
            lines.append(f"  blast_radius: {radius}")
            lines.append(f"  br_source: {br}")
            lines.append(f"  anchors: [{', '.join(anchor_list)}]")
            lines.append("  evidence: unlabelled")
            lines.append("  tn: null")
            lines.append("  band: null")
            if vague == "y":
                lines.append("  vague: true")
    OUT.write_text("\n".join(lines) + "\n")
    print(f"{OUT.relative_to(OUT.parent.parent)}: {n} claims")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
