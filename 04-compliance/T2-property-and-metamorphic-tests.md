# T2 — Property and Metamorphic Tests

Normative. The properties each layer and each seam must satisfy for *every*
admissible input, and the metamorphic relations that make correctness testable
without an oracle.

---

## 1. Two kinds of test, and why the second one matters

A **property test** states something that must be true of a single output. "The
planned trajectory is feasible." "`V`'s slopes are strictly decreasing." "No
effect is priced twice." You generate many inputs, run the layer, and assert the
predicate. When it fails, you shrink the input to a minimal reproduction.

A **metamorphic test** states something that must be true of the *relationship
between two outputs*. You do not assert what the answer is; you assert how the
answer must change when the input changes in a known direction.

This distinction is the crux of testing an optimiser. For almost every
interesting input, **nobody knows the correct output**. There is no oracle. You
cannot write `Assert.Equal(expectedPlan, actualPlan)` for a 192-slot stochastic
MILP over eight correlated series, because computing `expectedPlan` by hand is
the problem you were trying to solve. This is the classic *oracle problem*, and
it is why optimiser test suites so often degenerate into "it ran, it was
feasible, ship it".

Metamorphic testing dissolves it. You may not know what the plan should be, but
you know with certainty that:

- if aFRR capacity prices go **up** and nothing else changes, planned reserve
  cannot go **down**;
- if the peak charge goes **up**, the planned peak cannot go **up**;
- if the POI limit is **tightened**, the objective cannot **improve**;
- if every price is multiplied by `k > 0`, every euro figure must be multiplied
  by exactly `k` and every kW bound must be **unchanged**.

Each of those is a testable assertion over a *pair* of runs, requires no ground
truth, and is generated automatically from any input in your corpus. One input
becomes dozens of tests.

What this catches that feasibility testing does not:

| Bug class | Why feasibility misses it | Which relation catches it |
|---|---|---|
| Sign error on a revenue term | A plan with an inverted sign is still perfectly feasible; it just loses money silently | Price monotonicity |
| Unit mixing (`EUR/MWh` treated as `EUR/kWh`, `kW` as `MW`) | A factor of 1000 produces a feasible, plausible-looking plan | Scale equivariance |
| Missing coupling constraint | The relaxed problem is *more* feasible, not less | POI tightening monotonicity; DA curve monotonicity |
| Double-counted effect | Every constraint holds; the objective is simply inflated | Duplicate-effect injection; zero-price invariance |
| Ignoring the dependence structure of the ensemble | Marginals are all correct; only the joint is wrong | Ensemble axis coherence |
| Peak term over-weighted by a bad `prorationFactor` | Feasible and conservative — looks like prudence | Peak price monotonicity; proration scaling |

### 1.1 Controlling false failures

Metamorphic relations over a MILP can fail for reasons that are not bugs.
Degenerate optima, ties broken differently by the solver, and a non-zero MIP gap
all produce legitimate small violations. The suite controls for this rather than
loosening the assertion into uselessness:

1. **Assert on the objective and on aggregates first, on the argmin second.**
   `objective(k·prices) = k·objective(prices)` is exact; "the same slots are used"
   is not.
2. **Run the relation instances at a MIP gap of zero** where the instance is
   small enough, and at the production gap only for scaling instances. A
   monotonicity assertion must be compared against `2 ×` the gap tolerance, and
   the test records which regime it ran in.
3. **Fix the tie-break.** The Planner declares a lexicographic tie-break rule
   (ADR-013 iteration ordering); with it fixed, degenerate alternates stop
   flapping between runs.
4. **Prefer LP instances for the sharpest relations.** When every `PwlTerm` is
   `(Concave, Maximize)` or `(Convex, Minimize)` the model is an LP (`C2` §3.2)
   and the relations hold exactly. The generator has an "LP-only" mode used for
   the strict assertions.
5. **A violation within tolerance is recorded, not silently passed.** The suite
   reports the distribution of near-violations; a relation that is *always*
   within `1e-9` of failing is telling you something.

---

## 2. `L1` Belief

Purity claim under test: Belief is a pure read over immutable, bitemporal data.

### 2.1 No-lookahead, structural

**Property.** For every series, every `validRange` and every `asOf`, the result
of `Get(series, validRange, asOf)` contains no row with `knowledge_time > asOf`,
and is identical to the result computed against a store from which all such rows
have been physically deleted (`INV-D-02`, ADR-004 §1).

The second clause is the strong one. It asserts not merely that the filter
works, but that *nothing else in the read path* consults the excluded rows —
no cache warm-up, no index statistic, no "latest value" fast path.

```
property NoLookaheadStructural(series, validRange, asOf):
    full     = Store.Load(all rows)
    truncated= Store.Load(rows where knowledge_time <= asOf)

    a = full.Get(series, validRange, asOf)
    b = truncated.Get(series, validRange, asOf)

    assert bitwise_equal(a, b)
    assert a.rows.all(r => r.knowledge_time <= asOf)
```

**Why it catches a real bug.** The failure mode ADR-004 exists to prevent is a
backtest that looks like alpha. A "latest available" lookup with no `asOf`, or a
settlement value stamped with delivery time rather than publication time, gives
excellent backtests and losing live performance. This test makes the leak
structural to detect: to pass while peeking, a component would have to bypass the
storage layer entirely — which `T6`'s whole-run poisoning audit then catches.

**Negative test.** A deliberately-broken store with the `asOf` filter removed
must fail this property. An assertion nobody has seen fail is an assertion
nobody knows is wired up.

### 2.2 Revision replay

**Property.** A series revised `n` times is stored as `n` appended rows with the
same `valid_time` and distinct `knowledge_time`s; `asOf` selects the latest row
at or before the read time. Replaying a tick at each historical `asOf` reproduces
exactly what the engine saw then.

| `asOf` | Rows visible | Expected value | Rationale |
|---|---|---|---|
| before first publication | none | field absent → quality `Missing` → ladder rung 5 or 6 | Never a silent zero |
| after `k1` (original) | 1 | `v1` | |
| after `k2` (revision) | 2 | `v2` | Latest at-or-before |
| between `k1` and `k2` | 1 | `v1` | **Not** `v2` — this is the lookahead case |
| after all revisions, `asOf = now` | 3 | `v3` | Settlement's ex-post view |

```
property RevisionReplay(series):
    for asOf in sorted(distinct knowledge_times) ∪ midpoints:
        snapshot = Belief.Snapshot(asOf)
        assert snapshot.Get(series, vt) == latest_row_at_or_before(series, vt, asOf).value
        assert snapshot.contentHash == recorded_hash_for(asOf)   # if recorded
```

**Why it catches a real bug.** Revisions are what let Settlement distinguish "we
were wrong" from "the data was later corrected" (ADR-004). A store that
overwrites in place makes that distinction unrecoverable, and the loss is silent.

### 2.3 DST and calendar fixtures

**Property family** `INV-T-01…04`. Every calendar-dependent test carries both
DST transition days as fixtures.

| Fixture | Slots in civil day | What must hold |
|---|---|---|
| Ordinary day, Europe/Berlin | 96 | Baseline |
| Spring forward (last Sunday in March) | 92 | No mapping ever returns the nonexistent local hour. A per-civil-day span computed as `96` is short by four slots and the test catches it at the accounting boundary |
| Autumn back (last Sunday in October) | 100 | The repeated local hour appears as two distinct slot ranges, distinguished only by UTC offset. HLZF membership is evaluated for both occurrences independently |
| Month boundary in each month | varies | Peak accounting resets at **local** midnight, which is a different UTC instant in winter and summer (`INV-S-01`) |
| Year boundary | varies | Qualification-year accumulators reset with the accounting period, not with the UTC year |

