# L2 — Valuation

Turns "what do we believe about the world right now" into "what is a MWh, a kW
of peak, or a MW of aFRR capacity worth in euros right now" — expressed as
linearizable primitives (ADR-008), not as prices.

**Input:** `BeliefSnapshot` (C1) + `StateSnapshot` (L0, lagged)
**Output:** `ValuationBundle` (C2)
**Purity:** total function. No I/O, no clock, no state, no randomness.

---

## 1. Shape of the layer

```
BeliefSnapshot ─┬─▶ PeakView          ─┐
StateSnapshot  ─┼─▶ OppCostView        │
                ├─▶ AfrrCapacityView   │
                ├─▶ AfrrEnergyView     ├─▶ Composer ──▶ ValuationBundle
                ├─▶ ImbalanceRiskView  │      │
                ├─▶ IdOptionView       │      ├─ ownership check
                ├─▶ FillProbView       │      ├─ stage preconditions
                ├─▶ TariffView         │      ├─ curvature verification
                └─▶ DelineationView   ─┘      └─ problem-class hint
```

Views are **independent**. A view may not read another view's output, may not
share mutable state, and may not know that another view exists. Composition is
the composer's job alone. This is what makes each view a small, individually
testable pure function of a recorded C1 payload.

```csharp
interface IValuationView<TConfig> {
    ViewId Id { get; }
    FieldSchema PublishedSchema { get; }          // the closed field table
    IReadOnlyList<EconomicEffect> ClaimedEffects { get; }
    ViewOutput Evaluate(in BeliefSnapshot b, in StateSnapshot s, in TConfig cfg);
}
```

`PublishedSchema` is declared **as data**, and `Evaluate`'s output is validated
against it. A view that emits a field it did not declare fails. This is the
mechanism behind the closed published field table: it is enforced, not
documented.

---

## 2. The view catalog

### PeakView — network demand charge

Prices the marginal kW of grid peak, under whichever tariff regimes are active
(ADR-011).

- **Emits:** one `EpigraphTerm` per active regime.
- **Slot set:** all slots for `AnnualLeistungspreis`; **HLZF slots only** for
  `AtypicalHlzf` — expressed through `EpigraphTerm.overSlots`, which is why that
  field is a slot *set* rather than a range.
- **Floor:** `realisedPeak` from L0, adjusted upward for the unsettled gap
  (C5 §2) using the engine's own modelled trajectory.
- **Risk treatment:** the peak level used is an **empirical CVaR tail mean over
  the joint ensemble**, not the mean forecast. Concretely, over the reduced
  ensemble with weights `w_s`, take the weighted mean of the worst `(1−α)` mass
  of `max_t p_poi[s,t]`. This is a risk-averse peak level and it is the correct
  treatment: peak charge is a max-of-a-max, so the mean forecast systematically
  understates it (Jensen, in the unhelpful direction).
- **Proration:** the horizon is shorter than the accounting period. The
  `prorationFactor` scales the in-horizon charge; the remainder is priced by `V`.
  Getting this wrong is the most common way to make the engine pathologically
  peak-averse.
- **Degradation response:** poor `load` quality adds
  `riskProfile.peakSafetyMarginKw` to the floor rather than changing behaviour.

### OppCostView — the internal cost of using the battery

- **Emits:** `LinearTerm` (cycle/throughput cost per kWh, from the degradation
  model), `BoundTerm` (power limits, POI limits), and the **`PwlTerm` carrying
  V(SOC)** (ADR-007).
- **Does not emit λ_SOC.** λ is an output of the Planner, obtained as the
  subgradient of `V` at the optimum, and is reported back in `PlanResult` for
  diagnostics only.
- Degradation cost is charged **once**, here, on `BatteryThroughput`. If the
  value function was fitted on net-of-degradation cashflows, the ownership check
  will catch the double count (`INV-V-01`) — which is exactly the scenario ADR-009
  exists for.

### AfrrCapacityView — reserve capacity value

- **Emits:** `BoundTerm` (MW envelope from prequalification and asset limits) and
  a `PwlTerm` (concave, Maximize) for the expected value of the capacity bid
  curve `V(E)`.
- Concavity arises from the clearing model: offering more MW lowers the
  probability of clearing at a good price, so expected revenue is concave in
  offered volume. Concavity is *verified against the breakpoints*, not assumed —
  if the fitted curve is not concave, that is a modelling signal, and it must be
  declared `General` (costing binaries) rather than mis-declared.

### AfrrEnergyView / ImbalanceRiskView — activation value and imbalance risk

- **Emits:** `LinearTerm`s for expected activation value, and a `PwlTerm`
  (convex, Minimize) for the CVaR functional on imbalance cost.
- CVaR is linear-programmable (Rockafellar–Uryasev), so the risk term does not
  change the problem class. This is why CVaR rather than, say, a variance
  penalty: same risk intent, no quadratic term, no loss of MILP structure.
- Activation and imbalance draw from the **same** scenario axis as prices
  (ADR-005), which is what makes "activated when prices are extreme" representable
  at all.

### IdOptionView — option value of the post-DA intraday position

