# L5 — Settlement

Recomputes what actually happened from realised data, attributes P&L to the same
economic effects Valuation used, and decomposes the gap between planned and
realised value into four owned buckets. This is the layer that makes the system
**improvable** rather than merely operable: without it the engine can trade, but
nobody can say whether a change made it better.

**Input:** `ExecutionOutcome` (C4) + metered reality + tariff sheets
**Output:** `StateUpdate` (C5); `SettlementResult` (internal, recorded — ADR-013)
**Purity:** total function. No I/O, no clock, no state, no randomness.

Settlement has **no access to what the Planner intended**. `intentId` crosses C4
as an opaque key and nothing else does (C4 §8). Ex-post accounting therefore
cannot be contaminated by ex-ante belief — which is the only reason its numbers
are worth anything as evidence against the Planner.

---

## 1. Two phases, and why the wall between them matters

Settlement runs in two phases with a hard boundary. The boundary is the point of
the layer.

| Phase | Reads | Produces | Sees the plan? |
|---|---|---|---|
| **A — Accounting** | C4, metered series, tariff configuration, `V` artefact | `realisedByEffect`, peak state, qualification state, reconciliation results | **No** |
| **B — Attribution** | Phase A's *sealed* output, plus recorded C1/C2/C3/`PlanResult` | error buckets, strategy-tag and mode conditioning | Yes |

Phase A is the function C4 §8 describes: truth from realised data alone. Its
output is content-hashed and sealed before Phase B starts, so no attribution
question can reach back and change an accounting number. Phase B is a *comparison*
of two independently produced artefacts, and it is allowed to read the recorded
plan precisely because it can no longer alter the ledger side of the comparison.

`StrategyTag` and `shadowValueEurPerMwh` live on C3, not C4, and are read only in Phase B.
This is what makes "did the quoting policy trade through indifference?" answerable
without giving the accounting phase a view of intent.

---

## 2. The recomputation principle

**Settlement trusts nothing upstream computed.** It does not read a P&L number
from Execution, it does not accept the Planner's expected value, and it does not
carry a running total forward. Every euro is recomputed from realised inputs:
executed fills, awards and clearing prices, metered energy at the POI and the
battery terminal, published imbalance prices, and the tariff sheets in force for
that period.

It recomputes using the **same `EconomicEffect` enumeration Valuation used**
(ADR-009, `L2-valuation.md` §5). That shared enumeration is the whole mechanism:

- `plannedByEffect` (from `PlanResult`, `L3-planner.md` §8) and `realisedByEffect`
  (from Phase A) are indexed by the same closed set of thirteen effects, so
  planned-versus-realised is a term-by-term comparison, not an aggregate one.
- A discrepancy localises to an effect, and the ownership matrix maps that effect
  to exactly one view. "We lost money" becomes "`NetworkPeakCharge` came in 4 kEUR
  above plan and `PeakView` owns it."
- The exclusivity check that prevents double-counting in the objective (ADR-009)
  prevents the same double-count in the ledger, because both sides use the same
  matrix. A term counted twice in Valuation and once in Settlement shows up as a
  persistent, signed model error rather than as a plausible-looking profit.

If Settlement invented its own decomposition, the two sides would drift and the
four-bucket decomposition would be uncomputable. **Any new effect is added to the
enumeration once and implemented on both sides in the same commit.**

---

## 3. Provisional and final

Meter data is provisional for days, imbalance prices for weeks, and network
invoices reconcile over months. Both provisional and final are first-class
(C4 §1). **A restatement is a new artefact carrying `revisionOf`, never an
in-place edit** — append-only, consistent with ADR-004 and enforced by
`INV-S-09`.

### Timeline

| Stage | Trigger | Data quality | Can conclude | Cannot conclude |
|---|---|---|---|---|
| **Intraday provisional** | every tick, `C_tick` | raw meter pulses, indicative imbalance | POI trajectory so far; whether a new peak candidate occurred; commitment status; energy-balance and SOC checks | anything price-final; anything about the month |
| **D+1 provisional** | daily, `C_slow` | validated meter day, preliminary imbalance | day P&L by effect to within imbalance uncertainty; peak candidate confirmed or rejected; slippage and forecast buckets for the day | final imbalance cost; qualification status |
| **M+X final** | settlement calendar | final meter register, final imbalance, network invoice | the period's `NetworkPeakCharge`; qualification outcome; final P&L; `isFinal = true` | nothing further — this is the reference |

