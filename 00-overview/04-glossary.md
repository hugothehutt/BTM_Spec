# 04 — Glossary

The vocabulary of this specification. One word, one meaning.

## 1. What this document is

Normative for what a word **means**. Never for what the thing **does**.

Each entry gives one sentence and names the document that owns the concept. The
owner is where the rules live; this file does not restate them. An entry that
restates a rule is a defect — a second normative statement of one thing is what
this document exists to remove.

Scope is the domain: the battery, the markets, the optimisation, the accounting.
The vocabulary of how this repository verifies itself — evidence labels, masking,
adjudication, blast radius, hole markers, voiding — is owned by
`07-verification/PROTOCOL.md` and is not repeated here.

Not every English word is here. A term earns an entry when a reader needs it
defined to work: the identifiers, the named quantities, the concepts a document
leans on. Ordinary words used ordinarily are not vocabulary.

The rulings are not here. Which word kept which meaning is data, in
`07-verification/rulings.json`; the argument for each ruling is
`06-theory/TN-03-vocabulary.md` §3. No two of the three state the same fact, so
none of them can drift from the others.

`07-verification/check_glossary.py` checks this file against that data. Four
things **fail**: a term defined twice, an entry whose owning document does not
exist, a replacement name in the ruling set with no entry here, and a retired
spelling outside a written allowance. One thing **warns** without failing: a
term defined here and used nowhere else in the corpus.

## 2. Layers and components

| Term | Definition | Owner |
|---|---|---|
| **adapter** | The L4 component that translates, records and reconciles, and chooses nothing. | `02-layers/L4-execution-boundary.md` |
| **composer** | The L2 component that applies view output in a declared order and performs no economics itself. | `02-layers/L2-valuation.md` |
| **contract** | The closed description of what crosses one seam: field table, version, invariants. | `03-contracts/C0-contract-conventions.md` |
| `L0` | The State/Value Store: everything that must survive a tick, and the system's only backwards edge. | `02-layers/L0-state-value-store.md` |
| `L1` | Belief: what the engine could have known at a stated knowledge boundary. | `02-layers/L1-belief.md` |
| `L2` | Valuation: belief and state to economic terms. | `02-layers/L2-valuation.md` |
| `L3` | Planner: terms and commitments to order intent. | `02-layers/L3-planner.md` |
| `L4` | Execution boundary: the adapter onto the external simulator. | `02-layers/L4-execution-boundary.md` |
| `L5` | Settlement: outcomes to ex-post truth and attribution. | `02-layers/L5-settlement.md` |
| **quoting policy** | The component mapping a target position, shadow value and urgency onto a limit-order ladder. | `01-adr/ADR-012-order-intent.md` |
| **seam** | A layer boundary carrying a versioned, serialisable contract. | `03-contracts/C0-contract-conventions.md` |
| **slow loop** | The off-tick process that fits the terminal value function; its output is an input, never a dependency. | `02-layers/L0-state-value-store.md` |
| **view** | An independent pure function of belief and state emitting economic terms, owning a declared set of effects. | `02-layers/L2-valuation.md` |

## 3. Time