- **Emits:** `PwlTerm` in volume (concave — the marginal MWh gets a worse price),
  and `BoundTerm`s from SOC/η feasibility.
- Directional buy **and** sell, restricted to volumes the battery can physically
  support. The restriction is a bound, not a penalty, because an infeasible
  intraday plan is not a cheap plan, it is an invalid one.

### FillProbView — how much volume may be counted on

- **Emits:** `BoundTerm` **only**. No price field exists on a `BoundTerm`, so the
  original design intent — constrain volume without telling the Planner what the
  fill would earn — is enforced by the type system rather than by convention.
  `INV-V-16` asserts it.
- The full fill-probability curve is available to the quoting policy through a
  separate path (ADR-012), which is where the price/probability trade-off belongs.

### DelineationView — the Abgrenzungsoption routes

Prices the two delineation routes and the levies they relieve (ADR-017,
`00-overview/03-mispel-reference.md`).

- **Emits:** `LinearTerm`s carrying the marginal values `λ_j = ∂V_del/∂A_j` of the
  seven month-to-date accumulators `{(3),(5),(6),(9),(11),(26),(29)}`, plus the
  `BoundTerm`s bounding `q` to available generation (ADR-016).
- **Emits no coefficient on any lever.** Curtailment, charge source, discharge routing
  and reserve headroom reach the objective only through the accumulator state
  equations the Planner carries (L3 §2). A lever coefficient would have to embed a
  `MIN`-branch case analysis evaluated at a state not yet chosen.
- **`V_del` is evaluated arithmetically, never fitted.** Given a projection of the
  remaining month, every quantity from `(10)` to `(33)` is closed-form, so the `λ_j`
  are derivatives of published formulas. There is no artefact to go stale — only a
  projection to refresh.
- **Risk treatment:** `V_del` is evaluated per scenario and the tail of `V_del` itself
  is taken, not a per-accumulator sign. `MAX[AW − MW_month; 0]` is convex, so the mean
  projection understates the premium; the direction of pessimism differs per
  accumulator and a single `cvarLevel` would push some of them the wrong way.
- **Curvature exists only through grid charging.** At `(9) = 0` the ratio in
  `(31) = (30)·(28)` cancels and the whole green route is a per-slot linear sum.

### TariffView — the closed list of scarcity-euro terms

- **Emits:** `LinearTerm`s only, from a closed enumerated list — volumetric network
  charge, and the taxes and surcharges a delineation regime cannot reduce — all
  normalised to EUR/MWh and all stamped for the tick. The **reducible** EnFG components
  are not here: they are charged on `(21)` by `DelineationView` at stage 3 (ADR-017).
- "Closed list" is enforced: the configuration enumerates permitted components
  and an unrecognised component is a configuration error, not a silently ignored
  field.

---

## 3. Views the catalog does not yet have

Recorded so the gaps are deliberate:

- **PvCurtailmentView** — **not needed, and deliberately absent.** `q` is a decision
  variable of the Planner's MILP (ADR-016), bounded by `DelineationView` to available
  generation and priced against spot and the delineation `λ_j`. A view that scheduled
  curtailment would be a rule wearing a price.
- **SelfConsumptionView** — **not needed, and deliberately absent** (ADR-017). Avoided
  import is already `TariffView` + `SpotView`, and the delineation leg — a kWh
  discharged into load never enters `(11)` and so forfeits both routes — falls out of
  the accumulator state equations. Revisit only if retail supply stops being
  spot-indexed.
- **CapacityMarketView / redispatch** — out of scope for v1.

---

## 4. The composer

Individual views do not give a usable objective, because the terms interact. The
composer applies them in stages with **declared preconditions**, so that
composing in the wrong order raises an exception rather than producing a
plausible wrong number (ADR-009).

| Stage | Requires | Establishes | Rationale |
|---|---|---|---|
| 1 `Tariff` | — | POI energy fully marked | Everything downstream prices *marked* energy |
| 2 `OppCost + V` | POI energy marked | Battery energy marked; terminal value attached | Battery energy must carry its own cost before anything prices its use |
| 3 `Delineation` | Battery energy marked | `(21)` established; delineation `λ` attached | Levies are charged on `(21)`, which depends on the month's PV-versus-grid charging mix — so it cannot be marked at stage 1 |
| 4 `ReserveCoupling` | Battery energy marked | SOC corridor and headroom constraints present | Reserve consumes headroom, which must exist before peak prices it |
| 5 `Peak` | POI **and** battery energy marked | `zPeak` bounded below by realised peak | Peak headroom is priced on top of a battery whose energy is already marked — reversing this double-counts |
| 6 `Validate` | all above | Bundle well-formed and complete | |

This table is the **single source** for stage order. `INV-V-07`, `T2` §3.9 and
`stubs/Interfaces.cs` point at it rather than restating it.

Stage 1 carries only the **non-reducible** tariff surface and stays linear. The
reducible EnFG components move to stage 3, because their base `(21)` is not knowable
until battery energy is marked — the one place where the original
`tariff → opp-cost → peak` ordering could not be preserved.

### Composer responsibilities, in order

