# P0 — Workstreams and Sequencing

Advisory (per the root `README.md` status table), but the ordering argument is
not. Everything below follows from one property of the ADR register: some
decisions are cheap to reverse and some require rewriting every layer above them.

---

## 1. The sequencing principle

**Order by reversibility, not by excitement.**

The interesting parts of this system are the cross-market co-optimisation
(ADR-010) and the value function (ADR-007). Both are the wrong place to start.
They sit at the top of the dependency stack, they are the parts most likely to be
rewritten as understanding improves, and — critically — they are cheap to rewrite
*provided the layers beneath them do not have to move*.

`01-adr/README.md` grades each decision's reversibility. That column is the build
order:

| Reversibility | Decisions | Consequence for sequencing |
|---|---|---|
| **Very expensive** | ADR-004, ADR-005 (belief store, joint ensemble), ADR-006, ADR-008 (contract surface), ADR-013 (determinism) | Build first, build correctly. Retrofitting means rewriting everything above. |
| Expensive later | ADR-003 (typed quantities), ADR-007 (V not λ), ADR-011 (tariff plug-in) | Get the *types and interfaces* right early; the implementations can follow. |
| Moderate | ADR-002, ADR-009, ADR-010, ADR-012, ADR-014 | Sequence for convenience. |
| Cheap | ADR-001 (solver behind an abstraction) | Defer deliberately — this is OPEN-1. |

Three of these deserve naming explicitly, because they are the ones where "we'll
add it later" is not a real option:

**ADR-004 / ADR-005 — the belief store.** Bitemporality is not a feature that can
be bolted onto a store that was built single-temporal. Every read site in the
engine would have to change, and — worse — the *absence* of a lookahead bug is
not observable by inspection. A store built without `asOf` in the read signature
produces a backtest that looks like alpha. Similarly, the shared scenario axis
(ADR-005) cannot be introduced after views have been written against per-series
marginals: every view would have to be re-derived, and the estimated value of the
strategy would move, in an embarrassing direction.

**ADR-006 / ADR-008 — the contract surface.** The five term shapes and the C0–C5
envelope are the vocabulary every other workstream speaks. If W4 and W5 begin
against a provisional surface, they encode a provisional surface, and the
"contract change requires doc + version + tests in the same commit" discipline
becomes a change-management problem across four in-flight branches instead of a
one-line edit before anyone has started.

**ADR-013 — determinism.** Content addressing is an *envelope field*
(`contentHash`, `inputHashes`) and a *solver configuration*. Both are trivially
cheap on day one and violently expensive on day 200, because retrofitting them
means auditing every code path for wall-clock reads, hash-order iteration, and
unpinned artefacts — on a codebase large enough that nobody can hold it in their
head. And until determinism exists, no refactor is verifiable and no regression is
detectable, so the cost is not only the retrofit; it is every unverified change
made in the meantime.

---

## 2. Workstreams

One refinement to the proposed order, justified below at W0 and W2: **the
content-addressing and manifest spine of W2 is pulled forward into W0**, and W2
retains the harness that consumes it. The reason is that `contentHash` /
`inputHashes` / `manifestId` are fields of `ContractEnvelope` (C0 §7) and a
`SolveBudget` in deterministic work units is a field of the planner interface —
they are surface, not tooling, and surface freezes at W0. What remains in W2
(golden replay, lookahead audit, acyclicity audit, seam recording) is tooling and
correctly sits after W1. Everything else keeps the proposed order.

### W0 — Contract surface, quantities, validators

| | |
|---|---|
| **Goal** | Freeze the vocabulary before anyone writes against it. |
| **Deliverable** | `stubs/` promoted to `src/Flexbid.Btm.Contracts` with implementations of the value types, the envelope, `IContractValidator` and the five invariant registers (`INV-G-*`, `INV-D-*`, `INV-V-*`, `INV-P-*`, `INV-X-*`, `INV-S-*`); stable binary + JSON serialisers; `ContentHash` / `ManifestId` computation; `SolveBudget` in deterministic work units. |
| **Depends on** | Nothing. This is the root. |
| **Definition of done** | (a) Every field in every C0–C5 field table has a corresponding typed member, verified by a table-driven test that parses the markdown field tables and asserts one-to-one correspondence with the DTO members — the doc and the code cannot drift silently. (b) Round-trip property test: for 10 000 generated payloads per contract, `Deserialize(Serialize(x)) == x` and `Hash(Serialize(x))` is stable across process restarts and across machine architectures. (c) Every invariant in every register has at least one passing test and one *failing* test that asserts the correct `ViolationAction`. (d) A payload containing a bare `double` in a contract position fails a Roslyn analyzer rule (`INV-G-02`). |
| **ADR-015 review** | None of OPEN-1/2/3 is touched. Confirm no contract field has acquired a dependency on solver identity, latency budget or fill-model fidelity. `FillProbView`'s output is a `BoundTerm` either way (OPEN-3), so C2 is safe. |

