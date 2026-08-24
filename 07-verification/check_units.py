#!/usr/bin/env python3
"""INV-G-02 — the name/unit-table lint.

`00-overview/02-conventions.md` §5.2 moved unit and frame safety out of the type
system and into the identifier. That is only worth anything if a machine checks
it, so this is the machine.

Three checks, all read off the contract field tables themselves:

  (a) SUFFIX  a dimensioned field's identifier ends in its unit, and that suffix
              equals the Unit column of its own row
  (b) FRAME   a power or energy field carries a frame prefix — pBatt/soc
              (battery), pPoi (POI), or a declared market-product name — so that
              no MW or MWh quantity is frame-ambiguous
  (c) RANGE   a dimensionless field carries `—` in the Unit column plus a range
              (§2.3); a dimensionless field with no range is a violation, not a
              field awaiting documentation

Stdlib only, no venv. Run: python3 07-verification/check_units.py
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CORPUS = ["03-contracts"]

# Unit string -> the suffix the identifier must end in (conventions §2, §5.2).
SUFFIX = {
    "MW": "Mw",
    "MWh": "Mwh",
    "EUR/MWh": "EurPerMwh",
    "EUR/MW": "EurPerMw",
    "EUR/MW/h": "EurPerMwH",
    "EUR": "Eur",
}

# Units that are neither dimensioned in the sense above nor dimensionless: they
# are counts, instants and durations, which carry no suffix rule.
EXEMPT_UNITS = {"—", "-", "", "slots", "slot", "h", "hours", "int", "UTC",
                "µs", "us", "ms", "bytes", "count", "EUR/t", "%"}

# The generic term shapes of C2 are polymorphic in their variable: a LinearTerm's
# coefficient is EUR per whatever the variable is. These units are declared, not
# missing, and carry no suffix rule.
POLY_UNITS = {"variable unit", "target unit", "EUR per variable unit",
              "EUR per unit violation", "EUR/MWh, MWh"}

# Frame tokens for power and energy (§5.2). Matched case-insensitively anywhere
# in the identifier, so `vSocBreakpointsXMwh` reads as battery frame.
FRAMES = {
    "battery": ("pbatt", "soc"),
    "poi": ("ppoi",),
    # The site frame: load and PV are site-level quantities that feed the bridge
    # and belong to neither the battery nor the POI frame. `pvAvail` (belief) and
    # `pvOut` (post-decision) are distinguished by name, per §5.2.
    "site": ("load", "pv", "aux"),
    # Market-frame quantities are named for their product rather than for a
    # frame. Membership here is the declaration §5.2 asks for; adding a token is
    # a decision, not a convenience.
    "market": ("volume", "capacity", "awarded", "activated", "delivery",
               "residual", "imbalance", "reserve", "bid", "rup", "rdn",
               "curve", "lot", "signed", "obligation", "shortfall"),
    # The delineation frame. The seven accumulators and the four derived
    # registers are sign-defined by the MiSpel register arithmetic — always a
    # non-negative accumulation — and belong to neither the battery nor the POI
    # frame. The German names are proper nouns from the register; naming them
    # here is the declaration.
    "delineation": ("mtd", "saldierungsfaehig", "foerderfaehig",
                    "umlagebelastet", "fremdtank"),
}
FRAME_TOKENS = tuple(t for group in FRAMES.values() for t in group)

FIELD_TOKEN = re.compile(r"`([A-Za-z][A-Za-z0-9_]*)`")
NUMERIC_TYPE = re.compile(r"^`double(\[[^\]]*\])?`$")

# A declared range, in a Range column or stated in the Notes where a table has
# no Range column. §2.3 wants the range declared, not the column.
RANGE = re.compile(r"[\[\(]\s*-?[\d.]+\s*,\s*-?[\d.]+\s*[\]\)]"
                   r"|[≥≤<>]\s*-?[\d.]+"
                   r"|\d+\s*\.\.\s*\d+"
                   r"|non-negative|unit interval")


def split_row(line: str) -> list[str]:
    return [c.strip() for c in line.strip().strip("|").split("|")]


def tables(text: str):
    """Yield (header, rows) for every markdown table carrying Field and Unit."""
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        if lines[i].lstrip().startswith("|") and i + 1 < len(lines) \
                and set(lines[i + 1].replace("|", "").replace(" ", "")) <= {"-", ":"} \
                and lines[i + 1].lstrip().startswith("|"):
            header = split_row(lines[i])
            rows, j = [], i + 2
            while j < len(lines) and lines[j].lstrip().startswith("|"):
                rows.append((j + 1, split_row(lines[j])))
                j += 1
            if "Field" in header and "Unit" in header:
                yield header, rows
            i = j
        else:
            i += 1


def main() -> int:
    errors: list[str] = []
    checked = elided = 0

    for folder in CORPUS:
        for path in sorted((ROOT / folder).glob("*.md")):
            rel = path.relative_to(ROOT).as_posix()
            text = path.read_text()
            for header, rows in tables(text):
                fi = header.index("Field")
                ui = header.index("Unit")
                ri = header.index("Range") if "Range" in header else None
                ti = header.index("Type") if "Type" in header else None
                for lineno, row in rows:
                    if len(row) <= max(fi, ui):
                        continue
                    # Only numeric fields are governed. An enum, a bool, an id or
                    # a SlotId carries no unit and no range.
                    if ti is None or len(row) <= ti \
                            or not NUMERIC_TYPE.match(row[ti]):
                        continue
                    unit = row[ui]
                    names = FIELD_TOKEN.findall(row[fi])
                    if "…" in row[fi] or "..." in row[fi]:
                        elided += 1
                    if not names:
                        continue
                    rng = row[ri] if ri is not None and len(row) > ri else ""

                    if unit in POLY_UNITS:
                        continue
                    if unit in SUFFIX:
                        want = SUFFIX[unit]
                        for n in names:
                            checked += 1
                            if not n.endswith(want):
                                errors.append(
                                    f"{rel}:{lineno}: SUFFIX `{n}` declares unit "
                                    f"{unit} so it must end in `{want}`")
                            if unit in ("MW", "MWh") \
                                    and not any(t in n.lower()
                                                for t in FRAME_TOKENS):
                                errors.append(
                                    f"{rel}:{lineno}: FRAME `{n}` is a power or "
                                    f"energy field naming no frame")
                    elif unit in EXEMPT_UNITS:
                        if unit in ("—", "-"):
                            # The range may live in the Range column, or — where
                            # the table has none — anywhere else on the row.
                            declared = RANGE.search(rng) if ri is not None \
                                else RANGE.search(" ".join(row))
                            if not declared:
                                for n in names:
                                    errors.append(
                                        f"{rel}:{lineno}: RANGE `{n}` is "
                                        f"dimensionless and declares no range "
                                        f"(conventions §2.3)")
                    else:
                        for n in names:
                            errors.append(
                                f"{rel}:{lineno}: UNIT `{n}` declares unit "
                                f"{unit!r}, which conventions §2 does not define")

    for e in errors:
        print(f"FAIL {e}")
    print(f"\n{checked} dimensioned fields checked, "
          f"{elided} rows carry an elided name, {len(errors)} failures")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
