# C2 — Valuation → Planner

**Payload:** `ValuationBundle` **Version:** 1.0 **Direction:** L2 → L3

The load-bearing seam. Everything the Planner knows about economics arrives here,
expressed in the closed algebra of ADR-008. The Planner has **no** access to
Belief, to prices, or to scenario arrays.

If you read one contract, read this one.

---

## 1. Envelope

C0 §7 fields, plus:

| Field | Type | Notes |
|---|---|---|
| `compositionOrder` | `StageId[]` | The stages actually applied, in order — recorded for audit |
| `effectCoverage` | `map<EconomicEffect, ViewId>` | Which view owned which effect this tick (ADR-009) |
| `unclaimedEffects` | `EconomicEffect[]` | Priced at zero; empty is the expected case |
| `riskProfile` | `RiskProfile` | See §6 |
| `problemClassHint` | `ProblemClassHint` | See §7 |

## 2. Decision variable vocabulary

Terms refer to decision variables by a closed enumeration. The Planner owns the
variables; Valuation may only name them.

| Symbol | Meaning | Frame | Index |
|---|---|---|---|
| `pCharge` | Battery charge power | battery | `[H]` |
| `pDischarge` | Battery discharge power | battery | `[H]` |
| `soc` | State of charge, end of slot | — | `[H]` |
| `socTerminal` | SOC at end of horizon | — | 1 |
| `pPoi` | POI net power | POI | `[H]` |
| `zPeak` | Peak epigraph variable, per regime | POI | per regime |
| `vDaBuy`,`vDaSell` | DA volume | market | `[H]` |
| `vIdBuy`,`vIdSell` | Intraday volume | market | `[H]` |
| `rUp`,`rDn` | aFRR capacity offered | market | `[B]` |
| `eAfrrUp`,`eAfrrDn` | aFRR activated energy | market | `[S,H]` |
| `eImbalance` | Imbalance exposure | market | `[S,H]` |
| `q` | Curtailed generation (ADR-016) | — | per plant, `[H]` |
| `aDel` | Month-to-date delineation accumulator `A_j` (ADR-017) | — | per accumulator |

`aDel` spans `j ∈ {(3),(5),(6),(9),(11),(26),(29)}`. Like `zPeak` it is not a free
decision — the Planner's state equations (`L3` §2) pin it — but a term must be able
to name it, because `DelineationView` prices it at `λ_j = ∂V_del/∂A_j`.

**Meter-shaped only.** No symbol attributes energy to a path or to a named
installation. The delineation's own per-slot variables — `Z1NB`, `Z1NE`, `Z2V`,
`Z2E` and the `MIN` selectors — are Planner-internal and deliberately absent:
ADR-017 forbids any lever from carrying a delineation coefficient, so Valuation
never names them. `INV-V-17`.

Adding a symbol is a **major version bump**. This list is the shared vocabulary
of the two layers and must not drift.

## 3. Term shapes

Exactly five (ADR-008). Every term carries a common header.

### Common header

| Field | Type | Notes |
|---|---|---|
| `termId` | `string` | Stable, unique within the bundle |
| `originView` | `ViewId` | Which view emitted it |
| `effect` | `EconomicEffect` | From the closed enumeration; basis of the exclusivity check |
| `base` | `TermBase` | `PoiImport \| BatteryThroughput \| MarketVolume \| TerminalSoc \| ReserveCapacity \| Delineation` |
| `slots` | `SlotRange` | Which slots it applies to |
| `mandatory` | `bool` | If true, dropping it invalidates the plan (peak protection is mandatory) |
| `confidence` | `Confidence` | Propagated from input quality; feeds `riskProfile`, never a branch |

### 3.1 `LinearTerm`

| Field | Type | Unit | Notes |
|---|---|---|---|
| `variable` | `VarSymbol` | — | From §2 |
| `coefficient` | `double[H]` | EUR per variable unit | Per slot |
| `sense` | `Maximize \| Minimize` | — | |

### 3.2 `PwlTerm`

