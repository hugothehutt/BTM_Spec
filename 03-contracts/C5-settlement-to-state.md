# C5 — Settlement → State/Value Store

**Payload:** `StateUpdate` **Version:** 1.0 **Direction:** L5 → L0

The **only** backwards edge in the system (ADR-006). Written at the end of a
tick, read at the top of the next. Everything here is therefore, by
construction, at least one tick old when it is used — and that lag is explicit
and priceable rather than hidden.

---

## 1. Envelope

C0 §7 fields, plus `isFinal`, `revisionOf`, and `effectiveFrom` (the slot from
which this state applies).

## 2. Peak state

| Field | Type | Unit | Card. | Notes |
|---|---|---|---|---|
| `pPoiRealisedPeakMw` | `double` | MW | per regime | The epigraph floor for the next tick |
| `realisedPeakSlot` | `SlotId` | — | per regime | When it occurred — diagnostically important |
| `periodStart`,`periodEnd` | `SlotId` | — | per regime | Accounting period bounds, local-calendar derived |
| `peakIsProvisional` | `bool` | — | per regime | True while meter data is unfinal |
| `unsettledGapFrom` | `SlotId` | — | per regime | Earliest slot not yet settled |
| `pPoiHeadroomToPeakMw` | `double` | MW | per regime | Distance from `pPoiRealisedPeakMw` to the level at which a new peak would be set — the continuous analogue of `qualificationMarginHours` |
| `peakCritical` | `bool` | — | per regime | `pPoiHeadroomToPeakMw` inside the configured margin, **or** the horizon contains a slot whose CVaR peak belief exceeds `pPoiRealisedPeakMw`. Forces tier escalation (ADR-010) and the protective bound (ADR-011) |

`peakCritical` is written here and read by L2 (`PeakView`) and L3 (escalation
policy). It is the peak-side counterpart of `qualCritical` in §3; both are
computed by Settlement, never by the Planner, so that the escalation trigger
cannot be influenced by the thing it is meant to guard.

`unsettledGapFrom` is what makes the lag safe. The Planner knows the realised
peak is authoritative only up to that slot, and treats the gap conservatively —
using its own modelled POI trajectory as a provisional peak contribution rather
than assuming the gap contained nothing.

## 3. Tariff qualification state (ADR-011)

| Field | Type | Unit | Notes |
|---|---|---|---|
| `pPoiAnnualEnergyMwh` | `double` | MWh | Accumulated in the qualification year |
| `pPoiAnnualPeakMw` | `double` | MW | Denominator of full-load hours |
| `fullLoadHours` | `double` | h | `annualEnergy / annualPeak` |
| `pPoiHlzfPeakMw` | `double` | MW | Peak within HLZF windows only |
| `qualificationStatus` | `Qualified \| AtRisk \| Lost \| NotApplicable` | — | Per applicable §19(2) path |
| `qualificationMarginHours` | `double` | h | Distance to the threshold — drives `qualCritical` |
| `projectedYearEndFlh` | `double` | h | Forward projection from the slow loop |
| `qualCritical` | `bool` | — | Within the configured margin; forces tier escalation and the protective bound |

The margin, not just the status, is carried. A binary "qualified" flag tells the
engine nothing until the moment it is too late; the margin lets the value
function price the approach to the cliff continuously.

## 3.1 Delineation state (ADR-017)

Month-to-date accumulators of the Abgrenzungsoption machinery
(`00-overview/03-mispel-reference.md`). Computed by Settlement Phase A from `Z1`/`Z2`
alone. **Seven accumulators are carried and no others**: everything from `(10)` to
`(33)` is a closed-form function of them.

| Field | Formula | Type | Unit | Null | Default | Notes |
|---|---|---|---|---|---|---|
| `mtdGridImportMwh` | `(3)` | `double` | MWh | no | `0` | gesamter Netzbezug |
| `mtdStorageChargeMwh` | `(5)` | `double` | MWh | no | `0` | Verbrauch im Stromspeicher |
| `mtdStorageDischargeMwh` | `(6)` | `double` | MWh | no | `0` | Erzeugung im Stromspeicher |
| `mtdSimultaneousGridChargeMwh` | `(9)` | `double` | MWh | no | `0` | `∑ MIN[Z1NB¼; Z2V¼]` |
| `mtdStorageExportMwh` | `(11)` | `double` | MWh | no | `0` | `∑ MIN[Z1NE¼; Z2E¼]` |
| `mtdDirectFeedInAwPosMwh` | `(26)` | `double` | MWh | no | `0` | direct feed-in, AW>0 hours only |
| `mtdStorageExportAwPosMwh` | `(29)` | `double` | MWh | no | `0` | storage feed-in, AW>0 hours only |

Derived and published for readability and for the escalation triggers. Recomputable
from the seven above; carried so that four layers do not each re-derive them.