```
property CivilDaySlotCount(date in [2023-01-01 .. 2030-12-31]):
    n = CivilCalendar.SlotsInCivilDay(date)
    assert n in {92, 96, 100}
    assert n == 92  iff date is a spring-forward date
    assert n == 100 iff date is an autumn-back date
    assert CivilCalendar.SlotsOfCivilDay(date).count == n
    assert CivilCalendar.SlotsOfCivilDay(date) are contiguous and gap-free in SlotId

property PeakPeriodBoundary(month):
    start = CivilCalendar.PeriodStart(month)     # local midnight
    assert SlotId.ToUtc(start).TimeOfDay == expected_offset_for(month)   # not constant
    assert realisedPeak resets exactly at start, and not one slot early or late
```

**Why it catches a real bug.** Any code that assumes 96 slots per day is wrong
twice a year (`00-overview/02-conventions.md` §4.2). The damage is not cosmetic:
a peak-accounting period that ends four slots early can miss the period's actual
peak, which mis-prices the epigraph floor for the entire following period.

### 2.4 Ensemble axis coherence

**Property.** Scenario index `s` denotes the same coherent state of the world in
every scenario-indexed series (`INV-D-05`, ADR-005). Any consumer that ignores
this is destroying the dependence structure that the entire cross-market case
rests on.

The test is metamorphic and it is the sharpest test in the Belief suite:

```
metamorphic EnsembleCoherence(snapshot, view):
    base     = view.Evaluate(snapshot)

    # permute the scenario axis of ONE series only, leaving weights and all
    # other series untouched. This preserves every marginal exactly and
    # destroys the joint dependence.
    permuted = snapshot.with(daPrice = permute_scenarios(snapshot.daPrice, σ))
    out      = view.Evaluate(permuted)

    assert out != base        # a view that uses the dependence MUST react

    # and the control: permuting ALL series by the same σ changes nothing,
    # because scenario labels carry no meaning of their own
    all_permuted = snapshot.permute_all_scenarios(σ)   # weights permuted too
    assert view.Evaluate(all_permuted) == base
```

The pair matters. The first assertion catches a view that has collapsed to
marginals. The second catches a view that has become sensitive to scenario
*labelling*, which would make the output depend on the reduction artefact's
arbitrary ordering and break determinism.

| View | Must react to a single-series permutation? | Why |
|---|---|---|
| `PeakView` | yes | CVaR of `max_t p_poi[s,t]` depends jointly on `load`, `pv` and dispatch |
| `AfrrEnergyView` | yes | Activation value depends on activation co-moving with price |
| `ImbalanceRiskView` | yes | Tail co-movement is the entire quantity |
| `IdOptionView` | yes | Volume value depends on price/liquidity joint |
| `TariffView` | no | Deterministic per-slot rates; exempt, and the exemption is declared |
| `FillProbView` | no | Volumes only, no scenario index (`C1` §5) |

An unexpected "no" is a finding. `T2` fails a view that is listed as
scenario-sensitive and does not react.

### 2.5 Quality ladder matrix

ADR-014 §1 is the central claim under test: **quality is data, not control
flow**. Downstream layers do not branch on quality; they map it to risk
parameters. `T2` proves this exhaustively rather than by inspection.

**The matrix.** Every field in `C1` that carries a `QualityStamp` × every value
of `quality ∈ {Good, Degraded, Stale, Imputed, Missing}`. For each cell, assert
the fallback rung taken, the resulting global mode, and the risk-parameter
response.

| `C1` field | `Good` | `Degraded` | `Stale` | `Imputed` | `Missing` |
|---|---|---|---|---|---|
| `socNow` | `NORMAL` | `DEGRADED` | `DEFENSIVE` | **not permitted** — never defaulted (`C1` §2) | `HALT` via `INV-D-01`/critical |
| `load` | `NORMAL` | `DEGRADED`, ↑`peakSafetyMarginKw`, ↑`cvarLevel` | `DEGRADED`, ↑↑`peakSafetyMarginKw` | `DEGRADED`, default = **high quantile** | `DEFENSIVE`, default high, `criticalMissing` populated |
| `pv` | `NORMAL` | `DEGRADED`, ↑`peakSafetyMarginKw` | as `Degraded` | default = `0` (protects peak) | `DEGRADED`, default `0` |
| `daPrice` | `NORMAL` | ↓`positionScale`, ↑`cvarWeight` | ↓↓`positionScale` | ↓↓`positionScale` | `DEFENSIVE` — critical |
| `idPriceRef` | `NORMAL` | ↓`positionScale` | ↓`positionScale` | ↓`positionScale` | `DEGRADED` |
| `idSpreadBelief` | `NORMAL` | wider | default **wide** (suppresses trading) | wide | `DEGRADED`, wide |
| `afrrCapPrice` | `NORMAL` | ↓`positionScale` | default `0` — do not chase | `0` | `DEGRADED`, `0` |
| `activationUp/Dn` | `NORMAL` | ↑`chanceLevel` | ↑`chanceLevel` | ↑↑`chanceLevel` | `DEFENSIVE` |
| `imbalancePrice` | `NORMAL` | ↑`cvarWeight` | ↑`cvarWeight` | ↑`cvarWeight` | `DEGRADED` |
| `isHlzf` | `NORMAL` | default `true` — conservative is *inside* the window | `true` | `true` | `true`, `DEGRADED` |
| `peakPrice` | `NORMAL` | — | — | — | `HALT` — no safe default for a mandatory term (`INV-V-04`) |
| `V(SOC)` beyond `validityHorizon` | `NORMAL` | apply `stalenessPenalty` shrink to slopes | shrink | shrink | `DEFENSIVE` (ADR-014 trigger) |
| `idReliableVolume*` | `NORMAL` | ↓ bound | default `0` | `0` | `0` |

**The assertions per cell**, in order:

```
property QualityMatrix(field, qualityLevel):
    snap = base_snapshot.with_quality(field, qualityLevel)

    # 1. the ladder stopped where it should and said so
    assert snap.provenanceOf(field) == expected_rung(field, qualityLevel)
    assert snap.seriesQuality[field].quality == qualityLevel

    # 2. the mode is what the table says
    assert Mode.Compute(snap) == expected_mode(field, qualityLevel)

    # 3. quality became a parameter, not a branch
    bundle = Valuation.Compose(snap, state)
    assert bundle.riskProfile == expected_risk_profile(field, qualityLevel)
    assert field in bundle.riskProfile.driverSummary        # explainable

    # 4. THE KEY ASSERTION: same code path
    assert bundle.compositionOrder == base_bundle.compositionOrder
    assert set(bundle.terms.map(t => t.termId)) ==
           set(base_bundle.terms.map(t => t.termId))
    # the terms present are identical; only coefficients, bounds and
    # risk parameters differ. A branch would change the term SET.

    # 5. never degraded away (ADR-014 §3)
    assert bundle.has_mandatory_term(NetworkPeakCharge)
    assert bundle.deliveryObligation_unchanged()
```

Assertion 4 is what makes this a test of the architecture rather than of the
values. If a degraded input causes a *different set of terms* to be emitted, a
branch has appeared somewhere and the "one code path, parameterised" claim is
false.

**Hysteresis.** Separately: entering `DEFENSIVE` requires `N` consecutive bad
ticks and leaving requires `M > N` consecutive good ones.

```
property Hysteresis(N, M):
    feed(N-1 bad ticks);  assert mode != DEFENSIVE
    feed(1 more bad tick); assert mode == DEFENSIVE
    feed(M-1 good ticks); assert mode == DEFENSIVE      # does not leave early
    feed(1 more good tick); assert mode == DEGRADED or NORMAL
    # anti-flap: alternating good/bad never leaves DEFENSIVE
    feed(alternating good/bad × 100); assert mode == DEFENSIVE
```

