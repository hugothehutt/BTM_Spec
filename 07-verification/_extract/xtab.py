#!/usr/bin/env python3
"""Cross-tabulate the vague entries by blast radius and class."""
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
        cur[k.strip()] = v.strip()

vg = [e for e in entries if e.get("vague") == "true"]
emp = [e for e in entries if e["class"] == "empirical"]
g = collections.Counter((e["blast_radius"], e["class"]) for e in vg)
print(f"{'radius':18}{'analytic':>10}{'empirical':>11}{'regulatory':>12}{'total':>7}")
for r in ["very-expensive", "expensive-later", "moderate", "cheap"]:
    row = [g[(r, c)] for c in ["analytic", "empirical", "regulatory"]]
    print(f"{r:18}" + "".join(f"{x:>10}" if i == 0 else f"{x:>11}" if i == 1
                              else f"{x:>12}" for i, x in enumerate(row))
          + f"{sum(row):>7}")
print(f"\nempirical claims total {len(emp)}, of which vague "
      f"{sum(1 for e in emp if e.get('vague') == 'true')} "
      f"({100 * sum(1 for e in emp if e.get('vague') == 'true') / len(emp):.0f}%)")