| Term | Definition | Owner |
|---|---|---|
| **accounting period** | The month or year a charge is assessed over, carried as an explicit input rather than a unit denominator. | `00-overview/02-conventions.md` |
| `asOf` | The knowledge boundary a read is taken at; mandatory on every belief read. | `01-adr/ADR-004-bitemporal-belief-store.md` |
| **bitemporality** | Carrying both times on every fact, so that what was knowable is separable from what was true. | `00-overview/02-conventions.md` |
| `C_accounting` | The clock of month and year boundaries. | `00-overview/01-system-model.md` |
| `C_gate` | The market clock: auction and gate closures. | `00-overview/01-system-model.md` |
| `C_slow` | The clock of value-function refits. | `00-overview/01-system-model.md` |
| `C_tick` | The engine's own event-driven clock. | `00-overview/01-system-model.md` |
| **civil day** | A local calendar day: 92, 96 or 100 slots, never a constant. | `00-overview/02-conventions.md` |
| `CivilCalendar` | The only component permitted to resolve a civil boundary or tariff window to a `SlotId`. | `00-overview/02-conventions.md` |
| `H_hot` | The length of the belief hot window; at least `H_plan`. | `00-overview/02-conventions.md` |
| `H_plan` | The Planner's optimisation horizon, truncated and closed by the terminal value function. | `00-overview/02-conventions.md` |
| `H_slow` | The slow loop's horizon, long enough to reach the end of the longest accounting period. | `02-layers/L0-state-value-store.md` |
| `knowledge_time` | The instant a fact could first have been known. | `01-adr/ADR-004-bitemporal-belief-store.md` |
| **lookahead** | Using a fact whose knowledge time is later than the read boundary. | `01-adr/ADR-004-bitemporal-belief-store.md` |
| `MarketCalendar` | The versioned artefact holding gate closures, product blocks, exchange holidays and tick sizes. | `01-adr/ADR-002-clocks-and-calendar.md` |
| `SlotId` | A count of quarter-hours since the Unix epoch, UTC; monotone, gap-free, DST-independent. | `00-overview/02-conventions.md` |
| `SlotSpan` | A duration expressed as a count of 15-minute slots. | `00-overview/02-conventions.md` |
| `valid_time` | The slot a fact is about. | `01-adr/ADR-004-bitemporal-belief-store.md` |
| **watermark** | The knowledge time up to which ingestion guarantees completeness. | `02-layers/L1-belief.md` |

## 4. Solution method

| Term | Definition | Owner |
|---|---|---|
| **commitment ledger** | The L0-held record carrying frozen decisions forward between gate solves. | `02-layers/L3-planner.md` |
| **fan** | An ensemble of `S` complete paths, all distinct from slot 1, with a flat filtration and no interior nodes. | `06-theory/TN-02-fan-not-tree.md` |
| **filtration distance** | The reduction-error term that measures information structure rather than path proximity; `O(1)` for a fan against any tree. | `06-theory/TN-02-fan-not-tree.md` |
| **first stage** | What a gate makes irreversible: the reserve offer at `S1`, the bid curve at `S2`, the order sent this tick at `S3` and `S4`. | `06-theory/TN-02-fan-not-tree.md` |
| **Gate** | A point at which the engine commits: `S0` slow loop, `S1` reserve, `S2` day-ahead, `S3` post-DA rebalance, `S4` continuous intraday, `S5` dispatch. | `02-layers/L3-planner.md` |
| **non-anticipativity** | The requirement that a decision not depend on information unavailable when it is taken; enforced structurally by omitting the scenario index. | `06-theory/TN-02-fan-not-tree.md` |
| **second stage** | A valuation of the future whose only job is to price the first-stage commitment. Not a plan. | `06-theory/TN-02-fan-not-tree.md` |
| **Stage** | A measurability level inside one solve. There are exactly two: the first carries no scenario index, the second does. | `06-theory/TN-02-fan-not-tree.md` |
| **stage-aggregation error** | The pessimistic error from modelling two stages where the world has many; it survives an exact value function. | `06-theory/TN-02-fan-not-tree.md` |
| **tree** | A scenario structure that branches as time passes. The engine does not build one. | `06-theory/TN-02-fan-not-tree.md` |

## 5. Belief and uncertainty