---

## 3. `L2` Valuation

Purity claim under test: total function of `(BeliefSnapshot, StateSnapshot,
config)`. No I/O, no clock, no state, no randomness (`L2` header).

### 3.1 Determinism

**Property.** Same `C1` payload and same config in, byte-identical `C2` out —
including `contentHash`.

```
property ValuationDeterminism(c1_recorded):
    b1 = Valuation.Compose(c1_recorded, state, cfg)
    b2 = Valuation.Compose(c1_recorded, state, cfg)
    assert bytes(b1) == bytes(b2)
    assert b1.contentHash == b2.contentHash

    # stronger: across process boundaries and machine boundaries
    b3 = run_in_fresh_process(Valuation.Compose, c1_recorded, state, cfg)
    assert bytes(b3) == bytes(b1)

    # stronger still: order of view evaluation must not matter
    b4 = Valuation.Compose(c1_recorded, state, cfg, view_eval_order=shuffled)
    assert bytes(b4) == bytes(b1)     # views are independent (L2 §1)
```

The last clause is the one that finds real defects. Views are specified as
independent — a view may not read another view's output or share mutable state
(`L2` §1). Shuffling evaluation order is the cheapest way to prove it.

### 3.2 Schema closure

**Property.** A view's output contains only the fields declared in its
`PublishedSchema`, and the bundle contains only the shapes of ADR-008
(`INV-V-06`).

```
property SchemaClosure(view, snapshot):
    out = view.Evaluate(snapshot, state, cfg)
    assert fields(out) ⊆ view.PublishedSchema.fields
    assert fields(out) ⊇ view.PublishedSchema.required_fields
    assert out.terms.all(t => t.shape in {Linear, Pwl, Epigraph, Bound, Coupling})
    assert no_field_of_type(out, Delegate | ObjectRef | BeliefCursor | Array[S,·])
    assert view.ClaimedEffects == distinct(out.terms.map(t => t.effect))
```

The last line matters: a view that *declares* it claims `CycleDegradation` but
emits a term tagged `StoredEnergyContinuation` will pass the ownership check for
the wrong reason and eventually collide with the real owner.

### 3.3 Price monotonicity

**Metamorphic.** Raising a price never lowers the value the corresponding lever
is priced at, all else equal.

| Input change | Expected relation on the bundle |
|---|---|
| `peakPrice ↑` | `EpigraphTerm.unitPrice ↑`; the priced value of a kW of peak reduction is non-decreasing |
| `afrrCapPrice[s,b] ↑ ∀s` | `capacityValueCurve[b]` is pointwise non-decreasing in offered MW |
| `daPrice[s,t] ↑ ∀s` | the `LinearTerm` coefficient on `vDaSell[t]` is non-decreasing; on `vDaBuy[t]` non-increasing (the cost of buying rose) |
| `volumetricCharge ↑` | the `LinearTerm` on `PoiImport` becomes more negative |
| `imbalancePrice` spread widened | the CVaR `PwlTerm` is pointwise non-decreasing in penalty |

```
metamorphic PriceMonotone(snapshot, field, delta > 0):
    a = Valuation.Compose(snapshot, ...)
    b = Valuation.Compose(snapshot.with(field += delta), ...)
    for term in matching_terms(a, b):
        assert term_value_b(x) >= term_value_a(x) - MONEY_TOL   for all x on the grid
    assert bounds(b) == bounds(a)          # a price change moves no bound
```

The second assertion is easy to overlook and catches a common defect: a price
change that leaks into a feasibility bound means a price is constraining
physics.

### 3.4 Zero-price invariance

**Property.** A term whose price is zero contributes nothing to the objective and
introduces no binaries.

```
property ZeroPriceInvariance(snapshot, view):
    a = Compose(snapshot.with(view.price_fields = 0))
    b = Compose(snapshot.with(view disabled))
    assert objective_contribution(a, view) == 0
    assert a.problemClassHint.binariesByOrigin[view.terms] == 0
    assert plan(a) == plan(b)      # the Planner's optimum is unchanged
```

**Why it catches a real bug.** A zero-priced term that still contributes is
either double-counting somewhere else or has a hardcoded constant in it. A
zero-priced term that still introduces binaries is a solve-time tax for nothing —
and the `binariesByOrigin` attribution (`C2` §7) exists precisely so this is
visible.

### 3.5 Scale equivariance

**The single highest-value cheap test in the suite.**

**Property.** Multiply every price input by a constant `k > 0` — `daPrice`,
`idPriceRef`, `idSpreadBelief`, `afrrCapPrice`, `afrrEnergyPrice*`,
`imbalancePrice`, `peakPrice`, `volumetricCharge`, `leviesAndTaxes`, and the
value function's `Y` breakpoints. Then:

- **every EUR-denominated quantity scales by exactly `k`** — every
  `LinearTerm.coefficient`, every `PwlTerm.breakpointsY`, every
  `EpigraphTerm.unitPrice`, the objective value, `V`'s slopes, every entry in
  `plannedByEffect`, and the shadow value on every position constraint;
- **every physical quantity is bit-identical** — every `BoundTerm.lower/upper`,
  every `PwlTerm.breakpointsX`, `EpigraphTerm.floor`, the SOC trajectory, the
  planned dispatch, `rUp`/`rDn`, and the peak level;
- **the argmax is unchanged** — scaling a linear objective by a positive constant
  does not move the optimum.

```
metamorphic ScaleEquivariance(snapshot, k in {0.01, 0.5, 2, 100, 1e4}):
    a = Compose(snapshot)
    b = Compose(scale_all_prices(snapshot, k))

    for (ta, tb) in zip(a.terms, b.terms):
        assert tb.termId == ta.termId
        assert_close(tb.coefficient,   k * ta.coefficient,   rel=1e-12)
        assert_close(tb.breakpointsY,  k * ta.breakpointsY,  rel=1e-12)
        assert_exact(tb.breakpointsX,  ta.breakpointsX)          # BIT-identical
        assert_exact(tb.lower, ta.lower); assert_exact(tb.upper, ta.upper)

    assert_exact(b.vSocBreakpointsX, a.vSocBreakpointsX)
    assert_close(b.vSocSlopes, k * a.vSocSlopes, rel=1e-12)

    pa = Plan(a); pb = Plan(b)
    assert_exact(pb.trajectory, pa.trajectory)
    assert_close(pb.objective, k * pa.objective, rel=1e-9)
    assert_close(pb.lambdaSoc, k * pa.lambdaSoc, rel=1e-9)
    assert_close(pb.muStar,    k * pa.muStar,    rel=1e-9)   # Tier 2
```

**Why this catches unit mixing, precisely.** Suppose a term computes a euro
amount as `price_EUR_per_MWh * energy_kWh` and someone forgot the `/1000`. That
term is wrong by a factor of 1000 — but it is *still linear in price*, so it
scales by `k` along with everything else and this test passes. Now suppose the
same term computes `price_EUR_per_MWh * energy_kWh / 1000 + fixed_fee_EUR` where
`fixed_fee_EUR` was sourced from a price field the scaling did not touch, or a
constant was baked into a breakpoint, or a threshold in EUR is compared against a
quantity in kW. **Then the term does not scale by `k`, and the test fires.**

More generally, scale equivariance separates the two halves of the model that
must never mix: the **euro half**, which is homogeneous of degree 1 in prices,
and the **physical half**, which is homogeneous of degree 0. Any quantity that is
a mixture of the two — a bound that moved when a price changed, a breakpoint `X`
that scaled, a "price" constant living inside a feasibility check — is a
unit-mixing bug, and this one test finds all of them at once. It also catches
absolute epsilons in the wrong dimension: a comparison against `1e-6 EUR` applied
to a kW quantity survives `k=1` and dies at `k=1e4`.

