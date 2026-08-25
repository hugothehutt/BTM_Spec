# T5 — Optimality Gap and Performance

Normative. The operational procedure for measuring how much value the shipped
co-optimisation tier gives up against the oracle, and what it costs in solve
time.

---

## 1. The discipline this level enforces

ADR-010 states it without qualification:

> **No tier ships without its gap measured against Tier 1.**

`T5` is the procedure that produces that measurement, and the release gate that
enforces it. The argument behind the rule is short: Tier 1 must exist even though
it may never run in production, because without an oracle there is no gap, and
without a gap there is no engineering, only hope.

Three quantities are produced per release, and they answer three different
questions.

| Quantity | Question | Fix lives in |
|---|---|---|
| Tier `k` → Tier 1 gap | How much does the fast path cost us? | `L3`, the tier itself |
| Tier 1 → perfect foresight distance | How much is our optimiser weak, versus the world being uncertain? | `L1` / the scenario model, versus `L3` |
| Solve time and objective as functions of `S` and breakpoint count | Are the configured knobs at the right point on the accuracy/speed curve? | Configuration, evidenced |

---

## 2. The Tier 1 oracle run

### 2.1 What Tier 1 is for

Tier 1 is the joint stochastic MILP of ADR-010: reserve capacity `rUp[b]`,
`rDn[b]` as decision variables alongside spot dispatch, with the coupling
constraints explicit. It is the **accuracy reference**. It does not need to be
fast. Its primary job is to be right and to serve as the oracle everything else
is scored against, and **it runs offline on every backtest day regardless of
which tier ships**.

### 2.2 Procedure

```
procedure Tier1OracleRun(corpus, manifest):
    for day in corpus:
        for tick in ticks(day):
            bundle = recorded_C2(tick)           # identical input for all tiers
            state  = recorded_StateSnapshot(tick)

            r1 = CoOptimizer.Solve(bundle, state, tier=1,
                                   budget = ORACLE_BUDGET)     # generous
            require r1.status in {Optimal, GapLimited}
            require r1.mipGap <= ORACLE_GAP_TOL                 # default 1e-4
            record Oracle(tick) = { objective, trajectory, rUp, rDn,
                                    mipGap, solveTime, status }
```

Rules that make the comparison valid:

| Rule | Reason |
|---|---|
| Every tier is solved on the **identical recorded `C2` bundle and `StateSnapshot`** | The three tiers share one interface (`ICoOptimizer`), so a backtest can run all three on the same data (ADR-010). Anything else measures input differences, not tier differences |
| The oracle budget is generous but **bounded and recorded** | An oracle that timed out at a 3 % gap is not an oracle; the recorded `mipGap` is part of the gap arithmetic (§3.4) |
| Rounding to tick and lot sizes happens **only** at `C3` (`INV-P-04`) | Rounding earlier corrupts the optimality-gap measurement — this is stated in `00-overview/02-conventions.md` §6 and it is this measurement that it protects |
| The oracle runs under the reproducibility manifest | A non-reproducible oracle makes every gap unfalsifiable (`T3` §5.3) |
| Ticks where Tier 1 fails to reach `ORACLE_GAP_TOL` are reported separately, never dropped | Silently dropping the hard instances biases every reported gap downward, and the hard instances are the ones that matter |

### 2.3 Gap definition

For a maximisation objective, per tick:

```
gap_abs(k)  =  Oracle.objective − Tier_k.objective                  [EUR]
gap_rel(k)  =  gap_abs(k) / max(|Oracle.objective|, FLOOR_EUR)      [fraction]
```

`FLOOR_EUR` is a configured denominator floor. Without it, a tick whose oracle
objective is near zero produces an unbounded relative gap and poisons every
percentile. Both the absolute and the relative gap are reported; **the absolute
gap in EUR is the one that gates**, because a 40 % relative gap on a EUR 3 tick
is noise and a 2 % relative gap on a peak-critical day is not.

`gap_abs(k) < 0` — the tier beating the oracle — is possible only through an
oracle gap tolerance or a defect. It is reported as a **finding**, never
truncated to zero, and the arithmetic in §3.4 accounts for it.

