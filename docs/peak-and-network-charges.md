# Peak shaving and network demand charges — reference

A cross-cutting read of how the network peak charge is treated across the
specification: the regulatory surface, beliefs, valuation, planning, settlement,
state, invariants and tests. Descriptive only — every normative statement lives
in the document cited beside it.

---

## 1. The governing distinction

Peak shaving is not a strategy module. It is **one priced term in one
objective**, plus the state that makes it priceable, plus a guard that survives
every degradation mode.

Everything below follows from one property of the charge: **it is levied on the
maximum over an accounting period, and the period does not care when that
maximum occurred.** A max is not time-additive, so the charge does not decompose
across a planning horizon the way an energy cost does.

| | Peak charge | Volumetric charge |
|---|---|---|
| Base | `PoiImport` | `PoiImport` |
| Priced on | `max_t` over an accounting period | each slot independently |
| Unit | EUR/MW | EUR/MWh |
| Effect | `NetworkPeakCharge` | `NetworkVolumetricCharge` |
| Owner view | `PeakView` | `TariffView` |
| Term shape | `EpigraphTerm` | `LinearTerm` |

Two effects sharing one base is legitimate and is explicitly allowed
(`ADR-009` §3, `L2` §5). What is forbidden is two terms claiming the same effect
on the same base and slot.

**Sign frame.** `00-overview/02-conventions.md` §1 fixes the POI frame as
**import positive**, precisely so that "peak" is a maximum rather than a
minimum. Battery ↔ POI is the only sign flip in the system and it happens in one
place, the POI bridge (`L3` §2).

**Unit.** `peakPriceEurPerMw` is EUR/MW — a charge per MW of peak, for one
accounting period. The period is a **mandatory explicit input** to the term,
carried as its own field, and is **never a unit denominator**
(`02-conventions.md` §2.1). The objective's unit algebra closes as

```
peakPriceEurPerMw · zPeak    (EUR/MW) · MW  =  EUR
```

with exactly one period in scope per term.

---

## 2. The regulatory surface

Verified August 2026; sources at `ADR-011`.

- **Leistungspreis.** EUR per MW of maximum POI import over a month or a year.
  Today's baseline regime.
- **§19(2) S.1 StromNEV — atypische Netznutzung.** The charge is based on the
  peak within published **Hochlastzeitfenster**, not the annual maximum. HLZF
  windows are published per DSO per year, so the optimisation target is a max
  over a *subset* of slots defined by a data table.
- **§19(2) S.2 StromNEV — stromintensive Netznutzung.** Qualification requires
  ≥ 7,000 full-load hours **and** > 10 GWh annual consumption at the metering
  point, with tiered reductions. Full-load hours are annual energy divided by
  annual peak power, so **battery operation moves both the numerator and the
  denominator** and therefore moves qualification directly.

The third one is a **cliff**: falling below threshold loses the reduction for the
entire year. It is reachable by a single day's spread trade, and of the four
clocks it is the only one whose irreversibility column reads **Absolute**
(`00-overview/01-system-model.md` §3). `ADR-011` states the consequence
plainly — losing a year of network-charge reduction for a day's spread is the
single largest downside event in the BTM business case and it must not be
reachable through an approximation error.

All numeric thresholds — 7,000 h, 10 GWh, tariff-tier percentages, HLZF
windows — are **configuration**, sourced from the current published tables per
DSO and per year, never constants.

> **Naming.** The §19(2) tariff tier keeps its name and is written **tariff
> tier** in full. Bare `tier` means the co-optimiser ladder and nothing else
> (`ADR-011` Consequences, `TN-03` §3.3).

---

## 3. The regime plug-in (ADR-011)

`PeakView` does not hardcode `max`. `ITariffRegime` is a plug-in interface;
`PeakView` selects regimes from the versioned `MarketCalendar` (ADR-002) and
emits shapes accordingly.

| Regime | Slot set / objective shape | Charge as settled (`L5` §5) |
|---|---|---|
| `AnnualLeistungspreis` | `EpigraphTerm` over all slots in the local year, floored by realised peak | `peakPriceEurPerMw · pPoiRealisedPeakMw` |
| `MonthlyLeistungspreis` | `EpigraphTerm` per calendar month | same, per month |
| `AtypicalHlzf` (§19(2) S.1) | `EpigraphTerm` over HLZF slots only, from the DSO table | `hlzfPeakPriceEurPerMw · pPoiHlzfPeakMw` |
| `IntensiveUse` (§19(2) S.2) | `EpigraphTerm` + discrete qualification state | reduced rate conditional on qualification |

**Regimes compose.** A site can be subject to more than one simultaneously, so
peak state is kept **per regime, not per site**: a site under `AtypicalHlzf` and
a volumetric charge has two peak accumulators over different slot sets and
different period bounds (`L0` §2.1). Each regime emits terms tagged with its own
`EconomicEffect`, so the composer's exclusivity check keeps them from
overlapping, and the same tagging applies on the settlement side (`L5` §5).