Run it at extreme `k` deliberately. `k = 0.01` and `k = 1e4` expose tolerance
constants that are dimensionally wrong.

### 3.6 Concavity

**Property** `INV-V-11`, `INV-V-12`.

```
property ConcavityOfV(state, context):
    v = SlowLoop.Fit(state, context)
    slopes = diff(v.Y) / diff(v.X)
    assert strictly_decreasing(slopes)                      # INV-V-11
    assert v.X strictly increasing and spans [socMin, socMax]
    # near the §19(2) cliff, concavity in SOC must hold GIVEN the discrete
    # qualification state (ADR-007, ADR-011)
    for qualState in all_discrete_states:
        assert strictly_decreasing(slopes_of(v | qualState))

property ConcavityOfCapacityCurve(snapshot):
    curve = AfrrCapacityView.Evaluate(snapshot).capacityValueCurve
    assert concave(curve.breakpoints)
    assert curve.curvature == Concave and curve.sense == Maximize
    assert problemClassHint.binariesByOrigin[curve.termId] == 0
```

**Why it catches a real bug.** Concavity is not an aesthetic property here — it
is the reason the PWL is binary-free (ADR-007, ADR-008). A `V` that is
accidentally non-concave either makes the solve exponentially slower or, worse,
gets silently declared `Concave` and solved as an incorrect relaxation.

### 3.7 Curvature-declaration honesty

**Property.** The declared `curvature` on every `PwlTerm` matches the
breakpoints, and the binary count follows from the declaration (`INV-V-12`,
`C2` §3.2).

The test injects mis-declarations and asserts a clean failure, not a wrong number.

| Injected term | Declared | Actual breakpoints | Expected |
|---|---|---|---|
| `T_a` | `Concave` | concave | accepted; 0 binaries |
| `T_b` | `Concave` | convex | **`HALT`**, `INV-V-12`, message names `T_b` |
| `T_c` | `Concave` | S-shaped (neither) | **`HALT`**, `INV-V-12` |
| `T_d` | `General` | concave | accepted; SOS2 built; `binariesByOrigin[T_d] > 0` |
| `T_e` | `Convex` + `Maximize` | convex | accepted; requires SOS2; declared in `problemClassHint` |
| `T_f` | `Concave` | concave but with a tied slope (two collinear segments) | accepted; documented boundary case; slopes non-increasing, not strictly |
| `T_g` | any | 2 points only | accepted; degenerate PWL is a `LinearTerm` in disguise |
| `T_h` | any | non-increasing `X` | **`HALT`**, `C2` §3.2 requires strictly increasing |

```
property CurvatureHonesty(term):
    declared = term.curvature
    actual   = classify_curvature(term.breakpointsY, term.breakpointsX)
    if declared != actual and not (declared == General):
        assert_raises(ContractViolation[INV-V-12], () => Compose(bundle_with(term)))
    else:
        b = Compose(bundle_with(term))
        assert b.problemClassHint.binariesByOrigin[term.termId] ==
               expected_binaries(declared, term.sense)
```

`T_f` is worth stating explicitly because it is where implementations disagree:
`INV-V-11` requires **strictly** decreasing slopes for `V(SOC)`, while a general
`PwlTerm` declared `Concave` needs only non-increasing slopes to be LP-exact. The
two thresholds are different on purpose and the test pins both.

### 3.8 Double-count detection

**Property** `INV-V-01`. The composer rejects any bundle in which one
`(effect, variable, slot)` triple is priced more than once, and does so **loudly**
rather than netting the terms (`L2` §4: "the composer performs no economics").

```
property DoubleCountRejected(bundle, effect):
    original = Compose(bundle)                       # passes

    # inject a duplicate-effect term: same effect, same variable, same slot,
    # different originView
    dup = clone(pick_term(bundle, effect)).with(originView = SomeOtherView,
                                                termId = fresh())
    assert_raises(ContractViolation[INV-V-01], () => Compose(bundle + dup))

    # the failure names the collision — a HALT that does not say what collided
    # is an outage, not a diagnosis
    err = capture(() => Compose(bundle + dup))
    assert err.mentions(effect, dup.variable, dup.slots, both originViews)

    # AND: the composer must not have netted them
    assert not exists(b) where Compose(bundle + dup) succeeds with
           coefficient == original.coefficient    # silent netting
```

Four concrete duplicate-effect scenarios, from ADR-009's sharpened context, each
a separate fixture:

| Scenario | Injection | Expected |
|---|---|---|
| Energy priced twice | `OppCostView` marks a MWh as internal cost **and** a spot view marks it as market value on the same `(variable, slot)` | `HALT` `INV-V-01`. Arbitrage would otherwise appear free |
| Reserve/ID headroom | `AfrrCapacityView` reserves SOC headroom that `IdOptionView` also plans to use, both claiming the same base and slot | `HALT` `INV-V-01` |
| Peak vs volumetric | `NetworkPeakCharge` and `NetworkVolumetricCharge`, both on base `PoiImport` | **accepted** — different effects on the same base is legal (ADR-009 §3) |
| Degradation twice | `OppCostView` charges `CycleDegradation` per throughput **and** `V` was fitted on net-of-degradation cashflows, so `StoredEnergyContinuation` embeds it | `HALT` `INV-V-01`. This is the scenario ADR-009 explicitly exists for and it needs a dedicated fixture with a degradation-inclusive `V` artefact |

The third row is as important as the others: a test suite that rejects
legitimate co-existence is a test suite the team will disable.

**Coverage side.** `INV-V-05`: an *unclaimed* effect is a warning, priced at
zero, logged in `unclaimedEffects` — not a failure. Assert both directions, since
the asymmetry is deliberate: silently missing revenue is safer than silently
double-counting cost.

### 3.9 Composition-order violation

**Property** `INV-V-07`. Running the composer's stages out of order raises an
exception, **not** a wrong number. This is, in ADR-009's words, the entire point.

| Order run | Violated precondition | Expected |
|---|---|---|
| `Peak` before `Tariff` | Stage 5 requires POI energy marked | `HALT` `INV-V-07`, naming stage 5 and the unmet precondition |
| `Peak` before `OppCost` | Stage 5 requires battery energy marked | `HALT` `INV-V-07` — and this is the double-count the ordering exists to prevent |
| `ReserveCoupling` before `OppCost` | Stage 4 requires battery energy marked | `HALT` `INV-V-07` |
| `Delineation` before `OppCost` | Stage 3 requires battery energy marked | `HALT` `INV-V-07` — `(21)` is not knowable until battery energy is marked |
| `Validate` first | Stage 6 requires all above | `HALT` `INV-V-07` |
| The order declared by `L2` §4 | — | accepted; `compositionOrder` recorded in the bundle for audit |
| Correct order, one stage omitted | Stage 6's coverage check | `HALT` `INV-V-04` if a mandatory term is now absent, else `warn` `INV-V-05` |

```
property CompositionOrderEnforced(bundle):
    for order in all_permutations(stages) where order != canonical:
        result = try(() => Compose(bundle, order))
        assert result.is_exception                      # never a value
        assert result.error.code == INV-V-07
        assert result.error.names(first_violated_stage(order))

    ok = Compose(bundle, canonical)
    assert ok.compositionOrder == canonical             # recorded, C2 §1
```

**Why this is a property test and not a unit test.** A unit test would pin one
wrong order. The property enumerates *all* permutations, which is the only way to
be sure the precondition machinery — rather than a hardcoded check on one case —
is doing the work. With five stages there are 120 permutations; the whole
property runs in under a second.

---

