#!/usr/bin/env python3
"""The glossary check.

`00-overview/04-glossary.md` is normative for what a word *means* and for
nothing else. `07-verification/rulings.json` is normative for which word kept
which meaning. That is only worth anything if a machine checks it, so this is
the machine.

Failures — these stop the run being clean:

  UNIQUE       no term is defined twice. A second definition of a word is the
               defect the glossary exists to remove, and it is cheapest to catch
               inside the glossary itself.
  OWNER        every entry names an owning document, and that document exists.
               An entry with no owner is a definition with nobody to keep it
               true.
  RESOLVES     every replacement name in the ruling data is a glossary entry, so
               a half-recorded ruling is caught the day it is written.
  RETIRED      no retired spelling appears outside a written allowance. The
               retired spellings are the losing side of each ruling, and this is
               what stops them growing back.

Warning — reported, does not fail the run:

  LIVE         every defined term is used somewhere outside the glossary. Any
               honest implementation of this is either strict enough to be noisy
               about legitimately rare terms or loose enough to be vacuous, and a
               dead entry is a tidiness problem where a returned retired spelling
               is a correctness one.

Detection only. The machine reports; a human or an agent remediates. Nothing
here rewrites a document.

Seams: text in, data out. Parsing functions take text and return records. Check
functions take records and return findings. Parsers raise; they never exit. Only
`main` touches the filesystem and owns exit policy — 2 for a condition that
stops the check running, 1 for a violation, 0 for clean — matching
`check_claims.py`.

Stdlib only, no venv. Run: python3 07-verification/check_glossary.py
"""

from __future__ import annotations

import dataclasses
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
GLOSSARY_PATH = "00-overview/04-glossary.md"
RULINGS_PATH = "07-verification/rulings.json"

# Where a term must appear to count as live, and where a retired spelling is
# looked for. `06-theory` is included: a technical note is where a ruling is
# argued, so it is exactly where a retired word most easily survives.
CORPUS = [
    "00-overview",
    "01-adr",
    "02-layers",
    "03-contracts",
    "04-compliance",
    "05-implementation",
    "06-theory",
    "07-verification",
    "stubs",
    "README.md",
]
CORPUS_SUFFIXES = {".md", ".cs", ".py", ".yaml", ".json"}

# Files allowed to hold retired vocabulary wholesale, because saying what a word
# no longer means is their subject.
#
# `claims.yaml` is exempt for a different reason: it quotes the owning text by
# construction, so every retired word in the spec would appear there a second
# time. Reporting both would double the count and imply two sites to fix where
# there is one. The register is corrected *by* a propagation batch, not before
# it — a claim still quoting a retired word after its owning section is fixed is
# a register defect, and `check_claims.py` is what should catch it.
RETIRED_EXEMPT_FILES = {
    GLOSSARY_PATH,
    RULINGS_PATH,
    "06-theory/TN-03-vocabulary.md",
    "07-verification/check_glossary.py",
    "07-verification/claims.yaml",
}

# The check's own tests name retired spellings because they plant them as
# fixtures.
RETIRED_EXEMPT_PREFIXES = ("07-verification/tests/",)


# --- records ----------------------------------------------------------------


class RulingDataError(ValueError):
    """The ruling data is not something this module can read.

    Raised, never exited on. The reader this replaces identified records by
    exact indent position and returned an empty mapping on input it did not
    recognise — and an empty mapping reported success, so the check that costs
    something was the one that failed open.
    """


@dataclasses.dataclass(frozen=True)
class Allowance:
    path: str
    reason: str


@dataclasses.dataclass(frozen=True)
class RetiredSpelling:
    spelling: str
    replacement: str
    case_sensitive: bool
    allowed: list[Allowance] = dataclasses.field(default_factory=list)


@dataclasses.dataclass(frozen=True)
class Anchor:
    doc: str
    section: str


@dataclasses.dataclass(frozen=True)
class Ruling:
    word: str
    keeps: str
    loses: str
    replacements: list[str]
    anchor: Anchor
    retired: list[RetiredSpelling]


@dataclasses.dataclass(frozen=True)
class Entry:
    term: str
    definition: str
    owner: str
    line: int


