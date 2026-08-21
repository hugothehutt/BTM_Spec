# Vague claims — the register entries that are not testable as written

104 of 3054 claims carry `vague: true`. The statement is
transcribed as the specification asserts it; the flag records that no test could
be written against it without first supplying a threshold, a metric, a bound or
a definition. This list is a finding about the specification, not about the
register.

## very-expensive (57)

- **CLM-0038** — 00-overview/01-system-model.md §4 — analytic
  > Each seam exists because the two sides have different rates of change, different failure modes, or different testability.
- **CLM-0042** — 00-overview/01-system-model.md §4 — analytic
  > C2 is the seam that carries the most design load.
- **CLM-0094** — 00-overview/02-conventions.md §3 — empirical
  > Omitting the simultaneity binary by default gives LP-speed in the common case and correctness detection in the rare one.
- **CLM-0333** — 01-adr/ADR-004-bitemporal-belief-store.md §Consequences — analytic
  > Storage cost is higher than a last-value-wins store, and this is the correct trade.
- **CLM-0334** — 01-adr/ADR-004-bitemporal-belief-store.md §Consequences — empirical
  > The ingestion layer is more work than it looks and must be built before anything above it.
- **CLM-0296** — 01-adr/ADR-004-bitemporal-belief-store.md §Context — empirical
  > Lookahead leakage produces excellent backtests and losing live performance.
- **CLM-0297** — 01-adr/ADR-004-bitemporal-belief-store.md §Context — empirical
  > Lookahead leakage is usually introduced by an innocuous convenience: a silently revised forecast series, a settlement value stamped with the delivery time rather than the publication time, or a latest-available lookup with no as_of.
- **CLM-0316** — 01-adr/ADR-004-bitemporal-belief-store.md §Decision — empirical
  > The warm tier is memory-mapped so the OS page cache does the eviction work, and sequential replay is exactly the access pattern page caching is good at.
- **CLM-0338** — 01-adr/ADR-004-bitemporal-belief-store.md §Rejected — empirical
  > A database in the hot path introduces query latency and non-determinism, while a memory-mapped columnar frame is orders of magnitude faster and exactly reproducible.
- **CLM-0366** — 01-adr/ADR-005-joint-scenario-ensemble.md §Consequences — analytic
  > Re-integrating single-series improvements into the joint model is a real workflow cost and is the price of correctness.
- **CLM-0367** — 01-adr/ADR-005-joint-scenario-ensemble.md §Consequences — analytic
  > The reduced ensemble preserves the mean of each marginal within tolerance.
- **CLM-0342** — 01-adr/ADR-005-joint-scenario-ensemble.md §Context — empirical
  > aFRR activation is correlated with imbalance prices, which are correlated with intraday prices, which are correlated with the residual load that also drives the site's own load and PV.
- **CLM-0343** — 01-adr/ADR-005-joint-scenario-ensemble.md §Context — empirical
  > A high-price hour is precisely the hour where the peak charge bites, the aFRR is activated, and the battery is wanted in three places at once.
- **CLM-0370** — 01-adr/ADR-005-joint-scenario-ensemble.md §Rejected — empirical
  > Solve time scales with S, so reduction with weights gives most of the accuracy at a fraction of the cost, and the loss is measurable against a large-S reference run.
- **CLM-0458** — 01-adr/ADR-008-linearizable-primitives.md §Consequences — empirical
  > Breakpoint count is a tuning knob with a measurable accuracy/speed trade-off.
- **CLM-0452** — 01-adr/ADR-008-linearizable-primitives.md §Decision — empirical
  > Whether a PWL needs binaries — and therefore whether the solve takes 200 ms or 200 s — depends entirely on the declared curvature being true.
- **CLM-0752** — 01-adr/ADR-017-delineation-in-the-objective.md §Consequences — analytic
  > The linearizationGap is expected, budgeted and reported, and residual modelError is still asserted zero.
- **CLM-0762** — 01-adr/ADR-017-delineation-in-the-objective.md §Consequences — analytic
  > The negative resolution of self-consumption assumes retail supply is spot-indexed.
- **CLM-0945** — 02-layers/L0-state-value-store.md §6.3 — analytic
  > Beyond a configured maxGapSlots the peak floor is almost entirely modelled rather than measured, which escalates the degradation mode.
- **CLM-1047** — 02-layers/L1-belief.md §2.3 — empirical
  > Making the slot axis contiguous keeps the fixed-scenario scan at streaming bandwidth and leaves the cross-scenario reduction as a strided gather with a fixed, prefetchable stride.
