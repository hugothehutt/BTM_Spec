#!/usr/bin/env python3
"""Rank the specification's areas by very-expensive claim mass and vagueness."""
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

ve = [e for e in entries if e["blast_radius"] == "very-expensive"]
print(f"very-expensive total {len(ve)}\n")
print(f"{'br_source':12} {'VE':>5} {'vague':>6} {'empirical':>10}")
rows = []
for src in {e["br_source"] for e in ve}:
    g = [e for e in ve if e["br_source"] == src]
    rows.append((len(g), src,
                 sum(1 for e in g if e.get("vague") == "true"),
                 sum(1 for e in g if e["class"] == "empirical")))
for n, src, v, em in sorted(rows, reverse=True):
    print(f"{src:12} {n:5} {v:6} {em:10}")