`X` is a configuration value per DSO and per BRP, not a constant.

### Rules

- The Planner never waits for final data. It consumes the latest state with
  `peakIsProvisional` and `unsettledGapFrom` set (C5 §2), and treats the unsettled
  gap with its own modelled trajectory rather than assuming it was empty.
- **Provisional never overwrites final** for the same settlement period
  (`INV-S-14`); `isFinal` is monotone per period.
- Restatement is **idempotent**: re-running settlement on unchanged inputs
  produces a byte-identical artefact and *no* new revision. A revision chain that
  grows without an input change is a defect.
- Error buckets computed at D+1 are marked provisional and recomputed at final.
  Attribution drawn from provisional buckets is directional evidence, not a
  verdict, and the distinction is carried in the artefact rather than in a
  convention.

---

## 4. P&L attribution by effect

Phase A computes one signed EUR figure per `EconomicEffect`, plus the mandatory
`unexplainedEur` bucket (C5 §5). Sign convention: **positive is revenue to the site**,
consistent with the battery frame (`02-conventions.md` §1), so charges and costs
are negative. `feesEur` are always explicit and never netted into a price (C4 §2).

Let `σ(Sell) = +1`, `σ(Buy) = −1`, and `Δt_h = 0.25` h. Every volume below is
already MWh and every price EUR/MWh, so no term carries a unit conversion
(`02-conventions.md` §2). The MiSpel registers are the one place a foreign unit
enters: they are transcribed in kWh because that is the unit the regulation
quotes them in (`00-overview/03-mispel-reference.md` §2), and are normalised to
MWh at dataload with everything else. There is no factor of 1000 in this layer.

```
SpotEnergyValue        = Σ_{f: market = Da}
                           σ(f.side) · f.priceEurPerMwh · f.volumeMwh − f.feesEur

IdEnergyValue          = Σ_{f: market ∈ {IdContinuous, IdAuction}}
                           σ(f.side) · f.priceEurPerMwh · f.volumeMwh − f.feesEur

ReserveCapacityRevenue = Σ_b ( awardedUpMw[b]·clearingPriceUpEurPerMwH[b]
                             + awardedDnMw[b]·clearingPriceDnEurPerMwH[b] ) · blockHours[b]
                         − Σ_{f: market = AfrrCapacity} f.feesEur
                         [ MW · EUR/MW/h · h = EUR ;  blockHours from the market
                           calendar, never a constant — block length is a product
                           attribute and changes ]

ReserveEnergyRevenue   = Σ_t ( activatedEnergyUpMwh[t]·activationPriceUpEurPerMwh[t]
                             − activatedEnergyDnMwh[t]·activationPriceDnEurPerMwh[t] )

ImbalanceCost          = Σ_t imbalanceCostEur[t]
                         [ taken from C4 §6 as authoritative, and cross-checked
                           against imbalanceVolumeMwh[t]·imbalancePriceEurPerMwh[t];
                           a mismatch is a reconciliation finding, not a silent
                           substitution ]

NetworkPeakCharge      = peak engine output (§5), negative

NetworkVolumetricCharge= − Σ_t pPoiMeteredImportMwh[t] · volumetricRate(t)

LeviesAndTaxes         = − Σ_t pPoiMeteredImportMwh[t] · Σ_c leviesRate_c(t)
                         [ closed component list of the components a delineation
                           regime cannot reduce — Stromsteuer, Konzessionsabgabe —
                           mirroring TariffView's enumeration; an unrecognised
                           invoice line is a configuration error, not an absorbed
                           cost ]

EnfgLevies             = − (21) · Σ_c enfgRate_c(month)
                         [ (21) = MAX[(3) − (16) − (19); 0], from §5.1. The
                           reducible EnFG components only. Charged on (21), never
                           on (3) — booking them on metered import overstates the
                           charge by the whole relief ]

SubsidyRevenue         = (32) · Σ_x ZFx · MAX[ AWx − marktwertMonthEurPerMwh ; 0 ]
                         [ Marktprämie per § 19 EEG. Spot revenue on the same MWh
                           stays in SpotEnergyValue, which is what makes the
                           double count impossible rather than merely checked.
                           Under a common EEG vintage (32x) = ZFx·(32), so the
                           per-plant sum collapses to one weighted premium ]

CycleDegradation       = − degCostEurPerMwh
                           · Σ_t (pBattChargeEnergyMwh[t] + pBattDischargeEnergyMwh[t]) / 2

StoredEnergyContinuation
                       = V(socMeasuredMwh[T], peakState_T, qualState_T)
                       − V(socMeasuredMwh[T₀], peakState_T₀, qualState_T₀)

ActivationRisk         = 0     (see below)
```

