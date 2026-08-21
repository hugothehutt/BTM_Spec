#!/usr/bin/env python3
"""Emit the vague-claim list, grouped by blast radius then owner."""
import collections
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
entries, cur = [], None
for line in (ROOT / "claims.yaml").read_text().splitlines():
    if line.startswith("- id:"):
        cur = {"id": line.split(":", 1)[1].strip()}
        entries.append(cur)
    elif cur is not None and line.startswith("  ") and ":" in line:
        k, _, v = line.strip().partition(":")
        cur[k.strip()] = v.strip().strip('"')

ORDER = ["very-expensive", "expensive-later", "moderate", "cheap"]
vague = [e for e in entries if e.get("vague") == "true"]
by = collections.defaultdict(list)
for e in vague:
    by[e["blast_radius"]].append(e)

out = ["# Vague claims — the register entries that are not testable as written",
       "",
       f"{len(vague)} of {len(entries)} claims carry `vague: true`. The statement is",
       "transcribed as the specification asserts it; the flag records that no test could",
       "be written against it without first supplying a threshold, a metric, a bound or",
       "a definition. This list is a finding about the specification, not about the",
       "register.",
       ""]
for r in ORDER:
    if not by[r]:
        continue
    out += [f"## {r} ({len(by[r])})", ""]
    for e in sorted(by[r], key=lambda x: x["owner"]):
        out.append(f"- **{e['id']}** — {e['owner']} — {e['class']}")
        out.append(f"  > {e['statement']}")
    out.append("")
(ROOT / "VAGUE.md").write_text("\n".join(out) + "\n")
print(f"07-verification/VAGUE.md: {len(vague)} entries")