A regime change is a configuration date plus a new regime implementation, not a
rewrite, and backtests spanning a regime change are honest because the regime is
selected from the versioned calendar.

---

## 4. The pipeline, layer by layer

```
L5 peak engine ──C5 §2──▶ L0 peak state ──C6 §2──▶ L2 PeakView
                                                        │
                                                  C2 EpigraphTerm
                                                        │
                                                   L3 objective
                                                        │
                                              dispatch ──▶ meter ──▶ L5
```

Peak is one of the three dependencies that break the one-directional pipeline
(`L0` §1). ADR-006's resolution is structural: forward within a tick, lagged
across ticks, with `C5` the only backwards edge and not traversed until the tick
has ended.

### L1 — belief

Two site series and one tariff block cross `C1`:

| Field | Card. | Note |
|---|---|---|
| `loadMw` | `[S,H]` | Conservative default is a **high quantile** — protects peak (`C1` §3) |
| `pvAvailMw` | `[S,H]` | Conservative default is **zero** — a PV shortfall raises import, so zero protects peak |
| `auxLoadMw` | `[H]` | Explicit, never folded into η, so it appears in the POI balance and therefore in the peak (`conventions` §3) |
| `activeRegimes` | `1..n` | From the calendar |
| `peakPriceEurPerMw` | per regime | EUR/MW, keyed by `activeRegimes` (`C1` §7) |

A warm-storage **slab** covers a *local* civil day precisely because every
consumer that cares about day boundaries — peak accounting, HLZF windows — cares
about local ones (`L1` §2.2). The hot window is slot-contiguous,
scenario-major, because the dominant hot-path operation is a weighted reduction
*across scenarios at a fixed slot*, which is `PeakView`'s CVaR (`L1` §2.3).

Peak/load climatology is a materialised quantile surface by calendar bucket,
feeding ladder rung 4 imputation (`L1` §5).

### L2 — valuation

`PeakView` prices the marginal MW of grid peak under whichever regimes are
active (`L2` §2).

- **Emits:** one `EpigraphTerm` per active regime.
- **Slot set:** all slots for `AnnualLeistungspreis`; **HLZF slots only** for
  `AtypicalHlzf` — expressed through `EpigraphTerm.overSlots`, which is why that
  field is a slot *set* rather than a range.
- **Floor:** `pPoiRealisedPeakMw` from L0, adjusted upward for the unsettled gap
  using the engine's own modelled trajectory, and the realised peak of the
  period the term declares in `accountingPeriod` (`INV-V-18`).
- **Risk treatment:** the peak level is an **empirical CVaR tail mean over the
  joint ensemble**, not the mean forecast. Over the reduced ensemble with weights
  `w_s`, take the weighted mean of the worst `(1−α)` mass of
  `max_t p_poi[s,t]`. The peak charge is a max-of-a-max, so the mean forecast
  systematically understates it — Jensen, in the unhelpful direction.
- **Rate:** the full `peakPriceEurPerMw`, with no scalar between the rate and
  the epigraph variable (`ADR-020`). Exceeding the floor by `Δ` costs
  `peakPriceEurPerMw · Δ` for the whole period, because the charge is levied on
  the period max.
- **Degradation response:** poor `loadMw` quality adds
  `riskProfile.pPoiPeakSafetyMarginMw` to the floor rather than changing
  behaviour.

**Composition stage 5 of 6.** The peak term is composed **last** among the
substantive stages, and the ordering is load-bearing (`L2` §5, `ADR-009`):

| Stage | Requires | Establishes | Why |
|---|---|---|---|
| 5 `Peak` | POI **and** battery energy marked | `zPeak` bounded below by realised peak | Peak headroom is priced on top of a battery whose energy is already marked — reversing this double-counts |

Stage 4 `ReserveCoupling` precedes it because reserve consumes headroom, which
must exist before peak prices it. `T2` §3.9 asserts the failure: composing
`Peak` before `OppCost` raises `INV-V-07`, "and this is the double-count the
ordering exists to prevent."

### L3 — planner

The objective (`L3` §2):

```
max  Σ LinearTerms + Σ PwlTerms + V(socTerminal) − Σ peakPriceEurPerMw·zPeak
     + Σ_j λ_j · A_j
     − cvarWeight · CVaR_α(imbalance cost)
```

The Planner owns no number of its own — if it needs one, a view must own it.

`peakCritical` is one of four triggers that escalate Tier 3 to Tier 2 within
budget (`L3` §3). Peak protection and commitment feasibility are what is **never
dropped**, in any mode (`L3` §6). Rung 3 of the budget-exhaustion ladder is the
`DEFENSIVE` baseline: honour commitments, protect peak, no new exposure.

The delineation accumulators and the peak charge price **physical** flow, which
is part of why every net market position must be physically backed
(`INV-X-07`): an unbacked position would earn nothing from the `λ_j` while still
perturbing the `A_j` (`L3` §2, `TN-02` §4).

### L5 — settlement

`L5` §5 is the peak accounting engine, per active regime. Instantaneous POI power
in the tariff's own frame, floored at zero because the charge is levied on
import:

```
pPoiImportMw[t]    = max( (pPoiMeteredImportMwh[t] − pPoiMeteredExportMwh[t]) / Δt_h , 0 )
pPoiRealisedPeakMw = max over t ∈ periodSlots(regime) of pPoiImportMw[t]
```

`NetworkPeakCharge` in the effect ledger is this engine's output, negative
(`L5` §4).

**Intensive-use qualification tracking:**

```
pPoiAnnualEnergyMwh = Σ_{t ∈ year} pPoiMeteredImportMwh[t]
pPoiAnnualPeakMw    = max_{t ∈ year} pPoiImportMw[t]
fullLoadHours   = pPoiAnnualEnergyMwh / pPoiAnnualPeakMw     [ MWh / MW = h ]  INV-S-06
flhMargin       = fullLoadHours − flhThreshold               [ config, 7,000 h ]
energyMargin    = pPoiAnnualEnergyMwh − energyThreshold      [ config, 10 GWh ]
qualificationMarginHours = min(flhMargin, energyMargin / pPoiAnnualPeakMw)
```

Both thresholds bind, so the published margin is the binding one, expressed in
hours so the value function sees a single continuous distance to the cliff.
**The margin, not the status, is the output that matters** — a boolean
`qualificationStatus` tells the engine nothing until it is too late to act.

**Settlement staging.** An intraday provisional pass can conclude whether a new
peak *candidate* occurred; D+1 confirms or rejects the candidate; only the M+X
final pass concludes the period's `NetworkPeakCharge` (`L5` §3). Provisional
never overwrites final for the same period (`INV-S-14`).

### L0 — state and value store

`C5` §2 writes peak state; `C6` §2 mirrors it for readers. Written by L5, read by
`PeakView` and by L3's escalation policy, on the `C_accounting` clock (`L0` §2).

Peak state and the value function change rarely while the ledger and the quality
summary churn every tick, which is why `L0` hashes its sections independently:
a typical commit writes one or two small blobs and a journal record, not a full
state image (`L0` §4.1).

---

## 5. The unsettled gap

`pPoiRealisedPeakMw` is a max over the **settled prefix only**, because meter
data is published with a lag — provisional telemetry in minutes to hours, final
metered values in days, market settlement later still (`L0` §6).

```
period start ─────────── unsettledGapFrom ─────────── t (now) ──────── horizon
     |<──── settled, authoritative ────>|<─── the gap ───>|<── planned ──>|
      pPoiRealisedPeakMw is a max            no meter        Planner's own
              over this                        data          trajectory
```

The Planner must not assume the gap was empty. The effective epigraph floor is

```
zPeak  ≥  max(  pPoiRealisedPeakMw ,
                max over τ ∈ [unsettledGapFrom, t) of  p̂_poi[τ]  )
```

where `p̂_poi` is the engine's own trajectory over the gap — telemetered where
telemetry exists, modelled from the plan and the load/PV beliefs where it does
not (`L0` §6.1, `C6` §2). The reason is specific: the gap is the *most recent*
window, so it is disproportionately likely to contain a peak the engine just
caused. `C6` §2 calls assuming otherwise "the single most likely way to set a new
monthly peak by accident."

Where the gap trajectory is itself uncertain it receives the same CVaR treatment
as the forward peak and picks up `pPoiPeakSafetyMarginMw`. The asymmetry is
deliberate: overstating the provisional peak costs a little optimisation
freedom, understating it can cost an entire period's demand charge.

**Provisional peak is not `pPoiRealisedPeakMw`** (`L0` §6.2). They are different
quantities, and the provisional one may legitimately exceed the peak that
eventually settles, because the modelled gap contribution is deliberately biased
high:

```
INV-G-16:  provisionalPeak ≥ pPoiRealisedPeakMw                    (always)
           provisionalPeak ≤ eventual settled peak           (NOT required)
```

When settlement arrives, `unsettledGapFrom` advances, `pPoiRealisedPeakMw` may
step up, and the modelled gap contribution is **discarded rather than blended**.
The step contributes to `stateDriftSignal`, which is correct: the fitted `V` was
conditioned on the old peak state.

**Gap length is a first-class quality signal** (`L0` §6.3). `unsettledGapFrom` is
non-decreasing and never exceeds `t` (`INV-G-17`); a gap that stops advancing
means the meter feed is dead, and beyond `maxGapSlots` the peak floor is almost
entirely modelled rather than measured. That escalates the degradation mode,
because peak protection is one of the two things never degraded away and it
cannot be protected against a quantity nobody is measuring.

---

## 6. Why the term has the shape it has

### `max(·)` in epigraph form (ADR-008)

The charge is not linear in the decision, so Valuation publishes a *function*,
not a scalar. `max(·)` is linear in epigraph form:

```
zPeak ≥ pPoi[t]        ∀ t ∈ overSlots
zPeak ≥ pPoiFloorMw                        (the floor, from L0)
objective:  − peakPriceEurPerMw · zPeak
```

