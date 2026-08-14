# ADR-015 — Register of deliberately deferred decisions

**Status:** Open — reviewed at each workstream boundary

Three decisions are deferred on purpose. Each is recorded here with what it
depends on, what must be true before it can be taken, and what in this
specification would change. **Nothing in the current design depends on any of
them**; every document references the abstraction rather than the choice.

---

## OPEN-1 — MILP solver

**Intended:** Gurobi. **Deferred until:** the Planner formulation is concrete.

**What must be true first:** the exact MILP is specified — variable count,
binary count by source, which PWL terms are `General` curvature, whether the
simultaneity binary is enabled, and the scenario count from ADR-005 tuning.
Solver choice should follow from a benchmark on the real model, not precede it.

**Blocked by:** `02-layers/L3-planner.md` reaching a fixed formulation.

**What would change:** nothing structural. `IOptimizationBackend` (ADR-001)
exists precisely so this is a configuration decision. Backends must in any case
be conformance-tested against each other (`T1`), and the LP-relaxation backend is
needed regardless for the Tier-2 bound in ADR-010.

**Note:** if Gurobi is confirmed, license seat count becomes a *backtest
throughput* constraint, not a runtime one — parallel backtests each need a seat.
Worth checking early even though the choice is deferred.

---

## OPEN-2 — Per-tick latency budget

**Deferred until:** operational requirements per market are settled.

**What must be true first:** a decision on how the engine participates in
continuous intraday — periodic re-optimisation on a timer, or event-driven
response to book updates. These imply budgets two orders of magnitude apart.

**What would change:** which tier of ADR-010 is the production path, and the
escalation thresholds. Not the architecture — the ladder exists to make this
decision late and reversible.

**Instrumentation to build now, so the decision is evidenced:** solve time
distribution per tier, per gate type, as a function of scenario count and
breakpoint count (`T5`). When the budget is set, the tier choice will already
have data behind it.

---

## OPEN-3 — Intraday fill-model fidelity

**Deferred until:** the existing simulator's behaviour is characterised.

**Current position:** Execution is fixed and pre-existing. The engine's question
is only which limit orders to submit, so the fill model's job is to predict *the
simulator's* behaviour, and its accuracy target is defined by that — not by the
real market in the abstract. This is a narrower and much more tractable problem
than a general LOB model, and it is why the decision can wait.

**What must be true first:** a characterisation run against the simulator
measuring realised vs. predicted fill rate by price band, product and
time-to-gate (`T4`).

**What would change:** the representation of `FillProbView`'s `BoundTerm`
(a scalar volume cap versus a per-price-band cap), and how much of the cold tier
must hold raw order-book data. Not the C2 contract shape — `BoundTerm` covers
both.

**Standing caveat:** whatever is calibrated against the simulator inherits the
simulator's biases. When the engine is pointed at a real venue, the fill model
must be recalibrated and the gap between simulator-calibrated and
venue-calibrated behaviour becomes a named risk. Record this now so it is not
discovered later.

---

## Review protocol

At each workstream boundary (`05-implementation/P0`), this register is reviewed:
is the blocking condition met, is the decision still safely deferrable, has any
new design work accidentally created a dependency on it? A decision that has
quietly acquired dependents must be taken immediately or the dependency removed.
