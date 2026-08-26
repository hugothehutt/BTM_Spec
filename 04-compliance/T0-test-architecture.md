# T0 — Test Architecture

Normative. Defines the seven test levels, what each one proves, when it runs,
who owns it, and which levels gate a merge, a release and a go-live.

---

## 1. The property everything rests on

`L1`, `L2`, `L3` and `L5` are **pure functions of their declared contract
inputs** (`00-overview/01-system-model.md` §1). Every seam payload is an
immutable, serialisable, content-addressed value (`C0` §1). Every run is
reproducible from a manifest (ADR-013).

Three consequences follow, and the whole of `04-compliance/` is built on them:

1. **Every seam can be recorded.** A run writes `C1`…`C5` payloads to disk. A
   recorded seam is a test fixture, not a log line.
2. **Every layer can be replayed in isolation.** Valuation runs against a
   recorded `C1` stream with no Belief store, no network, no clock. The Planner
   runs against a recorded `C2` stream with no Valuation present. Settlement runs
   against a recorded `C4` stream with no knowledge of what was intended.
3. **Every comparison is exact.** Two runs of the same manifest produce
   byte-identical artefacts, so "did this change behaviour?" is a hash
   comparison, not a judgement call.

Remove any one of these and the levels above `T1` degrade into smoke tests.
Specifically: hidden state in `L2` or `L3` breaks (2); a wall-clock read
(`INV-G-05`) or an unordered iteration breaks (3); a contract carrying an object
reference breaks (1). This is why those three are hard failures rather than
style rules.

```
recorded stream        replay target        what it proves
────────────────────────────────────────────────────────────────────
C1 stream          →   L2 alone         →   valuation is a function of belief
C2 stream          →   L3 alone         →   plan is a function of primitives
C3 + C4 streams    →   L5 alone         →   settlement is a function of outcome
C1…C5 + manifest   →   whole tick       →   golden hash, Merkle chain (T3)
```

---

## 2. The seven levels

| Level | Name | Question it answers |
|---|---|---|
| `T0` | Unit | Does this view / component compute what its own spec says, on its own? |
| `T1` | Contract conformance | Does what crosses this seam satisfy the contract — schema, units, ranges, no-NaN, version, and do two solver backends agree? |
| `T2` | Property & metamorphic | Does the layer obey the relations that must hold for *every* input, including inputs nobody wrote a fixture for? |
| `T3` | Determinism & replay | Is the run reproducible, is the artefact chain intact, and is the tick acyclic? |
| `T4` | Calibration & execution quality | Do the fitted models — fill, activation, quoting — match observed reality? |
| `T5` | Optimality gap & performance | How much value does the shipped tier give up against the oracle, and at what solve cost? |
| `T6` | Adversarial / lookahead audit | Can the engine be made to peek, to accept garbage, or to use an expired artefact? |

The levels are ordered by *what they need in place*, not by importance. `T2`
requires `T1` (a payload that fails schema cannot be property-tested
meaningfully). `T5` and `T6` both require `T3` (without determinism, a measured
difference proves nothing).

`T0` is also this document's number, because this file is the overview *and* the
specification of the unit level. §6 covers the unit level proper.

---

## 3. Level map: scope, budget, failure action, owner

| Level | Scope | Runtime budget | Failure action | Owner |
|---|---|---|---|---|
| `T0` | One view, one composition step, one calendar function, one term shape. No I/O, no solver. | ≤ 60 s for the whole suite | Block commit | Layer owner |
| `T1` | Each seam payload `C1`…`C5` and `StateSnapshot`; the `MarketCalendar` artefact; backend agreement on one model | ≤ 3 min | Block merge | Contracts owner |
| `T2` | Per layer (`L1`, `L2`, `L3`, `L5`) and per seam. Generated inputs, bounded shrinking. | ≤ 15 min in CI; extended corpus nightly | Block merge | Layer owner |
| `T3` | Whole tick, then whole run. Golden hashes over a canonical 7-day window in CI; full-history nightly. | ≤ 8 min CI; ≤ 3 h nightly | Block merge on unexplained hash change; nightly failure pages the on-call | Platform owner |
| `T4` | Fill model, activation model, quoting policy, against the simulator | Nightly, ≤ 2 h | Alert + calibration ticket; blocks release if drift exceeds the published band | Quant / calibration owner |
| `T5` | Tier 1 / Tier 2 / Tier 3 on a holdout backtest; scaling curves in `S` and in breakpoint count | Per release, overnight | Block release | Optimisation owner |
| `T6` | Whole run under adversarial mutation: poisoned future facts, poisoned config, fuzzed payloads, tampered ledger, expired artefacts | Nightly (reduced) + per release (full) | Block release; a lookahead divergence is a **stop-ship** | Risk / audit owner |