No binaries, no max operator, exact. `TN-01` §5a gives the underlying reason:
with the POI bridge substituted, `p_poi(x, s, t)` is for each `t` a *separate
affine function* of the decision vector, so `max_t` is a pointwise max of affine
functions and hence convex in the decisions. A pointwise max is
convexity-*preserving*, not convexity-*creating* — it is the affineness of the
constituents that does the work, which is also why the max is representable as
one linear row per slot with no assumption about the trajectory's shape.

The chain that keeps the term exactly LP-representable is

```
affine → max over t → max with the constant floor → × (peakPriceEurPerMw ≥ 0)
       → CVaR_α (convex AND non-decreasing) → Σ
```

Two limits on that. The term *adds* no binaries, but the model carries them
elsewhere, so the relaxation is exact per fixed integer assignment rather than
globally. And the affineness premise is falsifiable: making the activation
fraction a decision variable rather than a per-scenario parameter would break
convexity at the bridge and every step above it.

### The rate is charged in full (ADR-020)

The term prices the level at the full rate — `unitPriceEurPerMw · epigraphVar`,
with no scalar between them. The charge is levied on the period max, so
exceeding `pPoiFloorMw` by `Δ` costs the full `unitPriceEurPerMw · Δ` whichever
slot it happens in. This holds for all four `ADR-011` regimes uniformly; the
argument is about the shape of a max, not about a rate.

`accountingPeriod` is carried as the `SlotRange` it is, computed by
`ITariffRegime.AccountingPeriod(SlotId)`. It is what makes `pPoiFloorMw`
checkable: a floor is the realised peak *of a period*, and a floor with no
declared period cannot be validated against anything.

**The out-of-horizon remainder is priced at zero within the tick, and this is a
known gap.** Charging the full rate above the floor is right when the excursion
becomes the period max and over-charges when a later one would have dominated
it. The residual hazard is therefore over-aversion to peak. The remedy is `V`,
and it is not available in the form required — see §10.

### The level is risk-averse, and why the price sits outside the tail

`TN-01` §6a confirms the max-of-a-max reasoning formally: `max_t(·)` is convex,
so by Jensen `E[max_t p(t)] ≥ max_t E[p(t)]`, and
`CVaR_α(M) ≥ E[M] ≥ max_t E[p(t)]` — a mean-forecast peak understates the
risk-averse level **twice over**. The gap grows with slot count and dispersion
and at `H = 192` is not a rounding term.

`PeakView` takes the tail in **MW** and prices it outside the functional, which
is legal by positive homogeneity: for deterministic `c ≥ 0`,
`c · CVaR_α(X) = CVaR_α(c · X)`, so the two forms are the same number
(`TN-01` §0, §2). That pass-through is what fixes `peakPriceEurPerMw` as a
**scalar deterministic input**. It fails if the rate becomes scenario-dependent
or non-linear in the level, in which case the tail must be taken after pricing.

One consequence worth naming: `cvarWeight` is granted to the imbalance term
alone, so under a scalar rate **`peakPriceEurPerMw` is the peak term's only risk
weight**. An error in it is not merely a mispriced MW, it is a shift in risk
aversion.

### Non-linear rate schedules, if they ever apply

A rate that is piecewise with a *decreasing* marginal EUR/MW makes the charge
concave in the level, and `c(max_t p_poi)` is then **not convex**: representing
it under minimisation needs SOS2 or binaries, and the LP relaxation returns the
lower convex envelope — a cheaper peak than the tariff charges (`TN-01` §1a).
German utilisation-hour bands are the live instance and are worse than a kink,
since crossing reprices all energy. No such band appears anywhere in the
repository; under the regulatory-primitive rule it is a primitive the repo is
silent on.

If it ever applies, the guidance is that it does **not** belong in the tick as a
PWL. Band membership is settled by realised annual energy and peak while the
horizon is two days, so it belongs as a further discrete conditioning dimension
of `V`, exactly as `qualState` already is. The alternative costs `S·(bands−1)`
binaries — 64 per regime at `S = 64`, for a term currently priced with none.

---

## 7. The value function and the cliff

Peak is a **running max**, hence a path variable, hence a dimension of `V`:
`V(SOC, peakState, qualState)` (`ADR-007`, `ADR-011`). `qualState` carries
accumulated annual energy, accumulated full-load hours, current HLZF exposure and
a projected qualification probability. The value function is fitted conditional
on the discrete qualification state, which is how the cliff is represented
without breaking concavity in SOC.

`C2` §4 ships `V` as one concave PWL curve in SOC, with `conditionedOn`
selecting *which* curve — `(peakState, qualState, calendarContext)` — before the
solve.

**Cliff protection is a hard guard, not only an economic term.** When projected
qualification is within a configurable margin of a threshold, the engine raises
`peakCritical` / `qualCritical`, which (a) forces escalation to a higher
co-optimisation tier and (b) applies a bound restricting actions that would
worsen the projection.

