# C0 — Contract Conventions

Normative. Applies to every seam. Read before C1–C5.

---

## 1. What a contract is

A contract is the **complete, closed** description of what crosses a seam. If it
is not in the field table, it does not cross. There is no "additional context"
parameter, no `object`, no dictionary of extras.

A contract is:

- **A value.** Immutable record types and readonly structs. No references to
  mutable state, no handles into another layer's storage, no delegates.
- **Serialisable.** Round-trips to a stable binary and a stable JSON form. This
  is what makes seam recording and per-layer golden tests possible (ADR-013).
- **Versioned.** Every payload carries `schemaVersion`. A consumer rejects a
  version it does not understand rather than guessing.
- **Content-addressed.** Every payload carries `contentHash` (of itself) and
  `inputHashes` (of the payloads that produced it), forming the Merkle chain.

## 2. Field table format

Every contract documents its fields in a table with these columns:

| Column | Meaning |
|---|---|
| Field | Name, in the payload's own casing |
| Type | The primitive: `double` for every dimensioned and dimensionless quantity, `int`, `bool`, an enum, or `SlotId` |
| Unit | Explicit, and equal to the unit suffix the field name carries (`00-overview/02-conventions.md` §5.2). `—` for dimensionless fields, which then declare a kind and a range (§2.3) |
| Card. | `1`, `[H]` (per slot), `[S,H]` (per scenario/slot), `[B]` (per block), `0..1` (optional) |
| Range | Admissible values; violation is a contract failure |
| Null | Whether absent is legal, and what absent means |
| Default | The conservative default (ADR-014 rung 5), or `—` if none exists |
| MaxStale | Maximum age before quality degrades, in `SlotSpan` |
| Notes | |

**Rule:** a field with `Null = yes` must have either a `Default` or a documented
escalation. A nullable field with no defined behaviour on absence is an
incomplete contract.

## 3. Universal invariants

Applied at every seam by a shared validator, invoked on both sides.

| ID | Invariant |
|---|---|
| `INV-G-01` | No `NaN`, no infinity, in any numeric field |
| `INV-G-02` | Three mechanical equalities, all read off the field table. **(a)** Every dimensioned field's identifier ends in its unit — `Mw`, `Mwh`, `EurPerMwh`, `EurPerMw`, `EurPerMwH`, `Eur` — and that suffix **equals** the Unit column of its own row. **(b)** Every power and energy field carries a frame prefix — `pBatt*` or `soc*` (battery), `pPoi*` (POI), or the market product's own name — and appears in that frame's section. **(c)** Every dimensionless field carries `—` in the Unit column plus a declared kind and a closed range (`00-overview/02-conventions.md` §2.3). A suffix that disagrees with the Unit column, a power or energy field naming no frame, and a dimensionless field with no range are each a violation. |
| `INV-G-03` | `schemaVersion` is recognised by the consumer |
| `INV-G-04` | `contentHash` matches the payload; `inputHashes` are present and non-empty |
| `INV-G-05` | No payload contains a wall-clock timestamp taken at construction |
| `INV-G-06` | All slot-indexed arrays have the length declared by `horizon` |
| `INV-G-07` | All scenario-indexed arrays share the scenario axis length and ordering |
| `INV-G-08` | `asOf` is non-decreasing across successive payloads on the same seam |
| `INV-G-09` | Every field marked `Null = no` is present |
| `INV-G-10` | Ranges hold for every element, not merely the first |

Validation is **on by default in every environment**, including production. It
is a few microseconds against a solve measured in milliseconds. The temptation to
disable it in production is precisely the temptation to stop noticing corruption.

## 4. Validation placement

Each seam is validated **twice**: the producer validates before emitting
(catching its own bugs at the source, with its own context available) and the
consumer validates on receipt (defending against a producer it does not control).

Producer-side failure → the producing layer's degradation ladder.
Consumer-side failure → `HALT` (ADR-014), because a contract violation means the
producer is in an unknown state and nothing downstream can be trusted.

## 5. Versioning policy

- **Additive change** (new optional field with a default) → minor version bump;
  old consumers keep working.
- **Any other change** — removing a field, changing a type, changing a unit,
  changing a semantic, tightening a range → **major version bump**, and the
  consumer must be updated in the same commit.
- **Unit changes are always major.** A field moving from EUR/MWh to EUR/MW — an
  energy price becoming a capacity price — is the archetypal catastrophic change.
  It cannot be *silent*: `00-overview/02-conventions.md` §5.2 requires the
  identifier's unit suffix to change with the unit, so the rename is the alarm
  and `INV-G-02` fails the build if the two disagree.

The change discipline in the root `README.md` applies: a contract change requires
the doc, the version and the conformance tests in one commit.

## 6. Quality metadata

Fields subject to data quality carry a parallel quality channel rather than
embedding it in the value:

```
QualityStamp { provenance, quality, knowledgeTime, sourceId, maxStaleBreached }
```

Carried per series (not per element) where quality is uniform across the array,
and per element where it is not (e.g. a forecast with a measured prefix and a
forecast suffix). Consumers map quality to risk parameters, never to branches
(ADR-014 §1).

## 7. Identity and keying

Every payload carries:

```
tickId       — (SlotId, clock, sequence)  — identifies the tick uniquely
asOf         — knowledge-time boundary for every fact inside
horizon      — SlotSpan covered
assetId      — the battery
poiId        — the connection point
manifestId   — the run manifest hash (ADR-013)
```

Multi-asset is not in scope for v1, but `assetId` and `poiId` are present in
every contract from the start so that adding a second asset is not a schema
break.
