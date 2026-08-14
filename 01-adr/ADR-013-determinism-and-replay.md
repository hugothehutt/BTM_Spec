# ADR-013 — Content-addressed determinism; every artefact is replayable

**Status:** Accepted **Reversibility:** Very expensive

## Context

The system's entire feedback loop — is the new valuation better? did the tier-3
heuristic cost us anything? did that refactor change behaviour? — depends on
being able to re-run and compare. A pipeline that produces slightly different
answers on re-run cannot be improved, only argued about.

Non-determinism creeps in from: solver thread counts and time limits, hash-order
iteration, floating-point reduction order under parallelism, wall-clock reads,
un-pinned model artefacts, un-pinned tzdata, and data revisions arriving between
runs.

## Decision

**Every artefact is content-addressed, and every run is reproducible from a
manifest.**

### Run manifest

A run is defined by a manifest containing the hash of: the code revision, the
configuration, the market calendar artefact, the tzdata version, every model
artefact (scenario generator, value function, fill model, tier-3 surface), the
data snapshot boundary (`asOf` range and the ingestion watermark), the solver
identity and version, and the solver determinism settings.

Two runs with the same manifest **must** produce byte-identical outputs. This is
tested (`T3`), not asserted.

### Determinism rules

- **Solver:** fixed seed, fixed thread count, fixed time limit expressed in
  *deterministic work units* where the backend supports it, never wall clock.
  Where a backend cannot be made deterministic under parallelism, the
  reproducibility run uses single-threaded mode and the production run records
  its non-determinism explicitly rather than pretending.
- **Wall clock:** no component reads the system clock. Time enters through the
  tick's `SlotId` and `asOf`, both supplied by the driver. A clock read in a
  layer is a defect (`INV-G-05`, enforced by an analyzer rule).
- **Randomness:** no runtime randomness. Scenario draws are artefacts, generated
  offline with a recorded seed.
- **Iteration order:** no iteration over unordered collections in any code path
  that affects output. Ordered keys everywhere.
- **Floating point:** parallel reductions use a fixed partitioning scheme so
  summation order is stable.

### Snapshot addressing

`BeliefSnapshot`, `ValuationBundle`, `PlanResult`, `StateSnapshot` and
`SettlementResult` each carry a content hash of their own contents plus the
hashes of their inputs. This gives a Merkle chain over the whole tick: if a
downstream artefact differs between runs, the chain localises which upstream
input diverged, which turns "the backtest changed" from a day of bisecting into a
single comparison.

### Seam recording

Because every contract is a serialisable value type (ADR-008, C0), each seam can
be recorded to disk during a run. A recorded seam is a test fixture: Valuation
can be replayed against a recorded C1 stream with no Belief store present, the
Planner against a recorded C2 stream with no Valuation present. This is what
makes per-layer golden tests possible and it is the reason the contracts carry no
object references.

## Consequences

- Regression testing is a hash comparison. Refactors are safe to attempt because
  they are cheap to verify.
- Any behavioural change must be *intentional* and shows up as a diff in a named
  artefact, with the Merkle chain pointing at the cause.
- The lookahead audit (`T6`) is only meaningful because runs are deterministic —
  otherwise "the result changed" proves nothing.
- Storage: recorded seams for a long backtest are large. Policy is to record
  seams on demand for designated audit days, and to record only hashes for bulk
  runs.
- Cost: real discipline, particularly around solver settings and parallelism.
  Accept a measured throughput loss in the reproducibility configuration; run
  production with more threads and record that it is a different manifest.

## Rejected

- **Approximate reproducibility** ("close enough"). Removes the ability to detect
  small regressions, which are the ones that matter.
- **Timestamps as identifiers.** Not content-addressed; two different runs at the
  same time are indistinguishable, and the same run twice is distinguishable.
  Exactly backwards.