## 4. `L3` Planner

Purity claim under test: pure given a fixed solver configuration and seed
(`L3` header, ADR-013).

### 4.1 Feasibility under every scenario, not just the mean

**Property.** The planned trajectory satisfies every physical constraint, and
satisfies confirmed commitments under **every** scenario in the ensemble — not
the mean scenario, not a representative one (`L3` §9, `INV-P-01`, `INV-P-02`).

```
property FeasibleUnderEveryScenario(bundle, state):
    plan = Planner.Solve(bundle, state)

    for s in 0 .. S-1:                            # EVERY scenario, weighted or not
        traj = simulate(plan, scenario=s)         # apply scenario-s activation
        assert traj.soc[t] in [socMin, socMax]        ∀t   or  s in the ε-tail
        assert traj.p_charge[t]    <= pMaxCharge[t]   ∀t
        assert traj.p_discharge[t] <= pMaxDischarge[t] ∀t
        assert traj.p_poi[t] in [-poiExportLimit, poiImportLimit] ∀t
        assert commitments_served(plan, scenario=s)   ∀ confirmed entries

    # the chance constraint is a bound on the tail, not an excuse
    violating_mass = Σ w_s for s where soc leaves the band
    assert violating_mass <= ε + WEIGHT_TOL
    # confirmed commitments are NOT subject to ε — they are hard in every mode
    assert commitments_served in ALL scenarios, no exceptions
```

**Why it catches a real bug.** A model that satisfies SOC bounds on the mean
activation path and violates them on the 90th-percentile path is a delivery
failure waiting for a windy Tuesday, and `INV-S-04` is not an accounting item —
it is a prequalification risk. The distinction between the ε-tolerated chance
constraint (SOC comfort) and the never-tolerated commitment constraint is the
part implementations get wrong.

### 4.2 Commitment safety

**Property** `INV-P-02`. A plan that cannot honour a confirmed award never
reaches `C3`.

```
property CommitmentSafety():
    # inject a confirmed award the current SOC cannot serve
    state = base_state.with(ledger += ReserveAward{
                status = Confirmed, block = b, up = R_mw,
                feasibilityRequirement = SocCorridor(lower = socNow + huge) })

    result = try(() => Planner.Solve(bundle, state))

    assert result.mode == HALT                    # never a quiet under-delivery
    assert result.error.code == INV-P-02
    assert nothing_emitted_on_C3()
    assert result.diagnosis.names(b, R_mw, the binding corridor)

    # and the negative control: a servable award produces a plan that serves it
    state2 = base_state.with(ledger += servable_award)
    plan   = Planner.Solve(bundle, state2)
    assert plan.serves(servable_award) in every scenario
```

Run this against **every degradation mode**, including `SAFE` and `HALT`
recovery, because commitment feasibility is one of the two things never degraded
away (ADR-014 §3).

### 4.3 Determinism

**Property.** Same `C2` and same manifest, byte-identical `C3`.

```
property PlannerDeterminism(c2_recorded, manifest):
    p1 = Planner.Solve(c2_recorded, state, budget, seed=manifest.seed)
    p2 = Planner.Solve(c2_recorded, state, budget, seed=manifest.seed)
    assert bytes(p1.C3) == bytes(p2.C3)
    assert p1.contentHash == p2.contentHash

    # constraint build order must not matter to the answer
    p3 = Planner.Solve(..., row_order = declared_alternate_order)
    assert_close(p3.objective, p1.objective, rel=1e-9)
    # (byte-identity across row orders is NOT asserted — the solver's pivot
    #  sequence legitimately differs. Objective identity is the invariant.)

    # thread count must not matter in the reproducibility configuration
    p4 = Planner.Solve(..., threads=1)
    assert bytes(p4.C3) == bytes(p1.C3)         # CI config is single-threaded
```

The parenthetical is deliberate. Over-asserting here produces a flaky test that
gets deleted; asserting the objective and the emitted intent, and leaving the
internal pivot path free, is the line that holds.

### 4.4 DA bid curve monotonicity

**Property** `INV-P-09`. The curve produced by parametric re-solve over candidate
clearing prices is monotone: quantity non-increasing in price for a buy curve,
non-decreasing for a sell curve.

```
property DaCurveMonotone(bundle, state):
    curve = Planner.BuildDaCurve(bundle, state, price_grid)
    for i in 1 .. len(curve)-1:
        if curve.side == Buy:  assert curve[i].qty <= curve[i-1].qty + QTY_TOL
        else:                  assert curve[i].qty >= curve[i-1].qty - QTY_TOL

    # a non-monotone result must HALT, never be sorted into compliance
    broken = inject_missing_coupling_constraint(bundle)
    assert_raises(ContractViolation[INV-P-09], () => Planner.BuildDaCurve(broken,...))
```

**Why it catches a real bug.** `L3` §5 and ADR-012 both state it: a non-monotone
DA curve is almost always a *missing coupling constraint*, because without the
headroom or corridor coupling the parametric re-solve is free to jump between
disconnected optima as the candidate price sweeps. Sorting the curve to make the
exchange accept it would submit a schedule the model never endorsed and destroy
the diagnostic.

### 4.5 Budget exhaustion and the fallback rungs

**Property.** Forcing a time-out at each rung takes the correct next rung, and
records it (`L3` §6).

| Forced condition | Expected rung | Assertions |
|---|---|---|
| Solve completes within budget, gap ≤ tolerance | — | Normal path; `PlanResult.tierUsed`, `mipGap` recorded |
| Budget exhausted, incumbent feasible and within acceptable gap | 1 | Incumbent used; `mipGap` recorded and non-zero; `budgetExhausted = true` |
| Budget exhausted, incumbent infeasible or gap too wide | 2 | Previous tick's plan projected forward, **re-checked for feasibility**, and only then used |
| Rung 2's projected plan is infeasible under current state | 3 | `DEFENSIVE` baseline: commitments honoured, peak protected, no new speculative exposure |
| `DEFENSIVE` baseline itself infeasible | 4 | `HALT` with the irreducible infeasible subsystem reported |
| Rung 2 or 3 used on `N` consecutive ticks | — | Mode escalates (ADR-014); the escalation appears in the settlement record |

```
property FallbackRungs(rung):
    state = construct_state_forcing(rung)
    r = Planner.Solve(bundle, state, budget = tiny)
    assert r.rungTaken == rung
    assert r.recorded_in(PlanResult)
    assert peak_protection_present(r)          # never dropped, any rung
    assert commitments_served(r)               # never dropped, any rung
    if rung == 4: assert r.mode == HALT and r.iis is not empty
```

The rung-2 assertion "re-checked for feasibility" is the one that matters.
Reusing yesterday's plan without re-checking it against today's confirmed
commitments is exactly how a stale plan under-delivers a reserve award.

### 4.6 Feasibility-restoration ordering

**Property** `L3` §6, `INV-V-15`. Relaxation proceeds `Risk → Liquidity → stop`.
`Physical`, `Regulatory` and confirmed-commitment constraints are **never**
relaxed.

```
property RestorationOrdering(infeasible_bundle):
    r = Planner.RestoreFeasibility(infeasible_bundle)

    # 1. ordering: nothing of a later class is relaxed while an earlier class
    #    still has slack available
    assert r.relaxations.ordered_by(Risk before Liquidity)
    assert not (relaxed(Liquidity) and unrelaxed_slack_remains(Risk))

    # 2. THE HARD ASSERTION
    assert r.relaxations.none(b => b.reason in {Physical, Regulatory})
    assert r.relaxations.none(b => b.origin == ConfirmedCommitment)

    # 3. relaxation is by explicit slack at a declared penalty, never by
    #    deleting a constraint
    assert r.model.row_count == infeasible_bundle.expected_row_count
    assert r.slacks.all(s => s.penalty is declared and s.value is recorded)

    # 4. still infeasible with only Physical/Regulatory left => HALT with an IIS
    hard = infeasible_bundle.with(all Risk and Liquidity bounds removed)
    res  = Planner.RestoreFeasibility(hard)
    assert res.mode == HALT
    assert res.iis is not empty and names only Physical/Regulatory/Commitment rows
```