| Field | Type | Unit | Notes |
|---|---|---|---|
| `variable` | `VarSymbol` | — | The abscissa |
| `breakpointsX` | `double[n]` | variable unit | Strictly increasing |
| `breakpointsYEur` | `double[n]` | EUR | |
| `curvature` | `Concave \| Convex \| General` | — | **Verified against breakpoints, not trusted** |
| `sense` | `Maximize \| Minimize` | — | |
| `extrapolation` | `Clamp \| Forbid` | — | `Forbid` adds bounds at the end breakpoints |

**Binary cost rule.** `(Concave, Maximize)` and `(Convex, Minimize)` require **no**
binaries and are LP-exact. Any other combination, and any `General`, requires
SOS2 or binaries. The bundle must declare the count in `problemClassHint` (§7),
so a solve-time regression is attributable to a specific economic modelling
choice rather than mysterious.

### 3.3 `EpigraphTerm`

| Field | Type | Unit | Notes |
|---|---|---|---|
| `epigraphVar` | `VarSymbol` | — | e.g. `zPeak` |
| `dominates` | `VarSymbol` | — | e.g. `pPoi` |
| `overSlots` | `SlotId[]` | — | Subset — this is how HLZF is expressed |
| `floor` | `double` | variable unit | Realised peak so far, from L0 |
| `unitPriceEurPerMw` | `double` | EUR/MW | |
| `prorationFactor` | `double` | — | `fraction`, range `[0,1]`. Fraction of the accounting period inside this horizon |

`prorationFactor` matters: the horizon is shorter than the accounting period, so
the *full* period charge must not be applied to a partial window. The remaining
period value is carried by `V` (ADR-007). Mis-setting this is a classic
overweighting of peak; `INV-V-13` asserts it is in `[0,1]` and consistent with
the calendar.

### 3.4 `BoundTerm`

| Field | Type | Unit | Notes |
|---|---|---|---|
| `target` | `VarSymbol` or `LinearExpr` | — | |
| `lower`,`upper` | `double[H]` | target unit | Either may be `null` for one-sided |
| `reason` | `BoundReason` | — | `Physical \| Contractual \| Liquidity \| Risk \| Regulatory` |
| `softPenalty` | `double?` | EUR per unit violation | `null` = hard bound |

`reason` is not decoration. Feasibility restoration (`L3` §6) relaxes bounds in a
declared order — `Risk` first, then `Liquidity`, never `Physical` or
`Regulatory`. Without the tag, an infeasible model can only be relaxed blindly.

### 3.5 `CouplingConstraint`

| Field | Type | Notes |
|---|---|---|
| `kind` | `ReserveCorridor \| ReservePowerHeadroom \| Commitment \| ChanceConstraint \| Custom` | Closed set |
| `rows` | `LinearRow[]` | `Σ coef·var {≤,=,≥} rhs`, per slot or per block |
| `scenarioScope` | `All \| PerScenario \| ChanceLevel(ε)` | |
| `introducesBinaries` | `int` | Declared, and checked against what is built |

## 4. The value function

Carried separately from the term list because it is structurally distinctive and
because its staleness is handled specially.

| Field | Type | Unit | Notes |
|---|---|---|---|
| `vSocBreakpointsXMwh` | `double[n]` | MWh | Strictly increasing, spanning `[socMinMwh, socMaxMwh]` |
| `vSocBreakpointsYEur` | `double[n]` | EUR | |
| `vSocSlopesEurPerMwh` | `double[n-1]` | EUR/MWh | **Strictly decreasing** — concavity, asserted |
| `conditionedOn` | `ValueFunctionContext` | — | `(peakState, qualState, calendarContext)` |
| `producedAt` | `SlotId` | — | When the slow loop produced it |
| `validityHorizon` | `SlotSpan` | — | Beyond this, staleness penalty applies |
| `stalenessPenalty` | `double` | — | `fraction`, range `[0,1]`. Shrink factor applied when stale |
| `artefactHash` | `string` | — | For the Merkle chain |

