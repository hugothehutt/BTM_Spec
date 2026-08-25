#!/usr/bin/env python3
"""The glossary lint.

`00-overview/04-glossary.md` is normative for what a word *means* and for
nothing else. That is only worth anything if a machine checks it, so this is the
machine.

Four checks:

  (a) UNIQUE    no term is defined twice in the glossary. A second definition of
                a word is the defect the glossary exists to remove, and it is
                cheapest to catch inside the glossary itself.
  (b) OWNER     every entry names an owning document, and that document exists.
                An entry with no owner is a definition with nobody to keep it
                true.
  (c) LIVE      every term the glossary defines appears somewhere in the corpus
                outside the glossary. A defined word nobody uses is dead
                vocabulary, and dead vocabulary is how a glossary stops being
                read.
  (d) RETIRED   no retired spelling appears outside `retired-vocabulary.yaml`,
                which holds the sites where a retired word is load-bearing —
                a regulatory primitive, a quotation, or a sentence whose job is
                to say the word is retired. Adding a line there is a decision
                someone writes down.

(d) is the check that costs something. §2 of the glossary rules one meaning per
word; the retired spellings are the losing side of each ruling, and this is what
stops them growing back.

Stdlib only, no venv. Run: python3 07-verification/check_glossary.py
"""

from __future__ import annotations

import dataclasses
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
GLOSSARY = ROOT / "00-overview" / "04-glossary.md"
RETIRED_FILE = ROOT / "07-verification" / "retired-vocabulary.yaml"

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

# Files that are allowed to hold retired vocabulary wholesale, because saying
# what a word no longer means is their subject.
#
# `claims.yaml` is exempt for a different reason: it quotes the owning text by
# construction, so every retired word in the spec appears there a second time.
# Reporting both would double the count and imply two sites to fix where there
# is one. The register is corrected *by* the propagation, not before it — a
# claim whose statement still quotes a retired word after its owning section is
# fixed is a register defect, and `check_claims.py` is what should catch it.
RETIRED_EXEMPT_FILES = {
    "00-overview/04-glossary.md",
    "06-theory/TN-03-vocabulary.md",
    "07-verification/retired-vocabulary.yaml",
    "07-verification/check_glossary.py",
    "07-verification/claims.yaml",
    "07-verification/rulings.json",
}

# The check's own machinery names retired spellings because they are its subject:
# the ruling data records them, and the tests plant them as fixtures.
RETIRED_EXEMPT_PREFIXES = ("07-verification/tests/",)


def corpus_files() -> list[pathlib.Path]:
    out: list[pathlib.Path] = []
    for entry in CORPUS:
        p = ROOT / entry
        if p.is_file():
            out.append(p)
        elif p.is_dir():
            out.extend(sorted(q for q in p.rglob("*")
                              if q.is_file()
                              and q.suffix in {".md", ".cs", ".py", ".yaml"}
                              and "__pycache__" not in q.parts))
    return out


# --- glossary parsing -------------------------------------------------------

# An entry row: | term | definition | owner |
ROW = re.compile(r"^\|\s*(?P<term>[^|]+?)\s*\|\s*(?P<defn>[^|]+?)\s*\|\s*(?P<owner>[^|]*?)\s*\|\s*$")
SEP = re.compile(r"^\|[\s:|-]+\|$")
# A doc reference inside the Owner cell: `path/to/doc.md` or `path` §N
OWNER_PATH = re.compile(r"`([^`]+?\.md)`")


def strip_markup(s: str) -> str:
    return s.replace("`", "").replace("**", "").replace("*", "").strip()


def parse_glossary() -> list[dict]:
    if not GLOSSARY.exists():
        print(f"FAIL  glossary not found: {GLOSSARY.relative_to(ROOT)}")
        sys.exit(1)
    entries: list[dict] = []
    skipping = False
    for lineno, line in enumerate(GLOSSARY.read_text(encoding="utf-8").splitlines(), 1):
        if line.startswith("#"):
            # Two sections are not entry tables. §2 records the rulings, whose
            # replacement names are deliberately not yet in the corpus — they
            # go live with the propagation, not with the ruling. §15 lists the
            # losing side of each ruling.
            head = line.lower()
            skipping = "ruling" in head or "retired vocabulary" in head
            continue
        if skipping or SEP.match(line):
            continue
        m = ROW.match(line)
        if not m:
            continue
        term = strip_markup(m.group("term"))
        if not term or term.lower() in {"term", "word", "family"}:
            continue
        entries.append({
            "term": term,
            "defn": m.group("defn").strip(),
            "owner": m.group("owner").strip(),
            "line": lineno,
        })
    return entries


