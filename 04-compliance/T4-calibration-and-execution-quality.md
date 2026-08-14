# T4 — Calibration and Execution Quality

Normative. How the fill model is calibrated against the existing simulator, how
the quoting policy is evaluated, and how drift in either is detected before it
shows up in P&L.

---

## 1. Scope and the standing caveat

Two objects are under test at this level, and they are different in kind from
everything in `T0`–`T3`.

- **The fill model** — `FillProbView`'s underlying `P(fill | price, product,
  time-to-gate)` surface, and the derived `idReliableVolume*` bounds that cross
  `C1` §5. It is a *prediction* about an external system.
- **The quoting policy** — the mapping `(target, shadowValue, urgency, fill
  curve, microstructure state) → limit-order ladder` (ADR-012). It is a *policy*
  whose quality is measured, not a function whose correctness is asserted.

Neither has an invariant that says "this must be true". Both have a measured
distance to reality and a band outside which that distance is a release blocker.
`T4` is therefore the only level whose output is a *report* rather than a
pass/fail on a predicate — but the band is a gate, and the gate is enforced.

### 1.1 The calibration target is the simulator, not the market

ADR-012 fixes this: Execution is a pre-existing, strategy-independent simulator
(`L4`), and the engine's question is only which limit orders to submit. The fill
model's job is therefore to predict **the simulator's** behaviour, and its
accuracy target is defined by that — not by the real market in the abstract.
This is a much narrower and more tractable problem than a general limit-order-book
model, which is precisely why ADR-015 OPEN-3 can remain deferred.

### 1.2 The OPEN-3 caveat, restated because it is load-bearing

> Whatever is calibrated against the simulator inherits the simulator's biases.
> When the engine is pointed at a real venue, the fill model must be recalibrated
> and the gap between simulator-calibrated and venue-calibrated behaviour becomes
> a named risk. (ADR-015 OPEN-3, standing caveat.)

Operational consequences, which are requirements on this level and not
commentary:

1. Every `T4` report carries the banner `CALIBRATION TARGET: SIMULATOR
   <version-hash>` and the simulator's own version is part of the run manifest.
   A calibration report that does not name its target is void.
2. A fill model artefact is **stamped with the simulator version it was fitted
   against**. Loading a fill model whose target version does not match the
   configured Execution adapter is a `HALT` at startup, not a warning.
3. Measured accuracy against the simulator is **not** evidence of accuracy
   against a venue, and no document, dashboard or release note may present it as
   such. The go-live gate (`T0` §5.3) requires a recalibration plan and a shadow
   period precisely because this number does not transfer.
4. Everything downstream that consumes fill quality — `executionSlippage` in the
   four-bucket decomposition (`C5` §6), `idReliableVolume*` bounds, the quoting
   policy's capture ratio — inherits the same caveat and carries it in its own
   reporting.
5. When the simulator changes, the fill model is **re-verified before the release
   that consumes the new simulator**, not after (ADR-012).

---

## 2. Fill model calibration

### 2.1 The characterisation run

ADR-015 OPEN-3 names the prerequisite: *a characterisation run against the
simulator measuring realised vs. predicted fill rate by price band, product and
time-to-gate.* This is that run, specified.

**Design.** A grid sweep, not a backtest of the strategy. The engine submits a
designed set of orders whose sole purpose is to span the input space, so that the
calibration is not confounded by the strategy's own order placement (which
concentrates orders in a narrow region and leaves the rest of the surface
unmeasured).

| Factor | Levels |
|---|---|
| Price band | Distance from the reference price, in ticks or in spread units: `[−3σ, −2σ, −1σ, −0.5σ, 0, +0.5σ, +1σ, +2σ, +3σ]` relative to `idPriceRef`, signed by side |
| Product | Each intraday product traded: quarter-hour, hour, block, per liquidity class |
| Time-to-gate | `[> 8h, 4–8h, 2–4h, 1–2h, 30–60 min, 15–30 min, < 15 min]` |
| Side | Buy, Sell — measured separately; asymmetry is real and must not be averaged away |
| Volume bucket | Small / typical / large relative to the product's observed depth |
| Regime | Ordinary, high-volatility, negative-price, high-imbalance |

Every cell must have a minimum observation count before it is reported; cells
below it are reported as `insufficient data` and are **not** interpolated over.
An interpolated cell that later turns out to be systematically wrong is
indistinguishable from a measured one, which is the failure this rule prevents.

The characterisation run is a **manifest-pinned run** like any other (`T3` §2),
so a calibration can be reproduced exactly and a re-calibration can be diffed
against its predecessor.

### 2.2 Realised versus predicted fill rate

The core measurement, per cell:

```
for cell in (price_band × product × time_to_gate × side × volume_bucket):
    predicted = mean over orders in cell of  P_model(fill | order)
    realised  = count(filled) / count(submitted)   in cell
    n         = count(submitted)

    report cell: predicted, realised, n,
                 bias  = realised − predicted,
                 se    = sqrt(realised(1−realised)/n),
                 z     = bias / se
