# ADR-006 — Forward within a tick, lagged across ticks, via L0

**Status:** Accepted **Reversibility:** Very expensive

## Context

The five-layer pipeline is intended to be one-directional. Two dependencies
break that:

1. `λ_SOC` is a dual of the Planner's optimisation but was placed in Valuation.
2. `peak_to_go`, and the §19(2) qualification state, depend on realised history,
   which is a Settlement output.

There is also a third, easily missed: the **commitment ledger**. Orders filled at
the DA gate constrain the intraday solve; aFRR capacity awarded constrains the
SOC corridor for the whole delivery block. That is Execution output feeding a
later Planner run.

If these are handled ad hoc — a field passed sideways, a service both layers
happen to reference — the architecture's central claim (one-directional,
independently testable seams) becomes false, and nothing downstream of that claim
holds either.

## Decision

**Add L0, the State/Value Store. It is the only backwards path, and it is always
read at a lag of at least one tick.**

L0 holds exactly the state that must survive a tick:

| Item | Written by | Read by | Clock |
|---|---|---|---|
| Realised POI peak per accounting period and per tariff window | L5 | L2 (`PeakView`) | `C_accounting` |
| §19(2) qualification state: accumulated energy, full-load hours, HLZF exposure | L5 | L2 (`TariffView`) | `C_accounting` |
| Commitment ledger: awarded aFRR blocks, filled spot positions, open orders | L5 (confirmed) / L3 (pending) | L3 | `C_gate` |
| Value function `V(SOC, peakState, qualState)` | Slow loop | L2 → L3 | `C_slow` |
| Data quality state and degradation mode | L1 / L5 | all | `C_tick` |
| Model artefact versions in force | manifest | all | run |

**Within a tick, every arrow points right.** L0 is snapshotted at the top of the
tick into an immutable `StateSnapshot`, and written only at the end of the tick
from L5's output. No component reads a value written later in the same tick.

**Pending vs. confirmed.** The commitment ledger distinguishes *pending* entries
(the Planner submitted intent) from *confirmed* entries (Execution reported a
fill or award). The Planner treats pending commitments as probabilistic exposure,
confirmed ones as hard constraints. This is what makes the continuous intraday
loop safe: it can re-optimise around orders that may or may not fill without
either double-selling or double-counting.

## Consequences

- The dependency graph is a DAG within a tick and can be **verified
  mechanically**. `T3` includes an acyclicity audit: instrument each layer's
  reads and writes during a replay and assert no read of a same-tick write.
- The `StateSnapshot` is a contract (C5's mirror image) with a field table, a
  version and invariants like every other seam.
- Lag is explicit and therefore priceable. The Planner knows the peak state is
  as of the last settled interval, and can be conservative about the unsettled
  gap rather than pretending it is current.
- Restart and warm-start become straightforward: L0 is the entire state. An
  engine restart loads a `StateSnapshot` and continues. This also means L0 must
  be durably persisted with the same content-addressing as everything else
  (ADR-013).
- Cost: an extra component, and the discipline of routing everything through it
  even when a direct reference would be shorter.

## Rejected

- **Let Valuation call the Planner to get λ.** Circular, untestable, and the
  Planner's problem is not well posed without the valuation it would be
  computing.
- **Fixed-point iteration within a tick.** Convergence is not guaranteed, solve
  time becomes unbounded, and the result is not reproducible under a time limit.
- **Global mutable service.** Works until two things read it at different points
  in the tick and get different answers, at which point nothing is reproducible.