@dataclasses.dataclass(frozen=True)
class Finding:
    """A finding is structured so that an agent can act on it without parsing
    prose: it names the word at issue, what to write instead, and where."""
    check: str          # UNIQUE | OWNER | RESOLVES | RETIRED | LIVE
    severity: str       # "failure" | "warning"
    path: str
    line: int
    word: str
    replacement: str    # "" where the finding names no replacement
    detail: str


def format_finding(f: Finding) -> str:
    """Findings are formatted once, at the boundary."""
    where = f"{f.path}:{f.line}" if f.line else f.path
    tail = f" — use '{f.replacement}'" if f.replacement else ""
    return f"{f.check:<8} '{f.word}' at {where} — {f.detail}{tail}"


def failures(findings: list[Finding]) -> list[Finding]:
    return [f for f in findings if f.severity == "failure"]


def warnings(findings: list[Finding]) -> list[Finding]:
    return [f for f in findings if f.severity == "warning"]


# --- parsing: the ruling set ------------------------------------------------


def _require(obj: dict, key: str, kind: type, where: str):
    if key not in obj:
        raise RulingDataError(f"{where}: missing '{key}'")
    value = obj[key]
    if not isinstance(value, kind):
        raise RulingDataError(f"{where}: '{key}' must be {kind.__name__}, "
                              f"got {type(value).__name__}")
    return value


def parse_rulings(text: str) -> list[Ruling]:
    """Parse the ruling set. Raises `RulingDataError` on anything malformed."""
    try:
        doc = json.loads(text)
    except json.JSONDecodeError as exc:
        raise RulingDataError(f"not valid JSON: {exc}") from exc
    if not isinstance(doc, dict):
        raise RulingDataError("top level must be an object")
    rulings = _require(doc, "rulings", dict, "top level")

    out: list[Ruling] = []
    for word, rec in rulings.items():
        where = f"ruling '{word}'"
        if not isinstance(rec, dict):
            raise RulingDataError(f"{where}: must be an object")

        replacements = _require(rec, "replacements", list, where)
        for r in replacements:
            if not isinstance(r, str) or not r.strip():
                raise RulingDataError(f"{where}: every replacement must be a "
                                      f"non-empty string")

        anchor_obj = _require(rec, "anchor", dict, where)
        anchor = Anchor(doc=_require(anchor_obj, "doc", str, f"{where} anchor"),
                        section=_require(anchor_obj, "section", str,
                                         f"{where} anchor"))

        retired: list[RetiredSpelling] = []
        for entry in _require(rec, "retired", list, where):
            if not isinstance(entry, dict):
                raise RulingDataError(f"{where}: every retired entry must be an object")
            spelling = _require(entry, "spelling", str, where)
            seat = f"{where}, spelling '{spelling}'"
            # Case sensitivity is declared, never inferred. Inferring it from
            # whether the spelling contains a space is why five capitalised
            # occurrences of `Oracle` were invisible.
            case_sensitive = _require(entry, "caseSensitive", bool, seat)
            allowed: list[Allowance] = []
            for a in _require(entry, "allowed", list, seat):
                if not isinstance(a, dict):
                    raise RulingDataError(f"{seat}: every allowance must be an object")
                allowed.append(Allowance(
                    path=_require(a, "path", str, f"{seat} allowance"),
                    reason=_require(a, "reason", str, f"{seat} allowance"),
                ))
            retired.append(RetiredSpelling(
                spelling=spelling,
                replacement=_require(entry, "replacement", str, seat),
                case_sensitive=case_sensitive,
                allowed=allowed,
            ))

        out.append(Ruling(
            word=word,
            keeps=_require(rec, "keeps", str, where),
            loses=_require(rec, "loses", str, where),
            replacements=replacements,
            anchor=anchor,
            retired=retired,
        ))
    return out


# --- parsing: the glossary --------------------------------------------------