```

| Statistic | Definition | Release band |
|---|---|---|
| Bias per cell | `realised − predicted` | Within `± 0.05` for cells with `n ≥ 200` |
| Aggregate bias | Volume-weighted mean bias across cells | Within `± 0.02` |
| Brier score | `mean((p − y)²)` over orders | Reported; must not regress by more than 10 % against the previous calibration |
| Log loss | `−mean(y·log p + (1−y)·log(1−p))` | Reported; same regression rule |
| Monotonicity | Fill rate non-decreasing as the limit price becomes more aggressive, within a cell family | Violations reported individually; a violation with `n ≥ 200` on both sides blocks release |
| Time-to-gate shape | Fill rate non-decreasing as time-to-gate grows, all else equal | Same treatment |

The last two are **metamorphic relations on the model itself** (`T2` §1) and they
are the cheapest defence against a mis-specified surface: a fill model that
predicts a *lower* fill probability for a more aggressive price has a sign error
somewhere, and no amount of aggregate Brier score will reveal it.

### 2.3 Reliability diagrams

For each product and each time-to-gate band, bin orders by predicted fill
probability into deciles and plot realised frequency against predicted
probability.

```
ReliabilityDiagram(product, ttg_band):
    bins = decile_bins_of(predicted_probability)
    for b in bins:
        x = mean(predicted in b)
        y = realised_frequency in b
        n = count in b
    plot (x, y) with n as point weight;  reference line y = x
    report: ECE  = Σ (n_b / N) · |y_b − x_b|          # expected calibration error
            MCE  = max_b |y_b − x_b|                   # maximum calibration error
            slope, intercept of the fitted reliability line