| Term | Definition | Owner |
|---|---|---|
| `BeliefCursor` | The sequential replay position owning a hot window. | `02-layers/L1-belief.md` |
| `BeliefSnapshot` | The C1 payload: everything Valuation may know at one knowledge boundary. | `03-contracts/C1-belief-to-valuation.md` |
| **cold store** | The immutable, append-only record of every fact and every revision. | `02-layers/L1-belief.md` |
| **ensemble** | The `[S, H, K]` array of scenario paths with weights summing to one. | `01-adr/ADR-005-joint-scenario-ensemble.md` |
| **fill-probability surface** | The fitted belief about whether an order fills; it exports volumes and never a price. | `02-layers/L1-belief.md` |
| `fitScenarioCount` | The size of the slow loop's own, larger and lower-resolution ensemble; not `S`. | `02-layers/L0-state-value-store.md` |
| **hot window** | The preallocated buffer over `[t, t + H_hot]` that a cursor refills in place. | `02-layers/L1-belief.md` |
| `K` | The number of uncertain series sharing the scenario axis. | `01-adr/ADR-005-joint-scenario-ensemble.md` |
| **quarantine** | Where a fact whose knowledge time cannot be established is put, unreachable from a production read. | `02-layers/L1-belief.md` |
| `refillCount` | The hot window's own counter, bumped once per refill; not L0's `generation`. | `02-layers/L1-belief.md` |
| **reliable volume** | The volume that fills with at least the configured probability, used as a bound. | `02-layers/L1-belief.md` |
| **retraction** | A tombstone withdrawing a published fact. | `02-layers/L1-belief.md` |
| **revision** | A later publication of the same fact for the same slot; both values are retained permanently. | `02-layers/L1-belief.md` |
| `S` | The number of scenarios in the reduced ensemble, the one that crosses C1. | `01-adr/ADR-005-joint-scenario-ensemble.md` |
| **scenario** | A coherent state of the world across every uncertain series, indexed consistently. | `01-adr/ADR-005-joint-scenario-ensemble.md` |
| **scenario reduction** | The weighted mapping from the generated ensemble to the smaller one the Planner sees. | `01-adr/ADR-005-joint-scenario-ensemble.md` |
| **slab** | L1's warm-tier artefact: one memory-mapped, content-addressed file per `(series, civil day)`, derived from cold and rebuildable at any time. | `02-layers/L1-belief.md` |

## 6. Valuation

| Term | Definition | Owner |
|---|---|---|
| `B(E)` | The aFRR capacity bid curve's expected value as a function of offered energy; concave, and not the terminal value function. | `02-layers/L2-valuation.md` |
| **base** | The quantity an effect applies to: POI import, battery throughput, market volume, terminal SOC, reserve capacity, delineation. | `01-adr/ADR-009-term-ownership.md` |
| `BoundTerm` | A bound on a variable or expression, carrying no price field. | `03-contracts/C2-valuation-to-planner.md` |
| **coherent** | Of a risk measure: monotone, translation-equivariant, positively homogeneous and subadditive. | `06-theory/TN-01-cvar-across-stages.md` |
| **composition step** | One step of the composer's declared order, whose preconditions are checked before it runs and whose postconditions are established when it has. | `02-layers/L2-valuation.md` |
| `CouplingConstraint` | A declared structural constraint spanning decision variables. | `03-contracts/C2-valuation-to-planner.md` |
| **curvature** | The declared shape of a piecewise-linear term, verified against its breakpoints rather than trusted. | `01-adr/ADR-008-linearizable-primitives.md` |
| **CVaR** | The mean of the worst tail mass of a loss distribution, taken on the reduced measure. | `06-theory/TN-01-cvar-across-stages.md` |
| `EconomicEffect` | The closed enumeration naming what a term prices. | `01-adr/ADR-009-term-ownership.md` |
| `EpigraphTerm` | A variable, the slot set it must dominate, a floor and a unit price; how a maximum becomes linear. | `03-contracts/C2-valuation-to-planner.md` |
| **invalidation** | A value function fitted at a conditioning state the engine has left: the wrong curve, which no penalty repairs. | `02-layers/L0-state-value-store.md` |
| `LinearTerm` | A per-unit EUR coefficient on a decision variable and slot. | `03-contracts/C2-valuation-to-planner.md` |
| **proration** | Scaling the in-horizon share of a charge assessed over a longer accounting period. | `02-layers/L2-valuation.md` |
| `PwlTerm` | A breakpoint table with declared curvature and sense. | `03-contracts/C2-valuation-to-planner.md` |
| **staleness** | A value function past its validity horizon: the right curve, out of date, priced by shrinking its slopes. | `02-layers/L0-state-value-store.md` |
| **term ownership matrix** | The normative mapping from each effect to the one view that owns it. | `01-adr/ADR-009-term-ownership.md` |
| **term shape** | One of the five forms a MILP can ingest natively. Nothing else crosses C2. | `01-adr/ADR-008-linearizable-primitives.md` |
| `ValuationBundle` | The C2 payload: every economic term for this tick, sealed and hashed. | `03-contracts/C2-valuation-to-planner.md` |
| **value function** | `V`, the concave piecewise-linear curve pricing stored energy beyond the planning horizon, conditioned on discrete state. | `01-adr/ADR-007-value-function-not-lambda.md` |
| `λ_SOC` | The subgradient of the value function at the optimal terminal SOC; a Planner output, never a Valuation input. | `01-adr/ADR-007-value-function-not-lambda.md` |