### W1 — Belief store

| | |
|---|---|
| **Goal** | Make lookahead structurally impossible and bound memory by horizon rather than history. |
| **Deliverable** | Ingestion with declared publication semantics per source; append-only bitemporal storage; cold (Parquet, partitioned `(series, source, date)`) / warm (memory-mapped columnar, LRU) / hot (preallocated SoA over `[t, t+H_hot]`) tiers; `IBeliefCursor`; `BeliefSnapshot` materialisation; the joint scenario ensemble artefact and its reduction (ADR-005); the `CivilCalendar` service and the versioned `MarketCalendar` artefact. |
| **Depends on** | W0. |
| **Definition of done** | (a) `IBeliefStore` exposes exactly one read method and it takes `asOf`; a compile-time test (analyzer or reflection test over the public surface) fails if an overload without `asOf` exists. (b) Given a series with three revisions of the same `valid_time`, reads at three `asOf` values return the three distinct values, and a read at an `asOf` before the first publication returns absent-with-quality, never a later value. (c) Peak resident memory over a 12-month replay is within 5% of the 1-month replay — memory is `O(#series × H_hot × #scenarios)` and does not grow with backtest length. (d) `INV-D-04` holds on the real calendar artefact for every day of a 3-year span, including both DST transition days each year, which are fixtures rather than discoveries. (e) `INV-D-05` fails loudly when one series' scenario axis is permuted. |
| **ADR-015 review** | OPEN-3 becomes *decidable*: the cold tier's order-book retention policy is a W1 decision that constrains fill-model fidelity later. Record whether raw book data is being retained at a fidelity that keeps the per-price-band option open. |

### W2 — Determinism harness

| | |
|---|---|
| **Goal** | Make every subsequent change verifiable. |
| **Deliverable** | `RunManifest` capture and pinning; seam recording to disk (on demand for audit days, hashes-only for bulk runs); golden replay runner; the lookahead audit; the acyclicity audit; the Merkle-chain diff tool that localises which upstream input diverged. |
| **Depends on** | W0 (envelope hashing), W1 (something to replay). |
| **Definition of done** | (a) The same manifest replayed twice produces byte-identical output at every seam, on two different machines, with a CI job that fails on the first differing byte and names the artefact. (b) The lookahead audit passes: replacing every fact with `knowledge_time > t` by garbage produces bit-identical results (T6). (c) The acyclicity audit passes: instrumented reads and writes over a full replay show no read of a same-tick write (T3, ADR-006). (d) Given two runs differing in one input, the Merkle diff names the changed artefact in one step, demonstrated on a deliberately perturbed fixture. |
| **ADR-015 review** | OPEN-2 instrumentation starts here: solve-time distribution per tier per gate type is a harness capability, and ADR-015 requires it to exist *before* the latency budget is set so the decision is evidenced. |