def parse_retired() -> dict[str, dict]:
    """Minimal YAML reader for the retired-vocabulary shape. Stdlib only.

    retired:
      "cold tier":
        replacement: cold store
        allowed:
          - path: 04-compliance/T3-determinism-and-replay.md
            reason: quotes the retired name to say it is retired
    """
    if not RETIRED_FILE.exists():
        print(f"FAIL  retired vocabulary not found: {RETIRED_FILE.relative_to(ROOT)}")
        sys.exit(1)
    out: dict[str, dict] = {}
    current: str | None = None
    current_allow: dict | None = None
    for raw in RETIRED_FILE.read_text(encoding="utf-8").splitlines():
        line = raw.rstrip()
        if not line.strip() or line.strip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip())
        body = line.strip()
        if indent == 0:
            continue  # the `retired:` key
        if indent == 2 and body.endswith(":"):
            current = body[:-1].strip().strip('"').strip("'")
            out[current] = {"replacement": "", "allowed": []}
            current_allow = None
        elif indent == 4 and body.startswith("replacement:") and current:
            out[current]["replacement"] = body.split(":", 1)[1].strip().strip('"')
        elif indent == 6 and body.startswith("- path:") and current:
            current_allow = {"path": body.split(":", 1)[1].strip().strip('"'), "reason": ""}
            out[current]["allowed"].append(current_allow)
        elif indent == 8 and body.startswith("reason:") and current_allow is not None:
            current_allow["reason"] = body.split(":", 1)[1].strip().strip('"')
    return out


# --- the ruling set ---------------------------------------------------------
#
# Text in, data out. `parse_rulings` takes text and returns records; it raises
# on anything it does not understand and never exits. Exit policy belongs to the
# entry point alone, so that an unrunnable check can never be mistaken for a
# clean one.


class RulingDataError(ValueError):
    """The ruling data is not something this module can read.

    Raised, never exited on. The old reader identified records by exact indent
    position and returned an empty mapping on input it did not recognise — and
    an empty mapping reported success, so the check that costs something was the
    one that failed open.
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


# --- checks -----------------------------------------------------------------

def check_unique(entries: list[dict]) -> list[str]:
    seen: dict[str, int] = {}
    fails = []
    for e in entries:
        key = e["term"].lower()
        if key in seen:
            fails.append(f"UNIQUE  '{e['term']}' defined twice "
                         f"(04-glossary.md:{seen[key]} and :{e['line']})")
        else:
            seen[key] = e["line"]
    return fails


def check_owner(entries: list[dict]) -> list[str]:
    fails = []
    for e in entries:
        if not e["owner"] or strip_markup(e["owner"]) in {"", "—", "-"}:
            fails.append(f"OWNER   '{e['term']}' names no owning document "
                         f"(04-glossary.md:{e['line']})")
            continue
        for path in OWNER_PATH.findall(e["owner"]):
            if not (ROOT / path).exists():
                fails.append(f"OWNER   '{e['term']}' owner '{path}' does not exist "
                             f"(04-glossary.md:{e['line']})")
    return fails


def check_live(entries: list[dict], blobs: dict[str, str]) -> list[str]:
    fails = []
    for e in entries:
        needle = e["term"]
        # A term written as an identifier is matched verbatim; a prose term is
        # matched case-insensitively, since prose capitalises at sentence start.
        found = False
        for rel, text in blobs.items():
            if rel == "00-overview/04-glossary.md":
                continue
            hay = text if needle[:1].isupper() or "_" in needle else text.lower()
            hit = needle if hay is text else needle.lower()
            if hit in hay:
                found = True
                break
        if not found:
            fails.append(f"LIVE    '{e['term']}' is defined but used nowhere in the corpus "
                         f"(04-glossary.md:{e['line']})")
    return fails


def check_retired(retired: dict[str, dict], blobs: dict[str, str]) -> list[str]:
    fails = []
    for word, spec in retired.items():
        allowed = {a["path"] for a in spec["allowed"]}
        pattern = re.compile(r"(?<![A-Za-z0-9_])" + re.escape(word) + r"(?![A-Za-z0-9_])",
                             re.IGNORECASE if " " in word else 0)
        for rel, text in blobs.items():
            if (rel in RETIRED_EXEMPT_FILES or rel in allowed
                    or rel.startswith(RETIRED_EXEMPT_PREFIXES)):
                continue
            for m in pattern.finditer(text):
                line = text.count("\n", 0, m.start()) + 1
                repl = spec["replacement"] or "(no replacement recorded)"
                fails.append(f"RETIRED '{word}' at {rel}:{line} — use '{repl}'")
    return fails


def main() -> int:
    entries = parse_glossary()
    retired = parse_retired()
    blobs = {
        str(p.relative_to(ROOT)): p.read_text(encoding="utf-8", errors="replace")
        for p in corpus_files()
    }

    fails: list[str] = []
    fails += check_unique(entries)
    fails += check_owner(entries)
    fails += check_live(entries, blobs)
    fails += check_retired(retired, blobs)

    print(f"glossary: {len(entries)} terms, {len(retired)} retired spellings, "
          f"{len(blobs)} corpus files")
    if not fails:
        print("OK")
        return 0
    for f in fails:
        print(f)
    print(f"\n{len(fails)} failure(s)")
    return 1


if __name__ == "__main__":
    sys.exit(main())
