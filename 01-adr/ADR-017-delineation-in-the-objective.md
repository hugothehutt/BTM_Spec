# ADR-017 — Delineation enters the objective as state equations plus marginal values

**Status:** Accepted **Reversibility:** Very expensive — fixes the MILP's variable
set, two contract payloads and the effect enumeration

## Context

The Abgrenzungsoption (`00-overview/03-mispel-reference.md`) settles on **calendar-month
aggregates** built from quarter-hour `MIN`/`MAX` selections, and pays through two
competing routes:

```
(16) + (19) = (16) / η_month                     grey — relieves Umlagen on (20) ≤ (3)
(16)        = MAX[ (11) − (6)·pv_share ; 0 ]     pv_share = (10)/(5)
(28)        = MIN[ (11) ;  (6)·pv_share ]        green — Marktprämie via (30)·(28)
```

`(11)`, the storage energy that reached the grid, splits at `(6)·pv_share`. Below the
split it is green, above it grey. The split is therefore set by the month's PV-versus-grid
charging mix — the aggregate of ~2,880 quarter-hourly decisions, each also driven by
spot, aFRR, peak and imbalance.

Three properties make this hostile to the existing design:

| Property | Consequence |
|---|---|
| `(31) = (30)·(28)` with `(30) = (29)/(11)` | Ratio of decision-dependent monthly sums times a `MIN` — bilinear, outside ADR-008 |
| The active branch of every `MIN` is chosen by the optimiser | A lever's marginal effect has no fixed sign; see §Rejected |
| Levies are charged on `(21)`, not on `(3)` | The composer's stage-1 precondition ("POI energy fully marked") requires stage-2 information |

## Decision

**The MILP carries the seven month-to-date accumulators and their state equations as
constraints. Valuation publishes only the marginal values `λ_j = ∂V_del/∂A_j`,
recomputed every tick.**

```
A = { (3), (5), (6), (9), (11), (26), (29) }        month-to-date, reset at the
                                                    Europe/Berlin month boundary
A_j = A_j^MTD  +  Σ_{t ∈ H}  contribution_j(t)      in-horizon accumulation
```

Everything from `(10)` to `(33)` is a closed-form function of `A`, so `V_del` is
**evaluated arithmetically from a projection of the remaining month, never fitted**.
Its duals in the grey-present regime:

```
∂(31)/∂(29) =  (15)/(11)     ∂(31)/∂(11) = −(31)/(11)     ∂(31)/∂(6) = (31)/(6)
∂(31)/∂(5)  =  (31)·(9)/((5)·(10))         ∂(31)/∂(9)  = −(31)/(10)
```

Four rules follow and are normative:

1. **No lever carries a delineation coefficient of its own.** Curtailment `q`, charge
   source, discharge routing, reserve headroom and activation are priced through the
   accumulator constraints, not through terms written against them.
2. **`V_soc` and `V_del` are separable.** `V = V_soc(SOC, peakState, qualState) +
   V_del(delineation coordinates, slotsToMonthEnd)`. Permitted because `(12) = 0` is
   assured (§Consequences), which removes the only SOC coupling.
3. **`V_del` is evaluated per scenario and its risk treatment is the tail of `V_del`
   itself**, not a per-accumulator sign convention (ADR-005 ensemble, `PeakView`'s CVaR
   device).
4. **Settlement computes the exact machinery.** The linearisation lives only in
   Valuation, and the difference is booked (§Consequences).

## Consequences

### Effect enumeration — two additions, one base correction

| Effect | Owner | Base | Note |
|---|---|---|---|
| `SubsidyRevenue` | `DelineationView` | `Delineation` | `MAX[AW − MW_month; 0] · (32)`; spot revenue on the same MWh stays in `SpotEnergyValue` |
| `EnfgLevies` | `DelineationView` | `Delineation` | Charged on `(21)`, the reducible EnFG components only |
| `LeviesAndTaxes` | `TariffView` | `PoiImport` | **Narrowed** to the non-reducible components — Stromsteuer, Konzessionsabgabe |

Thirteen effects. The split is a base split, not a relief booked as revenue, so
ADR-009 exclusivity holds unchanged. A new composer stage **3 `Delineation`**
requires battery energy marked and establishes `(21)`; stage 1 keeps only the
non-reducible tariff surface and stays linear.

### Structural results that reduce the model