ROW = re.compile(r"^\|(?P<cells>.*)\|\s*$")
SEP = re.compile(r"^\|[\s:|-]+\|$")
# A doc reference inside the Owner cell: `path/to/doc.md`, with or without a §.
OWNER_PATH = re.compile(r"`([^`]+?\.md)`")
# The header a table must carry to be an entry table. Three columns, the first
# naming what the row defines. Deciding this by the *table's own header* rather
# than by a substring in the enclosing heading is deliberate: the reader this
# replaces skipped any section whose heading contained "ruling", so a future
# subsection so named would silently swallow every entry beneath it.
ENTRY_HEADERS = {
    ("term", "definition", "owner"),
    ("word", "definition", "owner"),
    ("family", "what its invariants cover", "owner"),
}


def strip_markup(s: str) -> str:
    return s.replace("`", "").replace("**", "").replace("*", "").strip()


def _cells(line: str) -> list[str] | None:
    m = ROW.match(line)
    if not m:
        return None
    return [c.strip() for c in m.group("cells").split("|")]


def parse_glossary(text: str) -> list[Entry]:
    """Parse the glossary's entry tables. Text in, records out."""
    entries: list[Entry] = []
    in_entry_table = False
    for lineno, line in enumerate(text.splitlines(), 1):
        cells = _cells(line)
        if cells is None:
            if line.strip():
                in_entry_table = False
            continue
        if SEP.match(line):
            continue
        header = tuple(strip_markup(c).lower() for c in cells)
        if header in ENTRY_HEADERS:
            in_entry_table = True
            continue
        if not in_entry_table or len(cells) != 3:
            continue
        term = strip_markup(cells[0])
        if not term:
            continue
        entries.append(Entry(term=term, definition=cells[1], owner=cells[2],
                             line=lineno))
    return entries


# --- matching ---------------------------------------------------------------


def _boundary(spelling: str, case_sensitive: bool) -> re.Pattern:
    return re.compile(r"(?<![A-Za-z0-9_])" + re.escape(spelling)
                      + r"(?![A-Za-z0-9_])",
                      0 if case_sensitive else re.IGNORECASE)


def find_retired(rulings: list[Ruling], path: str, text: str) -> list[Finding]:
    """Every retired spelling in one document.

    Longest match wins, so a nested spelling is reported once at its most
    specific form rather than once per enclosing spelling. Findings are
    deduplicated per line, so a line carrying three occurrences yields one: a
    backlog figure should count places to edit, not characters.
    """
    if path in RETIRED_EXEMPT_FILES or path.startswith(RETIRED_EXEMPT_PREFIXES):
        return []

    spellings = [(r, s) for r in rulings for s in r.retired
                 if path not in {a.path for a in s.allowed}]
    spellings.sort(key=lambda rs: len(rs[1].spelling), reverse=True)

    claimed: list[tuple[int, int]] = []
    hits: list[tuple[int, Finding]] = []
    seen: set[tuple[int, str]] = set()
    for _ruling, spelling in spellings:
        for m in _boundary(spelling.spelling, spelling.case_sensitive).finditer(text):
            if any(a < m.end() and m.start() < b for a, b in claimed):
                continue
            claimed.append((m.start(), m.end()))
            line = text.count("\n", 0, m.start()) + 1
            key = (line, spelling.spelling)
            if key in seen:
                continue
            seen.add(key)
            hits.append((m.start(), Finding(
                check="RETIRED", severity="failure", path=path, line=line,
                word=spelling.spelling, replacement=spelling.replacement,
                detail="retired spelling",
            )))
    return [f for _, f in sorted(hits, key=lambda h: h[0])]


# --- checks -----------------------------------------------------------------


def check_unique(entries: list[Entry]) -> list[Finding]:
    seen: dict[str, int] = {}
    out: list[Finding] = []
    for e in entries:
        key = e.term.lower()
        if key in seen:
            out.append(Finding(
                check="UNIQUE", severity="failure", path=GLOSSARY_PATH,
                line=e.line, word=e.term, replacement="",
                detail=f"defined twice, first at line {seen[key]}",
            ))
        else:
            seen[key] = e.line
    return out


