#!/usr/bin/env python3
"""Tests for the glossary check.

The repository's first tests. They exercise the check through its parsing and
checking functions — its public interface — and assert on returned records.
Nothing here reads a file from disk, reaches into module state, or recomputes
its expectation the way the code does: every expected value is planted in a
fixture below and asserted as a literal, so a test can genuinely disagree with
the implementation.

Standard library only, no venv, matching what the three checks promise about
their environment.

Run: python3 -m unittest discover -s 07-verification/tests
"""

from __future__ import annotations

import dataclasses
import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))

import check_glossary as cg  # noqa: E402


# --- planted fixtures -------------------------------------------------------

WELL_FORMED = """
{
  "rulings": {
    "frame": {
      "keeps": "the sign frame",
      "loses": "L1's warm storage artefact",
      "replacements": ["slab"],
      "anchor": {"doc": "06-theory/TN-03-vocabulary.md", "section": "3.1"},
      "retired": [
        {"spelling": "FrameSpecHash", "replacement": "SlabSpecHash",
         "caseSensitive": true, "allowed": []},
        {"spelling": "frame set", "replacement": "slab set",
         "caseSensitive": false,
         "allowed": [{"path": "06-theory/TN-02-fan-not-tree.md",
                      "reason": "names the old artefact in the sentence that renames it"}]}
      ]
    }
  }
}
"""


class ParserRaisesOnInputItDoesNotUnderstand(unittest.TestCase):
    """The first test, and it comes first deliberately.

    Until the parser is proven to fail loudly, every later passing test is
    ambiguous between "no violations found" and "no rulings loaded" — which is
    exactly the defect being removed. The old reader identified records by
    exact indent position and returned an empty mapping on input it did not
    recognise, and an empty mapping reported success.
    """

    def assert_rejects(self, text: str) -> None:
        with self.assertRaises(cg.RulingDataError):
            cg.parse_rulings(text)

    def test_not_json_at_all(self):
        self.assert_rejects("retired:\n  'frame set':\n    replacement: slab set\n")

    def test_json_but_no_rulings_key(self):
        self.assert_rejects('{"retired": {}}')

    def test_ruling_missing_anchor(self):
        self.assert_rejects('{"rulings": {"frame": {"keeps": "x", "loses": "y",'
                            ' "replacements": ["slab"], "retired": []}}}')

    def test_anchor_missing_section(self):
        self.assert_rejects('{"rulings": {"frame": {"keeps": "x", "loses": "y",'
                            ' "replacements": ["slab"],'
                            ' "anchor": {"doc": "06-theory/TN-03-vocabulary.md"},'
                            ' "retired": []}}}')

    def test_retired_spelling_missing_replacement(self):
        self.assert_rejects('{"rulings": {"frame": {"keeps": "x", "loses": "y",'
                            ' "replacements": ["slab"],'
                            ' "anchor": {"doc": "d.md", "section": "3.1"},'
                            ' "retired": [{"spelling": "frame set",'
                            ' "caseSensitive": false, "allowed": []}]}}}')

    def test_case_sensitivity_is_declared_never_inferred(self):
        """A spelling with no `caseSensitive` flag is malformed, not a default.

        Inferring it — the old implementation inferred it from whether the
        spelling contained a space — is why five capitalised occurrences of
        `Oracle` were invisible.
        """
        self.assert_rejects('{"rulings": {"frame": {"keeps": "x", "loses": "y",'
                            ' "replacements": ["slab"],'
                            ' "anchor": {"doc": "d.md", "section": "3.1"},'
                            ' "retired": [{"spelling": "frame set",'
                            ' "replacement": "slab set", "allowed": []}]}}}')

    def test_allowance_without_a_written_reason(self):
        """An allowance is a decision, so it carries a reason or it is malformed."""
        self.assert_rejects('{"rulings": {"frame": {"keeps": "x", "loses": "y",'
                            ' "replacements": ["slab"],'
                            ' "anchor": {"doc": "d.md", "section": "3.1"},'
                            ' "retired": [{"spelling": "frame set",'
                            ' "replacement": "slab set", "caseSensitive": false,'
                            ' "allowed": [{"path": "a.md"}]}]}}}')

    def test_parser_never_exits(self):
        """A parser raises; only the entry point owns exit policy."""
        try:
            cg.parse_rulings("{")
        except SystemExit:  # pragma: no cover - the failure this asserts against
            self.fail("parse_rulings called sys.exit instead of raising")
        except cg.RulingDataError:
            pass


