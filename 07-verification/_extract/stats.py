#!/usr/bin/env python3
"""Answer-record statistics over the generated register."""
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

ORDER = ["very-expensive", "expensive-later", "moderate", "cheap"]
CLASSES = ["analytic", "empirical", "regulatory"]

print(f"total {len(entries)}")
print("\n-- by class --")
for k, v in collections.Counter(e["class"] for e in entries).most_common():
    print(f"{k:12} {v:5}")

print("\n-- by blast_radius --")
c = collections.Counter(e["blast_radius"] for e in entries)
for k in ORDER:
    print(f"{k:18} {c[k]:5}")

print("\n-- class x radius --")
grid = collections.Counter((e["blast_radius"], e["class"]) for e in entries)
print(f"{'':18}" + "".join(f"{x:>12}" for x in CLASSES))
for r in ORDER:
    print(f"{r:18}" + "".join(f"{grid[(r, cl)]:>12}" for cl in CLASSES))

print("\n-- vague --")
vg = [e for e in entries if e.get("vague") == "true"]
print(f"vague total {len(vg)}")
for k in ORDER:
    print(f"  {k:18} {sum(1 for e in vg if e['blast_radius'] == k):4}")
print("  by class: " + ", ".join(
    f"{cl} {sum(1 for e in vg if e['class'] == cl)}" for cl in CLASSES))

print("\n-- br_source --")
c2 = collections.Counter(e["br_source"] for e in entries)
print(f"argued {c2['argued']}   copied from an ADR {len(entries) - c2['argued']}")
print("\n-- claims per owning ADR (top 12) --")
for k, v in c2.most_common(13):
    if k != "argued":
        print(f"  {k} {v}")

print("\n-- claims per document --")
per = collections.Counter(e["owner"].split(" §")[0] for e in entries)
for k, v in sorted(per.items()):
    print(f"  {v:5}  {k}")