| Result | Condition | Effect |
|---|---|---|
| `(12) = 0` | monthly charge throughput `> η_d·E/(1−η_rt)` ≈ 8 charge-equivalent cycles | Fremdtankstrom vanishes; `V_del` separable from SOC; asserted, not modelled |
| `(2)¼` needs no binary | `λ_11 ≥ 0` — `(11)` feeds `(13)`, the base of both routes | Hypograph relaxation `(2) ≤ Z1NE, (2) ≤ Z2E` is tight |
| Green route is linear | no grid charging, `(9) = 0` | `(31) = (29)`, the ratio cancels; all curvature in this design is created by grid charging |
| A5 reduces to A1 | all plants marktprämien-gefördert on a common EEG vintage, so `(24x)¼` is common | `(32x) = ZFx·(32)`; no per-plant accumulators; value is `(32) · Σ_x ZFx·premium_x` |
| `(21) = 0` identically | `load = 0`, connection never clips discharge | `(16)+(19) = (9) = (3)`. Held as a **settlement test fixture**, never as a code path |

Binaries: import/export exclusivity, one per slot, because levies penalise import while
the premium rewards export and the pair is otherwise unbounded. `(1)¼ = MIN[Z1NB; Z2V]`
needs a second when `λ_9` may be negative — which is whenever the site has load.

### Costs accepted

- **`INV-S-10` (zero-gap) is split.** `modelError` gains a `linearizationGap`
  sub-bucket: L2's `λ`-linearisation against L5's exact machinery on the same
  trajectory. It is expected, budgeted and reported; residual `modelError` is still
  asserted zero. Without the split the most valuable test in L5 fails permanently on
  day one. `linearizationGap` is interpretable as one thing — projection error.
- **`MW_month` is not known at decision time.** `MAX[AW − MW; 0]` is convex, so pricing
  at the mean understates the premium. It is **derived per scenario** from national spot
  and national solar generation on the shared ADR-005 axis, never modelled independently
  — an independent series would destroy the correlation that makes the opposing channels
  (cannibalisation raises the premium; negative-price hours lower `(30)`) representable.
- **`(24)¼` is a state machine, not a comparison.** § 51 / § 51b EEG carry a duration
  condition, so the indicator for a slot depends on a run of neighbouring day-ahead
  prices. Declared in `L1` as a function of the day-ahead series, with the threshold as
  configuration per EEG vintage.
- **Self-consumption forfeits both routes.** A MWh discharged into load never enters
  `(11)`. This falls out of the state equations, so no `SelfConsumptionView` is needed
  and RECONCILIATION item H resolves negatively — but the resolution assumes retail
  supply is spot-indexed.

## Rejected

- **Per-lever delineation coefficients.** A coefficient on `q` or on reserve headroom
  must embed the `MIN`-branch case analysis, evaluated at a state the Planner has not
  yet chosen. Raising `Z1NB` raises `(9)` only while `Z1NB < Z2V`; curtailment eats
  `(2)¼` when `Z1NE ≤ Z2E` and `(23)¼` otherwise. Four regimes, selected inside the same
  solve. This is ADR-016's rejected rule, one level up.
- **Provenance flow variables.** Attributing energy PV→battery, battery→grid, or a
  discharge to a named plant models a fiction. The delineation evaluates `MIN`/`MAX` on
  four metered scalars per quarter-hour (`Z1NB¼`, `Z1NE¼`, `Z2V¼`, `Z2E¼`); the labels
  follow with no attribution freedom. A flow formulation invents degrees of freedom —
  most damagingly an apparent choice of which plant's `AW` a discharge earns, which `ZF`
  fixes statically — and books value settlement will not pay. It also reconciles against
  nothing the Netzbetreiber records, which is what makes `L5` §5.1 a test rather than a
  second opinion. `C2` §2's vocabulary gains meter-shaped variables only.
- **Exact formulation to month end.** Up to 2,880 slots × scenarios per tick, with the
  bilinear terms intact. Not survivable at `C_tick`, and forbidden by ADR-008.
- **Fitting `V_del` like `V_soc`.** The formulas are closed-form; fitting would bake the
  uncertainty into coefficients instead of leaving it in a projection where it is visible
  and testable, and would import the staleness machinery for nothing.
- **A `LevyRelief` effect.** Double-counts against `LeviesAndTaxes` unless the base
  changes too, and relief is not a cashflow. `(20)` is reported as a diagnostic quantity.
- **Scoping v1 to co-location.** `(21) = 0` is a load-specific identity, not a
  conservative simplification; every dual and term built on it is wrong the moment load
  exists. The general case is built first.