class WellFormedDataYieldsTheRecordsPlantedInIt(unittest.TestCase):

    def test_records_match_the_fixture_literally(self):
        rulings = cg.parse_rulings(WELL_FORMED)
        self.assertEqual(len(rulings), 1)
        r = rulings[0]
        self.assertEqual(r.word, "frame")
        self.assertEqual(r.keeps, "the sign frame")
        self.assertEqual(r.loses, "L1's warm storage artefact")
        self.assertEqual(r.replacements, ["slab"])
        self.assertEqual(r.anchor.doc, "06-theory/TN-03-vocabulary.md")
        self.assertEqual(r.anchor.section, "3.1")
        self.assertEqual(len(r.retired), 2)

        first, second = r.retired
        self.assertEqual(first.spelling, "FrameSpecHash")
        self.assertEqual(first.replacement, "SlabSpecHash")
        self.assertTrue(first.case_sensitive)
        self.assertEqual(first.allowed, [])

        self.assertEqual(second.spelling, "frame set")
        self.assertEqual(second.replacement, "slab set")
        self.assertFalse(second.case_sensitive)
        self.assertEqual(len(second.allowed), 1)
        self.assertEqual(second.allowed[0].path, "06-theory/TN-02-fan-not-tree.md")
        self.assertEqual(second.allowed[0].reason,
                         "names the old artefact in the sentence that renames it")


class TheShippedRulingDataParses(unittest.TestCase):
    """The one test that reads a file, because the shipped data is the subject.

    Everything else about the parser is proven above without touching the disk.
    """

    def test_shipped_data_is_well_formed(self):
        root = pathlib.Path(__file__).resolve().parent.parent.parent
        text = (root / "07-verification" / "rulings.json").read_text(encoding="utf-8")
        rulings = cg.parse_rulings(text)
        self.assertEqual(
            sorted(r.word for r in rulings),
            ["V", "capture rate", "frame", "generation", "peak_to_go",
             "scenarioCount", "stage"],
        )




# --- the failures of the implementation this replaces -----------------------
#
# One test per known defect, so that each is a regression rather than a memory.


def _ruling(spelling: str, replacement: str, case_sensitive: bool,
            allowed=()) -> cg.Ruling:
    return cg.Ruling(
        word=spelling, keeps="x", loses="y", replacements=[replacement],
        anchor=cg.Anchor(doc="06-theory/TN-03-vocabulary.md", section="3.1"),
        retired=[cg.RetiredSpelling(
            spelling=spelling, replacement=replacement,
            case_sensitive=case_sensitive,
            allowed=[cg.Allowance(path=p, reason=r) for p, r in allowed],
        )],
    )


class TheMatcherRespectsDeclaredCase(unittest.TestCase):

    def test_a_capitalised_occurrence_of_a_case_insensitive_spelling_is_found(self):
        """Five capitalised `Oracle`s were invisible, because the old matcher
        inferred case sensitivity from whether the spelling contained a space."""
        found = cg.find_retired([_ruling("oracle", "accuracy reference", False)],
                                "04-compliance/T5.md", "The Oracle is the reference.")
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0].word, "oracle")
        self.assertEqual(found[0].replacement, "accuracy reference")
        self.assertEqual(found[0].line, 1)

    def test_an_exact_case_identifier_is_not_matched_loosely(self):
        found = cg.find_retired([_ruling("ORACLE_BUDGET", "REFERENCE_BUDGET", True)],
                                "04-compliance/T5.md", "the oracle_budget knob")
        self.assertEqual(found, [])