Three points that decide whether these numbers are comparable to plan at all:

- **Degradation base.** The `(charge + discharge)/2` throughput convention must be
  byte-identical to the one `OppCostView` charges on the `BatteryThroughput` base.
  If the two conventions differ by a factor, every day shows a constant model
  error and nobody will know why for a month.
- **Terminal value must use the same `V`.** `StoredEnergyContinuation` is evaluated
  with the *exact value-function artefact the Planner used*, addressed by hash from
  the run manifest (ADR-013). Using a refreshed `V` measures value-function drift
  and books it as P&L, which is wrong: drift belongs in the slow loop's own
  diagnostics, not in the day's ledger.
- **Risk terms are not cashflows.** `ActivationRisk` and the CVaR weighting on
  imbalance are decision-shaping penalties in the objective; ex post there is no
  risk, only an outcome, which has already been booked to `ReserveEnergyRevenue`
  and `ImbalanceCost`. Realised `ActivationRisk` is therefore identically zero, and
  the **four-bucket decomposition uses the risk-free objective on both sides**.
  `PlanResult` reports the objective gross and net of risk terms for exactly this
  reason. Comparing a risk-loaded planned value against a risk-free realised value
  makes the engine appear to lose the risk premium every single day.

`unexplainedEur` is the residual of `INV-S-02`: total realised P&L less the sum over
effects. It is mandatory, it is never absorbed into a neighbouring effect, and
`unexplainedRatio` is alerted on (`INV-S-07`). A rising ratio is the earliest
available signal that a term definition has drifted between L2 and L5.

---

## 5. The peak accounting engine

Per active tariff regime (ADR-011). Regimes compose; each produces its own
accounting period, its own slot set and its own charge, tagged with its own
effect so the composer's exclusivity rules apply on this side too.

Instantaneous POI power in the tariff's own frame, floored at zero because the
charge is levied on import (`02-conventions.md` §1):

```
pPoiImportMw[t]    = max( (pPoiMeteredImportMwh[t] − pPoiMeteredExportMwh[t]) / Δt_h , 0 )
pPoiRealisedPeakMw = max over t ∈ periodSlots(regime) of pPoiImportMw[t]
```

| Regime | `periodSlots` | Charge |
|---|---|---|
| `AnnualLeistungspreis` | all slots in the local calendar year | `peakPriceEurPerMw · pPoiRealisedPeakMw` |
| `MonthlyLeistungspreis` | all slots in the local calendar month | `peakPriceEurPerMw · pPoiRealisedPeakMw` per month |
| `AtypicalHlzf` (§19(2) S.1) | HLZF slots only, from the DSO's published window table for that year | `hlzfPeakPriceEurPerMw · pPoiHlzfPeakMw` |
| `IntensiveUse` (§19(2) S.2) | all slots in the qualification year | reduced rate conditional on qualification |

### Intensive-use qualification tracking

```
pPoiAnnualEnergyMwh = Σ_{t ∈ year} pPoiMeteredImportMwh[t]
pPoiAnnualPeakMw    = max_{t ∈ year} pPoiImportMw[t]
fullLoadHours   = pPoiAnnualEnergyMwh / pPoiAnnualPeakMw          [ MWh / MW = h ]  INV-S-06
flhMargin       = fullLoadHours   − flhThreshold          [ threshold config, 7,000 h ]
energyMargin    = pPoiAnnualEnergyMwh − energyThreshold       [ threshold config, 10 GWh ]
qualificationMarginHours = min(flhMargin, energyMargin / pPoiAnnualPeakMw)
```

