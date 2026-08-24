# C6 — State/Value Store → Layers (read side)

**Payload:** `StateSnapshot` **Version:** 1.0 **Direction:** L0 → L1, L2, L3

The read side of L0. ADR-006 asserts that the state store is "a contract with a
field table, a version and invariants like every other seam"; this document is
that contract. It was missing from the original five-seam framing and is
numbered C6 rather than folded into C5 because it has a different direction, a
different set of consumers, and a different failure policy.

`StateSnapshot` is taken **once**, immutably, at the top of every tick. Every
layer that needs state reads this one object. No layer consults L0 directly
mid-tick, which is what makes the "no read of a same-tick write" audit
(`04-compliance/T3`) mechanically decidable.

`StateUpdate` (C5) is the write side. The two are deliberately **not** the same
type: the snapshot is a coherent read view of the whole store at an instant; the
update is a delta produced by one component at the end of a tick.

---

## 1. Envelope

C0 §7 fields, plus:

| Field | Type | Notes |
|---|---|---|
| `snapshotAt` | `SlotId` | Tick at which the snapshot was taken |
| `stateAsOf` | `SlotId` | Latest tick whose `StateUpdate` is included — **always < `snapshotAt`** |
| `lagSlots` | `SlotSpan` | `snapshotAt − stateAsOf`; the explicit feedback lag |
| `isColdStart` | `bool` | True on the first tick after a restart, before reconciliation completes |

`lagSlots` is a first-class field rather than an implementation detail. Consumers
are expected to reason about it: a Planner that knows the peak state is three
slots stale treats the gap conservatively rather than assuming it was empty.

---

## 2. Peak state (per active regime)

Mirrors C5 §2. Read by `PeakView` (L2) and by the escalation policy (L3).

| Field | Type | Unit | Range | Null | Default | Notes |
|---|---|---|---|---|---|---|
| `regime` | `TariffRegimeId` | — | — | no | — | |
| `pPoiRealisedPeakMw` | `double` | MW | `≥0` | no | — | The `EpigraphTerm` floor |
| `realisedPeakSlot` | `SlotId` | — | — | no | — | Diagnostic |
| `periodStart`,`periodEnd` | `SlotId` | — | — | no | — | Local-calendar derived |
| `peakIsProvisional` | `bool` | — | — | no | `true` | |
| `unsettledGapFrom` | `SlotId` | — | — | no | — | Peak authoritative only up to here |
| `pPoiHeadroomToPeakMw` | `double` | MW | `≥0` | no | `0` | Conservative default: no headroom |
| `peakCritical` | `bool` | — | — | no | `true` | Conservative default: treat as critical |

**The unsettled gap is not optional to handle.** Between `unsettledGapFrom` and
`snapshotAt`, no settled meter data exists. The Planner must substitute its own
modelled POI trajectory over that window as a provisional peak contribution:

```
effectiveFloor = max( pPoiRealisedPeakMw,
                      max over slots in [unsettledGapFrom, snapshotAt) of
                          modelledPoiImport[t] )
```

Assuming the gap contained nothing is the single most likely way to set a new
monthly peak by accident, because the gap is exactly the window in which the
engine was most recently active.

---

## 3. Qualification state

Mirrors C5 §3. Read by `TariffView` and `PeakView` (L2), and by the slow loop.

| Field | Type | Unit | Null | Default | Notes |
|---|---|---|---|---|---|
| `pPoiAnnualEnergyMwh` | `double` | MWh | no | — | |
| `pPoiAnnualPeakMw` | `double` | MW | no | — | Denominator of full-load hours |
| `fullLoadHours` | `double` | h | no | — | Dimensionless-per-C0 exception: declared unit `h` |
| `pPoiHlzfPeakMw` | `double` | MW | no | — | |
| `qualificationStatus` | `QualificationStatus` | — | no | `AtRisk` | Conservative default |
| `qualificationMarginHours` | `double` | h | no | `0` | Conservative default: no margin |
| `projectedYearEndFlh` | `double` | h | yes | — | From the slow loop |
| `qualCritical` | `bool` | — | no | `true` | Conservative default |

Every conservative default in this section biases toward protecting the
qualification, because the asymmetry is extreme: the downside of an unnecessary
protective bound is a day's foregone spread; the downside of losing a §19(2)
reduction is a year of network-charge relief (ADR-011).

---

## 3.1 Delineation state (ADR-017)

Mirrors C5 §3.1. Read by `DelineationView` (L2) and by L3, which carries the
accumulators forward as MILP variables over the horizon.

| Field | Type | Unit | Null | Default | Notes |
|---|---|---|---|---|---|
| `mtdGridImportMwh`, `mtdStorageChargeMwh`, `mtdStorageDischargeMwh`, `mtdSimultaneousGridChargeMwh`, `mtdStorageExportMwh`, `mtdDirectFeedInAwPosMwh`, `mtdStorageExportAwPosMwh` | `double` | MWh | no | `0` | The seven accumulators of C5 §3.1, unchanged |
| `pvShare`, `awPositiveShare` | `double` | — | no | `0` | Conservative defaults: no PV attribution, no AW>0 share |
| `saldierungsfaehigMwh`, `foerderfaehigMwh`, `umlagebelasteterNetzbezugMwh`, `fremdtankMwh` | `double` | MWh | no | `0` | |
| `monthStart`, `monthEnd` | `SlotId` | — | no | — | |
| `slotsToMonthEnd` | `SlotSpan` | — | no | — | `V_del` coordinate |
| `throughputBoundMet` | `bool` | — | no | `false` | False forces the Planner to carry `(12)` explicitly rather than assume it away |
| `delineationIsProvisional` | `bool` | — | no | `true` | |
| `unsettledGapFrom` | `SlotId` | — | yes | — | |