| Field | Formula | Type | Unit | Notes |
|---|---|---|---|---|
| `pvShare` | `(10)/(5)` | `double` | — | PV share of charging; sets the route split |
| `awPositiveShare` | `(30)` | `double` | — | A ratio, so a late-month AW=0 export dilutes the whole month's `(28)` |
| `saldierungsfaehigMwh` | `(16)` | `double` | MWh | grey route |
| `foerderfaehigMwh` | `(32)` | `double` | MWh | green route |
| `umlagebelasteterNetzbezugMwh` | `(21)` | `double` | MWh | base of `EnfgLevies` |
| `fremdtankMwh` | `(12)` | `double` | MWh | Expected zero; see `INV-S-16` |

Calendar and status:

| Field | Type | Unit | Null | Default | Notes |
|---|---|---|---|---|---|
| `monthStart`, `monthEnd` | `SlotId` | — | no | — | Europe/Berlin civil-calendar bounds, derived via `CivilCalendar` (`INV-S-12`) |
| `slotsToMonthEnd` | `SlotSpan` | — | no | — | Coordinate of `V_del`; the accumulators reset at `monthEnd` |
| `throughputBoundMet` | `bool` | — | no | `false` | Charge throughput exceeds `η_d·E_usable/(1−η_rt)`, so `(12) = 0` is arithmetically assured (`INV-S-16`). Conservative default: not assured |
| `delineationIsProvisional` | `bool` | — | no | `true` | True while the month's meter data is unfinal |
| `unsettledGapFrom` | `SlotId` | — | yes | — | Earliest slot not yet reflected in the accumulators |

**The accumulators reset to zero at `monthEnd`, and SOC does not.** That discontinuity
is in value, not in physics: the same kWh in the same battery is worth a different
amount either side of the boundary because the aggregates its route ran through have
vanished. `slotsToMonthEnd` is what lets `V_del` price the approach continuously, and
it is the delineation counterpart of `qualificationMarginHours`.

`unsettledGapFrom` carries the same meaning as in §2: the Planner treats the gap with
its own modelled trajectory rather than assuming it contributed nothing.

## 4. Commitment ledger

| Field | Type | Notes |
|---|---|---|
| `entryId` | `string` | |
| `kind` | `SpotPosition \| ReserveAward \| OpenOrder` | |
| `status` | `Pending \| Confirmed \| Settled \| Cancelled` | Pending vs. Confirmed is load-bearing (ADR-006) |
| `market`,`productId`,`slot`/`block` | — | |
| `signedVolumeMwh` | `double` | Market frame |
| `priceEurPerMwh` | `double`/`double` | |
| `feasibilityRequirement` | `SocCorridor?` | For reserve awards: the corridor that must be maintained |

`Confirmed` reserve awards carry a `feasibilityRequirement` that becomes a hard
constraint in every degradation mode. `Pending` entries are exposure, not
obligation, and the Planner treats them probabilistically.

## 5. Realised P&L attribution

Decomposed into the **same** `EconomicEffect` enumeration used by Valuation
(ADR-009), so planned and realised value are comparable term by term.

| Field | Type | Unit | Notes |
|---|---|---|---|
| `realisedByEffect` | `map<EconomicEffect, Money>` | EUR | |
| `plannedByEffect` | `map<EconomicEffect, Money>` | EUR | What Valuation expected |
| `unexplainedEur` | `double` | EUR | **Mandatory bucket** — the *effect-level* residual, i.e. realised cash not attributable to any `EconomicEffect` (`INV-S-02`) |
| `unexplainedRatio` | `double` | — | Alerted above a threshold |

The `unexplainedEur` bucket is not optional and must not be allowed to be silently
absorbed elsewhere. A rising unexplained ratio is the single best early warning
that a term definition has drifted between Valuation and Settlement.

**Do not confuse this with `unexplainedError` in §6.** They are different
quantities: `unexplainedEur` here is the residual of the *cash* decomposition;
`unexplainedError` in §6 is the residual of the *error* decomposition. They are
never netted against one another, and each has its own invariant (`INV-S-02`
and `INV-S-03` respectively).

## 6. Error decomposition

The mechanism that makes the system improvable rather than merely operable.
The gap between planned value and realised value splits into four attributable
buckets:

| Field | Type | Meaning | Fix lives in |
|---|---|---|---|
| `optimalityGapEur` | `double` | Value given up because the tier actually used was not Tier 1 | L3 / ADR-010 |
| `forecastErrorEur` | `double` | Value lost because the world differed from the belief the plan was built on | L1 / scenario model |
| `modelErrorEur` | `double` | Value lost because the valuation was wrong *given* the belief | L2 |
| `executionSlippageEur` | `double` | Value lost between intent and fill | quoting policy |