```

| Reading | Diagnosis | Action |
|---|---|---|
| Points on the diagonal | Calibrated | None |
| Slope `< 1`, curve flatter than the diagonal | Over-confident: the model over-predicts extremes in both directions | Recalibrate (isotonic or Platt on a holdout); do not simply shrink the surface globally |
| Systematic offset above the diagonal | Under-predicts fill; the Planner is leaving executable volume on the table via over-tight `idReliableVolume*` bounds | Recalibrate; note that the bias is *conservative* and therefore safe to ship pending the fix |
| Systematic offset below the diagonal | Over-predicts fill; the Planner is planning on volume it will not get | **Blocks release.** This is the unsafe direction: it produces plans that are infeasible in practice and shows up as `executionSlippage` and unfilled `CommitmentCover` |
| High MCE in one time-to-gate band only | The urgency dimension is mis-specified | Recalibrate that dimension; check whether the quoting policy's urgency input is on the same scale |

The asymmetry in the two offset rows is deliberate and mirrors ADR-014's
philosophy: a conservative error costs revenue and is visible; an optimistic
error costs feasibility and is not.

### 2.4 Activation model calibration

The same machinery applies to the aFRR activation model, because
`activationUp`/`activationDn` (`C1` §4) drive the SOC chance constraint and a
mis-calibrated activation belief produces exactly the delivery shortfall that
`INV-S-04` treats as a prequalification risk.

| Measurement | Band |
|---|---|
| Realised activation fraction vs. predicted, by block, direction and regime | Aggregate bias within `± 0.03` |
| Reliability diagram on the activation probability | Same treatment as §2.3, with the same asymmetry: under-predicting activation is the unsafe direction |
| Chance-constraint calibration: realised frequency of SOC leaving the band vs. the configured `ε` | Realised frequency `≤ ε` over the holdout. A realised frequency above `ε` means the chance constraint is not delivering its stated confidence and blocks release |
| Joint check: activation conditional on price regime | The correlation ADR-005 exists to preserve must be present in the *realised* data too, or the ensemble is mis-specified |

The chance-constraint row is the one most often skipped and it is the one that
matters: `ε` is a promise about how often SOC feasibility is allowed to fail, and
an uncalibrated activation model makes that promise vacuous.

### 2.5 Calibration drift alerts

Calibration is not a one-off. It decays as the simulator, the products or the
market regime change.

| Monitor | Window | Threshold | Severity |
|---|---|---|---|
| Rolling aggregate bias | 20 trading days | `\|bias\| > 0.03` | `warn` |
| Rolling aggregate bias | 20 trading days | `\|bias\| > 0.05` | `alert` + calibration ticket |
| Rolling ECE per product | 20 trading days | `> 1.5 ×` the value at last calibration | `alert` |
| Page–Hinkley / CUSUM on per-order calibration residual | Continuous | Change point detected | `alert`; names the day the drift started |
| Cell coverage | Per release | Any previously-populated cell now `insufficient data` | `warn` — the strategy has stopped exploring a region and the surface is going stale there |
| Simulator version | Continuous | Version hash differs from the fill model's stamped target | `HALT` at startup (§1.2) |
| Monotonicity violations | Per nightly run | Any new violation with `n ≥ 200` | `alert` |

Drift alerts route to the calibration owner (`T0` §3), not to the on-call
engineer. A calibration ticket has a due date measured in days; a `HALT` does
not.

**Drift is recorded in the settlement artefact**, so P&L can be conditioned on
it. A quarter in which the fill model drifted for three weeks should be
attributable to that, not to strategy — the same argument ADR-014 makes for
degradation modes.

---

## 3. Quoting policy evaluation

The quoting policy is the component ADR-012 separates from the Planner precisely
so that its contribution to P&L is *separately measurable*. This section is that
measurement.

### 3.1 Capture ratio against shadow value

The Planner's `shadowValue` is its indifference price. The region between the
prevailing market and the shadow value is the space the quoting policy is free to
work in, and its job is to capture as much of it as fill probability allows
(ADR-012).

```
for each fill f with intent i:
    if i.side == Sell:
        available =  i.shadowValue_reference_market_price − i.shadowValue      # ≥ 0
        captured  =  f.price − i.shadowValue
    else:  # Buy
        available =  i.shadowValue − reference_market_price
        captured  =  i.shadowValue − f.price

    capture_ratio(f) = captured / available          # available > 0

CaptureRatio = Σ_f volume_f · captured_f  /  Σ_f volume_f · available_f
```

| Reported cut | Why |
|---|---|
| Overall, volume-weighted | Headline |
| By `StrategyTag` (`Arbitrage`, `PeakShave`, `ReserveHedge`, `Rebalance`, `CommitmentCover`) | `CommitmentCover` legitimately captures poorly — it is buying certainty — and must not drag the headline |
| By urgency decile | The policy's core trade-off. Capture should fall monotonically as urgency rises; if it does not, urgency is not wired into the ladder |
| By time-to-gate | The shape of the remaining trading window is an input to the policy |
| By product and liquidity class | |
| **Unfilled opportunity cost** | `Σ over unfilled intents of volume × available`. Capture ratio measured only on fills is the classic trap: a policy that quotes at the shadow value captures 0 % and fills nothing; a policy that quotes at the market captures 100 % of nothing. Both must be in the same report |

The frontier view is the useful one: plot capture ratio against fill rate, per
policy configuration, and compare configurations on the frontier rather than on
either axis alone.

### 3.2 Adverse selection measurement

A fill that occurs precisely because the market was about to move against you is
worth less than its price suggests. Adverse selection is the difference, and it
is the single most common way an execution policy that looks good on capture
ratio loses money.

```
for each fill f at slot/product p, executed at time τ:
    for Δ in {1 min, 5 min, 15 min, to gate}:
        mark[Δ] = reference_price(p, τ + Δ)
        markout(f, Δ) = side_sign(f) · (mark[Δ] − f.price) · volume_f
                        # positive  => the market moved in our favour after the fill
                        # negative  => we were adversely selected