The conservative defaults bias toward **understating** delineation value: a zero
`awPositiveShare` prices the green route at nothing, and `throughputBoundMet = false`
costs binaries rather than correctness. Both are the safe direction — the failure mode
worth avoiding is an engine that charges from the grid on the strength of a route it
turns out not to have.

`DelineationView` reads this block and the `MW_month` belief, and publishes the seven
`λ_j` (ADR-017). It reads no other view's output.

---

## 4. Commitment ledger (read view)

Mirrors C5 §4. Read by L3.

| Field | Type | Notes |
|---|---|---|
| `entries` | `CommitmentEntry[]` | Ordered by `(slot, entryId)` for determinism |
| `confirmedReserveUp`,`…Dn` | `double[B]` | Denormalised for the Planner's hard constraints |
| `confirmedSpotPosition` | `double[H]` | Denormalised, signed, market frame |
| `pendingExposure` | `double[H]` | Signed; probabilistic, not an obligation |
| `requiredSocCorridor` | `(EnergyKwh, EnergyKwh)[H]` | Union of all confirmed `feasibilityRequirement`s |
| `ledgerConsistent` | `bool` | False triggers `SAFE` (ADR-014) |

The denormalised views exist because the Planner needs them on every solve and
recomputing them from `entries` per tick is both wasteful and a chance to
disagree. `INV-G-11` asserts the denormalised views reconcile with `entries`.

`requiredSocCorridor` is the single most safety-critical field in this contract.
It is a hard constraint in **every** degradation mode including `SAFE`
(ADR-014 §3), and a plan that violates it never reaches C3 (`INV-P-02`).

---

## 5. Value function

Read by L2 (`OppCostView`), which republishes it across C2 §4.

| Field | Type | Unit | Null | Notes |
|---|---|---|---|---|
| `vSocBreakpointsXMwh` | `double[n]` | MWh | no | Strictly increasing, spanning `[socMinMwh, socMaxMwh]` |
| `vSocBreakpointsYEur` | `double[n]` | EUR | no | |
| `vSocSlopesEurPerMwh` | `double[n-1]` | EUR/MWh | no | Strictly decreasing (`INV-V-11`) |
| `conditionedOn` | `ValueFunctionContext` | — | no | `(peakState, qualState, calendarContext)` |
| `producedAt` | `SlotId` | — | no | |
| `validityHorizon` | `SlotSpan` | — | no | |
| `isStale` | `bool` | — | no | `snapshotAt − producedAt > validityHorizon` |
| `contextDrift` | `double` | — | no | Distance from the state `V` was fitted at |
| `artefactHash` | `string` | — | no | Merkle chain |

**Staleness is never silent.** If `isStale`, the mode escalates to at least
`DEFENSIVE` and `stalenessPenalty` shrinks `V`'s slopes toward zero — which makes
the Planner progressively indifferent to terminal SOC rather than confidently
wrong about it. `04-compliance/T6` includes a stale-artefact audit asserting the
engine refuses a value function beyond its validity horizon rather than using it
quietly.

---

## 6. Mode and quality

| Field | Type | Notes |
|---|---|---|
| `degradationMode` | `DegradationMode` | The mode in force entering this tick |
| `modeEnteredAt` | `SlotId` | For hysteresis accounting |
| `consecutiveBadTicks`,`consecutiveGoodTicks` | `int` | Hysteresis counters (ADR-014 §2) |
| `seriesQualityState` | `map<SeriesId, QualityStamp>` | Last known per series |

**The mode in this snapshot is a floor, not a verdict.** L1 may raise it within
the tick from its own same-tick inputs — a critical series missing now escalates
now, it does not wait a tick — but may never lower it. De-escalation is central
and hysteretic. This is not a violation of ADR-006: raising a floor from a
component's own inputs is not a read of another component's same-tick write.

---

## 7. Model artefact versions

| Field | Type | Notes |
|---|---|---|
| `artefactVersions` | `map<ArtefactKind, ContentHash>` | Scenario generator, value function, fill model, tier-3 surface, market calendar, tzdata |

Carried so that every downstream artefact can record which model versions
produced it, and so a backtest can be re-run with the versions that were actually
in force (ADR-013).

---

## 8. Invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-G-11` | Denormalised ledger views reconcile with `entries` | `HALT` |
| `INV-G-12` | `stateAsOf < snapshotAt`; `lagSlots` consistent with both | `HALT` — a zero lag means a same-tick read |
| `INV-G-13` | The snapshot is immutable for the duration of the tick; no consumer holds a mutable reference | structural, audited in `T3` |
| `INV-G-14` | Every field marked `Null = no` is present, or the default is applied and recorded | escalate |
| `INV-G-15` | `requiredSocCorridor` is non-empty wherever `confirmedReserveUp` or `…Dn` is non-zero | `HALT` |
| `INV-G-16` | On `isColdStart`, no speculative intent is emitted until reconciliation completes | `HALT` |

`INV-G-12` deserves emphasis: it is the mechanical enforcement of ADR-006. A lag
of zero would mean some component read state written in the same tick, which is
precisely the cycle the architecture exists to exclude.

---

## 9. What deliberately does *not* cross C6

- Any belief or forecast. Those come from L1 via C1.
- Any valuation output. L0 stores `V` as an artefact; it does not store prices.
- Raw settlement detail. Only the aggregated state the next tick needs.
- Mutable references to the store. The snapshot is a value.