---

## 3. Gap distribution

### 3.1 Report p50 / p90 / p99 / worst — never a mean

ADR-010 is explicit: reported as **p50 / p90 / p99 and worst case, never as a
mean, because the tail is where a fast heuristic destroys value**.

The reason is structural, not stylistic. The gap distribution of a heuristic is
sharply right-skewed: near zero on the many days when nothing is binding, and
large on the few days when reserve, peak and spot compete for the same headroom.
A mean over that distribution has three defects:

1. **It is dominated by the uninteresting mass.** Ninety ordinary days at a gap
   of EUR 0.40 and ten critical days at EUR 300 produce a mean of EUR 30.36,
   which describes no actual day and hides the EUR 300.
2. **It is not robust.** One oracle timeout, one negative gap, one outlier day
   moves it. Percentiles do not move.
3. **It answers the wrong question.** Nobody needs to know the average cost of
   the heuristic. They need to know the cost on the day it goes wrong, because
   that day is when the operator, the risk function and the P&L all find out at
   once. A heuristic that is fine on average and terrible on the ten days that
   matter is worse than useless — it is worse than not having the tier at all,
   because it has been trusted.

The report therefore carries, per tier:

| Statistic | Units | Notes |
|---|---|---|
| p50 | EUR and % | The typical day |
| p90 | EUR and % | |
| p99 | EUR and % | |
| **worst** | EUR and % | Named: which day, which tick, which regime, what was binding |
| Count of ticks in the tail above a configured EUR threshold | count | |
| Cumulative gap over the corpus | EUR | The only aggregate that is meaningful, because it is a sum and not an average |
| Negative-gap count | count | Findings, per §2.3 |

The **worst case is always named**, with a link to the recorded seam. An
unnamed worst case is a number nobody can act on; a named one is a fixture
(`T3` §7.1) and usually a bug report.

### 3.2 Gap conditioned on regime

ADR-010 requires the gap conditioned on regime. The regimes are declared, derived
from recorded data rather than from the tier's own output, and every one is
reported with the full percentile set.

| Regime | Definition (from recorded `C1` / `C5`) | Why it is separated |
|---|---|---|
| **High-spread days** | Intraday max-minus-min of `daPriceEurPerMwh` (ensemble mean) above the corpus 90th percentile | Where the arbitrage term is large and a mis-priced headroom transfer costs most |
| **Peak-critical days** | `peakCritical` set, or the modelled POI trajectory within a configured margin of `pPoiRealisedPeakMw` | The peak charge is the largest single downside in the BTM case; a heuristic that trades a peak breach for a spread is catastrophic here and invisible on average |
| **High reserve price days** | `afrrCapPriceEurPerMwH` block mean above the corpus 90th percentile | Where the reservation price `μ` matters most and Tier 3's learned surface is most likely to be out of distribution |
| **Qualification-critical days** | `qualCritical` set (ADR-011) | Losing a year of network-charge reduction for a day's spread is the single largest downside event in the business case |
| **Negative-price episodes** | Any slot with `daPriceEurPerMwh < 0` in the ensemble mean | Where `INV-P-07` becomes non-redundant and the LP relaxation can misbehave |
| **High activation days** | Realised activation fraction above the 90th percentile | Commitment feasibility under stress |
| **DST transition days** | From the calendar | Calendar-driven formulation errors surface as a gap |
| **Degraded-mode days** | Any tick not in `NORMAL` | Gap under a widened risk profile is a different measurement |
| **Ordinary** | The complement | The baseline the others are read against |

A tier passes only if it passes **per regime**, not merely in aggregate. The
release gate (§7) states the thresholds.

### 3.3 The escalation interaction

`ICoOptimizer` escalates at runtime: Tier 3 escalates to Tier 2 when the solution
sits near a coupling constraint boundary, the state is outside `μ̂`'s training
envelope, `peakCritical` or `qualCritical` is set, or `V` is stale (`L3` §3).
This means the *shipped* gap is not Tier 3's gap — it is the gap of the escalation
policy.

Three gaps are therefore reported for a Tier-3 production configuration:

| Reported gap | Definition | Use |
|---|---|---|
| `gap(T3, pure)` | Tier 3 forced, escalation disabled | Measures the learned surface `μ̂` itself |
| `gap(T2)` | Tier 2 on every tick | The escalation target's quality |
| `gap(shipped)` | The escalation policy as configured | **The number that gates the release** |

Plus the escalation rate, by trigger, by regime. A configuration that escalates
on 60 % of ticks is a Tier 2 deployment with extra steps and its latency budget
should be assessed as such (ADR-015 OPEN-2).

### 3.4 Gap arithmetic hygiene

```
# the oracle's own gap is part of the measurement, not ignored
true_gap(k) ∈ [ gap_abs(k) − Oracle.mipGap_abs ,  gap_abs(k) + Oracle.mipGap_abs ]

report gap_abs(k) with the oracle-gap band attached
require Oracle.mipGap_abs << p50(gap_abs(k))    # else the oracle cannot resolve
                                                # the quantity being measured
```

If the oracle's own MIP gap is comparable to the tier gap being measured, the
measurement is not meaningful and the report says so rather than printing a
number. Tightening `ORACLE_GAP_TOL` or shrinking the instance is the remedy.

### 3.5 Reconciliation with `C5`

`C5` §6's `optimalityGapEur` bucket is defined as *re-run the tick at Tier 1 and
compare objective values* — the same quantity. `T5` asserts that the gap it
measures and the `optimalityGapEur` bucket agree over the same period, within
tolerance. A divergence means the counterfactual re-run and the oracle run are
not using the same inputs, and it is a defect in one of them.

---

## 4. The perfect-foresight upper bound

### 4.1 What it is

Solve the same day with the realised path known: a single deterministic scenario
equal to what actually happened, all other inputs identical.

```
procedure PerfectForesightBound(day):
    realised = construct_single_scenario_from(C4 outcomes, C5 settlement,
                                              metered load, metered pv,
                                              realised prices, realised activation)
    bundle_pf = recompose_C2(belief = realised as a degenerate 1-scenario ensemble,
                             everything else identical)
    pf = CoOptimizer.Solve(bundle_pf, state, tier=1, budget = ORACLE_BUDGET)
    record PF(day) = pf.objective
```

Constraints on the construction, because it is easy to make it meaningless:

- **Same feasible set.** Physical bounds, POI envelope, commitments and
  regulatory constraints are identical. Perfect foresight removes uncertainty,
  not physics. A PF run that also relaxes a bound measures nothing.
- **Same market mechanics.** Gate structure and non-anticipativity across *market
  stages* remain: PF knows the future, but the DA gate still closes before the
  intraday window. Removing the staging turns the bound into a fantasy and
  inflates the reported uncertainty cost.
- **Confirmed commitments still bind.** The ledger is history, not a decision.
- **It is a bound, not a target.** No configuration change should ever be
  justified by "it closes the gap to PF".

### 4.2 What the Tier1-to-PF distance separates

This is the diagnostic ADR-010 names, and it is the most useful single number in
the report.

```
                 PF.objective
                      │
                      │   ← "the world is uncertain"
                      │      irreducible under the current information set
                      │      FIX LIVES IN: L1, the scenario model, data sources
                      │
              Tier1.objective
                      │
                      │   ← "our optimiser is weak"
                      │      reducible by better optimisation
                      │      FIX LIVES IN: L3, the tier ladder
                      │
              Tier_k.objective
```

| Observation | Reading | Where the effort should go |
|---|---|---|
| Tier1 → PF large, Tier k → Tier 1 small | The optimiser is close to the best achievable under uncertainty; the loss is informational | Forecast quality, ensemble structure, scenario count. Optimisation work here has almost no headroom |
| Tier1 → PF small, Tier k → Tier 1 large | The problem is nearly deterministic and the heuristic is leaving money on the table | The tier ladder. This is the case where escalation thresholds or `μ̂` need work |
| Both large | Two independent problems | Address them separately; do not let one hide behind the other |
| Both small | The system is near its ceiling on this corpus | Look for a missing lever. `L2` §3 names the remaining gaps; note that self-consumption is *not* one of them — it is priced through the delineation accumulators (ADR-017), and a plan that over-consumes behind the meter shows up as a `λ_11` misprice rather than as a missing view |