Battery operation moves the numerator **and** the denominator (ADR-011), so
qualification is not a passive observation — every peak shaved and every MWh
imported changes it. Both thresholds bind, so the published margin is the binding
one, expressed in hours so the value function sees a single continuous distance to
the cliff.

**The margin, not the status, is the output that matters.** A boolean
`qualificationStatus` tells the engine nothing until it is too late to act; the
margin lets `V` price the approach continuously and drives `qualCritical`, which
forces tier escalation and the protective bound (`INV-S-13`, C5 §3).

### Calendar rules

- **Period boundaries are local-calendar boundaries.** They are derived only via
  the `CivilCalendar` service against the manifest's pinned tzdata
  (`02-conventions.md` §4.2, `INV-S-12`). No UTC-offset arithmetic anywhere in the
  peak engine.
- A civil day has 92, 96 or 100 slots. Any sum or maximum over "a day" iterates
  the calendar's slot set for that day, never a fixed count. Both DST transition
  days are permanent fixtures in the peak-engine test set.
- HLZF windows are local-time windows from a data table (ADR-002), resolved to
  `SlotId` sets through the same service.
- `pPoiRealisedPeakMw` is non-decreasing within a period and resets **exactly** at
  the `SlotId` that `CivilCalendar` resolves the Europe/Berlin local period
  boundary to — a UTC instant, computed once, never a UTC offset applied to a
  local timestamp (`02-conventions.md` §4.2, `INV-S-01`). An off-by-one-hour reset
  at a DST boundary either destroys a period's accumulated peak or carries it into
  the next one, and both are silent.

---

## 5.1 The delineation accounting engine

Per active delineation regime (ADR-011, ADR-017). Evaluates the machinery of
`00-overview/03-mispel-reference.md` **exactly**, from `Z1`/`Z2` and the tariff sheet
alone. This is the reference against which Valuation's linearisation is measured, and
it is the only place in the system where the monthly formulas are evaluated.

```
Z1NB¼ = pPoiMeteredImportMwh[t]          Z2V¼ = pBattChargeEnergyMwh[t]
Z1NE¼ = pPoiMeteredExportMwh[t]          Z2E¼ = pBattDischargeEnergyMwh[t]

(1)¼ = MIN[Z1NB¼; Z2V¼]     (2)¼ = MIN[Z1NE¼; Z2E¼]     (23)¼ = Z1NE¼ − (2)¼
(24)¼ from the settled day-ahead price series and the EEG vintage's § 51 / § 51b
       duration rule — a realised parameter here, never a belief
```

Monthly aggregation runs `(3)`–`(33)` unchanged from the reference. Two identities
are asserted rather than assumed, because they are cheap and they catch a whole class
of sign and attribution error (§9):

```
(28) + (16) = (13)                    the route split
(16) + (19) = (16) · (5)/(6)          the loss-relief multiplier
```

**Period boundaries are Europe/Berlin civil-calendar month boundaries**, derived
through `CivilCalendar` under the manifest's pinned tzdata exactly as the peak engine's
are (`INV-S-12`). The seven accumulators reset at the boundary; a month containing a
DST transition is a permanent fixture in the test set.

`(12)` is computed unconditionally even though `INV-S-16` asserts the throughput
condition under which it must be zero. Settlement never assumes what it can measure —
the assumption belongs to the Planner, and a violated month must show as a booked cost
and a fired invariant rather than as a silent divergence.

The A5 case adds only `ZF`. Under a common EEG vintage the AW forfeit is simultaneous,
so `(24x)¼ = (24)¼`, hence `(32x) = ZFx·(32)` and no per-plant accumulator exists.

---

## 6. The four-bucket error decomposition

The centrepiece. Every layer is a pure function of a recorded input (ADR-013), so
each bucket is computable by re-running a layer with **exactly one thing changed**.
That is the entire justification for the purity discipline in
`00-overview/01-system-model.md` §1.

### Notation