## 5. Reserve envelope

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `rUpMaxMw`,`rDnMaxMw` | `double` | MW | `[B]` | Envelope from prequalification and asset limits |
| `capacityValueCurveUp`,`…Dn` | `PwlTerm` | — | `[B]` | Concave in offered MW; the expected value of the bid curve |
| `sustainDuration` | `SlotSpan` | — | 1 | Echoed from C1 for the corridor constraints |
| `deliveryObligationMw` | `double` | MW | `[B]` | Already-confirmed awards from L0 — a **hard** commitment |

`deliveryObligationMw` is separated from the offer decision deliberately: confirmed
awards are physical obligations that survive every degradation mode (ADR-014 §3),
whereas offers are decisions.

## 6. Risk profile

How input quality reaches the optimisation without becoming control flow.

| Field | Type | Notes |
|---|---|---|
| `cvarLevel` | `double` | e.g. 0.95; widened when quality degrades |
| `cvarWeight` | `double` | Weight on the CVaR term vs. expectation |
| `chanceLevel` | `double` | ε for SOC feasibility chance constraints |
| `positionScale` | `double` | `[0,1]` multiplier on speculative position bounds |
| `peakSafetyMarginMw` | `double` | Added to the epigraph floor under degraded load quality |
| `degradationMode` | `DegradationMode` | Echoed |
| `driverSummary` | `string[]` | Which quality issues moved which parameter — for the audit trail |

## 7. Problem class hint

Computed by the composer from the terms present, before the solver sees anything.

| Field | Type | Notes |
|---|---|---|
| `isLinearProgram` | `bool` | True when no term introduces a binary |
| `binaryCount` | `int` | Total |
| `binariesByOrigin` | `map<TermId, int>` | **Attribution** — which term costs what |
| `sos2SetCount` | `int` | |
| `scenarioCount` | `int` | |
| `estimatedRowCount`,`estimatedColCount` | `int` | |

This is the instrument that keeps solve time explainable. When a solve slows
down, `binariesByOrigin` names the term responsible.

## 8. Contract-specific invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-V-01` | No `(effect, variable, slot)` triple is priced by more than one term | `HALT` — double count |
| `INV-V-02` | Every term's `originView` is the owner of its `effect` in the ownership matrix | `HALT` |
| `INV-V-03` | Every term references only symbols in §2 | `HALT` |
| `INV-V-04` | Every mandatory term is present | `HALT` |
| `INV-V-05` | `unclaimedEffects` are logged; if any is in the critical set, escalate | escalate |
| `INV-V-06` | No scenario array and no Belief handle appears anywhere in the bundle | `HALT` |
| `INV-V-07` | `compositionOrder` satisfies the stage precondition table (ADR-009) | `HALT` |
| `INV-V-11` | `vSocSlopesEurPerMwh` strictly decreasing (concavity) | `HALT` |
| `INV-V-12` | Declared `curvature` matches the breakpoints | `HALT` |
| `INV-V-13` | `prorationFactor ∈ [0,1]` and consistent with the calendar | `HALT` |
| `INV-V-14` | `binariesByOrigin` sums to `binaryCount` | warn |
| `INV-V-15` | Every `BoundTerm` has a `reason`; no `Physical` bound is soft | `HALT` |
| `INV-V-16` | `FillProbView` emits only `BoundTerm`; it has no priced term | `HALT` |
| `INV-V-17` | No term references a provenance flow — energy attributed to a path or to a named installation (§2) | `HALT` |

## 9. What deliberately does *not* cross C2

- Prices. The Planner never sees a price series; it sees coefficients on
  variables. This is what stops it re-deriving economics.
- Scenario arrays. Scenario structure reaches the Planner only through
  `CouplingConstraint` rows with `scenarioScope`, and through terms already
  aggregated by Valuation.
- Delegates or expression trees (ADR-008, rejected alternatives).
- Anything from Belief that Valuation merely passed through unpriced. If the
  Planner needs it, a view must own it and price or bound it.