Budgets are targets for the CI configuration, which is the deterministic
single-threaded solver configuration (ADR-013). Production runs with more
threads and a different manifest; its throughput is not a test budget.

---

## 4. When each level runs

| Trigger | `T0` | `T1` | `T2` | `T3` | `T4` | `T5` | `T6` |
|---|---|---|---|---|---|---|---|
| Pre-commit hook | full | full | fast subset, fixed seed | 1-tick golden smoke | — | — | — |
| Pull request CI | full | full | full, fixed seed | 7-day golden window | — | — | — |
| Nightly | full | full | extended corpus, rotating seed | full history + acyclicity audit | full | scaling curves only | reduced |
| Pre-release | full | full | full | full history | full + drift review | full | full |
| Post-deploy canary | — | full (production validation is always on) | — | live hash of the production manifest | live drift monitors | live gap telemetry | — |

Two rules about seeds. The **pre-commit and PR runs use a fixed seed** so a
failure is reproducible by the author on the first attempt. The **nightly run
rotates the seed** and, on failure, writes the failing seed and the shrunk
counterexample into the corpus as a permanent regression fixture. A property
test that found a bug once becomes a unit test forever.

---

## 5. Gate rules

### 5.1 Before a merge

All of the following, no exceptions and no per-PR waivers:

- `T0` green.
- `T1` green for every seam. A contract change additionally requires the version
  bump, the layer-design update and the conformance-test update **in the same
  commit** (root `README.md`, change discipline).
- `T2` green.
- `T3` golden hashes over the canonical window either unchanged, or changed with
  a **declared, reviewed baseline update** naming the artefact that moved and the
  reason. An undeclared hash change is a merge blocker even when the new numbers
  look better.
- No new `INV-*` severity downgrade. Lowering an invariant from `HALT` to `warn`
  is an architecture change and needs an ADR, not a PR.

### 5.2 Before a release

Everything above, plus:

- `T4` calibration report attached: realised-vs-predicted fill rate by price
  band, product and time-to-gate, within the published tolerance band; reliability
  diagrams reviewed.
- `T5` gap report attached for **every tier that can run in production**. No tier
  ships without its gap measured against Tier 1 (ADR-010). The report carries
  p50 / p90 / p99 / worst, conditioned on regime, plus the Tier-1-to-perfect-
  foresight distance.
- `T5` scaling curves: objective and solve time versus scenario count `S` and
  versus PWL breakpoint count, so that the configured values are evidenced
  (ADR-005, ADR-008).
- `T6` full adversarial suite green, including the lookahead poisoning audit at
  bit-identity.
- Manifest pinned: code revision, config, `MarketCalendar` artefact, tzdata
  version, every model artefact, solver identity and determinism settings
  (ADR-013).

### 5.3 Before going live (first live venue, or a new market)

Everything above, plus:

- A **shadow period**: the engine runs against live data emitting intent that is
  recorded and not submitted, for a declared number of trading days. `T4`
  measures capture ratio and adverse selection over that period.
- `INV-S-04` (reserve delivery shortfall) exercised in the simulator under the
  worst activation scenario in the ensemble, asserting `HALT` rather than
  under-delivery.
- Degradation drill: force each of the five modes (ADR-014) and confirm the
  transition, the hysteresis, the logged cause, and that peak protection and
  commitment feasibility survive every mode.
- Operator runbook for `HALT` acknowledged, because `HALT` requires human
  acknowledgement to resume and an unrehearsed first `HALT` is an outage.
- Recalibration plan for the fill model, because a simulator-calibrated model
  inherits the simulator's biases (ADR-015 OPEN-3) and the venue gap is a named
  risk from day one.

### 5.4 Standing rules

- **Validation stays on in production.** `C0` §3: the universal invariants are
  checked by the shared validator on both sides of every seam, in every
  environment. The cost is microseconds against a solve measured in milliseconds.
  There is no `--fast` flag that disables it.
- **A skipped test is a failing test.** `[Skip]` requires a linked ticket and an
  expiry date; an expired skip fails the build.
- **Every `HALT`-severity invariant has at least one test that provokes it.** An
  invariant nobody has ever seen fire is an invariant nobody knows is wired up.

