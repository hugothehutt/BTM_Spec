# ADR-001 — C# for the engine; the solver sits behind an abstraction

**Status:** Accepted **Reversibility:** Cheap for the solver, moot for the language

## Context

A market simulation engine already exists in C#. The BTM engine must be built
around it. The question is not really "which language" — it is where the
non-C# pressure points are and how to keep them from leaking.

Three workloads have different needs:

1. **Engine** (Belief, Valuation, Planner orchestration, Settlement) — needs
   throughput, low allocation pressure, strong typing, and to sit next to the
   existing simulator.
2. **Optimisation** — needs a MILP solver. Gurobi is the intended choice but is
   deliberately not fixed yet (ADR-015).
3. **Model fitting** — forecast models, fill-probability calibration, value
   function approximation, scenario generation. This is research work, iterated
   far more often than the engine, and the tooling for it is not C#.

## Decision

**C# (.NET, current LTS) for everything in the runtime pipeline.** No polyglot
runtime. The existing simulator is the anchor and splitting the hot path across
a process boundary would destroy backtest throughput.

**The solver is behind `IOptimizationBackend`.** The Planner constructs a
solver-independent model (`MilpModel`: variables, linear terms, PWL segments,
epigraph constraints, indicator constraints, SOS2 sets) and the backend
translates. Gurobi, HiGHS and a pure-LP relaxation backend are all expressible.

**Model fitting is offline and out of process.** Whatever language produces the
best models produces them; the engine consumes **artefacts**, never a live
model. An artefact is a versioned, content-hashed file (coefficients,
breakpoints, lookup surfaces) with a declared schema, loaded through the Belief
cold tier like any other data. If a model needs to be evaluated at runtime, it is
either exported to a form C# can evaluate natively (PWL table, tree ensemble,
small tensor) or it does not go in the hot path.

## Consequences

- Fitting a model and using a model are cleanly separated, which is also what
  makes model versions replayable in backtest (ADR-013).
- No ONNX/Python interop in the tick loop. If a model is too complex to export,
  that is a signal it is too complex to be in the tick loop.
- `IOptimizationBackend` must be designed against the *union* of what the tiers
  in ADR-010 need, so switching backends does not silently change the
  formulation. Backend conformance is a test level (`T1`): the same model solved
  by two backends must agree on objective value within tolerance.
- Hot-path C# discipline is mandatory and specified in `02-layers/L1-belief.md`
  §6: struct-of-arrays, `Span<T>` / `ReadOnlySpan<T>`, `ArrayPool<T>`, no LINQ
  in the tick loop, no per-slot object allocation. A year-long backtest with
  scenario ensembles is allocation-bound long before it is CPU-bound.

## Rejected

- **Python for the Planner, C# for the rest.** Cross-process serialisation of
  scenario ensembles per tick dominates the solve time in backtest.
- **Solver called directly.** Ties the formulation to one vendor's API idioms and
  makes it impossible to run the LP-relaxation oracle used for gap measurement.
- **Live ML inference in the tick.** Non-deterministic across library versions,
  unreplayable, and a source of latency variance nobody can attribute.