Without this decomposition, a team spends quarters improving an optimiser whose
remaining gap is entirely forecast uncertainty, or tuning forecasts to close a
gap that is pure heuristic loss. The bound costs one extra Tier 1 solve per day
and it settles the argument.

### 4.3 Reporting

PF distance is reported with the same percentile discipline as the gap — p50,
p90, p99, worst, per regime — and the ratio
`(PF − Tier1) / (PF − Tier_k)` is reported per regime as the **headroom split**.

---

## 5. Tier 2 dual-bound certification per solve

Tier 2's Lagrangian decomposition gives, at any iteration, a bound on the true
optimum. ADR-010 calls this out as one of the three properties that make it the
centre of the design: *the gap to the best primal solution is a number you can
report, not a hope.*

Unlike the Tier-1 gap, this certificate is available **per solve, at runtime, in
production**, with no oracle present.

### 5.1 Per-solve certificate

```
CoOptResult (tier 2) carries:
    primal        : feasible objective value            (a real, deliverable plan)
    dualBound     : Lagrangian upper bound
    certifiedGap  : dualBound − primal                  [EUR, ≥ 0]
    certifiedRel  : certifiedGap / max(|primal|, FLOOR_EUR)
    iterations    : bundle-method iterations used
    exitReason    : Converged | IterationCap | TimeCap | NumericalTrouble
    muStar        : μ_up[b], μ_dn[b] — the reservation prices
```

### 5.2 Assertions on every Tier 2 solve

```
assert certifiedGap >= −MONEY_TOL          # a dual bound below the primal means
                                           # the bound is invalid — a defect in
                                           # the decomposition, not a tight gap
assert primal is feasible                  # EVERY exit path, including TimeCap
                                           # (L3 §3: "a decomposition that can
                                           #  return infeasible is not usable")
assert dualBound is finite
assert muStar is finite and non-negative   # a reservation price for headroom
if exitReason != Converged: record it in PlanResult and in the settlement record
```

`certifiedGap < 0` is the assertion that earns its keep. A dual bound that dips
below the primal is a broken relaxation — a coupling constraint relaxed with the
wrong sign, or a subproblem solved to the wrong sense — and it invalidates every
bound the tier has ever reported. It is a `HALT`.

### 5.3 Certificate quality, reported

| Statistic | Reported | Gate |
|---|---|---|
| `certifiedRel` distribution | p50 / p90 / p99 / worst, per regime | p99 within the configured band |
| Exit-reason mix | Counts of `Converged` / `IterationCap` / `TimeCap` / `NumericalTrouble` | A rising `NumericalTrouble` rate blocks release |
| Iterations to converge | Distribution, and versus instance size | Feeds the latency evidence for ADR-015 OPEN-2 |
| Certificate vs. truth | On the oracle corpus: `certifiedGap` versus the *measured* `Tier1 − Tier2` gap. The certificate must dominate — `certifiedGap ≥ measured gap − tol` | A certificate that under-states the true gap is worse than no certificate |
| `μ*` plausibility | `μ*` compared against the spot opportunity cost it is supposed to equal; reviewed by a human | ADR-010: `μ*` is diagnostically valuable precisely because a human can sanity-check it |

The `μ*` review is not ceremony. Tier 2's central claim is that `μ*` is
*provably consistent* with spot opportunity cost rather than a heuristic
weighting. A `μ*` of EUR 400/MW/h on a flat, low-price day contradicts that claim
and points at the subgradient step-size rule or a mis-scaled coupling row.

---

## 6. Scaling curves

ADR-005 requires reporting objective value and solve time as a function of
scenario count `S`, so the choice is evidenced rather than guessed. ADR-008 makes
the same requirement for PWL breakpoint count. ADR-015 OPEN-2 names this as the
instrumentation to build now so the latency decision is evidenced when it is
taken.

### 6.1 Sweeps

