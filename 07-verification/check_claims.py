#!/usr/bin/env python3
"""Machine check for 07-verification/claims.yaml.

The same trick as the W0 field-table-to-DTO test (`05-implementation/P0-workstreams.md`
W0 definition of done (a)): parse the specification markdown, derive the set of
things that must be covered, and assert one-to-one correspondence with the
register. The doc and the register cannot drift silently.

Eight checks:

  1. schema      — every entry has exactly the declared keys, enums are legal.
  2. ids         — CLM ids unique, well-formed, never recycled (RETIRED list).
  3. owners      — every `owner` names a file that exists and a section that
                   exists in that file.
  4. anchors     — every `INV-*` id defined anywhere in the specification is
                   the anchor of at least one register entry.
  5. sections    — every top-level (`##`) section of every in-scope document
                   either owns at least one claim or is listed, with a reason,
                   in `no-claim-sections.yaml`.
  6. bands       — an empirical claim that has been given an evidence label
                   must carry an acceptance band.
  7. regulatory  — a `regulatory` claim carries no evidence label. PROTOCOL §2.2:
                   a label invites re-derivation of something that is given.
  8. depends_on  — every dependency names a live claim, nothing depends on
                   itself, and the graph is acyclic, so PROTOCOL §10.1 promotion
                   terminates.

Stdlib only, no venv. Run: python3 07-verification/check_claims.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REGISTER = ROOT / "07-verification" / "claims.yaml"
EXEMPTIONS = ROOT / "07-verification" / "no-claim-sections.yaml"

# Documents the register must cover exhaustively.
CORPUS = ["00-overview", "01-adr", "02-layers", "03-contracts", "04-compliance"]

KEYS_REQUIRED = ["id", "statement", "owner", "class", "blast_radius",
                 "br_source", "anchors", "evidence", "tn", "band"]
KEYS_OPTIONAL = ["vague", "depends_on"]

CLASSES = {"analytic", "empirical", "regulatory"}
RADII = {"very-expensive", "expensive-later", "moderate", "cheap"}
EVIDENCE = {"unlabelled", "derived", "prototyped", "empirical-pending",
            "refuted", "assumed-declared"}

# Claim ids retired because the text asserting them was deleted (PROTOCOL §9) or
# because a narrower claim replaced them (PROTOCOL §6). Never reused — precedent:
# T1 §10 reserved invariant ids. Procedure ids are voided by a different
# mechanism into `voided.yaml` (PROTOCOL §4.3); the two id spaces never interact.
RETIRED: set[str] = {
    # ADR-015's OPEN-1/2/3 and review-protocol sections, deleted from the
    # specification. The deferred decisions are still asserted by `README.md`
    # and `05-implementation/P0-workstreams.md`; a rebuilt ADR-015 registers
    # them under fresh ids.
    *(f"CLM-{n:04d}" for n in range(678, 701)),
    # ADR-003 (typed quantities), deleted by the conventions audit. Quantities
    # are plain numerics; the unit and frame moved into the identifier, where
    # `INV-G-02` checks them mechanically. The successor statements are owned by
    # `00-overview/02-conventions.md` §5.2 and are registered under fresh ids.
    *(f"CLM-{n:04d}" for n in range(277, 295)),
}

INV_RE = re.compile(r"\bINV-[A-Z]+-\d{2}\b")
CLM_RE = re.compile(r"^CLM-\d{3,4}$")


# --- a deliberately small YAML reader -------------------------------------
# The register is a flat list of flat mappings. A dependency-free parser for
# exactly that shape keeps this check runnable before the harness exists.

def parse_register(text: str) -> list[dict]:
    entries: list[dict] = []
    current: dict | None = None
    for lineno, raw in enumerate(text.splitlines(), 1):
        line = raw.split("  #")[0].rstrip()
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith("- "):
            current = {}
            entries.append(current)
            line = "  " + line[2:]
        if current is None:
            die(f"{REGISTER.name}:{lineno}: content before the first entry")
        if not line.startswith("  "):
            die(f"{REGISTER.name}:{lineno}: unexpected indentation: {raw!r}")
        key, _, value = line.strip().partition(":")
        current[key.strip()] = scalar(value.strip(), lineno)
    return entries


def scalar(value: str, lineno: int):
    if value in ("null", "~", ""):
        return None
    if value in ("true", "false"):
        return value == "true"
    if value.startswith("[") and value.endswith("]"):
        inner = value[1:-1].strip()
        return [] if not inner else [scalar(v.strip(), lineno) for v in inner.split(",")]
    if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
        return value[1:-1].replace('\\"', '"')
    return value


def parse_exemptions(text: str) -> dict[str, str]:
    """`<path> §<section>: <reason>` per line."""
    out: dict[str, str] = {}
    for raw in text.splitlines():
        line = raw.split("#")[0].strip()
        if not line or line.startswith("-") is False and ":" not in line:
            continue
        line = line[2:] if line.startswith("- ") else line
        key, _, reason = line.partition(":")
        if key.strip():
            out[key.strip()] = reason.strip()
    return out


# --- the corpus -----------------------------------------------------------

def spec_files() -> list[Path]:
    files: list[Path] = []
    for folder in CORPUS:
        files.extend(sorted((ROOT / folder).glob("*.md")))
    return [f for f in files if f.name != "README.md" or f.parent.name == "01-adr"]


def sections_top(path: Path) -> list[str]:
    """Top-level `##` headings, reduced to their section token. Coverage is
    checked at this level only."""
    out = []
    for line in path.read_text().splitlines():
        if line.startswith("## ") and not line.startswith("### "):
            out.append(section_token(line[3:].strip()))
    return out


def sections_all(path: Path) -> list[str]:
    """Every `##` and `###` heading. An `owner` may name a subsection."""
    out = []
    for line in path.read_text().splitlines():
        for depth in ("## ", "### ", "#### "):
            if line.startswith(depth) and not line.startswith("#" + depth):
                out.append(section_token(line[len(depth):].strip()))
                break
    return out


def covering_top(tok: str, tops: list[str]) -> str | None:
    """The top-level section a (possibly nested) owner token belongs to."""
    if tok in tops:
        return tok
    prefix = tok.split(".")[0]
    return prefix if prefix in tops else None


def section_token(heading: str) -> str:
    m = re.match(r"^(\d+(?:\.\d+)*)\.?\s", heading)
    return m.group(1) if m else heading.strip().lower()


def find_cycle(start: str, graph: dict[str, list[str]]) -> list[str] | None:
    """The first cycle reachable from `start`, as a path, or None."""
    path: list[str] = []
    on_path: set[str] = set()

    def walk(node: str) -> list[str] | None:
        if node in on_path:
            return path[path.index(node):] + [node]
        if node not in graph:
            return None
        path.append(node)
        on_path.add(node)
        for nxt in graph[node]:
            if found := walk(nxt):
                return found
        path.pop()
        on_path.discard(node)
        return None

    return walk(start)


errors: list[str] = []


def fail(msg: str) -> None:
    errors.append(msg)


def die(msg: str) -> None:
    print(f"FATAL {msg}", file=sys.stderr)
    raise SystemExit(2)


def main() -> int:
    if not REGISTER.exists():
        die(f"{REGISTER} does not exist")
    entries = parse_register(REGISTER.read_text())
    exempt = parse_exemptions(EXEMPTIONS.read_text()) if EXEMPTIONS.exists() else {}

    # 1 schema, 2 ids, 6 bands
    seen: set[str] = set()
    for e in entries:
        cid = e.get("id", "<no id>")
        extra = set(e) - set(KEYS_REQUIRED) - set(KEYS_OPTIONAL)
        missing = set(KEYS_REQUIRED) - set(e)
        if extra:
            fail(f"{cid}: unknown keys {sorted(extra)}")
        if missing:
            fail(f"{cid}: missing keys {sorted(missing)}")
        if not CLM_RE.match(str(cid)):
            fail(f"{cid}: malformed id")
        if cid in seen:
            fail(f"{cid}: duplicate id")
        if cid in RETIRED:
            fail(f"{cid}: retired id reused")
        seen.add(cid)
        if e.get("class") not in CLASSES:
            fail(f"{cid}: class {e.get('class')!r} not in {sorted(CLASSES)}")
        if e.get("blast_radius") not in RADII:
            fail(f"{cid}: blast_radius {e.get('blast_radius')!r} not in {sorted(RADII)}")
        if e.get("evidence") not in EVIDENCE:
            fail(f"{cid}: evidence {e.get('evidence')!r} not in {sorted(EVIDENCE)}")
        if not str(e.get("statement") or "").strip():
            fail(f"{cid}: empty statement")
        if (e.get("class") == "empirical"
                and e.get("evidence") not in (None, "unlabelled")
                and not e.get("band")):
            fail(f"{cid}: labelled empirical claim with no acceptance band")
        if (e.get("class") == "regulatory"
                and e.get("evidence") not in (None, "unlabelled")):
            fail(f"{cid}: regulatory claim carries the evidence label "
                 f"{e.get('evidence')!r}; a primitive is given, not derived")
        if (dep := e.get("depends_on")) is not None and not isinstance(dep, list):
            fail(f"{cid}: depends_on must be a list of CLM ids")

    # 3 owners
    owned: set[str] = set()
    for e in entries:
        owner = str(e.get("owner") or "")
        path_part, _, sec = owner.partition("§")
        rel = path_part.strip()
        path = ROOT / rel
        if not rel or not path.exists():
            fail(f"{e.get('id')}: owner path {rel!r} does not exist")
            continue
        tok = section_token(sec.strip().lstrip("§").strip())
        if sec.strip() and tok not in sections_all(path):
            fail(f"{e.get('id')}: owner section §{sec.strip()} not a heading in {rel}")
        top = covering_top(tok, sections_top(path))
        if top is not None:
            owned.add(f"{rel} §{top}")

    # 4 anchors
    declared: set[str] = set()
    for f in spec_files():
        declared |= set(INV_RE.findall(f.read_text()))
    anchored: set[str] = set()
    for e in entries:
        for a in e.get("anchors") or []:
            anchored.add(str(a))
    for inv in sorted(declared - anchored):
        fail(f"{inv}: defined in the specification, anchored to no claim")

    # 8 depends_on
    graph = {str(e.get("id")): [str(d) for d in (e.get("depends_on") or [])]
             for e in entries}
    for cid, deps in graph.items():
        for d in deps:
            if d == cid:
                fail(f"{cid}: depends on itself")
            elif d not in graph:
                fail(f"{cid}: depends_on {d} is not a live claim"
                     + (" (retired)" if d in RETIRED else ""))
    for cid in graph:
        if cycle := find_cycle(cid, graph):
            fail(f"depends_on cycle: {' -> '.join(cycle)}")
            break

    # 5 sections
    for f in spec_files():
        rel = f.relative_to(ROOT).as_posix()
        for tok in sections_top(f):
            key = f"{rel} §{tok}"
            if key not in owned and key not in exempt:
                fail(f"{key}: section owns no claim and is not exempted "
                     f"in {EXEMPTIONS.name}")

    for err in errors:
        print(f"FAIL {err}")
    print(f"\n{len(entries)} claims, {len(declared)} invariants declared, "
          f"{len(errors)} failures")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