**Staleness versus invalidation** (`L0` §5.1). An accounting rollover —
`periodEnd` crossed, `pPoiRealisedPeakMw` reset — **invalidates** `V` rather than
marking it stale, forcing `DEFENSIVE` until a refit lands (`INV-G-19`). A stale
curve is an out-of-date approximation of the right curve and attracts
`stalenessPenalty` shrinkage; an invalidated curve is the *wrong* curve, because
one fitted against last period's realised peak prices a floor that no longer
exists. Shrinking the slopes of a curve conditioned on the wrong state does not
make it less wrong. A large peak event is also a `stateDriftSignal`
contribution, which is what makes the slow loop event-driven rather than purely
periodic.

**Fitting is where `peakState` costs the most** (`L0` §5.4). Of the three
candidate methods, backward recursion on a discretised state grid handles the
cliff natively but must discretise the running max, and "coarse peak buckets
misprice precisely the threshold region that dominates the BTM business case."
The slow-loop horizon must be long enough to see the end of the accounting
period, otherwise the truncation `V` exists to price is present inside the fit
itself (`L0` §5.2).

---

## 8. The calendar

Tariff logic is **not UTC** (`02-conventions.md` §4.2, `L5` §5, `INV-S-12`).

- Period boundaries and HLZF windows are derived **only** via the
  `CivilCalendar` service against the manifest's pinned tzdata. No UTC-offset
  arithmetic anywhere in the peak engine.
- A civil day has **92, 96 or 100** quarter-hour slots. Any sum or maximum over
  "a day" iterates the calendar's slot set for that day, never a fixed count.
  Code that assumes 96 is wrong twice a year (`INV-T-02`).
- HLZF windows are local-time windows from a data table (ADR-002), resolved to
  `SlotId` sets through the same service.
- `pPoiRealisedPeakMw` resets **exactly** at the `SlotId` that `CivilCalendar`
  resolves the Europe/Berlin local period boundary to — a UTC instant computed
  once, never a UTC offset applied to a local timestamp.

`L5` §5 states the failure mode: an off-by-one-hour reset at a DST boundary
either destroys a period's accumulated peak or carries it into the next one, and
both are silent. Both DST transition days are permanent fixtures in the
peak-engine test set.

---

## 9. Contract field inventory

### `C1` — belief → valuation

| § | Field | Card. | Note |
|---|---|---|---|
| 3 | `loadMw` | `[S,H]` | Default = high quantile, protects peak |
| 3 | `pvAvailMw` | `[S,H]` | Default = `0`, protects peak |
| 7 | `activeRegimes` | `1..n` | From the calendar |
| 7 | `peakPriceEurPerMw` | per regime | EUR/MW |

### `C2` §3.3 — `EpigraphTerm`

| Field | Type | Unit | Note |
|---|---|---|---|
| `epigraphVar` | `VarSymbol` | — | `zPeak` |
| `dominates` | `VarSymbol` | — | `pPoi` |
| `overSlots` | `SlotId[]` | — | A **subset** — this is how HLZF is expressed |
| `pPoiFloorMw` | `double` | MW | Realised peak so far, gap-adjusted, margin-adjusted |
| `unitPriceEurPerMw` | `double` | EUR/MW | |
| `accountingPeriod` | `SlotRange` | — | From `ITariffRegime` |

Plus `TermHeader`: `effect = NetworkPeakCharge`, `base = PoiImport`, and
`mandatory = true` — peak protection is mandatory, so dropping the term
invalidates the plan. `zPeak` is in `C2` §2's closed decision-variable
vocabulary, per regime; adding a symbol is a major version bump.
`riskProfile.pPoiPeakSafetyMarginMw` (`C2` §6) is the degradation response.

### `C5` §2 / `C6` §2 — peak state, per regime

| Field | Type | Unit | Note |
|---|---|---|---|
| `pPoiRealisedPeakMw` | `double` | MW | The epigraph floor for the next tick |
| `realisedPeakSlot` | `SlotId` | — | Diagnostic: a peak set inside an HLZF window and one set outside have different implications |
| `periodStart`, `periodEnd` | `SlotId` | — | Local-calendar derived |
| `peakIsProvisional` | `bool` | — | True while meter data is unfinal; a one-way latch per slot range |
| `unsettledGapFrom` | `SlotId` | — | Peak authoritative only up to here |
| `pPoiHeadroomToPeakMw` | `double` | MW | Distance to the level at which a new peak would be set — the continuous analogue of `qualificationMarginHours` |
| `peakCritical` | `bool` | — | Headroom inside the configured margin, **or** the horizon contains a slot whose CVaR peak belief exceeds the realised peak |

`peakCritical` is computed by **Settlement, never the Planner**, so that the
escalation trigger cannot be influenced by the thing it is meant to guard
(`C5` §2).

### `C5` §3 / `C6` §3 — qualification state

`pPoiAnnualEnergyMwh`, `pPoiAnnualPeakMw`, `fullLoadHours`, `pPoiHlzfPeakMw`,
`qualificationStatus`, `qualificationMarginHours`, `projectedYearEndFlh`,
`qualCritical`.