- **CLM-1068** — 02-layers/L1-belief.md §3.2 — empirical
  > Evicting unmaps the frame while the OS page cache retains the pages, so re-mapping a recently evicted frame costs a syscall and no I/O.
- **CLM-1073** — 02-layers/L1-belief.md §3.3 — empirical
  > Sequential forward replay with prefetch on costs about zero frame maps per tick amortised and is bound by the memory bandwidth of the refill copy.
- **CLM-1074** — 02-layers/L1-belief.md §3.3 — empirical
  > Sequential forward replay with prefetch off costs zero frame maps except a burst at each day boundary, on the critical path.
- **CLM-1076** — 02-layers/L1-belief.md §3.3 — empirical
  > A random jump costs one cold frame map per series and is bound by storage IOPS.
- **CLM-1081** — 02-layers/L1-belief.md §3.4 — empirical
  > On a jump, throughput falls from sequential bandwidth to random-read IOPS: typically one to two orders of magnitude on spinning or networked storage and a factor of a few on local NVMe.
- **CLM-1122** — 02-layers/L1-belief.md §5.2 — analytic
  > Reliable volume is defined as the volume q such that the probability of filling q within the residual window is at least θ, where θ is configuration and deliberately conservative.
- **CLM-1198** — 02-layers/L1-belief.md §8.3 — empirical
  > Frame resolution and mapping cost amortised about zero on the sequential path.
- **CLM-1332** — 02-layers/L2-valuation.md §7 — empirical
  > Scale equivariance catches a surprising number of unit-mixing bugs in one cheap test.
- **CLM-1353** — 02-layers/L3-planner.md §1 — empirical
  > A large measured S1 delineation foresight cost is a direct argument for smaller reserve volumes late in an undecided month, and a small one retires the question.
- **CLM-1649** — 02-layers/L5-settlement.md §10 — analytic
  > Zero-gap: settling the planned trajectory as if realised yields all four error buckets zero within tolerance, and violation fails the run.
- **CLM-1552** — 02-layers/L5-settlement.md §4 — empirical
  > A rising unexplained ratio is the earliest available signal that a term definition has drifted between L2 and L5.
- **CLM-1613** — 02-layers/L5-settlement.md §6 — analytic
  > linearizationGap is expected, budgeted and reported rather than a defect.
- **CLM-1621** — 02-layers/L5-settlement.md §6 — analytic
  > The reconciliation invariant warns when the magnitude of unexplainedError exceeds tolerance.
- **CLM-1774** — 03-contracts/C1-belief-to-valuation.md §9 — analytic
  > Reduced-ensemble marginal means match the full ensemble within tolerance, and violation warns and sets DEGRADED.
- **CLM-1828** — 03-contracts/C2-valuation-to-planner.md §4 — analytic
  > stalenessPenalty is a shrink factor in [0,1] applied when the value function is stale.
- **CLM-1835** — 03-contracts/C2-valuation-to-planner.md §6 — analytic
  > cvarLevel is widened when quality degrades.
- **CLM-1839** — 03-contracts/C2-valuation-to-planner.md §6 — analytic
  > peakSafetyMarginKw is added to the epigraph floor under degraded load quality.
- **CLM-1922** — 03-contracts/C4-execution-to-settlement.md §7 — analytic
  > The energy balance poiImport − poiExport = load − pv + chargeEnergy − dischargeEnergy holds within meter tolerance, warning if provisional and halting if final.
- **CLM-1924** — 03-contracts/C4-execution-to-settlement.md §7 — analytic
  > socMeasured is consistent with charge and discharge energy and η within tolerance, and violation warns because drift indicates an η or SOH model error.
- **CLM-1926** — 03-contracts/C4-execution-to-settlement.md §7 — empirical
  > Persistent divergence between modelled and measured SOC is the earliest available signal that the efficiency or degradation model has drifted, and it is cheap to monitor.
- **CLM-1988** — 03-contracts/C5-settlement-to-state.md §5 — empirical
  > unexplainedRatio is alerted above a threshold.
- **CLM-1990** — 03-contracts/C5-settlement-to-state.md §5 — empirical
  > A rising unexplained ratio is the single best early warning that a term definition has drifted between Valuation and Settlement.