class TheMatcherReportsAPlaceToEditNotACharacter(unittest.TestCase):

    def test_a_nested_spelling_reports_once_at_its_most_specific_form(self):
        """`frame set` and `warm frame set` both match the same text; the
        inflated 89-vs-81 figure came from counting both."""
        rulings = [_ruling("frame set", "slab set", False),
                   _ruling("warm frame set", "warm slab set", False)]
        found = cg.find_retired(rulings, "02-layers/L1.md", "map the warm frame set now")
        self.assertEqual([(f.word, f.replacement) for f in found],
                         [("warm frame set", "warm slab set")])

    def test_repeated_hits_on_one_line_collapse_to_a_single_finding(self):
        found = cg.find_retired([_ruling("frame set", "slab set", False)],
                                "02-layers/L1.md",
                                "a frame set, another frame set, a third frame set\n"
                                "and a frame set on the next line")
        self.assertEqual([f.line for f in found], [1, 2])

    def test_an_allowance_silences_only_the_document_it_names(self):
        rulings = [_ruling("staging table", "gate table", False,
                           allowed=[("06-theory/TN-02-fan-not-tree.md",
                                     "names the old table in the sentence that renames it")])]
        self.assertEqual(
            cg.find_retired(rulings, "06-theory/TN-02-fan-not-tree.md", "the staging table"),
            [])
        self.assertEqual(
            len(cg.find_retired(rulings, "02-layers/L3-planner.md", "the staging table")), 1)


class TheGlossaryParserReadsTablesNotHeadings(unittest.TestCase):

    GLOSSARY = """# Glossary

## 1. What this document is

## 2. Rulings

| Word | Keeps | Loses | Replacement |
|---|---|---|---|
| **frame** | the sign frame | L1's storage artefact | **slab** |

## 3. A ruling of thumb for reading this section

| Term | Definition | Owner |
|---|---|---|
| **slab** | L1's warm-tier artefact. | `02-layers/L1-belief.md` |
| **hot window** | The preallocated buffer. | |
"""

    def test_a_heading_that_resembles_a_skipped_section_swallows_nothing(self):
        """The old parser skipped any section whose heading contained the
        substring "ruling", so §3 above would have taken its entries with it."""
        entries = cg.parse_glossary(self.GLOSSARY)
        self.assertEqual([e.term for e in entries], ["slab", "hot window"])

    def test_a_four_column_ruling_table_is_not_an_entry_table(self):
        entries = cg.parse_glossary(self.GLOSSARY)
        self.assertNotIn("frame", [e.term for e in entries])

    def test_an_entry_with_no_owner_fails(self):
        entries = cg.parse_glossary(self.GLOSSARY)
        found = cg.check_owner(entries, exists=lambda rel: True)
        self.assertEqual([(f.check, f.word, f.severity) for f in found],
                         [("OWNER", "hot window", "failure")])

    def test_an_owner_that_does_not_exist_fails(self):
        entries = cg.parse_glossary(self.GLOSSARY)
        found = cg.check_owner(entries, exists=lambda rel: False)
        self.assertEqual([f.word for f in found], ["slab", "hot window"])

    def test_a_term_defined_twice_fails(self):
        entries = cg.parse_glossary(self.GLOSSARY + "| **slab** | Again. | `a.md` |\n")
        found = cg.check_unique(entries)
        self.assertEqual([(f.check, f.word) for f in found], [("UNIQUE", "slab")])


class LivenessIsAWordBoundaryWarning(unittest.TestCase):

    ENTRIES = [cg.Entry(term="gate", definition="A market commit point.",
                        owner="`x.md`", line=7)]

    def test_gate_is_not_satisfied_by_mitigate(self):
        found = cg.check_live(self.ENTRIES, {"a.md": "we mitigate the risk"})
        self.assertEqual([(f.check, f.word) for f in found], [("LIVE", "gate")])

    def test_a_real_use_satisfies_liveness(self):
        self.assertEqual(cg.check_live(self.ENTRIES, {"a.md": "the S3 gate closes"}), [])

    def test_liveness_never_fails_the_run(self):
        found = cg.check_live(self.ENTRIES, {"a.md": "we mitigate the risk"})
        self.assertEqual(cg.failures(found), [])
        self.assertEqual(cg.warning_findings(found), found)