**Conservative defaults on the read side** (`C6` §2–3): `pPoiHeadroomToPeakMw`
defaults to `0` (no headroom), `peakCritical` to `true`, `qualificationStatus`
to `AtRisk`, `qualificationMarginHours` to `0`, `qualCritical` to `true`. Every
one biases toward protection, because the asymmetry is extreme: the downside of
an unnecessary protective bound is a day's foregone spread, and the downside of
losing a §19(2) reduction is a year of network-charge relief.

`C6` §1 carries `lagSlots` as a first-class field, and consumers are expected to
reason about it: a Planner that knows the peak state is three slots stale treats
the gap conservatively rather than assuming it was empty.

---

## 10. Invariants

| ID | Statement | On failure |
|---|---|---|
| `INV-V-18` | Every `EpigraphTerm`'s `pPoiFloorMw` is the realised peak of the `accountingPeriod` it declares — gap-adjusted and, under degraded load quality, margin-adjusted — and no scalar stands between `unitPriceEurPerMw` and `epigraphVar` | `HALT` |
| `INV-V-04` | Every term marked `mandatory` is present. Peak protection and confirmed-commitment terms are mandatory in every degradation mode | `HALT` |
| `INV-V-07` | Stage preconditions met — peak is stage 5, requiring POI **and** battery energy marked | `HALT` |
| `INV-V-01` | No `(effect, variable, slot)` triple priced by more than one term | `HALT` |
| `INV-S-01` | `pPoiRealisedPeakMw` is non-decreasing within an accounting period and resets **exactly** at the local-calendar boundary | `HALT` |
| `INV-S-06` | `fullLoadHours = pPoiAnnualEnergyMwh / pPoiAnnualPeakMw` within tolerance | `HALT` |
| `INV-S-12` | Every period boundary and HLZF window derived via `CivilCalendar` under pinned tzdata; no UTC-offset arithmetic in the peak engine | `HALT` |
| `INV-S-13` | While `IntensiveUse` is active, `qualificationMarginHours` is published every tick and reflects both thresholds | `HALT` |
| `INV-G-16` | The Planner's provisional peak floor is `≥ pPoiRealisedPeakMw`; it is **not** required to be `≤` the eventual settled peak | `HALT` on the `≥` side only |
| `INV-G-17` | `unsettledGapFrom` is non-decreasing and never exceeds the current slot | `HALT` |
| `INV-G-19` | A change to `V`'s discrete conditioning state invalidates `V` — mode `≥ DEFENSIVE` until refit — rather than marking it stale | escalate per ADR-014 |
| `INV-T-02` | No computation assumes a fixed number of slots per civil day | `HALT` |

Adjacent: `INV-P-07` — the no-simultaneous-charge-and-discharge complementarity
binary is omitted by default because `η_c·η_d < 1` makes it automatic, but it is
**not** automatic under negative prices, an aFRR activation obligation, or **a
peak-driven incentive to import**, where burning energy can be profitable and
the LP relaxation will exploit it. When the binary is disabled this becomes a
post-solve monitor.

---

## 11. Degradation — peak is one of two things never dropped

ADR-014 §3: **peak protection and commitment feasibility apply in every mode,
including `SAFE`.** The epigraph floor from realised peak and the qualification
guard are never degraded away — "losing a year of network-charge reduction
because a price feed was stale is not an acceptable outcome of any degradation
path."

Degradation acts as a **parameter, not a branch** (ADR-014 §1). Bad data takes
the *same* code path with more conservative parameters:

| Degraded input | Response (`L2` §6) |
|---|---|
| `loadMw` quality | ↑ `pPoiPeakSafetyMarginMw`; ↑ `cvarLevel` for peak |
| `pvAvailMw` quality | ↑ `pPoiPeakSafetyMarginMw` (PV shortfall raises import) |

`peakPriceEurPerMw` is the one field in this area with **no safe default**: a
missing value is `HALT`, because it is a mandatory term (`T2` §2.5 quality
ladder matrix, `INV-V-04`). Rung 5 of ADR-014's imputation ladder is why the load and PV
defaults point the way they do — the default is chosen to be conservative *for
the decision it feeds*.

Every adjustment is recorded in `riskProfile.driverSummary`, so a conservative
plan can always be explained by naming the input that caused it.

---

## 12. P&L attribution

`PeakShave` is one of five `StrategyTag`s (`Arbitrage`, `PeakShave`,
`ReserveHedge`, `Rebalance`, `CommitmentCover`), resolved in Phase B through
`fillId → intentId → StrategyTag` (`L5` §7).

But `NetworkPeakCharge` is a **joint** effect, not a directly attributable one:
it is a property of the whole trajectory and is not the sum of per-order
contributions. It is allocated by a **declared rule** — counterfactual removal of
the tag's fills, re-run through the peak and SOC accounting, normalised across
tags — with the normalisation residual booked to an explicit `unallocated` line,
never smeared proportionally.

`L5` §7 states why this is not pedantry: peak charge attributed naively makes
`PeakShave` look free and `Arbitrage` look profitable, "which is the exact
misreading that leads to turning peak protection down."