- **CLM-2021** — 03-contracts/C5-settlement-to-state.md §6 — analytic
  > The four error buckets plus unexplainedError sum to Ĵ₁ − R within tolerance.
- **CLM-2033** — 03-contracts/C5-settlement-to-state.md §9 — analytic
  > The four error buckets sum to planned minus realised within tolerance, and violation warns.
- **CLM-2036** — 03-contracts/C5-settlement-to-state.md §9 — analytic
  > fullLoadHours equals annualEnergyKwh divided by annualPeakKw within tolerance, and violation is a HALT.
- **CLM-2037** — 03-contracts/C5-settlement-to-state.md §9 — empirical
  > unexplainedRatio is below threshold, and violation warns, escalating to alert.
- **CLM-2233** — 04-compliance/T1-invariants.md §1.3 — analytic
  > The meter energy balance tolerance is the meter tolerance per meterQuality, declared per site.
- **CLM-2253** — 04-compliance/T1-invariants.md §3 — analytic
  > The reduced ensemble preserves each marginal's mean against the full ensemble within tolerance; checked offline at reduction-artefact build and re-checked at C1, at warn severity with the mode moving to DEGRADED.
- **CLM-2292** — 04-compliance/T1-invariants.md §6 — analytic
  > socMeasured is consistent with batteryChargeEnergy, batteryDischargeEnergy and the declared efficiencies within tolerance, and persistent divergence is the earliest available signal that the efficiency or SOH model has drifted and feeds the modelError bucket; checked in L5 per slot and as a rolling statistic at warn severity escalating to alert on persistent drift.
- **CLM-2296** — 04-compliance/T1-invariants.md §7 — analytic
  > The four error buckets — forecastError, modelError, optimalityGap and executionSlippage — sum to planned minus realised within tolerance, and the residual is unexplained; checked in L5 after the counterfactual re-runs at warn severity.
- **CLM-2300** — 04-compliance/T1-invariants.md §7 — empirical
  > unexplainedRatio is below the configured threshold, and a rising ratio is the single best early warning that a term definition has drifted between Valuation and Settlement; checked in L5 per settlement period at warn severity escalating to alert.
- **CLM-2507** — 04-compliance/T2-property-and-metamorphic-tests.md §4.7 — analytic
  > Refining the PWL breakpoint grid to a superset of existing breakpoints changes the objective by at most the declared approximation bound, monotonically toward the Tier-1 value.
- **CLM-2521** — 04-compliance/T2-property-and-metamorphic-tests.md §5.1 — analytic
  > The cumulative SOC drift over the settlement period is asserted against a period drift threshold at alert severity, and it is the rolling form that actually catches efficiency drift.
- **CLM-2619** — 04-compliance/T3-determinism-and-replay.md §3.2 — analytic
  > The full history golden scope is the whole backtest corpus, run nightly and pre-release within three hours.
- **CLM-2672** — 04-compliance/T3-determinism-and-replay.md §6.4 — empirical
  > The acyclicity audit is not run in CI because the instrumentation cost is roughly three to five times, and is not run in production at all.
- **CLM-2686** — 04-compliance/T3-determinism-and-replay.md §7.1 — analytic
  > One in N ordinary days is sampled deterministically by hash so the corpus is not composed only of exciting days.
- **CLM-2780** — 04-compliance/T4-calibration-and-execution-quality.md §3.2 — analytic
  > T4 asserts that the adverse-selection measurement and the executionSlippage bucket are consistent over the same period within tolerance.

## expensive-later (17)

- **CLM-0024** — 00-overview/01-system-model.md §2 — analytic
  > Publishing V(SOC) is strictly more accurate than marking a scalar, because it prices the whole SOC distribution rather than a linearisation around a guess.
- **CLM-0071** — 00-overview/02-conventions.md §1 — empirical
  > Typed sign frames cost a little ceremony and eliminate an entire bug class.
- **CLM-0277** — 01-adr/ADR-003-typed-quantities.md §Context — empirical
  > Energy optimisation code is dominated by two silent bug classes: sign-frame confusion and unit confusion.
- **CLM-0278** — 01-adr/ADR-003-typed-quantities.md §Context — empirical
  > Sign-frame and unit confusion both produce plausible numbers, neither throws, and both survive code review.
- **CLM-0418** — 01-adr/ADR-007-value-function-not-lambda.md §Consequences — empirical
  > The Planner's horizon can be shortened, a real solve-time saving, because the truncation is priced rather than ignored.