```
b        the BeliefSnapshot used at the tick                       (recorded C1)
b*       the outturn belief: same series and schema, forecast values replaced by
         settled outturn, read from the bitemporal store at a later knowledge time
         (ADR-004 makes this a legitimate read, not a lookahead violation)
Score(x ; β)   L2's own term set at belief β, EVALUATED on trajectory/position
               vector x, risk-free → EUR.  Possible because C2 is a set of declared
               terms, so the objective can be evaluated as well as maximised.
x̂        the planned trajectory and positions                      (PlanResult)
x̂₁       the Tier-1 plan on the same belief b                      (ADR-010 oracle)
x        the realised trajectory and positions                     (C4)
Book(x)  Phase A's accounting of x  =  Σ_e realisedByEffect[e] + unexplained
```

Four reference values, each differing from its neighbour in one dimension:

```
Ĵ₁ = Score(x̂₁ ; b)     what the accuracy reference claimed, on the belief
Ĵ  = Score(x̂  ; b)     what the plan we ran claimed          = Σ_e plannedByEffect[e]
A  = Score(x̂  ; b*)    the same plan, scored on the outturn
C  = Score(x   ; b*)    what we actually did, scored on the outturn
J  = Book(x)           what Settlement booked
```

### The buckets

| Bucket | Definition | Counterfactual re-run | Large value means | Fix owned by |
|---|---|---|---|---|
| `optimalityGapEur` | `Ĵ₁ − Ĵ` | Re-solve the tick at Tier 1 on the identical C2 bundle, same seed, same `SolveBudget` shape; compare objectives | The fast path is costing real money on this class of day | L3 / ADR-010 |
| `forecastErrorEur` | `Ĵ − A` | Re-score the *unchanged* plan against `b*` | The belief carried value the world did not deliver | L1 / scenario model (ADR-005) |
| `executionSlippageEur` | `A − C` | Score plan versus realised positions under one common belief `b*` | We could not get the trades, or got them worse | Quoting policy (ADR-012) |
| `modelErrorEur` | `C − J` | Value the realised trajectory with L2's terms and compare against Phase A's independent accounting of the same trajectory | Valuation and Settlement disagree about what a term means | L2 |

`modelErrorEur` carries a declared sub-bucket, `linearizationGap` — see below. Residual
`modelErrorEur` net of it is still asserted zero.

By construction these telescope:

```
optimalityGapEur + forecastErrorEur + executionSlippageEur + modelErrorEur
  = (Ĵ₁ − Ĵ) + (Ĵ − A) + (A − C) + (C − J)
  = Ĵ₁ − J
```

**`planned` in `INV-S-03` is the Tier-1 reference value `Ĵ₁`.** When Tier 1 was the
tier actually used, `Ĵ₁ = Ĵ`, `optimalityGapEur = 0`, and the sum collapses to
C5 §5's `plannedByEffect` total minus realised. Both `Ĵ₁` and `Ĵ` are recorded;
anchoring at `Ĵ₁` is what gives `optimalityGapEur` somewhere to live, since a weak
tier lowers the planned *and* the realised side and is invisible in `Ĵ − J`.

### Sub-decompositions worth carrying