## 7. Planning

| Term | Definition | Owner |
|---|---|---|
| **certified bound** | The dual value: a number that can be reported as a bound on the optimum, not a hope. | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| **curtailment** | Generation deliberately not delivered; a priced decision variable, never a rule. | `01-adr/ADR-016-curtailment-as-priced-decision.md` |
| **feasibility restoration** | Softening bounds in a declared order when a solve is infeasible; physical and regulatory bounds are never in that order. | `02-layers/L3-planner.md` |
| **physically backed** | Of a market position: equal to physical flow in the same slot, so a matched buy and sell is not a position. | `06-theory/TN-02-fan-not-tree.md` |
| **POI bridge** | The identity relating site load, delivered generation and battery power to net grid power. | `00-overview/02-conventions.md` |
| **reservation price** | The internal transfer price of a MW of battery headroom, produced by the Tier 2 dual. | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| **shadow value** | The dual on the position accounting constraint; the price at which the Planner is indifferent to trading. | `01-adr/ADR-012-order-intent.md` |
| **SOC corridor** | The band the dispatch controller must remain within; the binding instruction, unlike the setpoint. | `02-layers/L3-planner.md` |
| `SolveBudget` | The deterministic work limit for a solve, never wall clock. | `01-adr/ADR-013-determinism-and-replay.md` |
| **Tier 1** | The joint model solved directly; the accuracy reference every other tier is scored against. | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| **Tier 2** | Lagrangian relaxation on the coupling block, returning a feasible plan and a certified bound. | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| **Tier 3** | The fast path: evaluate a learned surface, price headroom, solve one spot model. | `01-adr/ADR-010-cross-market-tier-ladder.md` |
| **trade-through** | Quoting past the shadow value: selling below it or buying above it. | `04-compliance/T4-calibration-and-execution-quality.md` |
| **urgency** | How much the objective degrades if a target position is not reached. | `01-adr/ADR-012-order-intent.md` |

## 8. Execution

| Term | Definition | Owner |
|---|---|---|
| **battery frame** | The sign convention in which positive power is discharge. | `00-overview/02-conventions.md` |
| `ExecutionIntent` | The C3 payload: what to submit, and nothing about why. | `03-contracts/C3-planner-to-execution.md` |
| `intentId` | The idempotency key for an order, stable across replaces. | `02-layers/L4-execution-boundary.md` |
| **market frame** | The sign convention in which a positive volume is a sale. | `00-overview/02-conventions.md` |
| **orphan** | A live venue order with no ledger entry. | `02-layers/L4-execution-boundary.md` |
| **phantom fill** | A fill referencing no submitted intent. | `03-contracts/C4-execution-to-settlement.md` |
| **POI frame** | The sign convention in which positive power is import. | `00-overview/02-conventions.md` |
| **projection** | The narrowing of C3 that removes the fields Execution must not read, enforced by a type with nowhere to put them. | `02-layers/L4-execution-boundary.md` |
| **reconciliation** | Diffing live venue state against the ledger after a restart or reconnect. | `02-layers/L4-execution-boundary.md` |

## 9. Settlement