> **Why this early, before there is much to test?** Because the harness is not a
> test — it is the *measuring instrument*, and instruments are built before
> experiments. Every later workstream produces a claim ("Tier 3 is within 1.2% of
> Tier 1", "this refactor changed nothing", "the engine did not peek") that is
> unfalsifiable without it. Building it at W2 costs a few weeks against an almost
> empty pipeline; building it at W7 costs the same few weeks *plus* an audit of
> everything written between W2 and W7 for the non-determinism it will
> immediately surface. The retrofit path is strictly worse, and it is worse in a
> way that is invisible until you attempt it.

### W3 — Settlement and the L0 state store

| | |
|---|---|
| **Goal** | Build the accounting system before the thing whose output must be accounted for. |
| **Deliverable** | `ISettlementEngine`: ex-post recomputation from C4 plus metered reality; `PnlAttribution` by `EconomicEffect`; the four-bucket `ErrorDecomposition` scaffolding (buckets that need a Planner return zero and are marked unavailable, not omitted); `IStateStore` with the durable, content-addressed L0; peak accounting on the civil calendar; the §19(2) qualification tracker; the commitment ledger with `Pending`/`Confirmed` semantics. |
| **Depends on** | W0, W1, W2. |
| **Definition of done** | (a) Replaying a recorded week of C4 payloads reproduces the independently-computed network bill and energy bill to within `1e-6 EUR`, against a spreadsheet reconciliation prepared by someone who did not write the code. (b) `INV-S-01` holds across a month boundary *and* across both DST transitions: realised peak resets at local midnight, not at a fixed UTC offset. (c) `INV-S-06` (`fullLoadHours = annualEnergy / annualPeak`) holds over a synthetic full year. (d) `unexplained` is zero on a fully-synthetic outcome whose true decomposition is known by construction. (e) A restart from a persisted `StateSnapshot` reproduces the pre-restart state bit-identically. |
| **ADR-015 review** | None of the three is touched. Check that no settlement code has acquired a dependency on which tier produced the plan beyond the recorded `TierUsed` field. |

> **Why before the Planner?** Two reasons, and the second is the important one.
> First, Settlement is genuinely simpler: it recomputes truth from realised data
> with no optimisation, no uncertainty and no tuning, so it is the right place to
> discover that the peak accounting calendar is subtler than expected — a
> discovery that is cheap here and expensive inside a MILP. Second, and decisively:
> **you cannot evaluate an optimiser without an accounting system.** The Planner's
> only meaningful acceptance criterion is realised P&L decomposed by effect, and
> the four-bucket decomposition (C5 §6) is what separates "the optimiser is weak"
> from "the forecast was wrong". Building the Planner first means building it
> against an objective value it computes for itself — which is not a measurement,
> it is a self-assessment. It also means the ownership matrix gets its first real
> test only when it is already load-bearing.

### W4 — Valuation views, composer, ownership matrix

| | |
|---|---|
| **Goal** | Turn beliefs into the five shapes, with double-counting structurally impossible. |
| **Deliverable** | `PeakView`, `OppCostView`, `AfrrCapacityView`, `AfrrEnergyView`, `ImbalanceRiskView`, `IdOptionView`, `FillProbView`, `TariffView`, `SpotView`; `IValuationComposer` with staged preconditions; the ownership matrix as enforced data; `ITariffRegime` implementations for `AnnualLeistungspreis`, `MonthlyLeistungspreis` and `AtypicalHlzf`; the quality→risk mapping (L2 §6). |
| **Depends on** | W0, W1, W3 (views read `StateSnapshot`). |
| **Definition of done** | (a) Every view passes the seven property tests of L2 §7 — determinism, schema closure, monotonicity, zero-price invariance, scale equivariance, concavity, ensemble coherence — with ensemble coherence asserted *negatively*: a view given a permuted scenario axis on one series must produce a **different** output, and a view that does not is ignoring the dependence structure ADR-005 exists to preserve. (b) A deliberately-injected duplicate term produces an `INV-V-01` `HALT`, not a number. (c) Composing stages out of order raises, verified for all 24 wrong permutations of the four substantive stages. (d) A `PwlTerm` declaring `Concave` over non-concave breakpoints fails `INV-V-12`. (e) `FillProbView` cannot be made to emit a priced term — verified by the type system, and by a test that asserts its output contains only `BoundTerm`. |
| **ADR-015 review** | OPEN-3: `FillProbView`'s bound is currently a scalar volume cap. Confirm the per-price-band refinement is still additive (C1 §5 `idVolumeByPriceBand` is optional and nullable) and has not become load-bearing. |

### W5 — Planner Tier 1 (joint MILP)

| | |
|---|---|
| **Goal** | The accuracy reference, and the first end-to-end optimiser. |
| **Deliverable** | The core model (L3 §2): SOC dynamics with split charge/discharge, POI bridge, market position accounting, the ADR-010 coupling block, two-stage structure with structural non-anticipativity; term ingestion from all five C2 shapes; `IOptimizationBackend` with at least two backends (one MILP, one LP-relaxation); feasibility restoration by `BoundReason` order; the `SolveBudget` fallback ladder; `PlanResult` recording; the DA parametric re-solve producing a monotone bid curve. |
| **Depends on** | W0, W2, W4. |
| **Definition of done** | (a) The metamorphic suite of L3 §9 passes: raise `afrrCapPrice` → planned reserve MW non-decreasing; shift all prices up uniformly → planned discharge does not decrease; raise `peakPrice` → planned peak does not increase. (b) Commitment safety: injecting a confirmed award the current SOC cannot serve produces `HALT`, never a quiet under-delivery. (c) Feasibility under *every* scenario in the ensemble, not the mean. (d) Each rung of the budget-exhaustion ladder is reachable under a forced timeout and the correct rung is taken and recorded. (e) Two backends agree on objective value within tolerance on the same model (T1). (f) `binariesByOrigin` matches the binaries the model builder actually created, exactly. |
| **ADR-015 review** | **OPEN-1 becomes decidable here** — ADR-015 states it is blocked by L3 reaching a fixed formulation. On W5 completion the variable count, binary count by source, `General`-curvature terms, simultaneity-binary status and scenario count are all known. Benchmark, then choose. Also check the Gurobi seat count question against *backtest* throughput, since parallel backtests each need a seat. |

> **Why Tier 1 before Tiers 2 and 3?** Because Tier 1 is the oracle, and without
> an oracle there is no gap, and without a gap there is no engineering, only hope
> (ADR-010). Tiers 2 and 3 are approximations, and an approximation whose error
> cannot be measured is indistinguishable from a bug. Building Tier 3 first would
> be building the fast path before the definition of "correct" exists — and Tier
> 3 is precisely the tier most likely to be *plausibly* wrong, because a learned
> surface fails smoothly and quietly. Tier 1 also produces the `optimalityGap`
> bucket in C5 §6, so the value of the whole ladder is unmeasurable until it
> exists. Tier 1 may never run in production; that is an accepted cost.

### W6 — Quoting policy and execution adapter

| | |
|---|---|
| **Goal** | Turn target positions into orders, without letting the fill process into the solver. |
| **Deliverable** | `IQuotingPolicy` mapping `(target, shadowValue, urgency, fill curve, microstructure) → limit order ladder`; the fill-probability artefact and its calibration against the existing simulator; `IExecutionAdapter` consuming `ExecutionIntentProjection`; cancel/replace semantics. |
| **Depends on** | W5 (`shadowValue` must be extracted from a real solve, not approximated). |
| **Definition of done** | (a) `INV-P-10` holds on a full replay: no intent priced through the shadow value except those tagged `CommitmentCover`. (b) `INV-X-04` is structural: a compile test asserts `IExecutionAdapter`'s parameter type has no `ShadowValue` or `Urgency` member, at any depth. (c) T4 calibration: realised fill rate matches predicted fill rate by price band, product and time-to-gate, within a declared tolerance; systematic divergence is reported as a calibration failure, not absorbed. (d) The execution-slippage bucket in C5 §6 becomes non-zero and reconciles with intent-versus-executed price on a recorded week. |
| **ADR-015 review** | **OPEN-3 becomes decidable here** — it is blocked on characterising the simulator, which is what (c) does. Take the decision, and record the simulator-bias caveat as an open risk (see §5, R3) rather than closing it. |

### W7 — Slow loop and value function

| | |
|---|---|
| **Goal** | Close the horizon properly rather than with an end-of-horizon SOC target. |
| **Deliverable** | `ISlowLoop`: rolling-horizon or ADP fit of `V(SOC, peakState, qualState)` over the joint ensemble; concave-hull projection; conditioning on the discrete qualification state; staleness and `stateDriftSignal` handling; `ITariffRegime` for `IntensiveUse`. |
| **Depends on** | W3 (state), W4 (V is emitted through `OppCostView`), W5 (V is scored by realised P&L through the Planner). |
| **Definition of done** | (a) `INV-V-11` holds by construction: slopes strictly decreasing after hull projection, on every fitted curve in a year of fits. (b) A/B on a recorded quarter: the engine with fitted `V` beats the engine with a fixed terminal-SOC target on realised P&L, and the difference is attributable through C5 §6 rather than asserted. (c) The qualification cliff is priced: a synthetic scenario in which a December spread trade would drop full-load hours below threshold results in the trade being declined, and the decline is explained by `V`'s conditioning rather than by a hard guard. (d) Staleness behaves: `V` past `validityHorizon` triggers the shrink, the `DEFENSIVE` transition and tier escalation, verified as a chain. |
| **ADR-015 review** | OPEN-2: the slow loop's cost is not in the tick budget, but its *staleness* interacts with the escalation policy. Confirm the budget decision is still unblocked. |

### W8 — Tier 2 Lagrangian decomposition

| | |
|---|---|
| **Goal** | Price the headroom, and get a certified bound while doing it. |
| **Deliverable** | Relaxation of the coupling block with multipliers `μ_up[b]`, `μ_dn[b]`; the fine-grained spot subproblem charged `μ·(headroom consumed)`; the coarse near-analytic reserve subproblem; a bundle method with step-size rule, iteration cap and time cap from `SolveBudget`; guaranteed feasible primal recovery on **every** exit path. |
| **Depends on** | W5. |
| **Definition of done** | (a) On every instance in a holdout set, Tier 2's dual bound dominates Tier 1's objective — a violation is a formulation error, not a tuning issue. (b) Every exit path, including cap-exhausted and non-converged, returns a feasible primal; verified by forcing each exit path. (c) The T5 gap report exists: p50/p90/p99 and worst case, conditioned on regime (high-spread days, peak-critical days, high reserve price days). (d) `μ*` is inspected by a human on ten sampled days and is defensible as a price of headroom — it is an interpretable number and its interpretability is part of its value. |
| **ADR-015 review** | OPEN-2: Tier 2's solve-time distribution is now measurable and is the main input to whether Tier 2 or Tier 3 is the production path. |

### W9 — Tier 3 learned surface and gap instrumentation

| | |
|---|---|
| **Goal** | The fast path, with a number attached to what it costs. |
| **Deliverable** | Offline fit of `μ̂ = f(features)` on Tier 1 / Tier 2 solutions over a long history; export to a form C# evaluates natively (PWL table, tree ensemble, small tensor); the runtime path — evaluate `μ̂`, price the headroom, solve one spot MILP; the escalation policy of L3 §3; out-of-distribution detection against the training envelope; the full T5 gap report including the distance from Tier 1 to a perfect-foresight upper bound. |
| **Depends on** | W5, W8. |
| **Definition of done** | (a) The T5 report is complete and published per release: gap distribution as p50/p90/p99 and **worst case, never a mean**; conditioned on regime; and the Tier-1-to-perfect-foresight distance, which is what separates "our optimiser is weak" from "the world is uncertain". (b) Escalation fires on every one of its four declared triggers, verified individually, and the escalation appears in the settlement record. (c) `μ̂` is a content-hashed artefact in the manifest; a model version change produces a different manifest and therefore a different, non-comparable run. (d) No live ML inference in the tick: a test asserts the tick path loads no runtime model framework. |
| **ADR-015 review** | **OPEN-2 must be taken by now** or Tier 3 has no acceptance criterion — "fast enough" requires a budget. If the budget is still unset, W9 ships as a measured artefact, not as the production path. |

---

## 3. The first vertical slice

Do not wait for W4–W9. As soon as W0–W3 are done and W4/W5 have their thinnest
possible implementation, drive one path end to end.

### In scope

| Dimension | Slice |
|---|---|
| Market | **Day-ahead only.** One gate per day. |
| Levers | **Arbitrage + peak shaving.** `SpotView`, `TariffView`, `PeakView`, and `OppCostView` with a *fixed*, hand-specified concave `V` — not a fitted one. |
| Tariff regime | **One.** `AnnualLeistungspreis`. |
| Uncertainty | The joint ensemble, at a small `S`. Real ensemble machinery, small size. |
| Planner | Tier 1 only, no reserve variables, no coupling block. |
| Execution | The existing simulator, DA only. |
| Settlement | Full: peak accounting, P&L by effect, and the `forecastError` / `modelError` / `executionSlippage` buckets. |
| Determinism | Full: manifest, seam recording, golden replay of **one recorded week**, lookahead audit. |

### Out of scope

aFRR (capacity and energy), continuous intraday, the quoting policy, imbalance,
HLZF and `IntensiveUse` regimes, the fitted value function, the slow loop,
Tiers 2 and 3, degradation modes beyond `NORMAL`/`HALT`, and the `optimalityGap`
bucket (which is identically zero when only Tier 1 exists — correctly so).

### Why DA-only is the right first market

Not because it is the largest revenue line, but because **DA needs no quoting
policy**. A DA submission is a monotone bid curve produced by the Planner's own
parametric re-solve (ADR-012, L3 §5); the price-quantity schedule *is* the
output. Every other market requires the target-position → limit-order mapping,
which requires `shadowValue` extraction, a fill model and a calibration run. DA
lets the slice cross C3 with the Planner's own artefact and defers an entire
workstream.

### What the slice proves

1. **The seams hold under a real payload.** Five contracts, validated on both
   sides, round-tripping and hashing, with a real week of data behind them.
2. **The loop closes.** Settlement's realised peak becomes next tick's
   `EpigraphTerm.floor` through L0, at a one-tick lag, and `INV-S-01` holds
   across a month boundary. This is the single hardest structural property in the
   system (ADR-006) and it is proven by the smallest possible instance.
3. **Determinism is real.** The recorded week replays byte-identically, and the
   lookahead audit passes on a pipeline that actually computes something.
4. **The ownership matrix works.** Four views, four effects, zero double counts —
   and `unexplained` in C5 §5 is near zero, which is the empirical statement that
   Valuation and Settlement agree on what the terms mean.
5. **The proration is right.** `prorationFactor` on a one-week horizon inside an
   annual accounting period is the most likely place for the engine to become
   pathologically peak-averse (L2 §2), and the slice surfaces it immediately
   against a real bill.

What the slice does **not** prove: anything about cross-market co-optimisation,
tier gaps, or the value function. Those are W7–W9 and they are supposed to be
unproven at this point.

---

## 4. Milestone gates

Each row must be demonstrably true — a passing, named test or a published report
— before the workstream is done and the next begins.

| Gate | Workstream complete | Must be demonstrably true |
|---|---|---|
| **G0** | W0 | Field-table-to-DTO correspondence test passes for all six contracts; serialisation round-trip and hash stability across machines; every invariant has a passing and a failing test with the correct `ViolationAction`; bare-`double`-in-contract analyzer rule fires. |
| **G1** | W1 | No read API without `asOf` exists (enforced, not reviewed); three-revision `asOf` test passes; 12-month replay memory within 5% of 1-month; DST fixtures for both transitions in three consecutive years; `INV-D-05` fails on a permuted axis. |
| **G2** | W2 | Byte-identical replay on two machines in CI; T6 lookahead audit passes; T3 acyclicity audit passes; Merkle diff localises a perturbed input in one step. |
| **G3** | W3 | Recorded week reconciles to an independently-prepared bill within `1e-6 EUR`; `INV-S-01` across a month boundary and both DST transitions; `INV-S-06` over a synthetic year; `unexplained = 0` on a synthetic outcome; bit-identical restart from `StateSnapshot`. |
| **G3.5** | **First vertical slice** | One recorded week runs L1→L5 and back to L0; replays byte-identically; realised peak from tick *n* is the epigraph floor at tick *n+1*; `unexplained` below threshold; three of four error buckets populated and reconciling. |
| **G4** | W4 | All seven L2 §7 properties per view, ensemble coherence asserted negatively; injected duplicate term halts; all 24 wrong stage permutations raise; mis-declared curvature halts; `FillProbView` proven priceless. |
| **G5** | W5 | Full metamorphic suite passes; commitment-safety `HALT` verified; feasibility under every scenario; all four budget-exhaustion rungs reachable and recorded; two backends agree within tolerance; declared binaries equal built binaries. **OPEN-1 taken.** |
| **G6** | W6 | `INV-P-10` clean over a replay; `INV-X-04` proven structurally by a compile test; T4 fill-rate calibration within tolerance by price band; slippage bucket reconciles. **OPEN-3 taken.** |
| **G7** | W7 | Every fitted curve concave by construction; fitted `V` beats fixed terminal SOC on a recorded quarter with attributable difference; qualification cliff declined a profitable-looking trade for the right reason; staleness chain fires end to end. |
| **G8** | W8 | Dual bound dominates Tier 1 objective on every holdout instance; every exit path returns a feasible primal; T5 gap report published with regime conditioning; `μ*` reviewed and defensible on ten sampled days. |
| **G9** | W9 | T5 report complete including perfect-foresight distance and worst case; all four escalation triggers fire individually and are recorded; `μ̂` in the manifest; no runtime ML framework loaded in the tick. **OPEN-2 taken, or Tier 3 does not ship as the production path.** |

---

## 5. Risk register

| ID | Risk | Why it is real here | Mitigation | Leading indicator |
|---|---|---|---|---|
| **R1** | **Solve time exceeds the budget** once it is set (OPEN-2). | The joint model is scenarios × slots × binaries, and the budget is unknown until intraday participation is decided — periodic versus event-driven imply budgets two orders of magnitude apart. | The ADR-010 tier ladder exists for exactly this: the production tier is a configuration choice made late, with data. `problemClassHint.binariesByOrigin` makes any regression attributable to a specific economic modelling choice rather than mysterious. Breakpoint count and scenario count are tuning knobs with measured accuracy/speed curves (T5). | Solve-time distribution per tier per gate type, tracked from W2 onward, before the budget exists. |
| **R2** | **Scenario model quality, not the optimiser, is the binding constraint on value.** | The engine can only be as good as the joint distribution it is handed. A perfect optimiser over a poor ensemble loses to a mediocre optimiser over a good one, and the whole cross-market case rests on *correlation* — activation with imbalance with intraday with residual load (ADR-005). | The C5 §6 decomposition is the instrument: `forecastError` versus `optimalityGap` says directly which one is binding. The T5 perfect-foresight upper bound separates "our optimiser is weak" from "the world is uncertain". Resource allocation follows the buckets, not intuition. | `forecastError` persistently exceeding `optimalityGap` by a wide margin. If it does, stop tuning the solver. |
| **R3** | **Simulator-calibration bias** (ADR-015 OPEN-3). | The fill model is calibrated against the existing simulator, so it inherits the simulator's biases. That is correct while the simulator is the venue, and quietly wrong the moment a real venue is connected. | Recorded now, as ADR-015 requires, rather than discovered later. Concretely: keep the fill model a separate versioned artefact (never inlined into the quoting policy), keep the calibration procedure runnable against any outcome stream, and treat simulator-versus-venue divergence as a named, measured quantity at cutover rather than a surprise. | T4 divergence between realised and predicted fill rate drifting after any simulator change — which is why T4 re-runs whenever the simulator changes, not only per release. |
| **R4** | **Value function fitting is harder than expected.** | `V` must be concave in SOC, conditioned on a discrete qualification state, fitted over a long horizon on a joint ensemble, and refreshed on drift. The §19(2) cliff makes the underlying value genuinely non-concave, and the response — a separate discrete state dimension — multiplies the fitting problem by the state count. It is also the piece with the most headroom, so it will attract the most churn. | `C_slow` output is an input, never a dependency: the Planner runs on the last valid `V` with a staleness penalty and never blocks. Concavity is enforced by hull projection, so a poor fit is *conservative*, not invalid. The fixed hand-specified `V` in the first vertical slice is a permanent fallback, not scaffolding. `V` is a versioned artefact, so a bad version is a one-line manifest revert. | `stateDriftSignal` persistently high; the fitted-`V` A/B in G7 failing to beat the fixed target. |
| **R5** | **Contract churn under parallel work.** | Nine workstreams, one frozen surface. A single unversioned field addition made on a branch propagates as silent disagreement between layers. | The change discipline (root `README.md`): doc + version + conformance tests in one commit, restated in the engine repo's `CLAUDE.md`. Surface frozen and committed at W0 before any parallel worktree opens. `unexplained` in C5 §5 is the empirical detector: it rises when a term definition drifts between Valuation and Settlement. | `unexplainedRatio` trending up with no change in data quality. |
| **R6** | **Determinism erodes.** | Solver thread counts, hash-order iteration, parallel float reduction order, an innocent `DateTime.UtcNow` in a log line that reaches a hash. Erosion is silent until a golden test fails for a reason nobody can localise. | Built at W2 rather than retrofitted; analyzer rule on wall-clock reads (`INV-G-05`); ordered keys everywhere; fixed reduction partitioning; production runs with more threads are recorded as a *different manifest* rather than pretended to be the same run. | Any non-deterministic CI replay, treated as a build break rather than a flake. A "flaky" determinism test is a real defect with a wrong label. |