**The adversarial case.** Construct a bundle where relaxing a `Physical` bound
would restore feasibility *cheaply* and relaxing `Risk` bounds would not restore
it at all. A greedy or penalty-sorted restoration will reach for the physical
bound. Assert `HALT` instead. This test exists because the tempting
implementation — minimise total penalty over all slacks — is wrong, and it is
wrong in a way that only shows up when the world has genuinely moved outside what
the asset can deliver, which is the worst possible moment to discover it.

### 4.7 The metamorphic family

Each relation is generated from every input in the corpus. Held fixed unless
named: the manifest, the state snapshot, the commitment ledger, the risk profile,
the calendar and every other price.

| # | Input change | Expected relation | Bug class caught |
|---|---|---|---|
| `M-P1` | `afrrCapPrice[·,b] ↑` by `δ > 0` | Planned reserve `rUp[b] + rDn[b]` is **non-decreasing** | Sign error on capacity revenue; headroom coupling with the wrong sense; a reserve term that is being ignored entirely |
| `M-P2` | All energy price beliefs shifted by `+Δ` (uniform additive, all scenarios, all slots; tariff, peak and reserve prices held fixed) | Planned **net export energy** over the horizon is non-decreasing | Sign frame confusion between battery and POI frames; a charge/discharge asymmetry with the wrong sign |
| `M-P3` | `peakPrice ↑` for a regime | The planned peak `zPeak` for that regime is **non-increasing**, and the objective is non-increasing | Epigraph built with the wrong sense; `prorationFactor` applied to the wrong side; peak floor ignored |
| `M-P4` | `poiImportLimit` tightened (or `poiExportLimit` tightened) | The objective is **non-increasing** | Missing POI envelope constraint; a bound applied to the wrong frame; a soft bound where a hard one was specified |

```
metamorphic M_P1_ReserveMonotone(bundle, block b, δ > 0):
    a = Plan(bundle)
    b2 = Plan(bundle.raise_afrr_cap_price(b, δ))
    assert reserve_mw(b2, b) >= reserve_mw(a, b) - MW_TOL

metamorphic M_P2_PriceLevelShift(bundle, Δ > 0):
    a  = Plan(bundle)
    b2 = Plan(bundle.shift_all_energy_prices(Δ))
    assert net_export(b2) >= net_export(a) - ENERGY_TOL

metamorphic M_P3_PeakPriceMonotone(bundle, regime, δ > 0):
    a  = Plan(bundle)
    b2 = Plan(bundle.raise_peak_price(regime, δ))
    assert zPeak(b2, regime) <= zPeak(a, regime) + KW_TOL
    assert objective(b2)     <= objective(a)     + MONEY_TOL

metamorphic M_P4_PoiTightening(bundle, factor in (0,1)):
    a  = Plan(bundle)
    b2 = Plan(bundle.scale_poi_limits(factor))
    assert objective(b2) <= objective(a) + MONEY_TOL
```

Notes that keep these honest:

- **`M-P2` asserts net export, not gross discharge.** A uniform `+Δ` on every
  energy price adds `Δ ×` (net exported energy) to the objective, so the
  incentive to be net short energy weakly increases. Gross discharge can legally
  fall if the plan also charges less. Asserting gross discharge produces a
  test that fails for correct reasons, gets weakened, and stops catching
  anything. This is worth stating because `L3` §9's shorthand ("planned discharge
  must not decrease") is the informal version of this precise relation.
- **`M-P4` is the relaxation-monotonicity relation.** Shrinking the feasible set
  cannot improve a maximisation. It is the cheapest possible check that a
  constraint is actually in the model — if the objective is unchanged when you
  halve the POI limit, the POI envelope is not binding anywhere, which is either
  a fixture problem or a missing constraint. The test therefore also asserts that
  *some* tightening in the sweep does move the objective.
- **`M-P1` and `M-P3` interact.** A day where reserve and peak compete for the
  same headroom is the interesting one; the generator is biased to produce them.

**Extended relations**, same machinery, run nightly:

| # | Change | Relation |
|---|---|---|
| `M-P5` | Add a redundant constraint (a copy of an existing binding row) | Objective unchanged exactly |
| `M-P6` | Add a `BoundTerm` that is strictly looser than an existing one | Objective unchanged exactly |
| `M-P7` | Increase `socMax` | Objective non-decreasing |
| `M-P8` | Increase `pMaxDischarge` | Objective non-decreasing |
| `M-P9` | Widen `cvarLevel` / lower `positionScale` (degradation) | Objective non-increasing; the plan is weakly more conservative |
| `M-P10` | Increase `deliveryObligation` on a block | Objective non-increasing; the corridor tightens |
| `M-P11` | Set `stalenessPenalty` more aggressive | `V`'s slopes shrink; terminal SOC moves toward the myopic optimum |
| `M-P12` | Refine the PWL breakpoint grid (superset of existing breakpoints) | Objective changes by at most the declared approximation bound, monotonically toward the Tier-1 value |

### 4.8 Tier agreement

**Property** `L3` §9, ADR-010. On instances small enough for Tier 1 to solve
exactly:

```
property TierAgreement(small_bundle):
    t1 = CoOptimizer.Solve(small_bundle, tier=1)
    t2 = CoOptimizer.Solve(small_bundle, tier=2)
    t3 = CoOptimizer.Solve(small_bundle, tier=3)

    assert t2.dualBound >= t1.objective - MONEY_TOL     # valid upper bound (max)
    assert t2.primal    <= t1.objective + MONEY_TOL     # Tier 1 is the reference
    assert t3.objective <= t1.objective + MONEY_TOL
    assert all_feasible([t1, t2, t3])                   # incl. every exit path
    record gap(t2), gap(t3)                             # feeds T5

    # every Tier 2 exit path returns a feasible primal, including cap-exhausted
    for exit in {converged, iteration_cap, time_cap, numerical_trouble}:
        r = CoOptimizer.Solve(small_bundle, tier=2, force_exit=exit)
        assert r.primal is feasible
        assert r.dualBound >= r.primal - MONEY_TOL
```

The recorded gaps feed `T5`; this level only asserts the *structural* relations
(bound validity, feasibility on every exit path). Whether the gap is acceptable
is a release question, not a merge question.

### 4.9 `INV-P-07` — simultaneous charge and discharge

**Property.** With the complementarity binary disabled by default, the post-solve
monitor fires exactly when it should.

| Fixture | Binary | Expected |
|---|---|---|
| Positive prices, `η_c·η_d < 1` | off | No simultaneity; monitor silent. This is the common case the omission is justified by |
| Negative energy prices for a block of slots | off | Simultaneity **may** appear; if it does, `alert` `INV-P-07`, the tick is flagged, and the plan is still emitted |
| Same, binary enabled by config | on | No simultaneity; objective ≤ the binary-off objective (the binary restricts) |
| aFRR down-activation obligation forcing import while discharge is committed | off | Monitor fires or does not, but the trajectory is recorded either way |

```
property SimultaneityMonitor(fixture):
    plan = Planner.Solve(fixture, simultaneity_binary = off)
    v = exists t: plan.pCharge[t] > 1e-6 and plan.pDischarge[t] > 1e-6
    assert monitor_fired(plan) == v
    if v: assert alert_raised(INV-P-07) and plan.flagged

    plan_on = Planner.Solve(fixture, simultaneity_binary = on)
    assert no_simultaneity(plan_on)
    assert plan_on.objective <= plan.objective + MONEY_TOL   # restriction
```