- `executionSlippageEur` splits into `priceSlippage` (fills valued at intent price
  versus executed price — C5 §6's gloss, and the narrowest reading), plus
  `unfilledOpportunity` (residual volume from C4 §3, valued at `b*`), plus
  `feeDrag`, plus `deliveryDeviation` (the controller's departure from setpoint
  within the corridor). Without the unfilled term the bucket cannot separate "we
  were wrong about value" from "we could not get the trade", which is precisely why
  C4 §3 exists.
- `linearizationGap` splits out of `modelErrorEur`: L2's `λ`-linearisation of the
  delineation machinery, scored on the realised trajectory, against §5.1's exact
  evaluation of the same trajectory. It is **expected, budgeted and reported**, not a
  defect. Without the split, `INV-S-10` fails permanently from the first day the
  delineation regime is active, and the most valuable test in the layer stops meaning
  anything. It is interpretable as exactly one thing — projection error, since `V_del`
  is evaluated arithmetically and never fitted (ADR-017) — which is what makes a budget
  for it meaningful rather than arbitrary.
- `valueOfForesight = Score(Plan(Val(b*)) ; b*) − A` is computed and reported
  alongside `forecastErrorEur`. It answers a different question — what perfect
  information would have been worth — and it is the ADR-010 upper bound that
  separates "our optimiser is weak" from "the world is uncertain". It is **not**
  booked as a bucket, because it does not telescope.

### The residual

The identity above is exact in algebra and inexact in practice, and the buckets are
computed by **independent re-runs rather than by differencing**, specifically so
that the residual is a test of the pipeline instead of a definition:

```
unexplainedErrorEur = (Ĵ₁ − J) − (optimalityGapEur + forecastErrorEur
                                + executionSlippageEur + modelErrorEur)
```

`INV-S-03` warns when `|unexplainedErrorEur|` exceeds tolerance. Legitimate sources
are bounded and enumerable: `b*` is itself an estimate while imbalance prices are
provisional; a term-set version changed between plan and settlement; tick-size
rounding at C3 (`02-conventions.md` §6 — which is why rounding happens once and
only there). Anything else is a defect. `unexplainedErrorEur` is a distinct quantity
from C5 §5's `unexplainedEur`, which is the *effect-level* residual under `INV-S-02`;
the two are never netted and never reported as one number.

---

## 7. Attribution by strategy tag

Phase B resolves each `fillId → intentId → StrategyTag` through the recorded C3
artefact and aggregates P&L per tag: `Arbitrage`, `PeakShave`, `ReserveHedge`,
`Rebalance`, `CommitmentCover`. This answers "which strategy made money", which is
the question that decides where the next month of engineering goes.

Two classes of effect, handled differently:

- **Directly attributable** — `SpotEnergyValue`, `IdEnergyValue`,
  `ReserveCapacityRevenue`, `ReserveEnergyRevenue`, and their fees. Each cashflow
  descends from one fill and therefore one tag.
- **Joint** — `NetworkPeakCharge`, `CycleDegradation`,
  `StoredEnergyContinuation`, `ImbalanceCost`. These are properties of the whole
  trajectory and are not the sum of per-order contributions. They are allocated by
  a **declared rule**: counterfactual removal of the tag's fills, re-run through
  the peak and SOC accounting, normalised across tags. The normalisation residual
  is booked to an explicit `unallocated` line, never smeared proportionally.

Stating the allocation rule is not pedantry. Peak charge attributed naively makes
`PeakShave` look free and `Arbitrage` look profitable, which is the exact
misreading that leads to turning peak protection down.

`CommitmentCover` is read together with `INV-P-10`: an intent priced through
`shadowValueEurPerMwh` is legitimate only under that tag, and Phase B reports the realised
cost of cover as a separate line so that "we had to buy back at any price" is
visible as an operational failure rather than as a bad arbitrage day.

---

## 8. Conditioning on operating mode and tier

P&L and every error bucket are reported conditioned on:

- **`DegradationMode`** (ADR-014), using `ticksByMode` and `modeTransitions`
  from C5 §7. A `DEFENSIVE` week is *supposed* to earn less; without conditioning,
  a data-feed outage looks like a strategy regression and gets "fixed" by loosening
  risk parameters, which is the worst available response.
- **Tier used** (ADR-010), from `PlanResult`. `optimalityGapEur` conditioned on tier
  is the number that decides whether the fast path ships, and per ADR-010 it is
  reported as **p50 / p90 / p99 and worst case**, never as a mean, and conditioned
  on regime — high-spread days, peak-critical days, high reserve-price days.
- **Escalations**, so "we were on the fast path that day" is always visible
  (`L3-planner.md` §3).

The output is a small cube: `(effect × tag) × (mode × tier)`. Its purpose is to
make "a bad week" answerable as attribution rather than as opinion, and its cells
must be reported with tick counts, because a mode occupied for four ticks explains
nothing regardless of its P&L.

---

## 9. Reconciliation checks

Run in Phase A, before anything is sealed. These are cheap, they are the layer's
own self-test, and several are the earliest available warning of a model defect.

| Check | Assertion | Signal |
|---|---|---|
| **Energy balance** | `pPoiMeteredImportMwh − pPoiMeteredExportMwh = meteredLoadMwh − meteredPvMwh + pBattChargeEnergyMwh − pBattDischargeEnergyMwh` within meter tolerance (`INV-X-03`). Metered PV is post-curtailment `pv_out`; the battery energies are AC-terminal, so **no η appears** (conventions §3) | Sub-metering fault, or a sign convention inverted somewhere |
| **SOC consistency** | `socMeasuredMwh[t+1] ≈ socMeasuredMwh[t] + η_c·charge[t] − discharge[t]/η_d` (`INV-X-05`) | **The early-warning signal for efficiency and SOH model drift.** Persistent one-signed divergence means η or the degradation model is wrong, and it feeds `modelErrorEur` directly |
| **Fee reconciliation** | Σ of C4 `feesEur` equals fees booked per effect; no fee netted into a price (`INV-S-11`) | Broker or venue fee schedule changed, or a price arrived net |
| **No phantom fills** | Every `fillId` maps to an `intentId` submitted this run (`INV-X-01`) | Adapter reconciliation failure — see `L4-execution-boundary.md` §6 |
| **Award coverage** | Every award has a commitment-ledger entry (`INV-X-06`) and every confirmed award a `feasibilityRequirement` (`INV-S-05`) | Ledger drift |
| **Delivery** | `deliveryShortfallMwh = 0` (`INV-S-04`) | Reserve delivery failure — prequalification risk, alert and `HALT` |
| **Peak monotonicity** | `pPoiRealisedPeakMw` non-decreasing in period, resets at the local boundary (`INV-S-01`) | Calendar or DST defect |
| **FLH identity** | `fullLoadHours = pPoiAnnualEnergyMwh / pPoiAnnualPeakMw` (`INV-S-06`) | Unit error in the qualification path |
| **Route split** | `(28) + (16) = (13)` (`INV-S-17`) | A `MIN`/`MAX` branch implemented with the wrong comparison |
| **Relief multiplier** | `(16) + (19) = (16)·(5)/(6)` (`INV-S-17`) | `(18)` or `(17)` mis-derived |
| **Throughput bound** | Charge throughput above `η_d·E_usable/(1−η_rt)` implies `(12) = 0` (`INV-S-16`) | An idle month, or a SOC/meter disagreement large enough to fake one |
| **Levy base** | `EnfgLevies` charged on `(21)`, never on `(3)` | The single most expensive available mistake in this layer: it overstates the charge by the whole relief |

`INV-X-05` deserves its emphasis. It costs almost nothing to evaluate, it needs no
market data, and it is the only check that catches a slowly wrong efficiency model
before that model has quietly mispriced a quarter of arbitrage decisions.

---

## 10. New invariants

| ID | Invariant | On failure |
|---|---|---|
| `INV-S-09` | Restatement is append-only: a revision is a new artefact with `revisionOf` set, the superseded artefact is retained, and the chain is acyclic | `HALT` |
| `INV-S-10` | Zero-gap: settling the planned trajectory as if realised yields all four error buckets zero within tolerance | fail the run |
| `INV-S-11` | Fees reconcile: Σ C4 `feesEur` = Σ fees booked by effect; no fee netted into a price | warn, escalating to alert |
| `INV-S-12` | Every accounting period boundary and HLZF window is derived via `CivilCalendar` under the manifest's pinned tzdata; no UTC-offset arithmetic in the peak engine | `HALT` |
| `INV-S-13` | While an `IntensiveUse` regime is active, `qualificationMarginHours` is published every tick and reflects both the FLH and the annual-energy threshold | `HALT` |
| `INV-S-14` | `isFinal` is monotone per settlement period; a provisional artefact never supersedes a final one | `HALT` |
| `INV-S-15` | Per-tag P&L plus `unallocated` sums to `realisedByEffect` per effect | warn |
| `INV-S-16` | While monthly charge throughput exceeds `η_d·E_usable/(1−η_rt)`, `(12) = 0`. Settlement computes `(12)` regardless and publishes `throughputBoundMet` | warn, escalating to alert |
| `INV-S-17` | The delineation identities hold: `(28)+(16) = (13)` and `(16)+(19) = (16)·(5)/(6)`, within meter tolerance | `HALT` |
| `INV-S-18` | The seven delineation accumulators are derived in Phase A from `Z1`/`Z2` and the settled price series alone; no belief, forecast or planned quantity enters them | `HALT` |

---

## 11. Testing this layer

Detail in `04-compliance/T2`. Settlement is the easiest layer in the system to
test well, because it is pure, because conservation laws apply, and because the
right answer is often computable by hand.

- **Determinism** — same C4 and same manifest, byte-identical C5 and
  `SettlementResult`.
- **Conservation** — total booked P&L equals the sum over effects plus
  `unexplainedEur` (`INV-S-02`); energy in equals energy out within meter tolerance;
  the sum of per-tag P&L equals the per-effect total (`INV-S-15`). Property-tested
  over generated outcomes, not spot-checked.
- **Zero-gap** — feed Settlement the **exact planned trajectory as realised**:
  every intent filled at its limit price, metered series equal to the planned POI
  trajectory, imbalance zero. Assert `optimalityGapEur = forecastErrorEur =
  executionSlippageEur = modelErrorEur = unexplainedErrorEur = 0` within tolerance
  (`INV-S-10`). This is the single most valuable test in the layer: any non-zero
  bucket is a definitional disagreement between L2 and L5 that would otherwise
  masquerade as a real finding for months.
- **Synthetic day** — a hand-constructed day with flat load, one price spike, one
  aFRR block and one peak event, where every effect and every bucket is computable
  analytically. It is the fixture that catches unit errors, and it is worth the
  afternoon it costs to build.
- **Restatement idempotency** — settle, restate with unchanged inputs, assert no
  new revision; restate with a corrected imbalance price, assert exactly one new
  revision, the prior artefact intact, and the delta confined to `ImbalanceCost`
  and the buckets that legitimately depend on it.
- **Provisional-to-final convergence** — replay a period through
  intraday → D+1 → final and assert the buckets converge monotonically in the
  magnitude of their own declared uncertainty. Divergence means a provisional stage
  is concluding something it has no data to conclude.
- **Calendar** — both DST transition days, a month boundary, and a year boundary
  as permanent fixtures; peak reset asserted to the exact slot (`INV-S-01`,
  `INV-S-12`).
- **Co-location identity** — the cheapest exact test in the layer. Settle a month with
  `load = 0`, no clipping of discharge and `(12) = 0`, and assert `(21) = 0` **to the
  cent**. The identity `(16)+(19) = (9) = (3)` holds independently of η, of the dispatch
  pattern, of `AW` and of prices, so any deviation localises a defect in the delineation
  chain with no modelling judgement involved.
- **Route split** — property-tested over generated months: `(28)+(16) = (13)` and
  `(16)+(19) = (16)·(5)/(6)`. Both are algebraic identities of the published formulas,
  so a failure is always an implementation defect and never a data artefact.
- **A5 collapse** — two plants on a common EEG vintage; assert `(32x) = ZFx·(32)` exactly
  and that no per-plant accumulator is required. Then a mixed case with one ungeförderte
  plant, asserting the collapse is **refused** rather than silently applied.
- **Month boundary** — accumulators reset at the exact slot of the Europe/Berlin month
  boundary, with a DST-containing month as a permanent fixture. A boundary off by one
  slot moves value between two months and is otherwise silent.
- **Qualification cliff** — construct a year that lands just above and just below
  each threshold; assert the margin is continuous through the crossing, that
  `qualCritical` fires at the configured distance, and that a single trade cannot
  cross the cliff without the flag having fired first.
- **Attribution invariance** — permute the order of fills within a slot; every
  bucket and every tag total must be unchanged. Order sensitivity here means an
  accumulator has hidden state, which would break purity.
- **Counterfactual soundness** — on a recorded day, re-run each bucket's
  counterfactual twice and assert byte-equality, and assert `optimalityGapEur ≥ 0`
  over a backtest in aggregate. Note that a *single-day* `optimalityGapEur` may be
  negative — a worse plan can get lucky — which is why ADR-010 requires a
  distribution rather than a point.