- **CLM-0420** — 01-adr/ADR-007-value-function-not-lambda.md §Consequences — empirical
  > A better V improves every decision downstream, so the slow loop is where research effort compounds.
- **CLM-0400** — 01-adr/ADR-007-value-function-not-lambda.md §Context — empirical
  > The mispricing from a scalar λ is quantitatively serious in a BTM setting.
- **CLM-0576** — 01-adr/ADR-011-tariff-regime-plugin.md §Consequences — regulatory
  > Regulation in the area of individual network charges is actively moving.
- **CLM-0572** — 01-adr/ADR-011-tariff-regime-plugin.md §Decision — empirical
  > Losing a year of network-charge reduction for a day's spread is the single largest downside event in the BTM business case and must not be reachable through an approximation error.
- **CLM-0883** — 02-layers/L0-state-value-store.md §5.1 — analytic
  > The state-drift trigger fires when stateDriftSignal is above a configured threshold and is a staleness trigger.
- **CLM-0893** — 02-layers/L0-state-value-store.md §5.2 — analytic
  > H_slow is at least the maximum over active regimes of (periodEnd − t) plus a tail margin.
- **CLM-0903** — 02-layers/L0-state-value-store.md §5.3 — analytic
  > If a fit is non-concave and the projection loses material value, that is a signal that a conditioning dimension is missing rather than that the projection needs loosening.
- **CLM-0919** — 02-layers/L0-state-value-store.md §5.4 — analytic
  > Backward recursion is tractable only with aggressive coarsening and its coarsening error is hard to bound.
- **CLM-0927** — 02-layers/L0-state-value-store.md §5.5 — analytic
  > The asymmetry of unbounded upside in fit quality against zero marginal runtime cost is rare, and it is why the slow loop deserves disproportionate research attention relative to its share of the codebase.
- **CLM-0928** — 02-layers/L0-state-value-store.md §5.5 — empirical
  > The measurement of V's quality is whether a better V produces better realised P&L, evaluated through C5's attribution rather than through the fit's own objective.
- **CLM-2028** — 03-contracts/C5-settlement-to-state.md §8 — analytic
  > stateDriftSignal measures how far the current state is from where V was fitted.
- **CLM-2087** — 03-contracts/C6-state-to-layers.md §5 — analytic
  > contextDrift is the distance from the state at which V was fitted.

## moderate (22)

- **CLM-0029** — 00-overview/01-system-model.md §3 — analytic
  > C_slow runs daily or on material state change, drives recomputation of V(SOC, peak_state, qualification_state) and recalibration of the fill and activation models, and is cheap to redo.
- **CLM-0067** — 00-overview/02-conventions.md §1 — empirical
  > The condition under which the q-free bridge is infeasible is an ordinary condition, not an edge case.
- **CLM-0500** — 01-adr/ADR-010-cross-market-tier-ladder.md §Context — empirical
  > Plugging everything into one solver and asking for the optimum is not doable at operational speed.
- **CLM-0504** — 01-adr/ADR-010-cross-market-tier-ladder.md §Context — empirical
  > Committed reserve turns into energy at random, correlated with exactly the prices that make the energy valuable elsewhere.
- **CLM-0505** — 01-adr/ADR-010-cross-market-tier-ladder.md §Context — analytic
  > The joint problem is hard at the scale of scenarios times slots times binaries.
- **CLM-0510** — 01-adr/ADR-010-cross-market-tier-ladder.md §Context — empirical
  > The dual price is the internal transfer price of a MW of battery headroom, and it is where the edge over less careful participants lives.
- **CLM-0547** — 01-adr/ADR-010-cross-market-tier-ladder.md §Rejected — empirical
  > Tier 1 only is correct and probably too slow, and until the latency budget is fixed this cannot be claimed otherwise.
- **CLM-0597** — 01-adr/ADR-012-order-intent.md §Decision — empirical
  > T4 includes a quoting-policy backtest that measures realised fill rate against predicted fill rate by price band, and systematic divergence is a calibration failure that is reported rather than absorbed.
- **CLM-0655** — 01-adr/ADR-014-degradation-ladder.md §Decision — analytic
  > Mode transitions are hysteretic: entering DEFENSIVE requires N consecutive bad ticks and leaving it requires M consecutive good ones, with M greater than N.
- **CLM-0688** — 01-adr/ADR-015-open-decisions.md §OPEN-2 — Per-tick latency budget — empirical
  > Periodic re-optimisation and event-driven response imply latency budgets two orders of magnitude apart.