```
procedure ScalingCurves(instances, manifest):
    for S in {1, 4, 8, 16, 32, 64, 128, 256}:
        for inst in instances:
            reduced = ReduceEnsemble(full_ensemble(inst), to = S)    # artefact
            for tier in {1, 2, 3}:
                r = Solve(inst.with(ensemble = reduced), tier, budget = LARGE)
                record (S, tier, inst, r.objective, r.solveTime,
                        r.rowCount, r.colCount, r.binaryCount, r.mipGap)

    for nbp in {2, 4, 8, 16, 32, 64}:          # PWL breakpoints per curve
        for inst in instances:
            b = Recompose(inst, breakpoints = nbp)   # V, capacity curve, ID curve
            for tier in {1, 2, 3}:
                r = Solve(b, tier, budget = LARGE)
                record (nbp, tier, inst, r.objective, r.solveTime,
                        r.binaryCount, approximation_error_vs_finest_grid)
```

Reference values: the objective at `S = 256` (a large-`S` reference run, per
ADR-005's rejected-alternatives note) and at the finest breakpoint grid.

### 6.2 What is reported

| Curve | Axes | What it decides |
|---|---|---|
| Objective vs. `S` | Objective (relative to the `S = 256` reference) against `S`, per tier | Where the **discretisation** curve flattens. Convergence to the ensemble's own large-`S` value — **not** an accuracy claim: the `S = 256` reference carries the same `O(1)` filtration error as `S = 64` (ADR-005, `TN-02` §6) |
| Solve time vs. `S` | p50 and p99 solve time against `S`, per tier | The cost side of the same trade |
| Reduction fidelity vs. `S` | `INV-D-07` marginal-mean error, and a Wasserstein distance to the full ensemble, against `S` | Distinguishes "more scenarios help" from "the reduction is bad at small `S`" |
| Objective vs. breakpoint count | Objective and approximation error against `nbp` | ADR-008: breakpoint count is a tuning knob with a measurable accuracy/speed trade-off |
| Solve time vs. breakpoint count | p50 and p99, and **binary count** against `nbp` | A `General`-curvature PWL costs binaries; this is where that cost becomes visible |
| Solve time vs. binary count | Scatter, coloured by `binariesByOrigin` (`C2` §7) | **Attribution.** When a solve slows down, `binariesByOrigin` names the term responsible; this curve is that instrument, plotted |
| Solve time vs. row/column count | Scatter | Detects super-linear blow-up before it reaches production |
| Determinism cost | Reproducibility (single-thread, deterministic work units) vs. production configuration solve time | Makes the cost of determinism a number rather than an argument (`T3` §5.3) |

### 6.2.1 Suboptimality instruments

`S` does not measure distance to the optimum. Three instruments do, and they measure
different things. Definitions and derivation: `06-theory/TN-02-fan-not-tree.md` §8.

| Instrument | Definition | Isolates | Band |
|---|---|---|---|
| `PolicyQuality` @ matched horizon | Realised value / benchmark value, benchmark solved over `H_plan` with the Planner's terminal `V` | Policy quality. Mixes anticipativity, ensemble misspecification and reduction error | none — reported |
| `PolicyQuality` @ monthly horizon | The same ratio, benchmark solved over one Berlin month with the peak term and the delineation regime live | Policy quality **plus** the `H_plan` truncation | none — reported |
| **`V`-truncation cost** | monthly − matched | What the terminal value function is repairing. The specification's only direct measurement of `V`'s adequacy | none — reported |
| Report-versus-realised drift `D(τ)` | `E_ω[z_rep − z_real]` per gate kind, EUR and %, over matched windows with matched terminal terms | Nothing — signed monitor. Positive means the model over-promises | **none, deliberately**: it mixes three errors, so a threshold would fire without saying which moved. Read as a trend; a persistently positive `D(S1)` is the earliest warning that the blind reserve commitment is mispriced |
| **Stage-aggregation error `A`** | `(z_MS − z_rolling-fan) / z_MS` on a synthetic instance small enough that a genuine multistage tree is enumerable | **Stage aggregation alone.** Sampling, misspecification, reduction, `V` error and financial arbitrage all cancel by construction | **`A ≤ 5 %`.** Breach reopens tree construction |

**The benchmark.** Sequential re-solve with only the imminent slot binding, full physical
constraints, **physically backed** (`INV-X-07`), transacting at `DA`, `IDA` and
`ID3`/`ID1` clearing prices only and never crossing the book, respecting §4.1's gate
ordering. Perfect foresight is **not** used: under `INV-X-07` a benchmark that buys and
sells the same delivery period violates the same constraint the policy obeys, so it is not
a bound on the right feasible region. The index-granularity cap does a separate job —
the engine trades at orderbook granularity, so a benchmark with book foresight would
absorb execution skill into the denominator.

**Naming.** The industry term *capture rate* is not used: its published denominators are
energy-only while its numerators include ancillary revenue, so it is unbounded above.
`T4` §3.1's `CaptureRatio` keeps its name and its meaning, which is execution quality —
a different quantity from `PolicyQuality` and complementary to it.

The staged denominator — delineation, host load and availability as separate stages — and
the market ladder are specified separately.

### 6.3 Reading the curves

The configured `S` and `nbp` must sit at a **declared point** on the curve, with
the declaration recorded in the configuration next to the value:

```
scenarioCount: 64      # T5 rel-2026-08: objective within 0.4% of S=256,
                       # p99 solve 1.9s (S=128: 0.2%, 4.4s). Knee at S≈48.
pwlBreakpoints: 16     # T5 rel-2026-08: approximation error 0.1%, 0 binaries.
                       # nbp=32 adds 0.03% accuracy for 2.1x solve time.
```

A configuration value with no `T5` citation next to it is a guess, and the
release checklist flags it.

---

## 7. The release gate

`T5` is a **release gate**, not a merge gate. It runs pre-release, overnight, on
a holdout corpus that is not the corpus any tier was tuned on.

### 7.1 Gate conditions

| # | Condition |
|---|---|
| 1 | The Tier 1 oracle run completed over the whole holdout corpus, with the count and identity of ticks failing `ORACLE_GAP_TOL` reported (not dropped) |
| 2 | Every tier that **can** run in production has its gap measured — including tiers reachable only by escalation |
| 3 | `gap(shipped)` reported as p50 / p90 / p99 / worst, in EUR and in %, **per regime** |
| 4 | Per-regime p99 within the configured band, and the **worst case named** with its recorded seam |
| 5 | Peak-critical and qualification-critical regimes assessed against their own, tighter band. A heuristic that is fine on average and terrible on the ten days that matter does not ship |
| 6 | Perfect-foresight bound computed; the Tier1-to-PF distance and the headroom split reported per regime |
| 7 | Tier 2 dual-bound certification: no invalid bound anywhere in the corpus; every exit path returned a feasible primal; exit-reason mix within band |
| 8 | Scaling curves for `S` and breakpoint count regenerated; the configured values cited against the current curves |
| 9 | Solve-time p99 per tier and per gate type recorded, feeding ADR-015 OPEN-2 |
| 10 | No configuration value in the shipped config lacking a `T5` citation |

### 7.2 Failure actions

| Failure | Action |
|---|---|
| A tier's gap exceeds its band in any regime | That tier does not ship as the production path. It may still ship as an escalation target if its gap is acceptable there |
| The oracle could not resolve the gap (§3.4) | The measurement is void. Tighten the oracle or reduce the instance; do not ship on an unresolved gap |
| An invalid Tier 2 dual bound | Stop-ship. The bound is the product |
| A regression against the previous release's gap distribution | Blocks release unless accompanied by a stated, accepted trade — for example a latency improvement with a quantified gap cost |
| Scaling curves show the configured `S` past the knee | `warn`; the configuration is re-declared or the citation updated |

### 7.3 What the gate does not do

It does not require the gap to be **small**. It requires it to be **measured,
named, conditioned and accepted**. A tier with a p99 gap of EUR 200 on
peak-critical days may be perfectly acceptable if the latency it buys is
necessary and the escalation policy covers those days — but that has to be a
decision someone took with the number in front of them, which is the whole point
of ADR-010's ladder.