1. Collect terms from every enabled view.
2. **Ownership check** — coverage and exclusivity against the matrix (§5).
3. **Stage precondition check** — each stage's requirements met.
4. **Curvature verification** — every `PwlTerm`'s declared curvature verified
   against its breakpoints (`INV-V-12`).
5. **Unit and range validation** — C0 universal invariants.
6. **Risk profile assembly** — map input quality to risk parameters (§6).
7. **Problem class hint** — count binaries by originating term (C2 §7).
8. **Seal** — compute `contentHash`, emit.

The composer performs **no economics**. It never adds, scales or reconciles a
term. If two terms would overlap, it fails rather than netting them — netting
would be economics, and economics belongs in a view where it can be tested.

---

## 5. Term ownership matrix

Normative. Each effect has exactly one owner.

| EconomicEffect | Owner | Base |
|---|---|---|
| `SpotEnergyValue` | `SpotView` | `MarketVolume` |
| `IdEnergyValue` | `IdOptionView` | `MarketVolume` |
| `ReserveCapacityRevenue` | `AfrrCapacityView` | `ReserveCapacity` |
| `ReserveEnergyRevenue` | `AfrrEnergyView` | `MarketVolume` |
| `ImbalanceCost` | `ImbalanceRiskView` | `MarketVolume` |
| `NetworkPeakCharge` | `PeakView` | `PoiImport` |
| `NetworkVolumetricCharge` | `TariffView` | `PoiImport` |
| `LeviesAndTaxes` | `TariffView` | `PoiImport` |
| `EnfgLevies` | `DelineationView` | `Delineation` |
| `SubsidyRevenue` | `DelineationView` | `Delineation` |
| `CycleDegradation` | `OppCostView` | `BatteryThroughput` |
| `StoredEnergyContinuation` | `OppCostView` (via `V`) | `TerminalSoc` |
| `ActivationRisk` | `AfrrEnergyView` | `MarketVolume` |

**Note on `SpotView`.** DA energy value enters as a `LinearTerm` on
`vDaBuy`/`vDaSell` emitted by a thin `SpotView`. This view is not in the original
catalog and is added here deliberately: leaving it implicit — "the Planner just
uses the DA price" — is how the largest term in the objective ends up owned by
nobody, and it would violate both `INV-V-02` (every term's `originView` is the
effect's owner) and L3 §2 ("the Planner adds no economics of its own"). `SpotView`
is trivial to implement and its existence is what keeps those two rules true.

**Note on the levy split.** `LeviesAndTaxes` carries the components a delineation
regime cannot reduce — Stromsteuer, Konzessionsabgabe — on `PoiImport`. `EnfgLevies`
carries the reducible EnFG components on `(21)`. This is a **base split, not relief
booked as revenue** (ADR-017), so exclusivity is unaffected: `(20)` is reported as a
diagnostic quantity and never as an effect.

**Note on `SubsidyRevenue`.** It is the Marktprämie `MAX[AW − MW_month; 0]` on `(32)`
only. Spot revenue on the same exported kWh stays in `SpotEnergyValue`, which is what
makes the double count impossible by construction rather than by check.

Two effects may share a **base** (`NetworkPeakCharge` and
`NetworkVolumetricCharge` both apply to `PoiImport`) — that is legitimate and
common. What is forbidden is two terms claiming the **same effect** on the same
base and slot.

---

## 6. Quality → risk mapping

Quality never becomes a branch (ADR-014 §1). It becomes parameters:

| Degraded input | Risk response |
|---|---|
| `load` quality | ↑ `peakSafetyMarginKw`; ↑ `cvarLevel` for peak |
| `pv` quality | ↑ `peakSafetyMarginKw` (PV shortfall raises import) |
| price beliefs | ↓ `positionScale`; ↑ `cvarWeight` |
| `activation` beliefs | ↑ `chanceLevel` for SOC feasibility |
| `V(SOC)` stale | apply `stalenessPenalty` shrink to `V`'s slopes |
| liquidity beliefs | ↓ `idReliableVolume` bounds (already conservative at C1) |

Every adjustment is recorded in `riskProfile.driverSummary`, so a conservative
plan can always be explained by naming the input that caused it.

---

## 7. Testing this layer

Beyond the seam conformance tests, the properties worth asserting per view
(detail in `04-compliance/T2`):

- **Determinism** — same C1 in, byte-identical C2 out.
- **Schema closure** — output contains only declared fields.
- **Monotonicity** — raising `peakPrice` never lowers the priced value of peak
  reduction; raising `afrrCapPrice` never lowers the capacity curve.
- **Zero-price invariance** — a term whose price is zero contributes nothing.
- **Scale equivariance** — scaling all prices by `k > 0` scales all EUR terms by
  `k` and leaves all bounds unchanged. This catches a surprising number of
  unit-mixing bugs in one cheap test.
- **Concavity** — `V`'s slopes strictly decreasing; capacity curve concave.
- **Ensemble coherence** — a view given a permuted scenario axis on one series
  only must produce a *different* output. If it does not, that view is ignoring
  the dependence structure ADR-005 exists to preserve.