class EveryReplacementNameResolvesToAnEntry(unittest.TestCase):

    def test_a_replacement_with_no_glossary_entry_fails(self):
        rulings = [_ruling("frame set", "slab set", False)]
        entries = [cg.Entry(term="hot window", definition="d", owner="`x.md`", line=1)]
        found = cg.check_replacements_resolve(rulings, entries)
        self.assertEqual([(f.check, f.severity, f.word) for f in found],
                         [("RESOLVES", "failure", "frame set")])

    def test_a_replacement_that_is_defined_passes(self):
        rulings = [_ruling("frame set", "slab set", False)]
        entries = [cg.Entry(term="slab set", definition="d", owner="`x.md`", line=1)]
        self.assertEqual(cg.check_replacements_resolve(rulings, entries), [])


class FindingsAreStructuredAndFormattedOnce(unittest.TestCase):

    def test_a_finding_names_the_spelling_its_replacement_and_its_location(self):
        f = cg.find_retired([_ruling("peak_to_go", "pPoiRealisedPeakMw", True)],
                            "README.md", "x\ny\nthe `peak_to_go` floor")[0]
        self.assertEqual((f.path, f.line, f.word, f.replacement), 
                         ("README.md", 3, "peak_to_go", "pPoiRealisedPeakMw"))
        self.assertIn("README.md:3", cg.format_finding(f))
        self.assertIn("pPoiRealisedPeakMw", cg.format_finding(f))


class EveryRulingIsTraceableToItsArgument(unittest.TestCase):

    RULING = cg.Ruling(
        word="frame", keeps="x", loses="y", replacements=["slab"],
        anchor=cg.Anchor(doc="06-theory/TN-03-vocabulary.md", section="3.1"),
        retired=[])

    def test_a_present_section_passes(self):
        blobs = {"06-theory/TN-03-vocabulary.md":
                 "## 3. The rulings\n\n### 3.1 `frame` — the sign frame keeps it\n"}
        self.assertEqual(cg.check_anchors([self.RULING], blobs), [])

    def test_a_renumbered_section_fails(self):
        blobs = {"06-theory/TN-03-vocabulary.md": "### 3.2 `gate` keeps it\n"}
        found = cg.check_anchors([self.RULING], blobs)
        self.assertEqual([(f.check, f.severity, f.word) for f in found],
                         [("ANCHOR", "failure", "frame")])

    def test_a_missing_anchor_document_fails(self):
        found = cg.check_anchors([self.RULING], {})
        self.assertEqual(len(found), 1)
        self.assertIn("does not exist", found[0].detail)

    def test_a_top_level_section_anchor_resolves(self):
        ruling = dataclasses.replace(
            self.RULING, anchor=cg.Anchor(doc="06-theory/TN-02-fan-not-tree.md",
                                          section="5"))
        blobs = {"06-theory/TN-02-fan-not-tree.md": "## 5. Two errors, opposite signs\n"}
        self.assertEqual(cg.check_anchors([ruling], blobs), [])


class ARetiredSpellingInThePluralIsTheSameSpelling(unittest.TestCase):
    """"Composition stages" survived batch one because the trailing boundary
    rejected the `s`. A matcher blind to the plural is a silent gap."""

    def test_a_plural_is_found_and_named_by_its_singular(self):
        found = cg.find_retired([_ruling("composition stage", "composition step", False)],
                                "stubs/Enums.cs", "/// Composition stages (L2 4).")
        self.assertEqual([(f.word, f.replacement) for f in found],
                         [("composition stage", "composition step")])

    def test_a_longer_word_that_merely_starts_the_same_is_not_a_plural(self):
        self.assertEqual(
            cg.find_retired([_ruling("frame set", "slab set", False)],
                            "a.md", "the frame setting"),
            [])

    def test_liveness_does_not_admit_the_plural(self):
        """A warning should err towards silence, so it stays exact."""
        entries = [cg.Entry(term="slab", definition="d", owner="`x.md`", line=1)]
        self.assertEqual([f.word for f in cg.check_live(entries, {"a.md": "the slabs"})],
                         ["slab"])


if __name__ == "__main__":
    unittest.main()
