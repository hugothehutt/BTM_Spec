#!/usr/bin/env python3
"""INV-G-02 — the name/unit-table lint.

`00-overview/02-conventions.md` §5.2 moved unit and frame safety out of the type
system and into the identifier. That is only worth anything if a machine checks
it, so this is the machine.

Three checks read off the contract field tables themselves:

  (a) SUFFIX  a dimensioned field's identifier ends in its unit, and that suffix
              equals the Unit column of its own row
  (b) FRAME   a power or energy field carries a frame token — pBatt/soc
              (battery), pPoi (POI), a market product's own name, load/pv/aux
              (site), or mtd and the MiSpel register names (delineation) — so
              that no MW or MWh quantity is frame-ambiguous
  (c) RANGE   a dimensionless field carries `—` in the Unit column plus a range
              (§2.3); a dimensionless field with no range is a violation, not a
              field awaiting documentation

INV-G-02 is a seam invariant, so (a)-(c) are scoped to `03-contracts`. A stale
unit in a layer design or a test fixture is just as wrong, and nothing was
looking for it, so a fourth check runs repo-wide:

  (d) FOREIGN no kW, no kWh and no factor of 1000 outside
              `allowed-foreign-units.yaml`, which holds the regulatory
              primitives and the sentences whose job is to say kW does not
              exist. Adding a line there is a decision someone writes down.

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
# are counts, instants and durations, which carry no suffix rule. A BLANK unit
# cell is deliberately absent: it is a field awaiting documentation, which §2.3
# calls a violation rather than an exemption.
EXEMPT_UNITS = {"—", "-", "slots", "slot", "h", "hours", "int", "UTC",
                "µs", "us", "ms", "bytes", "count", "EUR/t", "%"}

# The generic term shapes of ADR-008 are polymorphic in their variable: a
# LinearTerm's coefficient is EUR per whatever `variable` names, and there are
# thirteen VarSymbols, so the shape cannot be split per unit. §5.2 allows this
# exactly when the unit is fixed by a sibling VarSymbol field in the same record
# — which is checked below, not assumed: a table using a polymorphic unit must
# itself declare the field that resolves it.
POLY_UNITS = {"variable unit", "target unit", "EUR per variable unit",
              "EUR per unit violation"}
POLY_RESOLVERS = ("variable", "target", "epigraphvar", "dominates")

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
                    "umlagebelasteter", "fremdtank"),
}
FRAME_TOKENS = tuple(t for group in FRAMES.values() for t in group)


def names_a_frame(identifier: str) -> bool:
    """True when a frame token appears as a camelCase WORD of the identifier.

    Substring matching is not good enough: "lot" is inside "slot", "rup" is
    inside "socCorridorUpper", and "pv" is inside any identifier that happens to
    contain those letters. Splitting on camelCase boundaries first means a token
    has to be a word the author actually wrote.
    """
    words = re.findall(r"[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])", identifier)
    lowered = [w.lower() for w in words]
    if any(w in FRAME_TOKENS for w in lowered):
        return True
    # `pBattMw` and `pPoiMw` split as ['p','Batt','Mw'] / ['p','Poi','Mw'], so
    # check the leading pair as one token too.
    if len(lowered) >= 2 and lowered[0] + lowered[1] in FRAME_TOKENS:
        return True
    return False

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


FOREIGN = re.compile(r"\bkW\b|\bkWh\b|\bKwh\b|/ ?1000\b|\* ?1e-3\b|\b0\.001\b",
                     re.IGNORECASE)
ALLOWLIST = ROOT / "07-verification" / "allowed-foreign-units.yaml"


def load_allowlist() -> tuple[set[str], dict[str, list[str]]]:
    """A deliberately small reader for the allowlist's fixed shape."""
    whole: set[str] = set()
    subs: dict[str, list[str]] = {}
    current = None
    in_allow = False
    for raw in ALLOWLIST.read_text().split("\n"):
        if not raw.strip() or raw.lstrip().startswith("#"):
            continue
        if not raw.startswith(" ") and raw.rstrip().endswith(":"):
            current = raw.rstrip()[:-1].strip()
            subs.setdefault(current, [])
            in_allow = False
        elif current and raw.strip() == "allow:":
            in_allow = True
        elif current and raw.strip().startswith("allow_whole_file: true"):
            whole.add(current)
        elif current and in_allow and raw.strip().startswith("- "):
            item = raw.strip()[2:].strip()
            if len(item) >= 2 and item[0] == item[-1] and item[0] in "\"'":
                item = item[1:-1]
            subs[current].append(item)
        elif raw.strip().endswith(">") or raw.startswith("    "):
            continue
    return whole, subs


def foreign_units() -> list[str]:
    """Repo-wide: no kW, no kWh, no factor of 1000, outside the allowlist.

    INV-G-02 is scoped to the contract field tables, which is correct — it is a
    seam invariant. But a stale unit in a layer design or a test fixture is just
    as wrong and nothing was looking for it. This pass is that check.
    """
    whole, subs = load_allowlist()
    errors: list[str] = []
    for pattern in ("**/*.md", "**/*.cs"):
        for path in sorted(ROOT.glob(pattern)):
            rel = path.relative_to(ROOT).as_posix()
            if rel.startswith((".claude/", "docs/")) or rel in whole:
                continue
            allowed = subs.get(rel, [])
            for i, line in enumerate(path.read_text().split("\n"), 1):
                if not FOREIGN.search(line):
                    continue
                if any(a in line for a in allowed):
                    continue
                errors.append(
                    f"{rel}:{i}: FOREIGN a stale unit or a factor of 1000 — "
                    f"{line.strip()[:80]!r}")
    return errors


def main() -> int:
    errors: list[str] = foreign_units()
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
                        # Allowed only where the table declares the field that
                        # resolves the unit.
                        table_fields = {
                            m.lower()
                            for _, r in rows if len(r) > fi
                            for m in FIELD_TOKEN.findall(r[fi])
                        }
                        if not any(p in table_fields for p in POLY_RESOLVERS):
                            for n in names:
                                errors.append(
                                    f"{rel}:{lineno}: POLY `{n}` declares the "
                                    f"polymorphic unit {unit!r} but its table "
                                    f"declares no VarSymbol field to resolve it")
                        continue
                    if unit in SUFFIX:
                        want = SUFFIX[unit]
                        for n in names:
                            checked += 1
                            if not n.endswith(want):
                                errors.append(
                                    f"{rel}:{lineno}: SUFFIX `{n}` declares unit "
                                    f"{unit} so it must end in `{want}`")
                            if unit in ("MW", "MWh") and not names_a_frame(n):
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