The last assertion is the metamorphic one and it is the useful part: enabling the
binary can only *restrict*, so an objective that improves when the binary is
turned on means the relaxation and the restriction are not the same model.

---

## 5. `L5` Settlement

Purity claim under test: pure function of `(ExecutionOutcome, metered reality)`.
Settlement has no access to what the Planner intended (`C4` header).

### 5.1 Conservation / energy balance

**Property** `INV-X-03`. The POI bridge, evaluated on realised data, per slot.

```
property EnergyConservation(outcome):
    for t in slots:
        lhs = outcome.meteredPoiImport[t] - outcome.meteredPoiExport[t]
        rhs = outcome.meteredLoad[t] - outcome.meteredPv[t]
            + outcome.batteryChargeEnergy[t] - outcome.batteryDischargeEnergy[t]
        residual = lhs - rhs
        if outcome.isFinal: assert abs(residual) <= meter_tolerance    # HALT
        else:               warn_if(abs(residual) > meter_tolerance)

    # SOC consistency, the leading indicator (INV-X-05)
    for t in slots:
        predicted = soc[t-1] + η_c·charge[t] - discharge[t]/η_d
        assert abs(outcome.socMeasured[t] - predicted) <= soc_tolerance   # warn

    # and the rolling form, which is what actually catches η drift
    drift = cumulative(socMeasured - predicted) over the settlement period
    assert abs(drift) <= period_drift_threshold                          # alert
```

**Why it catches a real bug.** The per-slot check catches meter and sign errors.
The cumulative check catches the thing that matters: a slow divergence between
modelled and measured SOC is the earliest available signal that the efficiency or
degradation model has drifted (`C4` §7), and it feeds the `modelError` bucket
before the money shows up in P&L. Aux load folded into `η` instead of modelled as
`LoadKw` shows up here as a one-sided drift.

Sign-frame fixtures, run explicitly, because this is where frame confusion dies:

| Fixture | `p_batt` | `p_poi` | Assertion |
|---|---|---|---|
| Battery discharging into site load, no grid flow | `> 0` | `≈ 0` | Import and export both ≈ 0 |
| Battery charging from grid at night | `< 0` | `> 0` (import) | Import equals load + charge |
| PV export with battery idle | `= 0` | `< 0` (export) | Export equals PV − load |
| PV export while charging | `< 0` | sign flips at the crossover | Balance holds through the sign change |

### 5.2 Restatement idempotency

**Property** `INV-S-08`, ADR-004 append-only revisions. Meter data and imbalance
prices arrive late and get corrected; a restatement is a new artefact referencing
the old one, never an in-place edit.

```
property RestatementIdempotency(provisional, final):
    s1 = Settle(provisional)                       # isFinal = false
    s2 = Settle(final, revisionOf = s1.id)         # isFinal = true

    # 1. applying the same restatement twice changes nothing
    s3 = Settle(final, revisionOf = s1.id)
    assert s3.contentHash == s2.contentHash

    # 2. the provisional artefact still exists and is unchanged
    assert Store.Get(s1.id).contentHash == s1.contentHash

    # 3. order independence: two independent restatements of disjoint slots
    #    commute
    assert Settle(rA, then rB).state == Settle(rB, then rA).state

    # 4. no backward write without an explicit revisionOf
    assert_raises(ContractViolation[INV-S-08],
                  () => WriteState(effectiveFrom = earlier, revisionOf = null))

    # 5. peak state after restatement is recomputed, not patched
    assert s2.realisedPeak == recompute_peak_from_scratch(final)
    assert monotone_within_period(s2.realisedPeak)          # INV-S-01
```

Assertion 5 is the substantive one. A restatement that *lowers* the metered peak
must lower `realisedPeak` — but `INV-S-01` says `realisedPeak` is non-decreasing
within a period. Both are true simultaneously only if the restated artefact is a
fresh computation over the corrected series, not an incremental `max` against a
stale value. The test pins this, because the incremental implementation is the
obvious one and it is wrong.

### 5.3 The zero-gap test

**The cleanest single test in Settlement.** Feed the *planned* trajectory back in
as the *realised* outcome, with realised prices equal to the beliefs the plan was
built on and fills equal to intents. Then every one of the four error buckets
must be exactly zero.

```
property ZeroGap(bundle, plan):
    outcome = synthesise_outcome_from(plan,
                  prices   = the belief realisation the plan optimised against,
                  fills    = every intent filled in full at its limit price,
                  meter    = plan.trajectory exactly,
                  activation = the scenario the plan was evaluated on)

    s = Settle(outcome)

    assert_zero(s.forecastError,      MONEY_TOL)   # world == belief
    assert_zero(s.modelError,         MONEY_TOL)   # valuation == accounting
    assert_zero(s.optimalityGap,      MONEY_TOL)   # same tier, same solve
    assert_zero(s.executionSlippage,  MONEY_TOL)   # intent price == fill price
    assert_zero(s.unexplained,        MONEY_TOL)   # INV-S-02
    assert_close(s.realisedByEffect, plan.plannedByEffect, MONEY_TOL)  # term by term
```

**Why this is worth more than it looks.** Any non-zero bucket in this test is a
*definitional* disagreement between `L2` and `L5` about what a term means — a fee
netted into a price on one side and not the other, a proration applied twice, an
effect assigned to a different bucket. Those disagreements are invisible in
production because they hide inside genuine forecast error, and they are exactly
what makes `unexplainedRatio` creep upward over months (`INV-S-07`). The zero-gap
test finds them on day one, deterministically, with no market data at all.

Run it for each bucket in isolation by perturbing one input at a time:

| Perturbation | Expected non-zero bucket | All others zero |
|---|---|---|
| Realised prices differ from belief | `forecastError` | yes |
| Settlement values the same trajectory with a different term definition | `modelError` | yes |
| Re-run the tick at Tier 1 and compare | `optimalityGap` | yes |
| Fill prices differ from limit prices | `executionSlippage` | yes |
| Two perturbations at once | both buckets, additively within tolerance | `unexplained` ≈ 0 |

The last row is the strong one: it asserts the decomposition is (locally)
additive, which is what `INV-S-03` claims and what makes the four-way split
actionable rather than decorative.

### 5.4 Attribution completeness

**Property** `INV-S-02`. `realisedByEffect` plus `unexplained` sums to total
realised P&L, and the `unexplained` bucket is never absorbed elsewhere.

```
property AttributionCompleteness(outcome):
    s = Settle(outcome)
    total = sum(s.realisedByEffect.values()) + s.unexplained
    assert_close(total, s.totalRealisedPnl, MONEY_TOL)          # INV-S-02

    # the effect enumeration is the SAME one Valuation used (ADR-009)
    assert keys(s.realisedByEffect) ⊆ EconomicEffect.all
    assert keys(s.plannedByEffect)  ⊆ EconomicEffect.all
    assert every effect claimed in the bundle appears in realisedByEffect

    # unexplained cannot be hidden
    inject_unattributable_cashflow(outcome, amount = X)
    s2 = Settle(outcome)
    assert_close(s2.unexplained, X, MONEY_TOL)          # not spread across buckets
    assert s2.unexplainedRatio > 0
    if s2.unexplainedRatio > threshold: assert warn(INV-S-07) raised

    # fees are explicit, never netted into price (C4 §2)
    assert s.realisedByEffect accounted fees separately from execution price
```

The injection test is the point. A settlement implementation that distributes an
unattributable cashflow pro rata across the known effects will pass every sum
check and destroy the diagnostic value of the whole decomposition.