- **CLM-0698** — 01-adr/ADR-015-open-decisions.md §OPEN-3 — Intraday fill-model fidelity — empirical
  > When the engine is pointed at a real venue the fill model must be recalibrated, and the gap between simulator-calibrated and venue-calibrated behaviour becomes a named risk.
- **CLM-1411** — 02-layers/L3-planner.md §6 — analytic
  > On budget exhaustion the first fallback is the best incumbent if feasible and within an acceptable gap, with the gap recorded.
- **CLM-1462** — 02-layers/L4-execution-boundary.md §4 — empirical
  > The gap between simulator-calibrated and venue-calibrated behaviour is reported and never absorbed.
- **CLM-1886** — 03-contracts/C3-planner-to-execution.md §4 — analytic
  > H_near is short, typically the next few slots.
- **CLM-2143** — 04-compliance/T0-test-architecture.md §3 — empirical
  > T4's scope is the fill model, activation model and quoting policy against the simulator, run nightly within 2 hours, raising an alert and a calibration ticket and blocking a release if drift exceeds the published band, owned by the quant and calibration owner.
- **CLM-2162** — 04-compliance/T0-test-architecture.md §5.2 — empirical
  > Releasing requires a T4 calibration report showing realised-versus-predicted fill rate by price band, product and time-to-gate within the published tolerance band, with reliability diagrams reviewed.
- **CLM-2168** — 04-compliance/T0-test-architecture.md §5.3 — analytic
  > Going live requires a shadow period in which the engine runs against live data emitting intent that is recorded and not submitted, for a declared number of trading days.
- **CLM-2482** — 04-compliance/T2-property-and-metamorphic-tests.md §4.5 — analytic
  > Using rung 2 or 3 on N consecutive ticks escalates the mode and the escalation appears in the settlement record.
- **CLM-2684** — 04-compliance/T3-determinism-and-replay.md §7.1 — analytic
  > A day with a reserve activation above a threshold is a designated audit day, for commitment feasibility under stress.
- **CLM-2772** — 04-compliance/T4-calibration-and-execution-quality.md §3.2 — empirical
  > Adverse selection is the single most common way an execution policy that looks good on capture ratio loses money.
- **CLM-2787** — 04-compliance/T4-calibration-and-execution-quality.md §3.3 — analytic
  > Value destroyed above the configured budget over a period alerts and prompts review of the shadow value extraction itself, because a systematically wrong dual is a Planner defect surfacing as an execution symptom.
- **CLM-2835** — 04-compliance/T5-gap-and-performance.md §3.1 — empirical
  > The gap distribution of a heuristic is sharply right-skewed: near zero on the many days when nothing is binding and large on the few days when reserve, peak and spot compete for the same headroom.

## cheap (8)

- **CLM-0251** — 01-adr/ADR-001-language-and-solver-boundary.md §Consequences — analytic
  > A model too complex to export is too complex to be in the tick loop.
- **CLM-0253** — 01-adr/ADR-001-language-and-solver-boundary.md §Consequences — empirical
  > Backend conformance is a test level: the same model solved by two backends must agree on objective value within tolerance.
- **CLM-0255** — 01-adr/ADR-001-language-and-solver-boundary.md §Consequences — empirical
  > A year-long backtest with scenario ensembles is allocation-bound long before it is CPU-bound.
- **CLM-0242** — 01-adr/ADR-001-language-and-solver-boundary.md §Decision — empirical
  > The existing simulator is the anchor, and splitting the hot path across a process boundary would destroy backtest throughput.
- **CLM-0256** — 01-adr/ADR-001-language-and-solver-boundary.md §Rejected — empirical
  > Cross-process serialisation of scenario ensembles per tick dominates the solve time in backtest.
- **CLM-1149** — 02-layers/L1-belief.md §6.2 — empirical
  > The solve itself is milliseconds and is dominated by the solver, which is native code the GC never sees.
- **CLM-1150** — 02-layers/L1-belief.md §6.2 — empirical
  > The engine's own contribution is copies and reductions over a few hundred kB, so allocation rate and the resulting gen0 pauses are the throughput ceiling long before any arithmetic is.
- **CLM-1151** — 02-layers/L1-belief.md §6.2 — empirical
  > Backtest throughput is the binding constraint on how fast the strategy can be improved, so the allocation discipline is the difference between a research loop measured in hours and one measured in days.