| Term | Definition | Owner |
|---|---|---|
| **adverse selection** | The volume-weighted tendency of the market to move against a fill after it happens. | `04-compliance/T4-calibration-and-execution-quality.md` |
| `CaptureRatio` | Execution quality: the share of the room between the market and the shadow value that the fill actually got. | `04-compliance/T4-calibration-and-execution-quality.md` |
| **error bucket** | One of the four owned components of the difference between reference and realised value. | `03-contracts/C5-settlement-to-state.md` |
| `executionSlippageEur` | Value lost between intent and fill. | `03-contracts/C5-settlement-to-state.md` |
| `forecastErrorEur` | Value the belief carried that the world did not deliver, with the plan held fixed. | `03-contracts/C5-settlement-to-state.md` |
| `modelErrorEur` | Value lost because Valuation and Settlement disagree about what a term means. | `03-contracts/C5-settlement-to-state.md` |
| `optimalityGapEur` | Value given up because the tier used was not Tier 1. | `03-contracts/C5-settlement-to-state.md` |
| **Phase A** | Accounting: ex-post truth recomputed from realised data alone, without sight of the plan. | `02-layers/L5-settlement.md` |
| **Phase B** | Attribution: comparing the sealed accounting against what was planned and believed. | `02-layers/L5-settlement.md` |
| `PolicyQuality` | Realised value over the physically-backed, index-granularity benchmark value. | `06-theory/TN-02-fan-not-tree.md` |
| `pPoiRealisedPeakMw` | The grid peak already realised in the current accounting period; the epigraph floor the peak term is composed against. | `03-contracts/C5-settlement-to-state.md` |
| **provisional** | Of settled data: subject to revision until the meter series is final. | `02-layers/L5-settlement.md` |
| **recomputation principle** | Settlement trusts nothing upstream computed; every euro is recomputed from realised data. | `02-layers/L5-settlement.md` |
| **restatement** | A new artefact superseding an earlier one, never an edit in place. | `02-layers/L5-settlement.md` |
| **settlement point** | A point on the settlement timeline at which a conclusion becomes available. | `02-layers/L5-settlement.md` |
| `StrategyTag` | The attribution label on an intent: arbitrage, peak shaving, reserve hedge, rebalance, commitment cover. | `03-contracts/C3-planner-to-execution.md` |
| `unexplainedEur` | The residual of the effect decomposition; never absorbed into a neighbouring effect. | `02-layers/L5-settlement.md` |
| **unsettled gap** | The span between the last settled slot and now, priced with the engine's own trajectory. | `02-layers/L0-state-value-store.md` |
| `valueOfForesight` | What re-planning on realised data would have earned; reported, never booked, because it does not telescope. | `03-contracts/C5-settlement-to-state.md` |

## 10. Delineation

The MiSpel regime. Numbered quantities in parentheses are the source's own and
are regulatory primitives; `00-overview/03-mispel-reference.md` is the reference
for all of them.

| Term | Definition | Owner |
|---|---|---|
| **Abgrenzungsoption** | The delineation option in scope: quarter-hourly attribution between grid and storage. | `00-overview/03-mispel-reference.md` |
| **accumulator** | A month-to-date energy total the delineation value is a function of. | `01-adr/ADR-017-delineation-in-the-objective.md` |
| `AW` | The anzulegender Wert of a generating plant. | `00-overview/03-mispel-reference.md` |
| **Fremdtankstrom** | Storage output exceeding storage input over a month, possible only through carried state of charge. | `00-overview/03-mispel-reference.md` |
| **green** | Energy that is subsidy-eligible at injection. | `00-overview/03-mispel-reference.md` |
| **grey** | Energy that is nettable at injection but not subsidy-eligible. | `00-overview/03-mispel-reference.md` |
| **lever** | A decision the engine can take that moves an accumulator: curtailment, charge source, discharge routing, reserve headroom. | `01-adr/ADR-017-delineation-in-the-objective.md` |
| **route split** | The point at which the storage export stream divides between green and grey. | `01-adr/ADR-017-delineation-in-the-objective.md` |
| **Speichervorrang** | The assignment priority: grid supply first on charge, storage generation first on discharge. | `00-overview/03-mispel-reference.md` |
| `V_del` | The delineation value, evaluated arithmetically from a projection of the remaining month and never fitted. | `01-adr/ADR-017-delineation-in-the-objective.md` |
| `Z1` | The grid-point two-direction meter. | `00-overview/03-mispel-reference.md` |
| `Z2` | The storage two-direction meter. | `00-overview/03-mispel-reference.md` |
| `ZF` | The power-weighted share of grid injection assigned to one plant. | `00-overview/03-mispel-reference.md` |
| `λ_j` | The marginal value of one accumulator; the only delineation quantity Valuation publishes. | `01-adr/ADR-017-delineation-in-the-objective.md` |

