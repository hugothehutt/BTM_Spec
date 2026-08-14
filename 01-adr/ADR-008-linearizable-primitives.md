# ADR-008 — Valuation emits linearizable primitives, not prices

**Status:** Accepted **Reversibility:** Very expensive — this is the C2 contract

## Context

The original framing was correct and incomplete: *several terms are not linear in
x — peak is a max, aFRR is a curve — so we must publish functions, not scalars,
and bounds, not just prices.*

The question this ADR answers is: **functions in what form?** If Valuation
publishes arbitrary delegates (`Func<double, double>`), the Planner cannot embed
them in a MILP — it would have to sample them, and sampling a function to build a
MILP is both slow and lossy, and it silently changes the problem being solved.
If Valuation publishes scalars, the economics are wrong. Neither works.

## Decision

**Valuation emits a closed, small algebra of shapes that a MILP can ingest
natively.** Five shapes, nothing else. Every view's published field table
(the "IF-numbers") resolves to a set of these.

| Shape | Carries | MILP realisation | Binaries? |
|---|---|---|---|
| `LinearTerm` | coefficient per (decision variable, slot) | objective coefficient | no |
| `PwlTerm` | breakpoints `{(x_i, y_i)}`, `curvature ∈ {Concave, Convex, General}`, `sense ∈ {Max, Min}` | λ-formulation; exact LP if curvature matches sense, SOS2 otherwise | only if General |
| `EpigraphTerm` | a variable name, a set of slots it must dominate, a floor value, a unit price | `z ≥ expr[t] ∀t`, `z ≥ floor`, cost `c·z` | no |
| `BoundTerm` | lower/upper bound on a decision variable or on a linear expression, per slot | constraint | no |
| `CouplingConstraint` | a declared structural constraint spanning decision variables (e.g. reserve corridor, commitment) | constraint rows | sometimes, declared |

**Nothing else crosses C2.** No delegates, no scenario arrays, no Belief handles,
no `object`. Every shape is a serialisable value type with a declared schema.

### How each lever maps

- **PeakView → `EpigraphTerm`.** `max(·)` is linear in epigraph form:
  `z_peak ≥ p_poi[t] ∀t ∈ window`, `z_peak ≥ realisedPeak` (the floor, from L0),
  objective `−peakPrice · z_peak`. No binaries, no max operator, exact. The floor
  is precisely `peak_to_go`, and it is why the term must be composed last
  (ADR-009).
- **AfrrCapacity → `PwlTerm` (concave, Max) + `BoundTerm` (MW envelope).** The
  expected value of the capacity bid curve `V(E)` is concave in offered MW under
  a standard clearing model, so it embeds with no binaries.
- **AfrrEnergy / ImbalanceRisk → `LinearTerm` per scenario + `PwlTerm` (convex,
  Min) for the risk functional.** CVaR is representable as a linear program
  (Rockafellar–Uryasev), so a CVaR-penalised objective stays a MILP.
- **IdOption → `PwlTerm` in volume** (concave in volume: the marginal MWh gets a
  worse price), plus `BoundTerm` from SOC/η feasibility.
- **OppCost → `LinearTerm` (cycle cost per throughput) + `PwlTerm` (V(SOC),
  concave, Max, ADR-007) + `BoundTerm` (power, POI limits).**
- **FillProbView → `BoundTerm` only.** It constrains how much intraday volume may
  be *counted on*, and carries no price. This preserves the original intent
  exactly, and the type system now enforces it: a `BoundTerm` has no price field
  to populate.
- **Tariff + stamp → `LinearTerm` set**, closed and enumerated per tick.

### Curvature is declared and verified

Each `PwlTerm` declares its curvature; the composer verifies it against the
breakpoints (`INV-V-12`). A term declaring `Concave` with a non-concave
breakpoint set is a contract violation, not a silently-wrong relaxation. This
matters because whether a PWL needs binaries — and therefore whether the solve
takes 200 ms or 200 s — depends entirely on this property being true.

## Consequences

- **The Planner's problem class is known statically.** From the shapes present,
  the engine can report before solving: number of binaries, whether the model is
  an LP, and which term introduced each binary. Solve-time regressions become
  attributable to an economic modelling choice rather than mysterious.
- Valuation can express genuinely non-linear economics without knowing anything
  about MILP formulation, and the Planner can ingest them without knowing what an
  aFRR bid curve is. That is the seam doing its job.
- Adding a new economic lever means adding a view that emits existing shapes. If
  a lever cannot be expressed in the five shapes, that is a design conversation,
  not a quiet `Func<>`.
- Breakpoint count is a tuning knob with a measurable accuracy/speed trade-off
  (`T5`).
- Cost: some economics require care to express. This is the intended friction.

## Rejected

- **Delegates / callbacks into Valuation.** Not embeddable in a MILP, not
  serialisable, not replayable, and they let Valuation smuggle Belief state
  across the seam.
- **Scalars only.** Wrong economics for peak, reserve and option value — the
  three terms the whole system exists to trade off.
- **Let the Planner own the economics.** Collapses two layers into one and makes
  the "too complicated to just find the optimum" problem inevitable.
- **A general expression tree.** Expressive, but the Planner would need a full
  interpreter and could no longer guarantee the problem class or bound the
  binaries. The closed algebra buys static analysability, which is worth more
  than generality here.