### The anchor, and why it is not the used-tier plan value

The decomposition **telescopes from the Tier-1 reference value**, not from the
value of the plan that was actually used:

```
Ĵ₁   Tier-1 objective on the tick's beliefs        ← the anchor
Ĵ    used-tier objective on the same beliefs
A    the used plan re-scored on realised outturn
R    realised value from settled cash

optimalityGapEur     = Ĵ₁ − Ĵ
forecastErrorEur     = Ĵ  − A
modelErrorEur + executionSlippageEur + unexplainedError = A − R
```

This anchoring is necessary, not cosmetic. A suboptimal tier lowers the planned
value *and* the realised value, so it nets out of `planned − realised` entirely —
anchoring at `Ĵ` would leave `optimalityGapEur` with nowhere to live and would
silently attribute optimiser weakness to forecast error. `planned` in `INV-S-03`
therefore means `Ĵ₁`. When Tier 1 was the tier used, `Ĵ₁ = Ĵ` and the two
coincide. `plannedByEffect` in §5 remains the **used-tier** value, because that is
what the cash decomposition must reconcile against; both are recorded.

### Computation

Each bucket is a counterfactual re-run, possible only because every layer is a
pure function of a recorded input (ADR-013):

- `optimalityGapEur` — re-solve the tick at Tier 1 on the recorded C2 payload;
  difference of objective values (ADR-010's measured gap).
- `forecastErrorEur` — re-score the plan that was actually submitted against realised
  outturn, holding the plan fixed. This is the *telescoping* form. The alternative
  reading — re-plan on realised data — measures something different and genuinely
  useful, the **value of perfect foresight**; it is recorded separately as
  `valueOfForesight` and is also ADR-010's perfect-foresight upper bound. The two
  must not be conflated: one is a link in the chain, the other is a bound.
- `modelErrorEur` — value the actual trajectory using Valuation's own term
  definitions and compare with Settlement's independent accounting of the same
  trajectory. A non-zero result means the two disagree about what a MWh was worth,
  which is a definition drift.
- `executionSlippageEur` — value the fills at intent prices versus executed prices.

### Risk-term neutrality

`ActivationRisk` and the CVaR weighting are **objective penalties, not
cashflows**. Realised value can never contain them, so comparing a risk-loaded
planned value against a risk-free realised one books the risk premium as a loss
every single day. Both sides of the decomposition therefore use the **risk-free**
objective; `PlanResult` reports gross (risk-free) and net (risk-loaded) values,
and only the gross figure enters this chain.

### Reconciliation

The four buckets plus `unexplainedError` sum to `Ĵ₁ − R` within tolerance
(`INV-S-03`). `unexplainedError` is the residual of the *error* decomposition and
is distinct from `unexplainedEur` in §5, which is the residual of the *cash*
decomposition. This four-way split is what turns "we lost money last week" into
"the load forecast degraded on Tuesdays", which is actionable.

## 7. Quality and mode

| Field | Type | Notes |
|---|---|---|
| `degradationMode` | `DegradationMode` | Computed centrally, written here |
| `modeTransitions` | `(SlotId, from, to, cause)[]` | Full audit trail for the period |
| `ticksByMode` | `map<DegradationMode, int>` | Lets P&L be conditioned on operating mode |
| `seriesQualitySummary` | `map<SeriesId, QualityStats>` | Feeds data-source SLAs |

## 8. Value function refresh trigger

| Field | Type | Notes |
|---|---|---|
| `vSocStale` | `bool` | Beyond `validityHorizon` |
| `stateDriftSignal` | `double` | How far the current state is from where `V` was fitted |
| `refreshRequested` | `bool` | Slow loop should re-run |

`stateDriftSignal` allows the slow loop to be event-driven rather than purely
periodic: a large peak event or a qualification status change invalidates `V`
faster than the calendar does.

## 9. Invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-S-01` | `pPoiRealisedPeakMw` is non-decreasing within an accounting period, and resets exactly at the local-calendar boundary | `HALT` |
| `INV-S-02` | `realisedByEffect` sums, with `unexplainedEur`, to total realised P&L | `HALT` |
| `INV-S-03` | The four error buckets sum to (planned − realised) within tolerance | warn |
| `INV-S-04` | No reserve delivery shortfall (mirrors `C4`) | alert + `HALT` |
| `INV-S-05` | Every `Confirmed` commitment has a `feasibilityRequirement` where the product requires one | `HALT` |
| `INV-S-06` | `fullLoadHours = pPoiAnnualEnergyMwh / pPoiAnnualPeakMw` within tolerance | `HALT` |
| `INV-S-07` | `unexplainedRatio` below threshold | warn, escalating to alert |
| `INV-S-08` | State is never written for a slot earlier than `effectiveFrom` of the previous update, except as an explicit `revisionOf` | `HALT` |