On the reporting side, a discrepancy localises to an effect and the ownership
matrix maps that effect to exactly one view, so "we lost money" becomes
"`NetworkPeakCharge` came in 4 kEUR above plan and `PeakView` owns it"
(`L5` §2).

---

## 13. Testing

**`T2` §5.3 — zero-gap.** Settling the planned trajectory as if realised must
zero all four error buckets (`INV-S-10`). The peak bucket is called out as the
case to watch: the term-by-term assertion is satisfiable only because both sides
price the same thing — `L2` charges `peakPriceEurPerMw · zPeak` and `L5` books
`peakPriceEurPerMw · pPoiRealisedPeakMw`. A regression here is the first thing to
check if `unexplainedRatio` starts climbing.

**`T2` §4.7 — `M-P3` metamorphic.** Raise `peakPriceEurPerMw` for a regime → the
planned `zPeak` for that regime is **non-increasing**, and the objective is
non-increasing. It catches an epigraph built with the wrong sense, a rate
discounted for a partial horizon, and an ignored peak floor. `M-P1` and `M-P3`
interact on days where reserve and peak compete for the same headroom. The
`L2`-side counterpart is §3.3: raising the rate must not lower the priced value
of a MW of peak reduction.

**`T2` §2.3 — `PeakPeriodBoundary`.** The reset is asserted at the exact slot,
"not one slot early or late," across every month boundary and both DST
transitions. A period that ends four slots early can miss the period's actual
peak, which mis-prices the epigraph floor for the entire following period.

**`T2` §1 — the bug class this layer of testing exists for.** "Peak floor stale,
or taken from an accounting period the engine has left" is listed among the
defects feasibility cannot catch: the plan is feasible and *reads* as prudent
while shaving against a peak that is not this period's. Caught by peak-price
monotonicity plus the zero-gap test.

**`T2` §3.9 — composition order.** `Peak` before `Tariff` or before `OppCost`
must raise `INV-V-07` naming stage 5 and the unmet precondition, verified for all
24 wrong permutations of the four substantive stages.

**`T2` §5.2 — restatement.** After a restatement that lowers the metered peak,
peak state must be **recomputed from scratch, not patched**, and `INV-S-01`
monotonicity must still hold on the recomputed series.

**`T2` §2.4 — ensemble coherence.** `PeakView` must produce a *different* output
when one series' scenario axis is permuted, because the CVaR of
`max_t p_poi[s,t]` depends jointly on `loadMw`, `pvAvailMw` and dispatch. A view
that does not is ignoring the dependence structure ADR-005 exists to preserve.

**`T3` — determinism.** Field-level diffs are part of the tooling: reporting
"the `ValuationBundle` differs" is not actionable, reporting
"`term PeakView.zPeak.annual`, field `pPoiFloorMw`, `0.4127 → 0.4183 MW`" is.
A peak-critical or qualification-critical day is a mandatory member of the
replay corpus.

**`T5` — gap.** **Peak-critical days** are a named conditioning regime, defined
as `peakCritical` set or the modelled POI trajectory within a configured margin
of `pPoiRealisedPeakMw`, and are assessed against their own **tighter band**.
The gap is reported as p50/p90/p99/worst, never a mean, and the **absolute EUR**
gap gates, "because a 40 % relative gap on a EUR 3 tick is noise and a 2 %
relative gap on a peak-critical day is not." The `V`-truncation cost — the
difference between `PolicyQuality` at the matched horizon and at a monthly
horizon with the peak term and delineation live — is the specification's only
direct measurement of `V`'s adequacy.

**`T6` — adversarial.** `PeakView` is the **negative control** for the
lookahead-poisoning audit: inject a deliberate leak (read load at `t+96` to set
the safety margin), assert the audit fails and localises to the
`ValuationBundle` at the expected tick. Config poisoning asserts that
`peakPriceEurPerMw: "120 EUR/GW"` and `"120 EUR/MWh"` are both rejected at load
on the unit suffix (`INV-G-02`), before the value is parsed.

**`T0` / `T3` fixtures.** The canonical 7-day CI window must contain one
peak-critical day. `L5`'s synthetic day — flat load, one price spike, one aFRR
block, one peak event, every effect analytically computable — is the fixture
that catches unit errors.

**`P0` definition of done.** W3 (settlement and state) requires `INV-S-01` to
hold across a month boundary *and* both DST transitions, and `INV-S-06` over a
synthetic full year. W5 (Planner Tier 1) requires the `M-P3` monotonicity. W7
(slow loop) requires the cliff to be priced: a synthetic December spread trade
that would drop full-load hours below threshold must be **declined, and the
decline explained by `V`'s conditioning rather than by a hard guard**.

---

## 14. Open and unresolved

Two entries in the open-decision register (`ADR-015`):