## 11. Tariff and qualification

| Term | Definition | Owner |
|---|---|---|
| **full-load hours** | Annual energy over annual peak; the quantity a reduction qualification turns on. | `03-contracts/C5-settlement-to-state.md` |
| **HLZF** | The published high-load time windows a regime measures its peak within. | `01-adr/ADR-011-tariff-regime-plugin.md` |
| `peakCritical` | The flag raised when the realised peak is close enough to matter to this solve. | `03-contracts/C5-settlement-to-state.md` |
| `qualCritical` | The flag raised when the qualification margin is inside its configured band. | `03-contracts/C5-settlement-to-state.md` |
| **qualification margin** | The distance to the cliff, published as a continuous quantity because a boolean status arrives too late to act on. | `02-layers/L5-settlement.md` |
| **realised peak** | The highest settled POI import in the accounting period so far; the floor the planned peak cannot go below. | `03-contracts/C5-settlement-to-state.md` |
| **tariff regime** | A network charging scheme the site is billed under; regimes compose. | `01-adr/ADR-011-tariff-regime-plugin.md` |
| **the cliff** | The qualification threshold, below which a reduction is lost for the whole year. | `01-adr/ADR-011-tariff-regime-plugin.md` |

## 12. Degradation and data quality

| Term | Definition | Owner |
|---|---|---|
| **conservative default** | The value that biases toward inaction, chosen for the decision it feeds. | `01-adr/ADR-014-degradation-ladder.md` |
| **degradation mode** | The engine's global operating posture, from normal through to halt. | `01-adr/ADR-014-degradation-ladder.md` |
| **fallback ladder** | The per-field chain from fresh primary source down to missing. | `01-adr/ADR-014-degradation-ladder.md` |
| **hysteresis** | Requiring more consecutive good ticks to leave a mode than bad ticks to enter it. | `01-adr/ADR-014-degradation-ladder.md` |
| `provenance` | Where a value came from: measured, forecast, revised, imputed or defaulted. | `01-adr/ADR-014-degradation-ladder.md` |
| `quality` | How good a value is, on the ladder from good to missing. | `01-adr/ADR-014-degradation-ladder.md` |
| **risk multiplier** | How quality reaches the optimisation: as a parameter, never as a branch. | `01-adr/ADR-014-degradation-ladder.md` |
| **rung** | One step of the fallback ladder. | `01-adr/ADR-014-degradation-ladder.md` |

## 13. Invariant families

An invariant is a machine-checked statement that must hold wherever it is
scoped. `04-compliance/T1-invariants.md` is the register; the family letter says
what an invariant of that family is about, and nothing about which layer wrote
it.

| Family | What its invariants cover | Owner |
|---|---|---|
| `INV-D-*` | Belief and data: the knowledge boundary, ensemble axis coherence, reduction fidelity, publication semantics, store immutability. | `04-compliance/T1-invariants.md` |
| `INV-G-*` | Universal seam properties: no missing-value sentinels, identifier and unit agreement, hash and manifest identity, no wall-clock reads, array lengths. Applied at every seam by the shared validator. | `04-compliance/T1-invariants.md` |
| `INV-P-*` | Planner: commitment feasibility, bid-curve monotonicity, simultaneity, quoting against the shadow value. | `04-compliance/T1-invariants.md` |
| `INV-S-*` | Settlement and state: period resets, effect and bucket conservation, delivery, restatement ordering, the delineation identities. | `04-compliance/T1-invariants.md` |
| `INV-T-*` | Time and calendar: civil-day length, DST boundaries, tariff-window resolution. | `04-compliance/T1-invariants.md` |
| `INV-V-*` | Valuation: effect ownership and exclusivity, composition order, declared curvature, value-function concavity, what a bound may carry. | `04-compliance/T1-invariants.md` |
| `INV-X-*` | The execution boundary: fills matching intent, energy balance, position backing, idempotency, pass-through fidelity. | `04-compliance/T1-invariants.md` |

Severity is a property of an invariant, not of its family. An id is never reused
once allocated, and never renumbered: a reused id makes an incident timeline
unreadable.