AdverseSelection(Δ) = − volume_weighted_mean( markout(·, Δ) )
```

| Statistic | Reported by | Interpretation |
|---|---|---|
| Mean markout at each horizon | Overall, by tag, by urgency, by price band | A consistently negative markout means the policy is being picked off |
| Markout decay curve | Horizon on the x-axis | A curve that worsens monotonically with horizon indicates information-driven adverse selection; one that recovers indicates transient impact, which is a different and less alarming problem |
| Passive vs. aggressive split | Fills that crossed the spread vs. rested | Aggressive fills should show near-zero markout by construction; a negative markout on aggressive fills means the reference price itself is stale |
| Adverse selection net of capture | `captured − adverse_selection`, in EUR | **The number that matters.** A policy with a high capture ratio and worse adverse selection is destroying value |

The last row is the release-relevant one. A quoting policy change ships only if
capture net of adverse selection improves, or is neutral with a stated reason.

**Reconciliation with `C5`.** The `executionSlippage` bucket of the four-way
decomposition (`C5` §6) values fills at intent prices versus executed prices.
`T4` asserts that the adverse-selection measurement and the `executionSlippage`
bucket are consistent over the same period, within tolerance. A divergence means
one of the two definitions has drifted, and that divergence is exactly what
`unexplainedRatio` (`INV-S-07`) starts registering months later.

### 3.3 The `INV-P-10` trade-through check

`INV-P-10`: no sell intent priced below its `shadowValue`; no buy intent priced
above it. Warning severity, and the intent is blocked (`T1` §5). The legitimate
exception is a `CommitmentCover` intent — covering a commitment at a loss is
sometimes correct.

`T4` runs the check three ways.

```
# 1. EX-ANTE — every emitted intent, every tick, in production and in backtest
for intent in C3.intents:
    if intent.side == Sell:  violated = intent.limitPrice < intent.shadowValue − TOL
    else:                    violated = intent.limitPrice > intent.shadowValue + TOL
    if violated:
        assert intent.tag == CommitmentCover     # else BLOCK the intent
        record TradeThrough(intent, magnitude = |limitPrice − shadowValue|)

# 2. EX-POST — every fill, from C4, reconciled against the recorded intent
for fill in C4.fills:
    intent = lookup(fill.intentId)               # INV-X-01 guarantees it exists
    if worse_than_shadow(fill.price, intent):
        record RealisedTradeThrough(fill, value_destroyed =
            |fill.price − intent.shadowValue| × fill.volume)

# 3. AGGREGATE — the report
report:
    trade_through_rate      = count(violations) / count(intents)
    tagged_rate             = count(CommitmentCover violations) / count(violations)
    untagged_blocked_count  = count(blocked)                      # must be the
                                                                  # complement
    value_destroyed_eur     = Σ RealisedTradeThrough.value_destroyed
    by_tag, by_urgency_decile, by_product
```

| Finding | Severity | Action |
|---|---|---|
| An untagged trade-through reached `C3` | `HALT`-adjacent: the block failed | Stop-ship. The enforcement point is broken, not the policy |
| A tagged (`CommitmentCover`) trade-through | `warn`, counted | Expected in small numbers. A rising rate means commitments are being taken that the engine cannot cover economically — an ADR-010 / reserve-sizing question, not a quoting question |
| `value_destroyed_eur` above the configured budget over a period | `alert` | Review the shadow value extraction itself; a systematically wrong dual is a Planner defect surfacing as an execution symptom |
| Trade-through concentrated in the top urgency decile | `warn` | The policy is panicking near the gate; the urgency mapping is too aggressive |

The third row deserves emphasis. `INV-P-10` is stated as an economic safety net,
but a high trade-through rate is more often a signal that `shadowValue` is being
extracted incorrectly — an approximated dual rather than the real one (ADR-012's
cost note) — than that the quoting policy is misbehaving. `T4` therefore also
asserts a sanity property on the shadow value itself:

```
property ShadowValueSanity(plan):
    # the dual on the position accounting constraint must sit between the
    # marginal cost of sourcing the MWh and the marginal value of using it
    for slot, market:
        assert plan.shadowValue[market, slot] is finite
        assert sign(plan.shadowValue) consistent with the position's direction
        # metamorphic: scale all prices by k>0 => shadow value scales by k
        assert shadowValue(scaled_by_k) ≈ k · shadowValue(base)     # T2 §3.5
