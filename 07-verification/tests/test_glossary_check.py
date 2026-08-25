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


if __name__ == "__main__":
    unittest.main()