---

## 6. Level `T0` — unit tests

Scope: a single pure function, exercised through its own interface, with no
solver, no file system, no recorded seam.

| Target | What `T0` asserts |
|---|---|
| Each `IValuationView` | `Evaluate` output validates against the view's own `PublishedSchema`; `ClaimedEffects` matches what the terms actually carry; hand-computed values on a 4-slot fixture |
| Each composition step | Preconditions and postconditions of the stage table (ADR-009, `L2` §4) hold in isolation |
| Curvature checker | Concave, convex, general and pathological breakpoint sets classify correctly, including ties and a two-point curve |
| Epigraph builder | `z ≥ expr[t] ∀t`, `z ≥ floor`, correct `overSlots` subset, correct `prorationFactor` application |
| `CivilCalendar` | `SlotId ↔ (local date, local time, HLZF window)` on ordinary days and both DST days; 92 / 96 / 100-slot civil days |
| Typed quantities | Frame and unit conversions; that illegal conversions do not compile (compile-fail tests are part of the suite) |
| POI bridge | `p_poi = load − pv_out − p_batt` in isolation, both signs, with aux load present, and with `q > 0` so that `pv_out < pv_avail` — passing `pv_avail` where `pv_out` belongs is the defect this case exists to catch |
| SOC dynamics kernel | One-way efficiency on each direction, split charge/discharge, no free round trip |
| Term shape serialisers | Each of the five shapes round-trips to binary and to JSON |
| Fallback ladder | Each rung of ADR-014 §4 selected for the right input, and the stopping rung recorded |

`T0` deliberately does not test interactions. If a unit test needs two views, it
is a `T2` property test on the composer and belongs there.

---

## 7. The fixture corpus

Three kinds of input, all versioned and content-addressed like any other
artefact (ADR-004 §4):

| Kind | Source | Used by |
|---|---|---|
| **Hand-built micro-fixtures** | Written by hand, 4–16 slots, values computable on paper | `T0`, and as the minimal reproduction after `T2` shrinking |
| **Recorded seams** | Captured from designated audit days of a real backtest (`T3` §7) | `T1`, `T2`, `T3` per-layer replay |
| **Generated inputs** | Property-test generators constrained to produce contract-valid payloads | `T2`, `T6` fuzzing |

Generators are written to satisfy the contract by construction — a generator
that emits `NaN` is testing `T1`, not `T2`, and the two must not be conflated.
Deliberately invalid payloads are a `T6` concern (contract fuzzing), where the
assertion is clean rejection rather than correct computation.

The **canonical window** used for CI golden hashes is a fixed 7-day period
chosen to contain: one DST transition, one negative-price episode, one
peak-critical day, one high aFRR clearing price block, and one day with a
degraded input feed. It is a versioned artefact and changing it is a reviewed
baseline update.

---

## 8. Reading order

| Document | Contents |
|---|---|
| `T1-invariants.md` | The consolidated invariant register — every `INV-*`, its severity, where it is checked, and which level tests it |
| `T2-property-and-metamorphic-tests.md` | The property and metamorphic suite, by layer and by seam |
| `T3-determinism-and-replay.md` | Golden replay, run manifest, Merkle chain, acyclicity audit, bisect workflow |
| `T4-calibration-and-execution-quality.md` | Fill model calibration, quoting policy evaluation, drift alerting |
| `T5-gap-and-performance.md` | Optimality gap instrumentation and scaling curves |
| `T6-adversarial-audit.md` | Lookahead poisoning, config poisoning, contract fuzzing, ledger tampering, stale artefacts |

---

## 9. Non-goals of this architecture

Stated so they do not creep in.

- **Not a test of the simulator.** `L4` is external and fixed. `T4` measures how
  well the engine's *model of* the simulator matches it; it does not assert that
  the simulator is realistic.
- **Not a forecasting benchmark.** Forecast skill is an offline concern. `T2`
  asserts that Belief serves what it was given, at the right knowledge time; it
  does not assert that the forecast was good. Forecast error is measured ex post,
  in `C5` §6's `forecastErrorEur` bucket.
- **Not a substitute for the gap.** No amount of property testing tells you how
  much money the Tier 3 heuristic costs. Only `T5` does.
- **Not a coverage target.** Line coverage is reported and not gated. The gate is
  the invariant register: every `HALT` invariant provoked by at least one test,
  every seam replayed, every tier's gap measured.