**015-1 — how `V` prices the out-of-horizon peak remainder.** Charging the full
rate above the floor over-charges an excursion that a later one would have
dominated. `ADR-007` conditions `V` on `peakState`, but `C2` §4 ships `V` as one
curve in SOC with `conditionedOn` selecting which curve *before* the solve, so
`V` is not a function of the in-solve `zPeak` and cannot carry the remainder
however well it is fitted. Blocked on a domain and a discretisation for
`peakBuckets` — `L0` §2.4 is its only occurrence in the corpus and states
neither — and on whether `V` enters `L3`'s objective jointly in
`(socTerminal, zPeak)`. `W7` as scoped delivers qualification-state conditioning
only.

**015-2 — whether the peak epigraph is scenario-indexed.** `TN-01` §5a
adjudicates that `ADR-008`'s single unindexed `z_peak` and `L2`'s CVaR tail mean
**are not two descriptions of one term — they are two different terms, and only
`L2`'s is the risk-averse quantity.** One of the two texts is wrong and must be
deleted. The honest repair is `z_s` per scenario plus the RU block:
`S` epigraph vars + `S` RU vars + 1 `ζ`, and `S·|overSlots| + S` rows — ≈ 12k
rows per regime at `S = 64, H = 192`. Blocked on a cost estimate measured against
a real solve rather than counted. `TN-04` §2 sharpens the same point from the
contract side: `EpigraphTerm` carries no scenario index and no risk-averse
level, so the tail mean `PeakView` computes "has no field to arrive in — unless
it is folded into `pPoiFloorMw`, which is the *realised* peak, mislabelling a
forecast as history."

Two further `TN-01` findings on this term, advisory and not yet acted on:

- **`INV-D-07` is vacuous for the peak term** (`TN-01` §5c). It requires the
  reduced ensemble to preserve **marginal means**; the peak level is a functional
  of a **path**, not of any marginal, and neither of its biases is a mean
  effect. A tail-fidelity companion invariant is required.
- **Two independent downward biases**, both worsening as `S` falls, both
  pointing at under-hedging (`TN-01` §5b, §5d). Reduction error is amplified by
  `1/(1−α)` — a factor of **20** at `α = 0.95` — and scenario reduction
  minimises `W₁` against all functionals equally, so it is not tail-aware and
  drops the upper-tail paths that set the peak. At `S = 64, α = 0.95` the tail
  carries ≈ 3.2 scenarios' worth of mass under uniform weights and **one or two**
  under the non-uniform weights a reduction actually produces, so the peak risk
  level becomes a function of essentially the single worst retained path. The
  controlling quantity is `n_eff = (1−α)/max_s w_s`, which `TN-01` §9 proposes be
  computed and enforced rather than left implicit.

---

## 15. Source index

| Topic | Document |
|---|---|
| Layer model, four clocks | `00-overview/01-system-model.md` |
| Sign frame, EUR/MW, civil calendar | `00-overview/02-conventions.md` §1, §2.1, §4.2 |
| `pPoiRealisedPeakMw`, `peakCritical`, HLZF, full-load hours | `00-overview/04-glossary.md` |
| Epigraph primitive | `01-adr/ADR-008-linearizable-primitives.md` |
| Term ownership, stage ordering | `01-adr/ADR-009-term-ownership.md` |
| Tier ladder, escalation | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| Regime plug-in, qualification cliff | `01-adr/ADR-011-tariff-regime-plugin.md` |
| Never-degraded guarantees | `01-adr/ADR-014-degradation-ladder.md` §3 |
| Open decisions 015-1, 015-2 | `01-adr/ADR-015-open-decisions.md` |
| Full rate, `accountingPeriod` | `01-adr/ADR-020-peak-charge-carries-no-proration.md` |
| Peak state, unsettled gap, slow loop | `02-layers/L0-state-value-store.md` §2.1, §5, §6 |
| `PeakView`, stage table, degradation map | `02-layers/L2-valuation.md` §2, §5, §6 |
| Objective, escalation, fallback ladder | `02-layers/L3-planner.md` §2, §3, §6 |
| Peak accounting engine, qualification tracking, attribution | `02-layers/L5-settlement.md` §5, §7 |
| `EpigraphTerm`, `zPeak`, `INV-V-18` | `03-contracts/C2-valuation-to-planner.md` §2, §3.3, §8 |
| Peak state write | `03-contracts/C5-settlement-to-state.md` §2, §3 |
| Peak state read, conservative defaults | `03-contracts/C6-state-to-layers.md` §2, §3 |
| Invariant register | `04-compliance/T1-invariants.md` |
| Property and metamorphic tests | `04-compliance/T2-property-and-metamorphic-tests.md` |
| Gap conditioning, peak-critical band | `04-compliance/T5-gap-and-performance.md` |
| Lookahead and config-poisoning audits | `04-compliance/T6-adversarial-audit.md` |
| Convexity, CVaR, reduction bias | `06-theory/TN-01-cvar-across-stages.md` §1a, §5, §6a |
| Risk-measure carrier table | `06-theory/TN-04-imbalance-risk-measure.md` §1, §2 |
| Type sketches | `stubs/C2_ValuationBundle.cs`, `stubs/C5_StateUpdate.cs`, `stubs/Enums.cs` |
