# Claim extraction brief (ticket #5)

Transcribe every claim your assigned corpus asserts into YAML. You are a
transcriber, not an editor.

## Hard rules

1. **Transcribe, do not improve.** A vague claim is registered vague. Do not
   sharpen, qualify, or repair wording. If the doc says "should be small",
   the statement says "should be small" and the entry carries `vague: true`.
2. **One claim per entry.** A sentence asserting two things becomes two entries.
3. **`INV-*` invariants are claims like any other** — same register, no split.
   Their statement is the invariant text as written; `anchors` carries the ID.
4. **No evidence labels.** `evidence: unlabelled` on every entry, always.
5. Not a claim: pure cross-references, reading-order prose, tables of contents,
   rejected-alternative narration (unless the rejection asserts a fact, e.g.
   "X was rejected because it is O(n^2)" -> that is a claim), and code-block
   type sketches. A *definition* is a claim (analytic).

## Entry schema — exactly these keys, this order

```yaml
- id: CLM-NNN
  statement: <one sentence, the claim as asserted>
  owner: <file path §section>
  class: analytic | empirical | regulatory
  blast_radius: very-expensive | expensive-later | moderate | cheap
  br_source: <ADR-NNN | argued>
  anchors: [<INV-*, ADR-*, C*, T* ids the claim carries; [] if none>]
  evidence: unlabelled
  tn: null
  band: null
  vague: true        # OMIT this key unless the statement is untestable as written
```

## class

- `analytic` — true by derivation, definition or construction. Math identity,
  algebraic property, definitional invariant, structural guarantee.
- `empirical` — truth depends on data or runtime behaviour. Performance,
  calibration, accuracy, gap size, latency, staleness, "this estimator works".
- `regulatory` — a formula, rate, gate, qualification rule or product parameter
  transcribed from law or market rules. Given, never verified.

Class by what makes the claim true, not by the topic. "The peak charge is
EUR/kW/month on the annual maximum" is regulatory. "Our proration estimator of
that charge is unbiased" is empirical.

## blast_radius

Copy from `01-adr/README.md` where an ADR owns the claim, mapping
Cheap->cheap, Moderate->moderate, Expensive later->expensive-later,
Expensive->expensive-later, Very expensive->very-expensive. Set
`br_source: ADR-NNN`.

| ADR | radius | ADR | radius |
|---|---|---|---|
| 001 | cheap | 010 | moderate |
| 002 | moderate | 011 | expensive-later |
| 003 | expensive-later | 012 | moderate |
| 004 | very-expensive | 013 | very-expensive |
| 005 | very-expensive | 014 | moderate |
| 006 | very-expensive | 015 | (open register — argue) |
| 007 | expensive-later | 016 | moderate |
| 008 | very-expensive | 017 | very-expensive |
| 009 | moderate | | |

Where no ADR owns the claim, assign by argument and set `br_source: argued`.
Argue from retrofit cost: what has to be rewritten if the claim turns out false.
- `very-expensive` — every layer above is rewritten (contract surface, state
  representation, scenario axis, objective structure).
- `expensive-later` — one layer's internals rewritten; seam holds.
- `moderate` — a bounded module or policy is replaced.
- `cheap` — local, one document or one function.

## Output

Two files in `07-verification/_extract/`:

- `<name>.yaml` — a flat YAML list of entries, nothing else, no top-level key.
- `<name>.notes.md` — three sections:
  - `## Vague` — every entry you flagged `vague: true`, id + why it is untestable.
  - `## Duplicates` — statements you believe another corpus also asserts, id +
    the doc you think owns it.
  - `## Argued radii` — every `br_source: argued` entry, id + one-line argument.

Quote YAML statements that contain `:` or start with a special character.
Use your assigned ID range. Do not renumber; gaps are fine.