---

### 5.9 Delineation

Purity claim under test: the accumulators are a function of `Z1`/`Z2` and the settled
price series alone (`INV-S-18`).

**Property — route split.** `(28) + (16) = (13)` over generated months. Algebraic, so a
failure is always an implementation defect and never a data artefact.

**Property — relief multiplier.** `(16) + (19) = (16)·(5)/(6)`. Catches a mis-derived
`(17)` or `(18)`, which are otherwise invisible because both routes still look plausible.

**Property — throughput bound.** Generate months with charge throughput above
`η_d·E_usable/(1−η_rt)`; assert `(12) = 0` and `throughputBoundMet` (`INV-S-16`). Then
generate an idle month below the bound and assert the invariant fires rather than the
Planner silently assuming it away.

**Fixture — co-location identity.** The cheapest exact test in the suite. A month with
`load = 0`, discharge never clipped, `(12) = 0`; assert `(21) = 0` **to the cent**. The
identity `(16)+(19) = (9) = (3)` is independent of η, of dispatch, of `AW` and of prices,
so any deviation localises a defect with no modelling judgement involved.

**Fixture — A5 collapse.** Two plants on a common EEG vintage; assert `(32x) = ZFx·(32)`
exactly and that no per-plant accumulator is built. Then a mixed estate with one
ungeförderte plant, asserting the collapse is **refused** rather than silently applied
(`commonEegVintage = false`).

**Metamorphic — premium.** Raise `MAX[AW − MW_month; 0]`: planned PV charging must be
non-decreasing, because PV charging is what raises `(15)` and converts grey to green.

**Metamorphic — levy.** Raise the EnFG rate in a slack regime: planned grid charging must
not decrease. In a saturated regime it must not change the plan at all, since the
marginal relief is zero there.

**Metamorphic — dilution.** Add a storage export in an AW=0 quarter-hour late in the
month: `(30)` must fall and `(31)` must fall with it. This is the one term in the system
where a late action lowers the value of earlier ones, and a test that does not exercise
it will not notice when the ratio is implemented as a per-slot quantity.

**Zero-gap, split.** Feed the planned trajectory back as realised; assert residual
`modelError = 0` and `linearizationGap` within its declared budget. A `linearizationGap`
of zero is as suspicious as one above budget — it means the linearisation is not being
exercised.

**Calendar.** Accumulator reset asserted to the exact slot of the Europe/Berlin month
boundary, with a DST-containing month as a permanent fixture. An off-by-one-slot reset
moves value between two months and is otherwise silent.

---

## 6. Cross-seam properties

These apply to every seam and are generated once, parameterised by contract type.

### 6.1 Round-trip serialisation

```
property RoundTrip<TPayload>(payload):
    for format in {Binary, Json}:
        wire   = Serialise(payload, format)
        back   = Deserialise<TPayload>(wire, format)
        assert back == payload                       # structural equality
        assert back.contentHash == payload.contentHash
        assert Serialise(back, format) == wire       # canonical, byte-stable

    # cross-format agreement
    assert Deserialise<TPayload>(Serialise(payload, Binary), Binary) ==
           Deserialise<TPayload>(Serialise(payload, Json),   Json)

    # float32 storage, float64 money (02-conventions §6)
    assert money_and_soc_fields_round_trip_exactly(payload)   # no float32 for EUR
```

Byte-stability of the canonical form is not pedantry — it is what makes
`contentHash` meaningful and therefore what makes the Merkle chain of `T3` work.
A serialiser that emits map entries in hash order breaks determinism
(`ADR-013`, iteration order) and this test is where that surfaces.

### 6.2 Version rejection

```
property VersionRejection<TPayload>(payload):
    # unknown major -> clean rejection, no partial read
    bumped = payload.with(schemaVersion = major+1)
    r = try(() => Consumer.Receive(bumped))
    assert r.is_rejection and r.code == INV-G-03
    assert r.consumed_no_fields                      # never a partial read
    assert consumer_state_unchanged()

    # known minor bump with a new optional field -> accepted, default applied
    minor = payload.with(schemaVersion = minor+1, newOptionalField = present)
    assert Consumer.Receive(minor).ok
    assert Consumer.Receive(minor).newOptionalField == declared_default

    # unit change under an unchanged major version -> build failure (C0 §5)
    assert build_fails_when(unit_changed_without_major_bump)
```

The last clause is checked mechanically in `T1` §9.2 and asserted here as a
property so that adding a new contract cannot forget it.

### 6.3 Producer/consumer double validation

`C0` §4: every seam is validated twice, and the two validators must agree.

```
property DoubleValidation<TPayload>(payload):
    pv = ProducerValidator<TPayload>.Check(payload)
    cv = ConsumerValidator<TPayload>.Check(payload)

    assert pv.verdict == cv.verdict                  # agreement is the property
    assert pv.violated_ids == cv.violated_ids

    # divergent actions, same verdict (C0 §4)
    if verdict == Fail:
        assert producer_side_action == "enter producing layer's ladder"
        assert consumer_side_action == HALT

    # both are ON in every environment
    for env in {Dev, Ci, Staging, Production}:
        assert ValidatorEnabled(env) == true          # C0 §3, no exceptions
        assert no_config_key_exists_to_disable_it()
```

A producer/consumer disagreement is itself a failure, and it is a common one: the
two validators drift when someone adds a range check on one side only. Generating
both from the same field table makes the property hold by construction, and this
test proves the generation was not bypassed.

### 6.4 Seam-recording fidelity

```
property SeamRecordingFidelity(run):
    recorded = Record(run, seams = all)
    for seam in {C1..C5}:
        replayed = ReplayLayer(seam.consumer, recorded[seam])
        assert replayed.output.contentHash == run.artefacts[seam.next].contentHash
    # a recorded seam must be sufficient: no ambient state may be required
    assert ReplayLayer runs with Belief store absent, network absent, clock absent
```

This is the property that makes every other per-layer test in this document
possible, and it is asserted rather than assumed.

---

## 7. Generators

Property tests are only as good as their inputs. The generators are specified,
versioned artefacts.

| Generator | Produces | Constrained to satisfy |
|---|---|---|
| `BeliefGen` | `C1` payloads | Every `INV-G-*` and `INV-D-*` by construction |
| `EnsembleGen` | Joint ensembles with a controllable correlation structure | `INV-D-05`, `INV-D-06`; correlation is a *parameter*, so tests can sweep from independent to perfectly coupled |
| `BundleGen` | `C2` payloads | `INV-V-*`; with an "LP-only" mode (all PWL `(Concave, Maximize)` / `(Convex, Minimize)`) for the exact metamorphic relations |
| `LedgerGen` | Commitment ledgers | Mixed `Pending`/`Confirmed`, servable and unservable awards |
| `CalendarGen` | Calendar windows | Biased toward DST days, month boundaries, year boundaries, HLZF edges |
| `QualityGen` | `QualityStamp` assignments | The full quality matrix of §2.5, plus random mixtures |
| `RegimeGen` | Tariff regime combinations | Single and composed regimes across the `TariffRegimeId` set |

**Shrinking is mandatory.** A property failure that reports a 192-slot,
64-scenario counterexample is not actionable. Every generator declares a shrink
strategy that reduces horizon, scenario count, term count and magnitude while
preserving the failure, and the shrunk case is written into the fixture corpus as
a permanent `T0` regression test.

**Bias, not uniformity.** Uniform random inputs almost never produce an
interesting instance: they do not produce negative prices, peak-critical days,
binding POI limits, or an activation that collides with a peak. The generators
are explicitly biased toward the adversarial region and the bias parameters are
part of the versioned generator artefact, so a change in coverage is visible as
an artefact change.
