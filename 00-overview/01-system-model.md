# 01 — System Model

Normative. Defines the components, the direction of information flow, the
resolution of the two feedback edges, and the clocks that drive the engine.

---

## 1. Components

Six components. Five are the pipeline; the sixth exists to make the pipeline
acyclic.

| ID | Component | Role | Purity |
|---|---|---|---|
| L0 | **State/Value Store** | Everything that survives a tick: realised peak, tariff qualification state, commitment ledger, V(SOC) | Stateful by definition |
| L1 | **Belief** | Single source of truth about the world as known at a point in time | Pure read over immutable data |
| L2 | **Valuation** | Beliefs → linearizable economic primitives | Pure function |
| L3 | **Planner** | Primitives + commitments → order intent | Pure function (given a solver seed) |
| L4 | **Execution** | Order intent → market outcomes | External system (existing simulator) |
| L5 | **Settlement** | Outcomes → ex-post truth, P&L attribution, error decomposition | Pure function |

Purity is not an aesthetic preference. L1, L2, L3 and L5 being pure functions of
their declared inputs is what makes every seam independently recordable,
replayable and property-testable. It is the foundation of the entire compliance
architecture in `04-compliance/`. Any hidden state in L2 or L3 destroys it.

---

## 2. The flow, and the cycle you have to remove

The intended flow is one-directional:

```
L1 Belief → L2 Valuation → L3 Planner → L4 Execution → L5 Settlement
```

As drawn, this is **not acyclic**, because of two dependencies:

1. **λ_SOC** — the shadow price of state of charge — was placed in Valuation
   (`OppCost`). But λ_SOC is a *dual variable of the Planner's own optimisation*.
   Valuation cannot compute it without solving the Planner's problem.
2. **peak_to_go** — `PeakView` prices the marginal MW of grid peak, which
   requires the peak *already realised* in the current accounting period. That
   is a Settlement output. Likewise the §19(2) qualification state (accumulated
   full-load hours, HLZF exposure) is realised history.

Both are feedback edges from downstream to upstream. Left implicit, they become
the kind of hidden coupling that produces a system nobody can reason about or
test.

### Resolution

**Strictly forward within a tick; all feedback is lagged and passes through L0.**

```
                 ┌──────────────────────── L0 State/Value Store ────────────────────────┐
                 │  realised peak · qualification state · commitments · V(SOC) · quality │
                 └───┬──────────────────────────────────────────────────────────▲───────┘
        read (t-1)   │                                                          │  write (t)
                     ▼                                                          │
  L1 Belief ──C1──▶ L2 Valuation ──C2──▶ L3 Planner ──C3──▶ L4 Execution ──C4──▶ L5 Settlement
                                                                                     │
                                                                                     └──C5──▶ L0
```

Within a single tick, every arrow points right. L0 is read at the top of the
tick and written at the end. No component reads a value written later in the
same tick. This is checkable — see `04-compliance/T3` (acyclicity audit).

The consequence for λ_SOC is significant enough to be its own decision:
see **ADR-007**. In short, L2 does not publish a scalar λ_SOC. It publishes the
**value function V(SOC)** as a concave piecewise-linear curve, produced by the
slow loop and read from L0. The Planner then obtains λ_SOC *endogenously* as the
subgradient of V at the optimal terminal SOC. This removes the circularity and
is strictly more accurate than marking a scalar, because it prices the whole SOC
distribution rather than a linearisation around a guess.

---

## 3. The four clocks

The engine is not driven by one loop. Conflating these is a common and expensive
mistake, because each has a different information set and a different
irreversibility.

| Clock | Period | Drives | Irreversibility |
|---|---|---|---|
| **C_accounting** | Month / year boundary | Peak accounting reset, §19(2) qualification evaluation | Absolute — a lost qualification is lost for the period |
| **C_slow** | Daily, or on material state change | Recompute V(SOC, peak_state, qualification_state); recalibrate fill and activation models | Cheap to redo |
| **C_gate** | Market-defined (DA gate, aFRR auction gates, ID gate closures) | Binding submissions | Binding at gate — becomes a commitment |
| **C_tick** | Event-driven / sub-gate | Continuous intraday re-optimisation, dispatch | Reversible until the ID gate |

Rules:

- **Gate times are configuration, never code.** They live in a `MarketCalendar`
  loaded from data, versioned, and replayed with the backtest. Market rules
  change; a rule change must be a data change.
- **Every solve is stamped with the clock that triggered it** and the clock stamp
  is part of the content hash of the resulting plan. Two solves at the same wall
  time from different clocks are different artefacts.
- **C_slow output is an input, never a dependency.** If the slow loop has not run,
  the Planner uses the last valid V(SOC) with a staleness penalty (see the
  degradation ladder, ADR-014). It never blocks on it.

---

## 4. Why the split at each seam

Each seam exists because the two sides have different rates of change, different
failure modes, or different testability. Stating this explicitly prevents the
seams from eroding.

**C1 Belief → Valuation.** Belief changes when data sources change; Valuation
changes when economics change. Crossing this seam with raw arrays would let a
data-source change silently alter a price. The contract carries values and
quality metadata, never handles into Belief storage (ADR-004).

**C2 Valuation → Planner.** This is the seam that carries the most design load.
Valuation knows economics but not feasibility; the Planner knows feasibility but
must not re-derive economics. The contract carries *linearizable primitives*
(ADR-008) so that Valuation can express non-linear economics without the Planner
needing to know what a bid curve is.

**C3 Planner → Execution.** Execution is a pre-existing, strategy-independent
simulator. The Planner emits **intent** — what orders, at what price and volume,
with what validity — and nothing about how they are routed. This keeps the
Planner testable without a market and keeps the simulator replaceable by a real
venue adapter (ADR-012).

**C4 Execution → Settlement.** Fills, activations and meter data. Settlement must
be able to reconstruct truth from this alone, with no access to what the Planner
intended, so that ex-post accounting cannot be contaminated by ex-ante belief.

**C5 Settlement → State.** The only backwards edge. It carries realised peak,
qualification progress, closed commitments and the error attribution that feeds
the slow loop.

---

## 5. Non-goals

Stated so they do not creep in.

- **Not a market simulator.** L4 exists and is fixed. This specification treats
  it as an external system with a defined adapter.
- **Not a forecasting system.** Forecasts enter through L1 as data with declared
  provenance and quality. Model training is an offline concern; the engine
  consumes artefacts, it does not fit them online.
- **Not a portfolio system.** Single site, single POI, single battery in v1.
  Multi-asset is anticipated in the contracts (every quantity is keyed by
  `AssetId` / `PoiId`) but not designed for.
- **Not real-time control.** Sub-second dispatch and aFRR activation response are
  a separate, simpler control layer downstream of the Planner's setpoints. The
  boundary is defined in `02-layers/L3-planner.md` §7 but the controller is out
  of scope.
