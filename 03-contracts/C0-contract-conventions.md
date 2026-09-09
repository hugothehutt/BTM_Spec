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

**`C2` is at 5.0; `C5` is at 4.0; `C1`, `C3` and `C4` are at 3.0; `C6` is at
2.0.** Removing `prorationFactor` from `EpigraphTerm` and adding
`accountingPeriod` in its place
([ADR-020](../01-adr/ADR-020-peak-charge-carries-no-proration.md)) is one removal
and one required addition, each major on its own under the rule above. The factor
was never authorised by any ADR — `ADR-008`:27 gives the epigraph's cost as
`c·z` — and `L5`:225-228 always settled the charge without it, so the bump also
closes a definitional split between the two sides of one term. `C5` is untouched:
its peak accounting never carried the factor.

**`C2` and `C5` went to 4.0 together.**
Removing `ActivationRisk` from the `EconomicEffect` enumeration
([ADR-019](../01-adr/ADR-019-activation-carries-no-cost-term.md)) narrowed the
value set of `effect`, `effectCoverage` and `unclaimedEffects` (`C2`) and of
`realisedByEffect` and `plannedByEffect` (`C5`). No field table changed, but a
range that tightens is major under the rule above, and here for a concrete
reason: a producer still emitting the member would pass field validation and be
rejected by `INV-V-02` one stage later, which is the silent failure the major
bump exists to prevent. `C1`, `C3`, `C4` and `C6` never carried the enumeration.

**`C1` through `C5` went to 3.0 at once.** The pay-as-bid cascade
([ADR-018](../01-adr/ADR-018-reserve-bid-price-formation.md)) changed a semantic
on five seams at once: `afrrCapPriceEurPerMwH` became the *marginal* price rather
than an unqualified "capacity price belief" (`C1`), `capacityValueCurve*` became
the pay-as-bid envelope rather than a curve in an undefined clearing model (`C2`),
`C3` gained `reserveShadowValueEurPerMwH` and two invariants, `C4` renamed
`clearingPrice*EurPerMwH` to `awardedPrice*EurPerMwH` and deleted the duplicate
`AfrrCapacity` fill, and `C5` gained `reserveBidErrorEur`. Under the rule above
each is major on its own. `C6` is untouched: it reads the commitment ledger, whose
`priceEurPerMwH` means the same number before and after — pay-as-bid makes an
open order's limit price and an award's paid price the same value.

**`C1` through `C6` were at 2.0.** The MW/MWh cascade
([Propagate MW/MWh and retire typed quantities](https://github.com/hugothehutt/BTM_Spec/issues/41))
changed a unit on every one of them — `peakPriceEurPerMw` became a capacity
price with the accounting period an explicit input, `vSocSlopesEurPerMwh` moved to
EUR/MWh, and every power and energy field moved to MW/MWh — and split `C3`/`C4`'s
single `limitPrice`/`volume` pair, whose unit depended on `market`, into two
exclusive pairs. Under the rule above each of
those is major on its own, so there is no 1.x of any contract that a consumer
should still be reading. No 2.x consumer accepts a 1.x payload: `INV-G-03`
rejects an unrecognised major rather than guessing at it.

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