```

### 3.4 A/B evaluation

ADR-012's stated benefit is that the quoting policy can be replaced, A/B tested
and tuned with the Planner held fixed. The evaluation protocol:

| Requirement | Detail |
|---|---|
| Same `C3` targets | Both arms consume the identical recorded `PlanResult` stream, so the Planner is genuinely held fixed. This is only possible because `PlanResult` is recorded (ADR-013, `L3` §8) |
| Same simulator version and manifest | Otherwise the comparison measures the simulator |
| Paired comparison | Per tick, per product, both arms evaluated on the same instance — a paired test has far more power than two independent backtests |
| Primary metric | Capture net of adverse selection, in EUR, volume-weighted |
| Guardrail metrics | Trade-through rate, unfilled opportunity cost, `CommitmentCover` fill rate. A policy that improves capture by failing to cover commitments has not improved |
| Reported with the OPEN-3 banner | The result is a statement about the simulator |

---

## 4. What `T4` feeds back into the system

Calibration is not an end in itself; each measurement has a declared consumer.

| Measurement | Consumer | Effect |
|---|---|---|
| Fill-rate calibration by price band | `FillProbView` | Determines whether `BoundTerm` is a scalar volume cap or a per-price-band cap — the open question in ADR-015 OPEN-3. The `C2` contract shape does not change either way |
| Fill-rate bias | `idReliableVolumeBuy/Sell` (`C1` §5) | A conservative bias is tightened toward truth; an optimistic bias is corrected immediately |
| Activation calibration | `chanceLevel` in `riskProfile` (`C2` §6) | An under-confident activation model widens `ε` until recalibrated |
| Reliability diagram slope | The fill model artefact | Isotonic recalibration layer, versioned and content-hashed like any other artefact (ADR-004 §4) |
| Adverse selection by urgency | The quoting policy's urgency mapping | |
| `INV-P-10` trade-through concentration | Shadow-value extraction in `L3` §5 | |
| Every drift alert | `C5` §7 `modeTransitions` and the settlement record | So P&L can be conditioned on the calibration state of the period |

### 4.1 Recalibration is a manifest change

A recalibrated fill model is a **new artefact with a new hash**, which makes it a
new manifest, which makes every golden hash downstream of it change (`T3` §8
step 1). This is correct and expected: a recalibration is a deliberate behaviour
change and it should be visible as one. The baseline update carries the
calibration report as its justification.

---

## 5. Reporting

One report per nightly run, one per release, both archived with the manifest.

```
T4 CALIBRATION REPORT
manifest:            <manifestId>
CALIBRATION TARGET:  SIMULATOR <version-hash>        [ADR-015 OPEN-3]
window:              <first slot> .. <last slot>,  N orders,  N fills

FILL MODEL
  aggregate bias            ....   band ±0.02        PASS / FAIL
  Brier                     ....   vs prev ....      PASS / FAIL
  ECE by product            ....                     PASS / FAIL
  monotonicity violations   ....                     PASS / FAIL
  cells insufficient        ....                     WARN
  reliability diagrams      [attached, per product × ttg band]

ACTIVATION MODEL
  aggregate bias            ....   band ±0.03        PASS / FAIL
  realised SOC-band exit    ....   vs epsilon ....   PASS / FAIL

QUOTING POLICY
  capture ratio             ....   by tag / urgency / ttg  [table]
  unfilled opportunity cost .... EUR
  adverse selection         .... EUR at 1/5/15min/gate    [markout curve]
  capture net of AS         .... EUR                 PRIMARY
  INV-P-10 trade-through    ....%  tagged ....%  untagged-blocked ....
  value destroyed           .... EUR

DRIFT
  Page-Hinkley change point  <date or none>
  rolling bias 20d           ....
  open calibration tickets   ....

CAVEAT: every number above is measured against the simulator and does not
transfer to a live venue without recalibration (ADR-015 OPEN-3).
```

A release proceeds when every `PASS / FAIL` line passes, the primary quoting
metric has not regressed, and any `WARN` has an owner and a date.