def check_owner(entries: list[Entry], exists) -> list[Finding]:
    """`exists` is a predicate on a repository-relative path, so this check
    never touches the filesystem itself."""
    out: list[Finding] = []
    for e in entries:
        if strip_markup(e.owner) in {"", "—", "-"}:
            out.append(Finding(
                check="OWNER", severity="failure", path=GLOSSARY_PATH,
                line=e.line, word=e.term, replacement="",
                detail="names no owning document",
            ))
            continue
        for path in OWNER_PATH.findall(e.owner):
            if not exists(path):
                out.append(Finding(
                    check="OWNER", severity="failure", path=GLOSSARY_PATH,
                    line=e.line, word=e.term, replacement="",
                    detail=f"owning document '{path}' does not exist",
                ))
    return out


def check_replacements_resolve(rulings: list[Ruling],
                               entries: list[Entry]) -> list[Finding]:
    """Every replacement name the ruling data proposes is a defined term.

    A ruling recorded without its replacement being defined is half a ruling,
    and this is what catches it the day it is written.
    """
    terms = {e.term.lower() for e in entries}
    out: list[Finding] = []
    for r in rulings:
        for name in r.replacements:
            if name.lower() not in terms:
                out.append(Finding(
                    check="RESOLVES", severity="failure", path=RULINGS_PATH,
                    line=0, word=r.word, replacement="",
                    detail=f"replacement '{name}' is not a glossary entry",
                ))
    return out


def check_live(entries: list[Entry], blobs: dict[str, str]) -> list[Finding]:
    """A warning, never a failure.

    Matched on a word boundary rather than as a substring, so `gate` is not
    satisfied by "mitigate". Matched case-insensitively, because there is no
    declared flag to consult here and a warning should err towards silence.
    """
    out: list[Finding] = []
    for e in entries:
        pattern = _boundary(e.term, case_sensitive=False)
        if any(pattern.search(text) for rel, text in blobs.items()
               if rel != GLOSSARY_PATH):
            continue
        out.append(Finding(
            check="LIVE", severity="warning", path=GLOSSARY_PATH, line=e.line,
            word=e.term, replacement="",
            detail="defined but used nowhere in the corpus",
        ))
    return out


def run_checks(entries: list[Entry], rulings: list[Ruling],
               blobs: dict[str, str], exists) -> list[Finding]:
    out: list[Finding] = []
    out += check_unique(entries)
    out += check_owner(entries, exists)
    out += check_replacements_resolve(rulings, entries)
    for rel, text in sorted(blobs.items()):
        out += find_retired(rulings, rel, text)
    out += check_live(entries, blobs)
    return out


# --- entry point ------------------------------------------------------------
#
# The only part of this module that touches the filesystem, and the only part
# that decides an exit code.


def die(msg: str) -> None:
    print(f"FATAL {msg}", file=sys.stderr)
    raise SystemExit(2)


def corpus_files() -> list[pathlib.Path]:
    out: list[pathlib.Path] = []
    for entry in CORPUS:
        p = ROOT / entry
        if p.is_file():
            out.append(p)
        elif p.is_dir():
            out.extend(sorted(q for q in p.rglob("*")
                              if q.is_file()
                              and q.suffix in CORPUS_SUFFIXES
                              and "__pycache__" not in q.parts))
    return out


def read(rel: str) -> str:
    p = ROOT / rel
    if not p.exists():
        die(f"{rel} not found")
    return p.read_text(encoding="utf-8")


def main() -> int:
    entries = parse_glossary(read(GLOSSARY_PATH))
    try:
        rulings = parse_rulings(read(RULINGS_PATH))
    except RulingDataError as exc:
        die(f"{RULINGS_PATH}: {exc}")

    blobs = {
        str(p.relative_to(ROOT)): p.read_text(encoding="utf-8", errors="replace")
        for p in corpus_files()
    }
    findings = run_checks(entries, rulings, blobs, lambda rel: (ROOT / rel).exists())

    spellings = sum(len(r.retired) for r in rulings)
    print(f"glossary: {len(entries)} terms, {len(rulings)} rulings, "
          f"{spellings} retired spellings, {len(blobs)} corpus files")

    for f in warnings(findings):
        print(format_finding(f))
    bad = failures(findings)
    for f in bad:
        print(format_finding(f))
    if bad:
        print(f"\n{len(bad)} failure(s)")
        return 1
    print("OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
